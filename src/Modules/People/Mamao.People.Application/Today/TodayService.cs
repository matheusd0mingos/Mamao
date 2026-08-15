using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Missions;
using Mamao.People.Domain.Work;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Today;

/// <param name="Until">Último dia fora. Igual a hoje quer dizer que volta amanhã.</param>
public sealed record TodayPerson(
    EmployeeId EmployeeId,
    string Name,
    string? DepartmentName,
    OccupancyKind Kind,
    DateOnly Until);

/// <param name="Missing">Quantas pessoas ainda faltam. Zero com status Rascunho ainda pede confirmação.</param>
public sealed record TodayMission(
    MissionId Id,
    string Name,
    DateOnly On,
    TimeOnly? StartsAt,
    int RequiredPeople,
    int AssignedCount,
    int Missing,
    bool Confirmed);

public sealed record TodayWorkItem(
    WorkItemId Id,
    string Title,
    string? AssigneeName,
    DateOnly? DueOn,
    bool Overdue);

public sealed record TodayApproval(
    AbsenceRequestId Id,
    string EmployeeName,
    OccupancyKind Kind,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int DiasAteComecar);

/// <summary>
/// O estado de hoje, numa resposta só.
/// </summary>
/// <param name="TeamSize">Efetivo ativo. É o denominador de "3 de 12 fora".</param>
/// <param name="Out">Quem está fora HOJE.</param>
/// <param name="BackTomorrow">Quem volta amanhã. Serve para planejar, não para cobrar.</param>
/// <param name="Missions">Missões de hoje e amanhã, com o que falta.</param>
/// <param name="Work">Demandas atrasadas ou vencendo hoje.</param>
/// <param name="Approvals">Solicitações esperando decisão. Vazio para quem não aprova.</param>
public sealed record TodayPanel(
    DateOnly Today,
    int TeamSize,
    IReadOnlyList<TodayPerson> Out,
    IReadOnlyList<TodayPerson> BackTomorrow,
    IReadOnlyList<TodayMission> Missions,
    IReadOnlyList<TodayWorkItem> Work,
    IReadOnlyList<TodayApproval> Approvals);

/// <summary>
/// O painel de abertura.
///
/// A tese do produto é "o gestor não deveria precisar perguntar o que está acontecendo".
/// Esta é a tela onde isso ou é verdade ou não é: abrir o sistema tem que responder quem
/// está fora, o que precisa de gente, o que venceu e o que espera decisão — sem clicar.
///
/// Uma requisição só, de propósito. Quatro chamadas para montar a primeira tela fariam
/// o painel piscar em pedaços e, pior, permitiriam que uma delas falhasse deixando um
/// buraco que parece "nada pendente" — que é a mentira mais cara que este painel poderia
/// contar.
/// </summary>
public sealed class TodayService(IPeopleDbContext dbContext, ICurrentActor actor, TimeProvider timeProvider)
{
    /// <summary>Um painel é para ser lido de relance. Listas longas viram a tela de detalhe.</summary>
    private const int MaxPorLista = 12;

    public async Task<TodayPanel> GetAsync(CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var amanha = hoje.AddDays(1);

        var efetivo = await dbContext.Employees.AsNoTracking()
            .CountAsync(e => e.TerminatedOn == null, ct);

        // ── quem está fora, e quem volta ─────────────────────────────────────────
        var blocos = await dbContext.Occupancies.AsNoTracking()
            .Where(o => o.StartsOn <= amanha && o.EndsOn >= hoje)
            .Select(o => new
            {
                o.EmployeeId,
                o.Kind,
                o.StartsOn,
                o.EndsOn,
                o.StartsAt,
                o.EndsAt,
                Name = dbContext.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefault(),
                DepartmentName = dbContext.Employees
                    .Where(e => e.Id == o.EmployeeId)
                    .Select(e => dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault())
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Uma pessoa pode ter dois blocos no mesmo dia (folga de manhã, serviço à tarde).
        // O painel mostra UM por pessoa: o que termina mais tarde, porque é o que decide
        // quando ela volta a estar disponível.
        var fora = blocos
            .Where(b => b.StartsOn <= hoje && b.EndsOn >= hoje)
            .GroupBy(b => b.EmployeeId)
            .Select(g => g.OrderByDescending(b => b.EndsOn).First())
            .Select(b => new TodayPerson(b.EmployeeId, b.Name ?? "—", b.DepartmentName, b.Kind, b.EndsOn))
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.Name)
            .Take(MaxPorLista)
            .ToList();

        var voltam = blocos
            .Where(b => b.EndsOn == hoje)
            .GroupBy(b => b.EmployeeId)
            .Select(g => g.First())
            // Quem tem outro bloco começando amanhã não "volta": sai de um e entra no outro.
            .Where(b => !blocos.Any(o => o.EmployeeId == b.EmployeeId && o.StartsOn <= amanha && o.EndsOn >= amanha))
            .Select(b => new TodayPerson(b.EmployeeId, b.Name ?? "—", b.DepartmentName, b.Kind, b.EndsOn))
            .OrderBy(p => p.Name)
            .Take(MaxPorLista)
            .ToList();

        // ── o que precisa de gente ───────────────────────────────────────────────
        // OrderBy ANTES do Select. Depois, o EF tentaria ordenar pelo record projetado e a
        // consulta inteira deixa de traduzir — erro que so aparece rodando, porque o
        // compilador nao ve diferenca.
        var missoes = await dbContext.Missions.AsNoTracking()
            .Where(m => m.On >= hoje && m.On <= amanha && m.Status != MissionStatus.Cancelada)
            .OrderBy(m => m.On)
            .ThenBy(m => m.StartsAt)
            .Take(MaxPorLista)
            .Select(m => new TodayMission(
                m.Id, m.Name, m.On, m.StartsAt, m.RequiredPeople,
                m.Assignments.Count,
                m.RequiredPeople - m.Assignments.Count < 0 ? 0 : m.RequiredPeople - m.Assignments.Count,
                m.Status == MissionStatus.Confirmada))
            .ToListAsync(ct);

        // ── o que venceu ─────────────────────────────────────────────────────────
        var demandas = await dbContext.WorkItems.AsNoTracking()
            .Where(w => w.Status == WorkItemStatus.Aberta || w.Status == WorkItemStatus.EmAndamento)
            .Where(w => w.DueOn != null && w.DueOn <= hoje)
            .OrderBy(w => w.DueOn)
            .Take(MaxPorLista)
            .Select(w => new TodayWorkItem(
                w.Id,
                w.Title,
                w.AssigneeId == null
                    ? null
                    : dbContext.Employees.Where(e => e.Id == w.AssigneeId).Select(e => e.FullName).FirstOrDefault(),
                w.DueOn,
                w.DueOn < hoje))
            .ToListAsync(ct);

        // ── o que espera decisão ─────────────────────────────────────────────────
        // Só para quem aprova. Mostrar a fila de aprovação para quem não pode decidir
        // seria transformar informação em ansiedade.
        var aprovacoes = actor.Can(Permissions.TimeOffApprove)
            ? await dbContext.AbsenceRequests.AsNoTracking()
                .Where(r => r.Status == AbsenceRequestStatus.Pendente)
                .OrderBy(r => r.StartsOn)
                .Take(MaxPorLista)
                .Select(r => new TodayApproval(
                    r.Id,
                    dbContext.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.FullName).FirstOrDefault() ?? "—",
                    r.Kind,
                    r.StartsOn,
                    r.EndsOn,
                    r.StartsOn.DayNumber - hoje.DayNumber))
                .ToListAsync(ct)
            : [];

        return new TodayPanel(hoje, efetivo, fora, voltam, missoes, demandas, aprovacoes);
    }
}
