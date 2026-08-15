using Mamao.People.Domain.Organization;
using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Employees;
using Mamao.People.Domain.Missions;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Missions;

public sealed record CreateMissionRequest(
    string Name,
    DateOnly On,
    int RequiredPeople,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    Guid? DepartmentId,
    string? Notes);

public sealed record AssignMissionRequest(IReadOnlyList<Guid> EmployeeIds);

public sealed record MissionResponse(
    MissionId Id,
    string Name,
    DateOnly On,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    int RequiredPeople,
    int AssignedCount,
    MissionStatus Status,
    string? Notes,
    IReadOnlyList<EmployeeId> AssignedIds);

/// <param name="Eligible">Pode ir. Vem na ordem do rodízio: o primeiro é quem mais merece a vez.</param>
/// <param name="Blocked">Não pode, com o motivo. Aparece na tela para o gestor não procurar.</param>
public sealed record MissionSuggestion(
    MissionId MissionId,
    int RequiredPeople,
    int TotalConsidered,
    IReadOnlyList<SuggestedPerson> Eligible,
    IReadOnlyList<BlockedPerson> Blocked);

/// <param name="Suggested">Entraria na sugestão automática (está entre os primeiros).</param>
public sealed record SuggestedPerson(
    EmployeeId EmployeeId,
    string Name,
    string? DepartmentName,
    int Participations,
    DateOnly? LastParticipation,
    string Reason,
    bool Suggested);

/// <param name="Reason">Motivo de ausencia, quando o impedimento vem da agenda.</param>
/// <param name="Restriction">
/// Texto da restricao, quando o impedimento vem dela. Os dois nunca vem juntos: basta um
/// para a pessoa nao entrar, e mostrar dois motivos sugeriria que resolver um resolveria.
/// </param>
public sealed record BlockedPerson(
    EmployeeId EmployeeId,
    string Name,
    OccupancyKind? Reason,
    string? Restriction = null);

/// <summary>
/// Missões e a montagem da escala.
///
/// A rota que importa é a sugestão: ela junta disponibilidade e histórico e devolve a
/// ordem do rodízio COM o motivo de cada posição. O gestor ajusta e confirma — o sistema
/// sugere, nunca decide. Ver docs/adr/0019-escala-por-rodizio.md.
/// </summary>
public sealed class MissionService(IPeopleDbContext dbContext, IAuditLog audit, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MissionResponse>> ListAsync(DateOnly? from, CancellationToken ct)
    {
        var inicio = from ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var missoes = await dbContext.Missions
            .AsNoTracking()
            .Include(m => m.Assignments)
            .Where(m => m.On >= inicio && m.Status != MissionStatus.Cancelada)
            .OrderBy(m => m.On)
            .Take(100)
            .ToListAsync(ct);

        return [.. missoes.Select(ToResponse)];
    }

    public async Task<Result<MissionResponse>> CreateAsync(CreateMissionRequest request, CancellationToken ct)
    {
        var criacao = Mission.Create(
            request.Name, request.On, request.RequiredPeople, timeProvider.GetUtcNow(),
            request.StartsAt, request.EndsAt,
            request.DepartmentId is { } d ? new DepartmentId(d) : null,
            request.Notes);

        if (criacao.IsFailure)
            return Result.Failure<MissionResponse>(criacao.Error!);

        dbContext.Missions.Add(criacao.Value);
        await dbContext.SaveChangesAsync(ct);

        return Result.Success(ToResponse(criacao.Value));
    }

    /// <summary>
    /// Quem pode ir, na ordem do rodízio.
    ///
    /// Três passos, nesta ordem: tira quem não pode (disponibilidade), conta o histórico
    /// de quem sobrou, ordena. Contar o histórico de quem já está fora seria trabalho
    /// jogado fora — e é o passo mais caro.
    /// </summary>
    public async Task<Result<MissionSuggestion>> SuggestAsync(MissionId id, CancellationToken ct)
    {
        var missao = await dbContext.Missions.AsNoTracking()
            .Include(m => m.Assignments)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (missao is null)
            return Result.Failure<MissionSuggestion>(new Error("mission.not_found", "Missão não encontrada."));

        var candidatos = dbContext.Employees.AsNoTracking().Where(e => e.TerminatedOn == null);

        if (missao.DepartmentId is { } setor)
            candidatos = candidatos.Where(e => e.DepartmentId == setor);

        var pessoas = await candidatos
            .Select(e => new
            {
                e.Id,
                e.FullName,
                e.HiredOn,
                PrecedenceOrder = dbContext.Positions.Where(p => p.Id == e.PositionId)
                    .Select(p => p.PrecedenceOrder).FirstOrDefault(),
                DepartmentName = dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var politica = await PoliticaAsync(ct);
        var chave = Position.Normalize(missao.Name);

        // ── 1. quem não pode ─────────────────────────────────────────────────────
        // A ocupação gerada por ESTA missão não conta: senão, pedir a sugestão de novo
        // depois de confirmar excluiria justamente quem já está escalado nela.
        var blocos = await dbContext.Occupancies.AsNoTracking()
            .Where(o => o.StartsOn <= missao.On && missao.On <= o.EndsOn)
            .Where(o => o.Source != OccupancySource.Missao || o.SourceId != missao.Id.Value)
            .ToListAsync(ct);

        var impedidos = blocos
            .Where(o => o.Cobre(missao.On, missao.StartsAt, missao.EndsAt))
            .GroupBy(o => o.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().Kind);

        // Restrição é impedimento permanente, e por isso vem separada da agenda: quem não
        // faz serviço armado não passa a fazer porque o dia está livre.
        var restricoes = await dbContext.EmployeeRestrictions.AsNoTracking()
            .Where(r => r.StartsOn <= missao.On && (r.EndsOn == null || r.EndsOn >= missao.On))
            .Where(r => r.ActivityKey == null || r.ActivityKey == chave)
            .ToListAsync(ct);

        var restritos = restricoes
            .Where(r => r.Impede(chave, missao.On))
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().Rotulo());

        // ── 2. histórico dos que sobraram ────────────────────────────────────────
        var desde = missao.On.AddDays(-politica.WindowDays);

        // Conta participações em missões com o MESMO NOME. Rodízio de formatura não é o
        // mesmo de inspeção, e misturar os dois faria a sugestão parecer arbitrária.
        // O custo é conhecido: renomear a missão começa um rodízio novo. Um catálogo de
        // tipos resolve isso melhor, e é para onde isto caminha quando doer.
        // O filtro por TIPO vai ao banco, pela coluna dobrada. Antes carregava o historico
        // inteiro da janela para descartar a maior parte em memoria — barato com uma
        // secao, caro no dia em que a empresa tiver um ano de escala.
        var historico = await dbContext.MissionAssignments.AsNoTracking()
            .Join(dbContext.Missions.Where(m =>
                    m.Status == MissionStatus.Confirmada
                    && m.NormalizedName == chave
                    && m.On >= desde && m.On <= missao.On),
                a => a.MissionId, m => m.Id, (a, m) => new { a.EmployeeId, m.On })
            .ToListAsync(ct);

        var doMesmoTipo = historico
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(g => g.Key, g => (Vezes: g.Count(), Ultima: g.Max(x => x.On)));

        // Fim do último serviço ou missão de QUALQUER tipo, para o descanso mínimo. Só
        // busca quando a política pede — é uma consulta a mais que a maioria não precisa.
        var ultimoServico = politica.MinRestDays > 0
            ? await UltimoServicoAsync(missao.On, politica.MinRestDays, ct)
            : [];

        // ── 3. ordena ────────────────────────────────────────────────────────────
        var elegiveis = pessoas
            .Where(p => !impedidos.ContainsKey(p.Id) && !restritos.ContainsKey(p.Id))
            .ToList();

        var paraOrdenar = elegiveis.Select(p =>
        {
            var h = doMesmoTipo.TryGetValue(p.Id, out var v) ? v : (Vezes: 0, Ultima: (DateOnly?)null);
            return new RotationCandidate(
                p.Id, p.FullName, h.Vezes, h.Ultima,
                p.PrecedenceOrder, p.HiredOn,
                ultimoServico.TryGetValue(p.Id, out var fim) ? fim : null);
        });

        // "Impedir" tira de circulação; "evitar" só empurra para o fim da fila. A política
        // da empresa decide, porque as duas casas existem e as duas têm razão.
        //
        // Quem é impedido vai para a lista de bloqueados, NÃO some: uma pessoa que
        // desaparece da tela sem motivo é a forma mais rápida de o gestor deixar de
        // confiar na sugestão — e o funil deixaria de fechar a conta.
        var descansando = new Dictionary<EmployeeId, string>();

        if (politica.RestBlocks && politica.MinRestDays > 0)
        {
            var todos = paraOrdenar.ToList();

            foreach (var c in todos.Where(c =>
                RotationRanking.Descansando(c, missao.On, politica.MinRestDays)))
            {
                var dias = missao.On.DayNumber - c.LastDutyEnd!.Value.DayNumber;
                var quando = dias switch
                {
                    <= 0 => "sai de serviço hoje",
                    1 => "saiu de serviço ontem",
                    _ => $"saiu de serviço há {dias} dias",
                };

                descansando[c.EmployeeId] = $"descanso de {politica.MinRestDays} dias · {quando}";
            }

            paraOrdenar = todos.Where(c => !descansando.ContainsKey(c.EmployeeId));
        }

        var ranking = RotationRanking.Rank(paraOrdenar, missao.On, politica, missao.Id.Value);

        var setorPorPessoa = pessoas.ToDictionary(p => p.Id, p => p.DepartmentName);

        return Result.Success(new MissionSuggestion(
            missao.Id,
            missao.RequiredPeople,
            pessoas.Count,
            [.. ranking.Select(r => new SuggestedPerson(
                r.Candidate.EmployeeId,
                r.Candidate.Name,
                setorPorPessoa.GetValueOrDefault(r.Candidate.EmployeeId),
                r.Candidate.Participations,
                r.Candidate.LastParticipation,
                r.Reason,
                r.Position <= missao.RequiredPeople))],
            [.. pessoas
                .Where(p => impedidos.ContainsKey(p.Id)
                    || restritos.ContainsKey(p.Id)
                    || descansando.ContainsKey(p.Id))
                .Select(p => impedidos.TryGetValue(p.Id, out var motivo)
                    ? new BlockedPerson(p.Id, p.FullName, motivo)
                    : new BlockedPerson(
                        p.Id, p.FullName, null,
                        restritos.TryGetValue(p.Id, out var restricao) ? restricao : descansando[p.Id]))
                .OrderBy(b => b.Name)]));
    }

    public async Task<Result<MissionResponse>> AssignAsync(
        MissionId id, AssignMissionRequest request, CancellationToken ct)
    {
        var missao = await dbContext.Missions.Include(m => m.Assignments)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (missao is null)
            return Result.Failure<MissionResponse>(new Error("mission.not_found", "Missão não encontrada."));

        var resultado = missao.Assign(request.EmployeeIds.Select(e => new EmployeeId(e)));
        if (resultado.IsFailure)
            return Result.Failure<MissionResponse>(resultado.Error!);

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(missao));
    }

    /// <summary>Confirma a escala e bloqueia a agenda de quem foi escalado, na mesma transação.</summary>
    public async Task<Result<MissionResponse>> ConfirmAsync(MissionId id, CancellationToken ct)
    {
        var missao = await dbContext.Missions.Include(m => m.Assignments)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (missao is null)
            return Result.Failure<MissionResponse>(new Error("mission.not_found", "Missão não encontrada."));

        var confirmacao = missao.Confirm(timeProvider.GetUtcNow());
        if (confirmacao.IsFailure)
            return Result.Failure<MissionResponse>(confirmacao.Error!);

        dbContext.Occupancies.AddRange(confirmacao.Value);

        audit.Record(
            AuditActions.MissionConfirmed, nameof(Mission), id.ToString(), missao.Name,
            new { missao.On, missao.RequiredPeople, Escalados = missao.AssignedCount });

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(missao));
    }

    /// <summary>Cancela e libera a agenda de quem estava escalado.</summary>
    public async Task<Result> CancelAsync(MissionId id, CancellationToken ct)
    {
        var missao = await dbContext.Missions.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (missao is null)
            return Result.Failure("mission.not_found", "Missão não encontrada.");

        var cancelamento = missao.Cancel();
        if (cancelamento.IsFailure)
            return cancelamento;

        // Sem isto, as pessoas continuariam indisponíveis para uma missão que não existe
        // mais — e a próxima escala nasceria com gente faltando sem explicação.
        var bloqueios = await dbContext.Occupancies
            .Where(o => o.Source == OccupancySource.Missao && o.SourceId == id.Value)
            .ToListAsync(ct);

        dbContext.Occupancies.RemoveRange(bloqueios);

        audit.Record(AuditActions.MissionCancelled, nameof(Mission), id.ToString(), missao.Name);

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// A política da empresa, criada na primeira vez que alguém pede uma sugestão.
    ///
    /// Criada aqui e não no cadastro da empresa porque o cadastro não sabe que a política
    /// existe — e uma empresa que nunca montou escala não precisa de linha nenhuma.
    /// </summary>
    public async Task<RotationPolicy> PoliticaAsync(CancellationToken ct)
    {
        var politica = await dbContext.RotationPolicies.FirstOrDefaultAsync(ct);
        if (politica is not null)
            return politica;

        politica = RotationPolicy.Padrao();
        dbContext.RotationPolicies.Add(politica);
        await dbContext.SaveChangesAsync(ct);

        return politica;
    }

    public async Task<Result<RotationPolicy>> SalvarPoliticaAsync(
        RotationTiebreak tiebreak, int minRestDays, bool restBlocks, int windowDays, CancellationToken ct)
    {
        var politica = await PoliticaAsync(ct);

        var alteracao = politica.Update(tiebreak, minRestDays, restBlocks, windowDays);
        if (alteracao.IsFailure)
            return Result.Failure<RotationPolicy>(alteracao.Error!);

        audit.Record(
            AuditActions.RotationPolicyChanged, nameof(RotationPolicy), politica.Id.ToString(),
            "Política de rodízio",
            new { Desempate = tiebreak.ToString(), minRestDays, restBlocks, windowDays });

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(politica);
    }

    /// <summary>
    /// Quando cada pessoa saiu do último serviço ou missão antes da data de referência.
    ///
    /// Só olha para trás o suficiente para o descanso: quem saiu de serviço há dois meses
    /// não descansa mais, e carregar esse histórico seria varrer a tabela à toa.
    /// </summary>
    private async Task<Dictionary<EmployeeId, DateOnly>> UltimoServicoAsync(
        DateOnly referencia, int descansoMinimo, CancellationToken ct)
    {
        var desde = referencia.AddDays(-descansoMinimo - 1);

        var blocos = await dbContext.Occupancies.AsNoTracking()
            .Where(o => o.Kind == OccupancyKind.Servico || o.Kind == OccupancyKind.Missao)
            .Where(o => o.EndsOn >= desde && o.EndsOn < referencia)
            .Select(o => new { o.EmployeeId, o.EndsOn })
            .ToListAsync(ct);

        return blocos
            .GroupBy(o => o.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.EndsOn));
    }

    private static MissionResponse ToResponse(Mission m) => new(
        m.Id, m.Name, m.On, m.StartsAt, m.EndsAt, m.RequiredPeople, m.AssignedCount,
        m.Status, m.Notes, [.. m.Assignments.Select(a => a.EmployeeId)]);
}
