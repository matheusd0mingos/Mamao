using Mamao.People.Domain.Organization;
using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
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

public sealed record BlockedPerson(EmployeeId EmployeeId, string Name, OccupancyKind Reason);

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
                DepartmentName = dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

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

        // ── 2. histórico dos que sobraram ────────────────────────────────────────
        var desde = missao.On.AddDays(-RotationRanking.JanelaPadraoEmDias);
        var chave = Position.Normalize(missao.Name);

        // Conta participações em missões com o MESMO NOME. Rodízio de formatura não é o
        // mesmo de inspeção, e misturar os dois faria a sugestão parecer arbitrária.
        // O custo é conhecido: renomear a missão começa um rodízio novo. Um catálogo de
        // tipos resolve isso melhor, e é para onde isto caminha quando doer.
        var historico = await dbContext.MissionAssignments.AsNoTracking()
            .Join(dbContext.Missions.Where(m =>
                    m.Status == MissionStatus.Confirmada && m.On >= desde && m.On <= missao.On),
                a => a.MissionId, m => m.Id, (a, m) => new { a.EmployeeId, m.On, m.Name })
            .ToListAsync(ct);

        var doMesmoTipo = historico
            .Where(h => Position.Normalize(h.Name) == chave)
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(g => g.Key, g => (Vezes: g.Count(), Ultima: g.Max(x => x.On)));

        // ── 3. ordena ────────────────────────────────────────────────────────────
        var elegiveis = pessoas.Where(p => !impedidos.ContainsKey(p.Id)).ToList();

        var ranking = RotationRanking.Rank(
            elegiveis.Select(p =>
            {
                var h = doMesmoTipo.TryGetValue(p.Id, out var v) ? v : (Vezes: 0, Ultima: (DateOnly?)null);
                return new RotationCandidate(p.Id, p.FullName, h.Vezes, h.Ultima);
            }),
            missao.On);

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
            [.. pessoas.Where(p => impedidos.ContainsKey(p.Id))
                .Select(p => new BlockedPerson(p.Id, p.FullName, impedidos[p.Id]))
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

    private static MissionResponse ToResponse(Mission m) => new(
        m.Id, m.Name, m.On, m.StartsAt, m.EndsAt, m.RequiredPeople, m.AssignedCount,
        m.Status, m.Notes, [.. m.Assignments.Select(a => a.EmployeeId)]);
}
