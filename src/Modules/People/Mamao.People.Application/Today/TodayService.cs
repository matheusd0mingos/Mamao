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

/// <param name="DepartmentId">Nulo = a empresa inteira.</param>
/// <param name="IsChief">A pessoa chefia esta seção. É o que faz o painel abrir nela.</param>
public sealed record TodayScope(Guid? DepartmentId, string Name, bool IsChief);

/// <summary>
/// Uma linha por seção, para quem olha a empresa inteira.
///
/// É o painel do chefe da empresa: ele não precisa de mais detalhe que o chefe de seção
/// — precisa de MENOS, lado a lado, para saber em qual seção entrar.
/// </summary>
public sealed record TodaySection(
    Guid DepartmentId,
    string Name,
    string? ChiefName,
    int TeamSize,
    int Out,
    int MissionsMissingPeople,
    int OverdueWork);

/// <summary>
/// O estado de hoje, numa resposta só.
/// </summary>
/// <param name="Scope">O que este painel está mostrando.</param>
/// <param name="Scopes">Para onde a pessoa pode trocar. Um item só quer dizer que não há troca.</param>
/// <param name="Sections">Resumo por seção. Só vem no escopo da empresa inteira.</param>
/// <param name="TeamSize">Efetivo ativo. É o denominador de "3 de 12 fora".</param>
/// <param name="Out">Quem está fora HOJE.</param>
/// <param name="BackTomorrow">Quem volta amanhã. Serve para planejar, não para cobrar.</param>
/// <param name="Missions">Missões de hoje e amanhã, com o que falta.</param>
/// <param name="Work">Demandas atrasadas ou vencendo hoje.</param>
/// <param name="Approvals">Solicitações esperando decisão. Vazio para quem não aprova.</param>
public sealed record TodayPanel(
    DateOnly Today,
    int TeamSize,
    TodayScope Scope,
    IReadOnlyList<TodayScope> Scopes,
    IReadOnlyList<TodaySection> Sections,
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

    /// <param name="departmentId">
    /// Seção a olhar. Nulo usa o escopo natural de quem está pedindo: a seção que ela
    /// chefia, ou a empresa inteira quando ela não chefia nenhuma.
    /// </param>
    /// <param name="wholeCompany">
    /// Pediu a empresa inteira EXPLICITAMENTE.
    ///
    /// Existe porque nulo em <paramref name="departmentId"/> já significa "não escolhi", e
    /// o chefe de seção que escolhesse "a empresa inteira" caía de volta na própria seção —
    /// o seletor mudava e a tela não. Dois significados no mesmo nulo é sempre isso.
    /// </param>
    public async Task<TodayPanel> GetAsync(Guid? departmentId, bool wholeCompany, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var amanha = hoje.AddDays(1);

        var setores = await dbContext.Departments.AsNoTracking()
            .OrderBy(d => d.Path)
            .Select(d => new Setor(d.Id, d.Name, d.Path, d.ManagerId))
            .ToListAsync(ct);

        // Quem é esta pessoa do lado de People. Sem o vínculo conta↔funcionário não há
        // como saber que ela chefia alguma coisa — e o painel cai no da empresa inteira.
        var euId = actor.UserId is { } usuario
            ? await dbContext.Employees.AsNoTracking()
                .Where(e => e.UserId == usuario)
                .Select(e => (EmployeeId?)e.Id)
                .FirstOrDefaultAsync(ct)
            : null;

        var queChefio = euId is { } eu
            ? setores.Where(d => d.ManagerId == eu).ToList()
            : [];

        // As opções de escopo: a empresa e as seções que a pessoa chefia. Quem não chefia
        // nada vê só a empresa, e o seletor nem aparece.
        //
        // Isto NÃO é uma barreira de visibilidade: quem chefia a Seção Técnica continua
        // vendo a empresa inteira, aqui e nas outras telas. O que muda é onde o painel
        // ABRE — que é o que importa no dia a dia. Esconder dado de outra seção seria
        // escopo de permissão, e permissão não se implementa num dashboard.
        var escopos = new List<TodayScope> { new(null, "A empresa inteira", false) };
        escopos.AddRange(queChefio.Select(d => new TodayScope(d.Id.Value, d.Name, true)));

        var padrao = queChefio.Count > 0 ? queChefio[0].Id.Value : (Guid?)null;
        var alvo = wholeCompany ? null : departmentId ?? padrao;

        var escolhido = alvo is { } id ? setores.FirstOrDefault(d => d.Id.Value == id) : null;
        if (alvo is not null && escolhido is null)
            alvo = null;   // seção inexistente vira a empresa, em vez de um painel vazio sem explicação

        var escopo = escolhido is null
            ? new TodayScope(null, "A empresa inteira", false)
            : new TodayScope(escolhido.Id.Value, escolhido.Name, escolhido.ManagerId == euId);

        // A subárvore, pelo caminho materializado: "Operações" inclui "Turno A" e "Turno B".
        // Um chefe que só visse o próprio nó perderia justamente quem está embaixo dele.
        var noEscopo = escolhido is null
            ? null
            : setores.Where(d => d.Path.StartsWith(escolhido.Path, StringComparison.Ordinal))
                .Select(d => d.Id)
                .ToHashSet();

        var pessoasNoEscopo = dbContext.Employees.AsNoTracking().Where(e => e.TerminatedOn == null);

        if (noEscopo is not null)
        {
            pessoasNoEscopo = pessoasNoEscopo
                .Where(e => e.DepartmentId != null && noEscopo.Contains(e.DepartmentId.Value));
        }

        var idsNoEscopo = noEscopo is null
            ? null
            : await pessoasNoEscopo.Select(e => e.Id).ToListAsync(ct);

        var efetivo = await pessoasNoEscopo.CountAsync(ct);

        // ── quem está fora, e quem volta ─────────────────────────────────────────
        var blocos = await dbContext.Occupancies.AsNoTracking()
            .Where(o => idsNoEscopo == null || idsNoEscopo.Contains(o.EmployeeId))
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
            // Missão sem setor é da casa toda: aparece para todo mundo, porque ela vai
            // consumir gente de qualquer seção.
            .Where(m => noEscopo == null || m.DepartmentId == null
                || (m.DepartmentId != null && noEscopo.Contains(m.DepartmentId.Value)))
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
            .Where(w => idsNoEscopo == null
                || (w.AssigneeId != null && idsNoEscopo.Contains(w.AssigneeId.Value))
                || (w.DepartmentId != null && noEscopo!.Contains(w.DepartmentId.Value)))
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
                .Where(r => idsNoEscopo == null || idsNoEscopo.Contains(r.EmployeeId))
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

        // O resumo por seção só faz sentido olhando a empresa: dentro de uma seção ele
        // repetiria, em uma linha, o que os blocos acima já dizem em detalhe.
        var resumoPorSecao = escolhido is null
            ? await PorSecaoAsync(setores, hoje, ct)
            : [];

        return new TodayPanel(
            hoje, efetivo, escopo, escopos, resumoPorSecao,
            fora, voltam, missoes, demandas, aprovacoes);
    }

    /// <summary>
    /// Uma linha por seção, para o painel de quem olha a empresa.
    ///
    /// Carrega tudo de uma vez e agrupa em memória: são dezenas de setores e algumas
    /// centenas de linhas numa empresa deste tamanho. Uma consulta por seção seria N+1
    /// numa tela que abre o dia inteiro.
    /// </summary>
    private async Task<IReadOnlyList<TodaySection>> PorSecaoAsync(
        IReadOnlyList<Setor> setores, DateOnly hoje, CancellationToken ct)
    {
        var amanha = hoje.AddDays(1);

        var pessoas = await dbContext.Employees.AsNoTracking()
            .Where(e => e.TerminatedOn == null && e.DepartmentId != null)
            .Select(e => new { e.Id, DepartmentId = e.DepartmentId!.Value })
            .ToListAsync(ct);

        var chefes = await dbContext.Employees.AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var foraHoje = (await dbContext.Occupancies.AsNoTracking()
                .Where(o => o.StartsOn <= hoje && o.EndsOn >= hoje)
                .Select(o => o.EmployeeId)
                .ToListAsync(ct))
            .ToHashSet();

        var missoesFaltando = await dbContext.Missions.AsNoTracking()
            .Where(m => m.On >= hoje && m.On <= amanha && m.Status != MissionStatus.Cancelada)
            .Where(m => m.Assignments.Count < m.RequiredPeople)
            .Select(m => m.DepartmentId)
            .ToListAsync(ct);

        var atrasadas = await dbContext.WorkItems.AsNoTracking()
            .Where(w => w.Status == WorkItemStatus.Aberta || w.Status == WorkItemStatus.EmAndamento)
            .Where(w => w.DueOn != null && w.DueOn < hoje)
            .Select(w => new { w.AssigneeId, w.DepartmentId })
            .ToListAsync(ct);

        var linhas = new List<TodaySection>();

        foreach (var setor in setores)
        {
            var abaixo = setores
                .Where(d => d.Path.StartsWith(setor.Path, StringComparison.Ordinal))
                .Select(d => d.Id)
                .ToHashSet();

            var doSetor = pessoas.Where(p => abaixo.Contains(p.DepartmentId)).Select(p => p.Id).ToHashSet();

            linhas.Add(new TodaySection(
                setor.Id.Value,
                setor.Name,
                setor.ManagerId is { } c ? chefes.GetValueOrDefault(c) : null,
                doSetor.Count,
                doSetor.Count(p => foraHoje.Contains(p)),
                missoesFaltando.Count(d => d is not null && abaixo.Contains(d.Value)),
                atrasadas.Count(w =>
                    (w.AssigneeId is { } a && doSetor.Contains(a))
                    || (w.DepartmentId is { } d && abaixo.Contains(d)))));
        }

        return linhas;
    }

    /// <summary>Projeção da árvore de setores. Record e não tipo anônimo porque atravessa método.</summary>
    private sealed record Setor(DepartmentId Id, string Name, string Path, EmployeeId? ManagerId);
}
