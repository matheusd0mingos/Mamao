using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Availability;

public sealed record CreateAbsenceRequest(
    EmployeeId EmployeeId,
    OccupancyKind Kind,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string? Reason);

public sealed record DecideAbsenceRequest(string? Note);

/// <param name="Conflicts">Quem mais da mesma seção já está fora no período. Informa, não bloqueia.</param>
public sealed record AbsenceRequestResponse(
    AbsenceRequestId Id,
    EmployeeId EmployeeId,
    string EmployeeName,
    OccupancyKind Kind,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    int Days,
    string? Reason,
    AbsenceRequestStatus Status,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    IReadOnlyList<string> Conflicts);

/// <summary>
/// Propor e aprovar. Um caminho só para férias, folga, dispensa e justificativa de falta.
///
/// A aprovação é o que escreve na ocupação, na MESMA transação: aprovar sem bloquear a
/// agenda deixaria a escala escalando quem está de férias — e o erro só apareceria no dia.
/// </summary>
public sealed class AbsenceRequestService(
    IPeopleDbContext dbContext,
    IAuditLog audit,
    ICurrentActor actor,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AbsenceRequestResponse>> ListAsync(
        AbsenceRequestStatus? status, EmployeeId? employeeId, CancellationToken ct)
    {
        var consulta = dbContext.AbsenceRequests.AsNoTracking();

        if (status is { } s)
            consulta = consulta.Where(r => r.Status == s);

        if (employeeId is { } id)
            consulta = consulta.Where(r => r.EmployeeId == id);

        var pedidos = await consulta
            .OrderByDescending(r => r.StartsOn)
            .Join(dbContext.Employees, r => r.EmployeeId, e => e.Id, (r, e) => new { Pedido = r, e.FullName })
            .Take(200)
            .ToListAsync(ct);

        return [.. pedidos.Select(p => ToResponse(p.Pedido, p.FullName, []))];
    }

    public async Task<Result<AbsenceRequestResponse>> CreateAsync(CreateAbsenceRequest request, CancellationToken ct)
    {
        var funcionario = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);

        if (funcionario is null)
            return Result.Failure<AbsenceRequestResponse>(new Error("employee.not_found", "Funcionário não encontrado."));

        var criacao = AbsenceRequest.Create(
            request.EmployeeId, request.Kind, request.StartsOn, request.EndsOn,
            timeProvider.GetUtcNow(), request.StartsAt, request.EndsAt, request.Reason);

        if (criacao.IsFailure)
            return Result.Failure<AbsenceRequestResponse>(criacao.Error!);

        var pedido = criacao.Value;
        dbContext.AbsenceRequests.Add(pedido);
        await dbContext.SaveChangesAsync(ct);

        return Result.Success(ToResponse(pedido, funcionario.FullName, await ConflitosAsync(pedido, ct)));
    }

    /// <summary>
    /// Aprova e bloqueia a agenda no mesmo SaveChanges. Se a gravação da ocupação falhar,
    /// a aprovação cai junto — nunca fica "aprovada" sem o bloqueio correspondente.
    /// </summary>
    public async Task<Result<AbsenceRequestResponse>> ApproveAsync(
        AbsenceRequestId id, DecideAbsenceRequest request, CancellationToken ct)
    {
        var pedido = await dbContext.AbsenceRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (pedido is null)
            return Result.Failure<AbsenceRequestResponse>(new Error("absence_request.not_found", "Solicitação não encontrada."));

        var funcionario = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == pedido.EmployeeId, ct);

        var aprovacao = pedido.Approve(actor.UserId, actor.Name, timeProvider.GetUtcNow());
        if (aprovacao.IsFailure)
            return Result.Failure<AbsenceRequestResponse>(aprovacao.Error!);

        dbContext.Occupancies.Add(aprovacao.Value);

        // Quem aprovou o afastamento de quem, e por quanto tempo. É a pergunta que aparece
        // meses depois, quando falta gente na escala e ninguém lembra da decisão.
        audit.Record(
            AuditActions.AbsenceApproved, nameof(AbsenceRequest), id.ToString(),
            funcionario?.FullName ?? pedido.EmployeeId.ToString(),
            new { pedido.Kind, pedido.StartsOn, pedido.EndsOn, pedido.Dias });

        await dbContext.SaveChangesAsync(ct);

        return Result.Success(ToResponse(pedido, funcionario?.FullName ?? string.Empty, []));
    }

    public async Task<Result<AbsenceRequestResponse>> RejectAsync(
        AbsenceRequestId id, DecideAbsenceRequest request, CancellationToken ct)
    {
        var pedido = await dbContext.AbsenceRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (pedido is null)
            return Result.Failure<AbsenceRequestResponse>(new Error("absence_request.not_found", "Solicitação não encontrada."));

        var recusa = pedido.Reject(actor.UserId, actor.Name, timeProvider.GetUtcNow(), request.Note);
        if (recusa.IsFailure)
            return Result.Failure<AbsenceRequestResponse>(recusa.Error!);

        var funcionario = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == pedido.EmployeeId, ct);

        audit.Record(
            AuditActions.AbsenceRejected, nameof(AbsenceRequest), id.ToString(),
            funcionario?.FullName ?? pedido.EmployeeId.ToString(),
            new { pedido.Kind, pedido.StartsOn, pedido.EndsOn, request.Note });

        await dbContext.SaveChangesAsync(ct);

        return Result.Success(ToResponse(pedido, funcionario?.FullName ?? string.Empty, []));
    }

    /// <summary>Desistência de quem pediu, antes da decisão.</summary>
    public async Task<Result> CancelAsync(AbsenceRequestId id, CancellationToken ct)
    {
        var pedido = await dbContext.AbsenceRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (pedido is null)
            return Result.Failure("absence_request.not_found", "Solicitação não encontrada.");

        var cancelamento = pedido.Cancel(timeProvider.GetUtcNow());
        if (cancelamento.IsFailure)
            return cancelamento;

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Quem mais do MESMO SETOR já está fora no período.
    ///
    /// Não bloqueia: a decisão é de quem aprova, e uma regra rígida aqui erraria na semana
    /// em que a chefia sabe de algo que o sistema não sabe. Mas aprovar sem essa informação
    /// é como o gestor faz hoje — de memória, descobrindo o buraco no dia.
    /// </summary>
    private async Task<IReadOnlyList<string>> ConflitosAsync(AbsenceRequest pedido, CancellationToken ct)
    {
        var setor = await dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == pedido.EmployeeId)
            .Select(e => e.DepartmentId)
            .FirstOrDefaultAsync(ct);

        if (setor is null)
            return [];

        return await dbContext.Occupancies.AsNoTracking()
            .Where(o => o.StartsOn <= pedido.EndsOn && pedido.StartsOn <= o.EndsOn)
            .Join(dbContext.Employees.Where(e => e.DepartmentId == setor && e.Id != pedido.EmployeeId),
                o => o.EmployeeId, e => e.Id, (o, e) => e.FullName)
            .Distinct()
            .Take(10)
            .ToListAsync(ct);
    }

    private static AbsenceRequestResponse ToResponse(
        AbsenceRequest r, string nome, IReadOnlyList<string> conflitos) => new(
        r.Id, r.EmployeeId, nome, r.Kind, r.StartsOn, r.EndsOn, r.StartsAt, r.EndsAt,
        r.Dias, r.Reason, r.Status, r.DecidedByName, r.DecidedAt, r.DecisionNote, conflitos);
}
