using Mamao.People.Application.Work;
using Mamao.People.Contracts;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// Demandas e o quadro. A rota que importa e GET /work: ela ja devolve o alerta de cada
/// cartao, para a tela nao ter que perguntar disponibilidade de novo.
/// </summary>
public static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/work")
            .WithTags("Work")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
            Guid? departmentId, Guid? assigneeId, bool? includeDone,
            WorkItemService service, CancellationToken ct) =>
            TypedResults.Ok(await service.BoardAsync(departmentId, assigneeId, includeDone ?? false, ct)))
        .WithName("workBoard")
        .Produces<WorkBoard>()
        .RequireAuthorization(Permissions.WorkRead);

        group.MapPost("/", async Task<IResult> (
            CreateWorkItemRequest request, WorkItemService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("createWorkItem")
        .Produces<WorkItemResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.WorkAssign);

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id, UpdateWorkItemRequest request, WorkItemService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(new WorkItemId(id), request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("updateWorkItem")
        .Produces<WorkItemResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.WorkAssign);

        // Rota propria para o gesto mais frequente do quadro. Arrastar um cartao nao pode
        // exigir reenviar titulo, prazo e responsavel — um PUT parcial de tela concorrente
        // sobrescreveria o que o outro acabou de mudar.
        group.MapPost("/{id:guid}/move", async Task<IResult> (
            Guid id, MoveWorkItemRequest request, WorkItemService service, CancellationToken ct) =>
        {
            var result = await service.MoveAsync(new WorkItemId(id), request.Status, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("moveWorkItem")
        .Produces<WorkItemResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.WorkAssign);

        group.MapDelete("/{id:guid}", async Task<IResult> (
            Guid id, WorkItemService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(new WorkItemId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("deleteWorkItem")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.WorkAssign);

        return app;
    }
}
