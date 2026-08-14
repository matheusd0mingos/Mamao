using FluentValidation;
using Mamao.People.Application.Organization;
using Mamao.People.Contracts;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// Setores e cargos. Ficam sob /api/v1 e nao sob /employees porque sao recursos proprios —
/// aninhar sugeriria que so existem dentro de um funcionario, e o Marco 4 vai perguntar
/// "quantos vigilantes cobrem este turno?" sem passar por pessoa nenhuma.
/// </summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        MapDepartments(app);
        MapPositions(app);
        return app;
    }

    private static void MapDepartments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/departments")
            .WithTags("Organization")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (DepartmentService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)))
        .WithName("listDepartments")
        .Produces<IReadOnlyList<DepartmentNode>>()
        .RequireAuthorization(Permissions.PeopleRead);

        group.MapPost("/", async Task<IResult> (
            CreateDepartmentRequest request,
            IValidator<CreateDepartmentRequest> validator,
            DepartmentService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(created => TypedResults.Created($"/api/v1/departments/{created.Id}", created));
        })
        .WithName("createDepartment")
        .Produces<DepartmentNode>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id,
            UpdateDepartmentRequest request,
            IValidator<UpdateDepartmentRequest> validator,
            DepartmentService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.UpdateAsync(new DepartmentId(id), request, ct);
            return result.ToHttpResult();
        })
        .WithName("updateDepartment")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        // Mover e sub-recurso proprio, e nao um campo do PUT: reescrever o caminho de toda
        // a subarvore e uma operacao com regra e custo diferentes de renomear.
        group.MapPost("/{id:guid}/move", async Task<IResult> (
            Guid id, MoveDepartmentRequest request, DepartmentService service, CancellationToken ct) =>
        {
            var result = await service.MoveAsync(new DepartmentId(id), request, ct);
            return result.ToHttpResult();
        })
        .WithName("moveDepartment")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapDelete("/{id:guid}", async Task<IResult> (
            Guid id, DepartmentService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(new DepartmentId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("deleteDepartment")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);
    }

    private static void MapPositions(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/positions")
            .WithTags("Organization")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (PositionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)))
        .WithName("listPositions")
        .Produces<IReadOnlyList<PositionResponse>>()
        .RequireAuthorization(Permissions.PeopleRead);

        group.MapPost("/", async Task<IResult> (
            CreatePositionRequest request,
            IValidator<CreatePositionRequest> validator,
            PositionService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(created => TypedResults.Created($"/api/v1/positions/{created.Id}", created));
        })
        .WithName("createPosition")
        .Produces<PositionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id,
            UpdatePositionRequest request,
            IValidator<UpdatePositionRequest> validator,
            PositionService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.RenameAsync(new PositionId(id), request, ct);
            return result.ToHttpResult(value => TypedResults.Ok(value));
        })
        .WithName("updatePosition")
        .Produces<PositionResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapDelete("/{id:guid}", async Task<IResult> (
            Guid id, PositionService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(new PositionId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("deletePosition")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);
    }
}
