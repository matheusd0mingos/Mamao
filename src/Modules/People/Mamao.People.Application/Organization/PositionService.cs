using Mamao.People.Contracts;
using Mamao.People.Domain.Organization;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Organization;

/// <summary>
/// Casos de uso de cargo. A operacao mais usada nao e nenhum CRUD: e
/// <see cref="ResolveOrCreateAsync"/>, chamada pela importacao de planilha para transformar
/// a coluna "Cargo" em cargo de verdade sem obrigar o cliente a cadastrar antes.
/// </summary>
public sealed class PositionService(IPeopleDbContext dbContext)
{
    public async Task<IReadOnlyList<PositionResponse>> ListAsync(CancellationToken ct)
    {
        // Contagem por subconsulta em vez de GroupJoin: uma consulta so, e o Postgres
        // resolve com o indice (tenant_id, position_id) de employees.
        return await dbContext.Positions
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PositionResponse(
                p.Id,
                p.Name,
                dbContext.Employees.Count(e => e.PositionId == p.Id && e.TerminatedOn == null)))
            .ToListAsync(ct);
    }

    public async Task<Result<PositionResponse>> CreateAsync(CreatePositionRequest request, CancellationToken ct)
    {
        var criacao = Position.Create(request.Name);
        if (criacao.IsFailure)
            return Result.Failure<PositionResponse>(criacao.Error!);

        var cargo = criacao.Value;

        if (await dbContext.Positions.AnyAsync(p => p.NormalizedName == cargo.NormalizedName, ct))
        {
            return Result.Failure<PositionResponse>(new Error(
                "position.duplicate", $"Já existe o cargo \"{cargo.Name}\".", nameof(CreatePositionRequest.Name)));
        }

        dbContext.Positions.Add(cargo);
        await dbContext.SaveChangesAsync(ct);

        return Result.Success(new PositionResponse(cargo.Id, cargo.Name, 0));
    }

    public async Task<Result<PositionResponse>> RenameAsync(
        PositionId id, UpdatePositionRequest request, CancellationToken ct)
    {
        var cargo = await dbContext.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (cargo is null)
            return Result.Failure<PositionResponse>(new Error("position.not_found", "Cargo não encontrado."));

        var normalizado = Position.Normalize(request.Name?.Trim() ?? string.Empty);

        if (await dbContext.Positions.AnyAsync(p => p.NormalizedName == normalizado && p.Id != id, ct))
        {
            return Result.Failure<PositionResponse>(new Error(
                "position.duplicate", "Já existe outro cargo com esse nome.", nameof(UpdatePositionRequest.Name)));
        }

        var renomear = cargo.Rename(request.Name);
        if (renomear.IsFailure)
            return Result.Failure<PositionResponse>(renomear.Error!);

        await dbContext.SaveChangesAsync(ct);

        var emUso = await dbContext.Employees.CountAsync(e => e.PositionId == id && e.TerminatedOn == null, ct);
        return Result.Success(new PositionResponse(cargo.Id, cargo.Name, emUso));
    }

    public async Task<Result> DeleteAsync(PositionId id, CancellationToken ct)
    {
        var cargo = await dbContext.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (cargo is null)
            return Result.Failure("position.not_found", "Cargo não encontrado.");

        // A FK ja recusaria, mas com uma violacao de constraint que vira erro 500 e nao diz
        // nada ao usuario. Contar antes custa uma consulta e devolve a frase certa.
        var emUso = await dbContext.Employees.CountAsync(e => e.PositionId == id, ct);
        if (emUso > 0)
        {
            return Result.Failure(
                "position.in_use",
                emUso == 1
                    ? "1 pessoa ocupa este cargo. Mude o cargo dela antes de excluir."
                    : $"{emUso} pessoas ocupam este cargo. Mude o cargo delas antes de excluir.");
        }

        dbContext.Positions.Remove(cargo);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Devolve o cargo com este nome, criando se nao existir. NAO salva — quem chama decide
    /// a transacao, porque a importacao grava dezenas de linhas de uma vez.
    /// O cache local evita ir ao banco uma vez por linha da planilha.
    /// </summary>
    public async Task<Result<Position>> ResolveOrCreateAsync(
        string nome, Dictionary<string, Position> cache, CancellationToken ct)
    {
        var normalizado = Position.Normalize(nome?.Trim() ?? string.Empty);

        if (cache.TryGetValue(normalizado, out var jaVisto))
            return Result.Success(jaVisto);

        var existente = await dbContext.Positions.FirstOrDefaultAsync(p => p.NormalizedName == normalizado, ct);
        if (existente is not null)
        {
            cache[normalizado] = existente;
            return Result.Success(existente);
        }

        var criacao = Position.Create(nome);
        if (criacao.IsFailure)
            return criacao;

        dbContext.Positions.Add(criacao.Value);
        cache[normalizado] = criacao.Value;
        return criacao;
    }

    /// <summary>Carrega todos os cargos do tenant num dicionario pelo nome dobrado.</summary>
    public async Task<Dictionary<string, Position>> LoadCacheAsync(CancellationToken ct)
        => await dbContext.Positions.ToDictionaryAsync(p => p.NormalizedName, p => p, ct);
}
