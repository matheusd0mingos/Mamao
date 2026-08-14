using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Employees;

public sealed record CreateRestrictionRequest(
    Guid EmployeeId,
    string? ActivityName,
    string? Reason,
    DateOnly StartsOn,
    DateOnly? EndsOn);

public sealed record RestrictionResponse(
    RestrictionId Id,
    EmployeeId EmployeeId,
    string EmployeeName,
    string? ActivityName,
    string? Reason,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    bool Vigente,
    string Label);

/// <summary>
/// Quem não entra em certa atividade.
///
/// Fica no módulo People e não em Missions porque a restrição é da PESSOA — ela vale para
/// qualquer escala futura, inclusive as que ainda não existem.
/// </summary>
public sealed class RestrictionService(
    IPeopleDbContext dbContext,
    IAuditLog audit,
    ICurrentActor actor,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RestrictionResponse>> ListAsync(Guid? employeeId, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var consulta = dbContext.EmployeeRestrictions.AsNoTracking();

        if (employeeId is { } pessoa)
            consulta = consulta.Where(r => r.EmployeeId == new EmployeeId(pessoa));

        var itens = await consulta
            .OrderByDescending(r => r.StartsOn)
            .Take(300)
            .ToListAsync(ct);

        var ids = itens.Select(r => r.EmployeeId).Distinct().ToList();

        var nomes = ids.Count == 0
            ? []
            : await dbContext.Employees.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        return
        [
            .. itens.Select(r => new RestrictionResponse(
                r.Id,
                r.EmployeeId,
                nomes.GetValueOrDefault(r.EmployeeId, "—"),
                r.ActivityName,
                r.Reason,
                r.StartsOn,
                r.EndsOn,
                r.StartsOn <= hoje && (r.EndsOn is null || r.EndsOn >= hoje),
                r.Rotulo())),
        ];
    }

    public async Task<Result<RestrictionResponse>> CreateAsync(
        CreateRestrictionRequest request, CancellationToken ct)
    {
        var pessoa = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == new EmployeeId(request.EmployeeId), ct);

        if (pessoa is null)
        {
            return Result.Failure<RestrictionResponse>(new Error(
                "restriction.employee_not_found", "Funcionário não encontrado.", "employeeId"));
        }

        var criacao = EmployeeRestriction.Create(
            pessoa.Id,
            request.StartsOn,
            timeProvider.GetUtcNow(),
            actor.Name,
            request.ActivityName,
            request.Reason,
            request.EndsOn);

        if (criacao.IsFailure)
            return Result.Failure<RestrictionResponse>(criacao.Error!);

        dbContext.EmployeeRestrictions.Add(criacao.Value);

        // O motivo NÃO vai para a auditoria: pode ser dado de saúde, e a auditoria é lida
        // por mais gente do que a restrição. Registrar quem restringiu quem, e de quê,
        // basta para prestar contas.
        audit.Record(
            AuditActions.RestrictionCreated, nameof(EmployeeRestriction),
            criacao.Value.Id.ToString(), pessoa.FullName,
            new { Atividade = criacao.Value.ActivityName ?? "(toda escala)", request.StartsOn, request.EndsOn });

        await dbContext.SaveChangesAsync(ct);

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return Result.Success(new RestrictionResponse(
            criacao.Value.Id, pessoa.Id, pessoa.FullName,
            criacao.Value.ActivityName, criacao.Value.Reason,
            criacao.Value.StartsOn, criacao.Value.EndsOn,
            criacao.Value.StartsOn <= hoje && (criacao.Value.EndsOn is null || criacao.Value.EndsOn >= hoje),
            criacao.Value.Rotulo()));
    }

    public async Task<Result> DeleteAsync(RestrictionId id, CancellationToken ct)
    {
        var restricao = await dbContext.EmployeeRestrictions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (restricao is null)
            return Result.Failure("restriction.not_found", "Restrição não encontrada.");

        dbContext.EmployeeRestrictions.Remove(restricao);

        audit.Record(
            AuditActions.RestrictionRemoved, nameof(EmployeeRestriction),
            id.ToString(), restricao.ActivityName ?? "(toda escala)");

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }
}
