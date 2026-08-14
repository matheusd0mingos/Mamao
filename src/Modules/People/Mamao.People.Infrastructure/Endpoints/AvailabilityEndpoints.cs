using Mamao.People.Application.Availability;
using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// Disponibilidade e solicitações.
///
/// A rota que importa é <c>GET /availability</c>: é dela que sai "quem pode amanhã", e é
/// ela que a escala vai consumir quando chegar. As outras existem para alimentá-la.
/// </summary>
public static class AvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var disponibilidade = app.MapGroup("/api/v1/availability")
            .WithTags("Availability")
            .RequireAuthorization();

        disponibilidade.MapGet("/", async Task<IResult> (
            DateOnly? on,
            TimeOnly? from,
            TimeOnly? to,
            AvailabilityService service,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var dia = on ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return TypedResults.Ok(await service.OnAsync(dia, from, to, ct));
        })
        .WithName("listAvailability")
        .Produces<IReadOnlyList<AvailabilityResponse>>()
        .RequireAuthorization(Permissions.PeopleRead);

        disponibilidade.MapGet("/occupancies", async Task<IResult> (
            DateOnly from,
            DateOnly to,
            Guid? employeeId,
            AvailabilityService service,
            CancellationToken ct) =>
        {
            var lista = await service.ListAsync(
                from, to, employeeId is { } id ? new EmployeeId(id) : null, ct);

            return TypedResults.Ok(lista);
        })
        .WithName("listOccupancies")
        .Produces<IReadOnlyList<OccupancyResponse>>()
        .RequireAuthorization(Permissions.PeopleRead);

        disponibilidade.MapPost("/occupancies", async Task<IResult> (
            CreateOccupancyRequest request,
            AvailabilityService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("createOccupancy")
        .Produces<OccupancyResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        disponibilidade.MapDelete("/occupancies/{id:guid}", async Task<IResult> (
            Guid id,
            AvailabilityService service,
            CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(new OccupancyId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("deleteOccupancy")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        // ── solicitações ─────────────────────────────────────────────────────────
        var solicitacoes = app.MapGroup("/api/v1/absence-requests")
            .WithTags("AbsenceRequests")
            .RequireAuthorization();

        solicitacoes.MapGet("/", async Task<IResult> (
            AbsenceRequestStatus? status,
            Guid? employeeId,
            AbsenceRequestService service,
            CancellationToken ct) =>
        {
            var lista = await service.ListAsync(
                status, employeeId is { } id ? new EmployeeId(id) : null, ct);

            return TypedResults.Ok(lista);
        })
        .WithName("listAbsenceRequests")
        .Produces<IReadOnlyList<AbsenceRequestResponse>>()
        .RequireAuthorization(Permissions.PeopleRead);

        // Pedir exige `timeoff.request`, que todo funcionario tem. Decidir exige
        // `timeoff.approve`, que e da chefia — a separacao existe para que o proprio
        // solicitante nao aprove o que pediu.
        solicitacoes.MapPost("/", async Task<IResult> (
            CreateAbsenceRequest request,
            AbsenceRequestService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("createAbsenceRequest")
        .Produces<AbsenceRequestResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.TimeOffRequest);

        solicitacoes.MapPost("/{id:guid}/approve", async Task<IResult> (
            Guid id,
            DecideAbsenceRequest request,
            AbsenceRequestService service,
            CancellationToken ct) =>
        {
            var result = await service.ApproveAsync(new AbsenceRequestId(id), request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("approveAbsenceRequest")
        .Produces<AbsenceRequestResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.TimeOffApprove);

        solicitacoes.MapPost("/{id:guid}/reject", async Task<IResult> (
            Guid id,
            DecideAbsenceRequest request,
            AbsenceRequestService service,
            CancellationToken ct) =>
        {
            var result = await service.RejectAsync(new AbsenceRequestId(id), request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("rejectAbsenceRequest")
        .Produces<AbsenceRequestResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.TimeOffApprove);

        solicitacoes.MapPost("/{id:guid}/cancel", async Task<IResult> (
            Guid id,
            AbsenceRequestService service,
            CancellationToken ct) =>
        {
            var result = await service.CancelAsync(new AbsenceRequestId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("cancelAbsenceRequest")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.TimeOffRequest);

        return app;
    }
}
