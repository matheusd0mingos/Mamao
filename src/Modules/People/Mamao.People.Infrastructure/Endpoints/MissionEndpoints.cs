using Mamao.People.Application.Employees;
using Mamao.People.Application.Missions;
using Mamao.People.Domain.Missions;
using Mamao.People.Contracts;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// Missoes e montagem de escala. A rota que sustenta o produto e a de sugestao:
/// GET /missions/{id}/suggestion.
/// </summary>
public static class MissionEndpoints
{
    public static IEndpointRouteBuilder MapMissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/missions")
            .WithTags("Missions")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
            DateOnly? from, MissionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(from, ct)))
        .WithName("listMissions")
        .Produces<IReadOnlyList<MissionResponse>>()
        .RequireAuthorization(Permissions.ScheduleRead);

        group.MapPost("/", async Task<IResult> (
            CreateMissionRequest request, MissionService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("createMission")
        .Produces<MissionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.ScheduleWrite);

        group.MapGet("/{id:guid}/suggestion", async Task<IResult> (
            Guid id, MissionService service, CancellationToken ct) =>
        {
            var result = await service.SuggestAsync(new MissionId(id), ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("suggestMission")
        .Produces<MissionSuggestion>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.ScheduleRead);

        group.MapPut("/{id:guid}/assignments", async Task<IResult> (
            Guid id, AssignMissionRequest request, MissionService service, CancellationToken ct) =>
        {
            var result = await service.AssignAsync(new MissionId(id), request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("assignMission")
        .Produces<MissionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.ScheduleWrite);

        group.MapPost("/{id:guid}/confirm", async Task<IResult> (
            Guid id, MissionService service, CancellationToken ct) =>
        {
            var result = await service.ConfirmAsync(new MissionId(id), ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("confirmMission")
        .Produces<MissionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.ScheduleWrite);

        group.MapPost("/{id:guid}/cancel", async Task<IResult> (
            Guid id, MissionService service, CancellationToken ct) =>
        {
            var result = await service.CancelAsync(new MissionId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("cancelMission")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.ScheduleWrite);

        // ── politica de rodizio ──────────────────────────────────────────────────
        // Rota propria e nao um campo de "configuracoes": a politica e o que explica a
        // ordem da sugestao, e quem monta escala precisa achar isso perto da escala.
        group.MapGet("/policy", async Task<IResult> (MissionService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.PoliticaAsync(ct))))
        .WithName("rotationPolicy")
        .Produces<RotationPolicyResponse>()
        .RequireAuthorization(Permissions.ScheduleRead);

        group.MapPut("/policy", async Task<IResult> (
            RotationPolicyRequest request, MissionService service, CancellationToken ct) =>
        {
            var result = await service.SalvarPoliticaAsync(
                request.Tiebreak, request.MinRestDays, request.RestBlocks, request.WindowDays, ct);

            return result.IsSuccess
                ? TypedResults.Ok(ToResponse(result.Value))
                : result.ToHttpResult();
        })
        .WithName("saveRotationPolicy")
        .Produces<RotationPolicyResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.SettingsWrite);

        // ── restricoes ───────────────────────────────────────────────────────────
        var restricoes = app.MapGroup("/api/v1/restrictions")
            .WithTags("Restrictions")
            .RequireAuthorization();

        restricoes.MapGet("/", async Task<IResult> (
            Guid? employeeId, RestrictionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(employeeId, ct)))
        .WithName("listRestrictions")
        .Produces<IReadOnlyList<RestrictionResponse>>()
        .RequireAuthorization(Permissions.PeopleRead);

        restricoes.MapPost("/", async Task<IResult> (
            CreateRestrictionRequest request, RestrictionService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("createRestriction")
        .Produces<RestrictionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        restricoes.MapDelete("/{id:guid}", async Task<IResult> (
            Guid id, RestrictionService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(new RestrictionId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("deleteRestriction")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        return app;
    }

    private static RotationPolicyResponse ToResponse(RotationPolicy p) =>
        new(p.Tiebreak, p.MinRestDays, p.RestBlocks, p.WindowDays);
}

public sealed record RotationPolicyRequest(
    RotationTiebreak Tiebreak,
    int MinRestDays,
    bool RestBlocks,
    int WindowDays);

public sealed record RotationPolicyResponse(
    RotationTiebreak Tiebreak,
    int MinRestDays,
    bool RestBlocks,
    int WindowDays);
