using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Availability;

public sealed record OccupancyResponse(
    OccupancyId Id,
    EmployeeId EmployeeId,
    string EmployeeName,
    string? DepartmentName,
    string? PositionName,
    OccupancyKind Kind,
    OccupancySource Source,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string? Note);

public sealed record CreateOccupancyRequest(
    EmployeeId EmployeeId,
    OccupancyKind Kind,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string? Note);

/// <param name="Busy">Por que a pessoa não está livre. Vazio quer dizer disponível.</param>
public sealed record AvailabilityResponse(
    EmployeeId EmployeeId,
    string EmployeeName,
    string? PositionName,
    string? DepartmentName,
    bool Available,
    IReadOnlyList<OccupancyKind> Busy);

/// <summary>
/// Responde "quem está livre" e mantém os blocos lançados à mão.
///
/// Toda pergunta sobre disponibilidade passa por aqui e por mais nada: é o que garante
/// que férias, serviço e missão sejam tratados pela mesma regra. Quando a escala chegar,
/// ela consome este serviço em vez de reimplementar a consulta.
/// </summary>
public sealed class AvailabilityService(IPeopleDbContext dbContext, IAuditLog audit, TimeProvider timeProvider)
{
    /// <summary>
    /// Quem está disponível num dia, opcionalmente num recorte de horário.
    ///
    /// Devolve TODO MUNDO, marcado como disponível ou não, e não só os livres: a tela
    /// precisa mostrar quem ficou de fora e por quê. Uma lista que esconde os ocupados
    /// obriga o gestor a conferir na memória — que é exatamente o que o produto promete
    /// eliminar.
    /// </summary>
    public async Task<IReadOnlyList<AvailabilityResponse>> OnAsync(
        DateOnly dia, TimeOnly? de, TimeOnly? ate, CancellationToken ct)
    {
        var pessoas = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.TerminatedOn == null)
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                e.Id,
                e.FullName,
                PositionName = dbContext.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault(),
                DepartmentName = dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Uma consulta para o dia inteiro, e o recorte de horário resolvido em memória: a
        // regra de sobreposição vive no domínio, e traduzi-la para SQL criaria uma segunda
        // versão dela — que um dia discordaria da primeira, na borda que importa.
        var blocos = await dbContext.Occupancies
            .AsNoTracking()
            .Where(o => o.StartsOn <= dia && dia <= o.EndsOn)
            .ToListAsync(ct);

        var porPessoa = blocos.GroupBy(o => o.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        return [.. pessoas.Select(p =>
        {
            var ocupacoes = porPessoa.TryGetValue(p.Id, out var lista)
                ? lista.Where(o => o.Cobre(dia, de, ate)).Select(o => o.Kind).Distinct().ToList()
                : [];

            return new AvailabilityResponse(
                p.Id, p.FullName, p.PositionName, p.DepartmentName, ocupacoes.Count == 0, ocupacoes);
        })];
    }

    /// <summary>Blocos de um período, para o calendário e para a tela da pessoa.</summary>
    public async Task<IReadOnlyList<OccupancyResponse>> ListAsync(
        DateOnly de, DateOnly ate, EmployeeId? employeeId, CancellationToken ct)
    {
        var consulta = dbContext.Occupancies.AsNoTracking()
            .Where(o => o.StartsOn <= ate && de <= o.EndsOn);

        if (employeeId is { } id)
            consulta = consulta.Where(o => o.EmployeeId == id);

        return await consulta
            .OrderBy(o => o.StartsOn)
            // Secao e funcao vem junto porque o calendario filtra por elas. Uma consulta a
            // mais por linha seria N+1; uma chamada a mais da tela seria duas fontes de
            // verdade para a mesma pergunta.
            .Join(dbContext.Employees, o => o.EmployeeId, e => e.Id, (o, e) => new OccupancyResponse(
                o.Id, o.EmployeeId, e.FullName,
                dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                dbContext.Positions.Where(c => c.Id == e.PositionId).Select(c => c.Name).FirstOrDefault(),
                o.Kind, o.Source,
                o.StartsOn, o.EndsOn, o.StartsAt, o.EndsAt, o.Note))
            .ToListAsync(ct);
    }

    /// <summary>Lançamento direto, sem passar por solicitação: falta, serviço, afastamento.</summary>
    public async Task<Result<OccupancyResponse>> CreateAsync(CreateOccupancyRequest request, CancellationToken ct)
    {
        var funcionario = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);

        if (funcionario is null)
            return Result.Failure<OccupancyResponse>(new Error("employee.not_found", "Funcionário não encontrado."));

        var criacao = Occupancy.Create(
            request.EmployeeId, request.Kind, request.StartsOn, request.EndsOn,
            timeProvider.GetUtcNow(), request.StartsAt, request.EndsAt,
            OccupancySource.Manual, null, request.Note);

        if (criacao.IsFailure)
            return Result.Failure<OccupancyResponse>(criacao.Error!);

        var ocupacao = criacao.Value;
        dbContext.Occupancies.Add(ocupacao);

        audit.Record(
            AuditActions.OccupancyCreated, nameof(Occupancy), ocupacao.Id.ToString(), funcionario.FullName,
            new { ocupacao.Kind, ocupacao.StartsOn, ocupacao.EndsOn });

        await dbContext.SaveChangesAsync(ct);

        return Result.Success(new OccupancyResponse(
            ocupacao.Id, ocupacao.EmployeeId, funcionario.FullName, null, null,
            ocupacao.Kind, ocupacao.Source,
            ocupacao.StartsOn, ocupacao.EndsOn, ocupacao.StartsAt, ocupacao.EndsAt, ocupacao.Note));
    }

    /// <summary>
    /// Remove um bloco. Só os lançados à mão: quem nasceu de solicitação ou missão se
    /// desfaz pela origem, senão a solicitação diria "aprovada" com a agenda livre.
    /// </summary>
    public async Task<Result> DeleteAsync(OccupancyId id, CancellationToken ct)
    {
        var ocupacao = await dbContext.Occupancies.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (ocupacao is null)
            return Result.Failure("occupancy.not_found", "Registro não encontrado.");

        if (ocupacao.Source != OccupancySource.Manual)
        {
            return Result.Failure(
                "occupancy.not_manual",
                ocupacao.Source == OccupancySource.Solicitacao
                    ? "Este bloqueio veio de uma solicitação aprovada. Cancele a solicitação."
                    : "Este bloqueio veio de uma missão. Tire a pessoa da missão.");
        }

        audit.Record(AuditActions.OccupancyDeleted, nameof(Occupancy), id.ToString(), ocupacao.Kind.ToString());

        dbContext.Occupancies.Remove(ocupacao);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }
}
