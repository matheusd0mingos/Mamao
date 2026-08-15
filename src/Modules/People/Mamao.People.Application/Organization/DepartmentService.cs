using Mamao.People.Contracts;
using Mamao.People.Domain.Organization;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Organization;

/// <summary>
/// Casos de uso de setor. A parte que exige cuidado e mover um setor: o caminho
/// materializado e rapido para LER e caro para MOVER, porque o caminho de todos os
/// descendentes precisa ser reescrito. Essa e a troca consciente — no produto se lê
/// "quem está abaixo de Operações?" o tempo todo e se move setor quase nunca.
/// </summary>
public sealed class DepartmentService(
    IPeopleDbContext dbContext,
    IAuditLog audit,
    ICurrentActor actor,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<DepartmentNode>> ListAsync(CancellationToken ct)
    {
        var setores = await dbContext.Departments
            .AsNoTracking()
            .OrderBy(d => d.Path)   // ordenar por caminho ja entrega a arvore na ordem certa
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.ParentId,
                d.Depth,
                d.Path,
                d.ManagerId,
                d.CreatedByName,
                d.CreatedAt,
                Diretos = dbContext.Employees.Count(e => e.DepartmentId == d.Id && e.TerminatedOn == null),
            })
            .ToListAsync(ct);

        if (setores.Count == 0)
            return [];

        // Nome do gestor numa consulta so, em vez de uma por setor.
        var gestores = setores.Where(d => d.ManagerId != null).Select(d => d.ManagerId!.Value).Distinct().ToList();
        var nomes = gestores.Count == 0
            ? []
            : await dbContext.Employees
                .Where(e => gestores.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        // O acumulado da subarvore sai do proprio caminho: um setor esta abaixo do outro
        // quando o caminho dele comeca pelo caminho do outro. Com dezenas de nos isso e
        // trivial em memoria e evita uma consulta recursiva.
        return [.. setores.Select(d => new DepartmentNode(
            d.Id,
            d.Name,
            d.ParentId,
            d.Depth,
            d.ManagerId,
            d.ManagerId is null ? null : nomes.GetValueOrDefault(d.ManagerId.Value),
            d.Diretos,
            setores.Where(f => f.Path.StartsWith(d.Path, StringComparison.Ordinal)).Sum(f => f.Diretos),
            d.CreatedByName,
            d.CreatedAt))];
    }

    public async Task<Result<DepartmentNode>> CreateAsync(CreateDepartmentRequest request, CancellationToken ct)
    {
        Department? pai = null;

        if (request.ParentId is { } parentId)
        {
            pai = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == parentId, ct);
            if (pai is null)
                return Result.Failure<DepartmentNode>(new Error("department.parent_not_found", "Setor pai não encontrado."));
        }

        var criacao = Department.Create(request.Name, pai, timeProvider.GetUtcNow(), actor.Name);
        if (criacao.IsFailure)
            return Result.Failure<DepartmentNode>(criacao.Error!);

        var setor = criacao.Value;

        // Compara pelo nome DOBRADO. Antes comparava pelo nome cru, entao "Secao Tecnica"
        // passava ao lado de "Seção Técnica" — e a equipe se dividia entre os dois.
        if (await dbContext.Departments.AnyAsync(
                d => d.ParentId == setor.ParentId && d.NormalizedName == setor.NormalizedName, ct))
        {
            return Result.Failure<DepartmentNode>(new Error(
                "department.duplicate",
                $"Já existe um setor com esse nome neste nível.",
                nameof(CreateDepartmentRequest.Name)));
        }

        dbContext.Departments.Add(setor);

        audit.Record(
            AuditActions.DepartmentCreated, nameof(Department), setor.Id.ToString(), setor.Name,
            new { Pai = pai?.Name });
        await dbContext.SaveChangesAsync(ct);

        return Result.Success(new DepartmentNode(
            setor.Id, setor.Name, setor.ParentId, setor.Depth, null, null, 0, 0,
            setor.CreatedByName, setor.CreatedAt));
    }

    public async Task<Result> UpdateAsync(DepartmentId id, UpdateDepartmentRequest request, CancellationToken ct)
    {
        var setor = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (setor is null)
            return Result.Failure("department.not_found", "Setor não encontrado.");

        if (request.ManagerId is { } managerId
            && !await dbContext.Employees.AnyAsync(e => e.Id == managerId, ct))
        {
            return Result.Failure("department.manager_not_found", "Gestor não encontrado.");
        }

        var nomeAnterior = setor.Name;
        var chefeAnterior = setor.ManagerId;

        var renomear = setor.Rename(request.Name);
        if (renomear.IsFailure)
            return renomear;

        // O nome dobrado tem indice unico: renomear "Secao Tecnica" para "Seção Técnica"
        // com as duas existindo daria erro de banco. Melhor a mensagem do que o 500.
        if (await dbContext.Departments.AnyAsync(
                d => d.Id != id && d.ParentId == setor.ParentId && d.NormalizedName == setor.NormalizedName, ct))
        {
            return Result.Failure(
                "department.duplicate",
                "Já existe um setor com esse nome neste nível.",
                nameof(UpdateDepartmentRequest.Name));
        }

        setor.AssignManager(request.ManagerId);

        if (nomeAnterior != setor.Name || chefeAnterior != setor.ManagerId)
        {
            audit.Record(
                AuditActions.DepartmentRenamed, nameof(Department), id.ToString(), setor.Name,
                new { De = nomeAnterior, Para = setor.Name, ChefeMudou = chefeAnterior != setor.ManagerId });
        }

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Move o setor e REESCREVE o caminho de todos os descendentes na mesma transacao.
    /// Esquecer os descendentes deixaria a arvore mentindo — eles continuariam apontando
    /// para o caminho antigo e sumiriam de "tudo abaixo de X" sem nenhum erro visivel.
    /// </summary>
    public async Task<Result> MoveAsync(DepartmentId id, MoveDepartmentRequest request, CancellationToken ct)
    {
        var setor = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (setor is null)
            return Result.Failure("department.not_found", "Setor não encontrado.");

        Department? novoPai = null;
        if (request.ParentId is { } parentId)
        {
            novoPai = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == parentId, ct);
            if (novoPai is null)
                return Result.Failure("department.parent_not_found", "Setor pai não encontrado.");
        }

        var caminhoAntigo = setor.Path;

        // Descendentes carregados ANTES do move, enquanto o caminho antigo ainda vale.
        var descendentes = await dbContext.Departments
            .Where(d => d.Id != id && d.Path.StartsWith(caminhoAntigo))
            .ToListAsync(ct);

        if (descendentes.Count > 0 && novoPai is not null
            && descendentes.Exists(d => d.Id == novoPai.Id))
        {
            return Result.Failure("department.cycle", "Não dá para mover um setor para dentro dele mesmo.");
        }

        var mover = setor.MoveTo(novoPai);
        if (mover.IsFailure)
            return mover;

        if (await dbContext.Departments.AnyAsync(
                d => d.ParentId == setor.ParentId && d.Name == setor.Name && d.Id != id, ct))
        {
            return Result.Failure("department.duplicate", $"Já existe o setor \"{setor.Name}\" neste nível.");
        }

        foreach (var descendente in descendentes)
        {
            // Troca so o prefixo: o resto do caminho descreve a posicao dele dentro da
            // subarvore, que o movimento nao altera. O pai direto tambem nao muda.
            var novoCaminho = string.Concat(setor.Path, descendente.Path.AsSpan(caminhoAntigo.Length));
            var novaProfundidade = Department.DepthFromPath(novoCaminho);

            if (novaProfundidade >= Department.MaxDepth)
            {
                return Result.Failure(
                    "department.too_deep",
                    $"O movimento deixaria \"{descendente.Name}\" além do limite de {Department.MaxDepth} níveis.");
            }

            descendente.Reparent(descendente.ParentId, novoCaminho, novaProfundidade);
        }

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(DepartmentId id, CancellationToken ct)
    {
        var setor = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (setor is null)
            return Result.Failure("department.not_found", "Setor não encontrado.");

        if (await dbContext.Departments.AnyAsync(d => d.ParentId == id, ct))
            return Result.Failure("department.has_children", "Este setor tem setores dentro dele. Exclua ou mova os de baixo antes.");

        var pessoas = await dbContext.Employees.CountAsync(e => e.DepartmentId == id, ct);
        if (pessoas > 0)
        {
            return Result.Failure(
                "department.in_use",
                pessoas == 1
                    ? "1 pessoa está neste setor. Mova-a antes de excluir."
                    : $"{pessoas} pessoas estão neste setor. Mova-as antes de excluir.");
        }

        audit.Record(AuditActions.DepartmentDeleted, nameof(Department), id.ToString(), setor.Name);

        dbContext.Departments.Remove(setor);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Devolve o setor com este nome no primeiro nivel, criando se nao existir. NAO salva.
    /// Usado pela importacao: a planilha traz "Portaria" como texto, sem hierarquia.
    /// </summary>
    public async Task<Result<Department>> ResolveOrCreateRootAsync(
        string nome, Dictionary<string, Department> cache, CancellationToken ct)
    {
        var chave = Position.Normalize(nome?.Trim() ?? string.Empty);

        if (cache.TryGetValue(chave, out var jaVisto))
            return Result.Success(jaVisto);

        var candidatos = await dbContext.Departments.Where(d => d.ParentId == null).ToListAsync(ct);
        var existente = candidatos.Find(d => Position.Normalize(d.Name) == chave);

        if (existente is not null)
        {
            cache[chave] = existente;
            return Result.Success(existente);
        }

        var criacao = Department.Create(nome, null, timeProvider.GetUtcNow(), actor.Name);
        if (criacao.IsFailure)
            return criacao;

        dbContext.Departments.Add(criacao.Value);
        cache[chave] = criacao.Value;
        return criacao;
    }
}
