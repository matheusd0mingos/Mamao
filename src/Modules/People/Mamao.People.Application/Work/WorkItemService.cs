using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Work;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Work;

public sealed record CreateWorkItemRequest(
    string Title,
    string? Details,
    WorkItemPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueOn,
    Guid? DepartmentId);

public sealed record UpdateWorkItemRequest(
    string Title,
    string? Details,
    WorkItemPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueOn,
    Guid? DepartmentId);

public sealed record MoveWorkItemRequest(WorkItemStatus Status);

public sealed record WorkItemResponse(
    WorkItemId Id,
    string Title,
    string? Details,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    EmployeeId? AssigneeId,
    string? AssigneeName,
    DateOnly? DueOn,
    Guid? DepartmentId,
    string? DepartmentName,
    DateTimeOffset CreatedAt,
    string CreatedByName,
    DateTimeOffset? CompletedAt,
    WorkAlert? Alert);

/// <param name="Atrasadas">Quantas demandas abertas já passaram do prazo. É o número que abre a conversa.</param>
public sealed record WorkBoard(
    IReadOnlyList<WorkItemResponse> Items,
    int Atrasadas,
    int SemResponsavel,
    int ComRiscoDeAusencia);

/// <summary>
/// Demandas e o quadro.
///
/// A leitura do quadro cruza cada cartão com a disponibilidade do responsável: o gestor vê
/// que o Marcos entra de férias na quarta e o prazo é sexta, sem abrir o calendário. Esse
/// cruzamento é a razão de a demanda morar no mesmo módulo que a ocupação — em produto
/// separado seria uma integração, e integração que ninguém liga não avisa nada.
///
/// O quadro não guarda ordem manual dentro da coluna. Ordem arrastada envelhece: quem
/// arrastou na segunda não volta na quinta para corrigir. Ordena por prioridade e prazo,
/// que continuam verdadeiros sozinhos.
/// </summary>
public sealed class WorkItemService(
    IPeopleDbContext dbContext,
    IAuditLog audit,
    ICurrentActor actor,
    TimeProvider timeProvider)
{
    /// <summary>Concluída some do quadro depois disto — o feito recente ainda serve de recibo.</summary>
    private const int DiasDeConcluidaNoQuadro = 7;

    private const int MaxCartoes = 300;

    public async Task<WorkBoard> BoardAsync(
        Guid? departmentId, Guid? assigneeId, bool incluirConcluidas, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var corteConcluidas = timeProvider.GetUtcNow().AddDays(-DiasDeConcluidaNoQuadro);

        var consulta = dbContext.WorkItems.AsNoTracking()
            .Where(w => w.Status != WorkItemStatus.Cancelada);

        if (!incluirConcluidas)
        {
            consulta = consulta.Where(w =>
                w.Status != WorkItemStatus.Concluida || w.CompletedAt >= corteConcluidas);
        }

        if (departmentId is { } setor)
            consulta = consulta.Where(w => w.DepartmentId == new DepartmentId(setor));

        if (assigneeId is { } pessoa)
            consulta = consulta.Where(w => w.AssigneeId == new EmployeeId(pessoa));

        // Ordem explícita e não `OrderByDescending(w => w.Priority)`: o enum é gravado como
        // TEXTO, então o banco ordenaria alfabeticamente — "Normal" > "Baixa" > "Alta" — e
        // a demanda de prioridade alta apareceria por último. Passou no build, passou nos
        // testes de domínio, e só apareceu ao olhar o quadro carregado.
        var itens = await consulta
            .OrderBy(w => w.Priority == WorkItemPriority.Alta ? 0
                : w.Priority == WorkItemPriority.Normal ? 1 : 2)
            .ThenBy(w => w.DueOn == null)
            .ThenBy(w => w.DueOn)
            .ThenBy(w => w.CreatedAt)
            .Take(MaxCartoes)
            .ToListAsync(ct);

        var nomes = await NomesAsync(itens, ct);
        var ausencias = await AusenciasAsync(itens, hoje, ct);

        var resposta = itens
            .Select(w => Montar(w, hoje, nomes, ausencias))
            .ToList();

        return new WorkBoard(
            resposta,
            resposta.Count(r => r.Alert?.Kind == WorkAlertKind.Atrasada),
            resposta.Count(r => r.Alert?.Kind == WorkAlertKind.SemResponsavel),
            resposta.Count(r => r.Alert?.Kind is WorkAlertKind.ResponsavelAusente or WorkAlertKind.ResponsavelSai));
    }

    public async Task<Result<WorkItemResponse>> CreateAsync(CreateWorkItemRequest request, CancellationToken ct)
    {
        var criacao = WorkItem.Create(
            request.Title,
            timeProvider.GetUtcNow(),
            actor.Name,
            request.Details,
            request.Priority,
            request.AssigneeId is { } a ? new EmployeeId(a) : null,
            request.DueOn,
            request.DepartmentId is { } d ? new DepartmentId(d) : null);

        if (criacao.IsFailure)
            return Result.Failure<WorkItemResponse>(criacao.Error!);

        var validacao = await ReferenciasValidasAsync(criacao.Value.AssigneeId, ct);
        if (validacao.IsFailure)
            return Result.Failure<WorkItemResponse>(validacao.Error!);

        dbContext.WorkItems.Add(criacao.Value);
        audit.Record(AuditActions.WorkItemCreated, nameof(WorkItem), criacao.Value.Id.ToString(), criacao.Value.Title);

        await dbContext.SaveChangesAsync(ct);

        return Result.Success(await UmAsync(criacao.Value, ct));
    }

    public async Task<Result<WorkItemResponse>> UpdateAsync(
        WorkItemId id, UpdateWorkItemRequest request, CancellationToken ct)
    {
        var item = await dbContext.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (item is null)
            return Result.Failure<WorkItemResponse>(new Error("work.not_found", "Demanda não encontrada."));

        var novoResponsavel = request.AssigneeId is { } a ? new EmployeeId(a) : (EmployeeId?)null;

        var validacao = await ReferenciasValidasAsync(novoResponsavel, ct);
        if (validacao.IsFailure)
            return Result.Failure<WorkItemResponse>(validacao.Error!);

        var alteracao = item.Update(
            request.Title,
            request.Details,
            request.Priority,
            novoResponsavel,
            request.DueOn,
            request.DepartmentId is { } d ? new DepartmentId(d) : null);

        if (alteracao.IsFailure)
            return Result.Failure<WorkItemResponse>(alteracao.Error!);

        audit.Record(AuditActions.WorkItemUpdated, nameof(WorkItem), id.ToString(), item.Title);
        await dbContext.SaveChangesAsync(ct);

        return Result.Success(await UmAsync(item, ct));
    }

    /// <summary>Arrastar de coluna. É a operação mais frequente do quadro, e por isso tem rota própria.</summary>
    public async Task<Result<WorkItemResponse>> MoveAsync(WorkItemId id, WorkItemStatus status, CancellationToken ct)
    {
        var item = await dbContext.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (item is null)
            return Result.Failure<WorkItemResponse>(new Error("work.not_found", "Demanda não encontrada."));

        var anterior = item.Status;

        var movimento = item.MoveTo(status, timeProvider.GetUtcNow());
        if (movimento.IsFailure)
            return Result.Failure<WorkItemResponse>(movimento.Error!);

        if (anterior != status)
        {
            audit.Record(
                AuditActions.WorkItemMoved, nameof(WorkItem), id.ToString(), item.Title,
                new { De = anterior.ToString(), Para = status.ToString() });
        }

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(await UmAsync(item, ct));
    }

    public async Task<Result> DeleteAsync(WorkItemId id, CancellationToken ct)
    {
        var item = await dbContext.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (item is null)
            return Result.Failure("work.not_found", "Demanda não encontrada.");

        dbContext.WorkItems.Remove(item);
        audit.Record(AuditActions.WorkItemDeleted, nameof(WorkItem), id.ToString(), item.Title);

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ── leitura ──────────────────────────────────────────────────────────────────

    private async Task<WorkItemResponse> UmAsync(WorkItem item, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        List<WorkItem> um = [item];

        return Montar(item, hoje, await NomesAsync(um, ct), await AusenciasAsync(um, hoje, ct));
    }

    private sealed record Nomes(
        Dictionary<EmployeeId, string> Pessoas,
        Dictionary<DepartmentId, string> Setores);

    private async Task<Nomes> NomesAsync(IReadOnlyCollection<WorkItem> itens, CancellationToken ct)
    {
        var pessoas = itens.Where(w => w.AssigneeId is not null).Select(w => w.AssigneeId!.Value).Distinct().ToList();
        var setores = itens.Where(w => w.DepartmentId is not null).Select(w => w.DepartmentId!.Value).Distinct().ToList();

        return new Nomes(
            pessoas.Count == 0
                ? []
                : await dbContext.Employees.AsNoTracking()
                    .Where(e => pessoas.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => e.FullName, ct),
            setores.Count == 0
                ? []
                : await dbContext.Departments.AsNoTracking()
                    .Where(d => setores.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, d => d.Name, ct));
    }

    /// <summary>
    /// As ausências que importam para estes cartões: de hoje até o prazo mais distante.
    ///
    /// Uma consulta só para o quadro inteiro. Perguntar a disponibilidade cartão a cartão
    /// daria trinta consultas numa tela que o gestor abre o dia todo.
    /// </summary>
    private async Task<ILookup<EmployeeId, Occupancy>> AusenciasAsync(
        IReadOnlyCollection<WorkItem> itens, DateOnly hoje, CancellationToken ct)
    {
        var responsaveis = itens
            .Where(w => w.Aberta && w.AssigneeId is not null)
            .Select(w => w.AssigneeId!.Value)
            .Distinct()
            .ToList();

        if (responsaveis.Count == 0)
            return Array.Empty<Occupancy>().ToLookup(o => o.EmployeeId);

        // Sem prazo não há "sai antes do prazo" a calcular, mas "está fora hoje" continua
        // valendo — por isso o limite mínimo é o próprio dia.
        var limite = itens
            .Where(w => w.Aberta && w.DueOn is not null)
            .Select(w => w.DueOn!.Value)
            .DefaultIfEmpty(hoje)
            .Max();

        if (limite < hoje)
            limite = hoje;

        var blocos = await dbContext.Occupancies.AsNoTracking()
            .Where(o => responsaveis.Contains(o.EmployeeId))
            .Where(o => o.StartsOn <= limite && o.EndsOn >= hoje)
            .ToListAsync(ct);

        return blocos.ToLookup(o => o.EmployeeId);
    }

    private static WorkItemResponse Montar(
        WorkItem w, DateOnly hoje, Nomes nomes, ILookup<EmployeeId, Occupancy> ausencias)
    {
        var responsavel = w.AssigneeId is { } a ? nomes.Pessoas.GetValueOrDefault(a) : null;

        return new WorkItemResponse(
            w.Id,
            w.Title,
            w.Details,
            w.Status,
            w.Priority,
            w.AssigneeId,
            responsavel,
            w.DueOn,
            w.DepartmentId?.Value,
            w.DepartmentId is { } d ? nomes.Setores.GetValueOrDefault(d) : null,
            w.CreatedAt,
            w.CreatedByName,
            w.CompletedAt,
            WorkAlertRule.Avaliar(
                w, hoje, responsavel,
                w.AssigneeId is { } id ? ausencias[id] : []));
    }

    /// <summary>
    /// O responsável existe e ainda está na empresa?
    ///
    /// A FK garante que existe; ela não impede atribuir demanda a quem foi desligado — e
    /// essa demanda ficaria parada sem ninguém entender por quê.
    /// </summary>
    private async Task<Result> ReferenciasValidasAsync(EmployeeId? assigneeId, CancellationToken ct)
    {
        if (assigneeId is not { } id)
            return Result.Success();

        var pessoa = await dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.FullName, e.TerminatedOn })
            .FirstOrDefaultAsync(ct);

        if (pessoa is null)
            return Result.Failure("work.assignee_not_found", "Funcionário não encontrado.", "assigneeId");

        if (pessoa.TerminatedOn is not null)
        {
            return Result.Failure(
                "work.assignee_terminated",
                $"{pessoa.FullName} foi desligado e não pode receber demandas.",
                "assigneeId");
        }

        return Result.Success();
    }
}
