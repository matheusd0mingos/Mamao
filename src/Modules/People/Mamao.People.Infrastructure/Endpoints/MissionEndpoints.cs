using Mamao.People.Application.Missions;
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

        return app;
    }
}
