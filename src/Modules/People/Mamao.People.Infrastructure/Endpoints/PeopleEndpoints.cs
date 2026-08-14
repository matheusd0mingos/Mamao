using FluentValidation;
using Mamao.People.Application.Employees;
using Mamao.People.Contracts;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// Minimal APIs agrupadas por recurso. Verbo de negocio vira sub-recurso quando o CRUD
/// nao expressa a intencao (ex.: /terminate em vez de PATCH de status) — o que permite
/// auditar e emitir o evento certo. Ver docs/arquitetura/visao-geral.md.
///
/// O tipo de retorno das lambdas e explicito (async Task&lt;IResult&gt;) porque a
/// validacao devolve um resultado opcional; sem isso a inferencia vira IResult? e o
/// endpoint aceitaria null.
/// </summary>
public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/employees")
            .WithTags("Employees")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
            string? search,
            bool? includeInactive,
            int? page,
            int? pageSize,
            EmployeeService service,
            CancellationToken ct) =>
        {
            var result = await service.ListAsync(search, includeInactive ?? false, page ?? 1, pageSize ?? 25, ct);
            return TypedResults.Ok(result);
        })
        .WithName("listEmployees")
        .Produces<PagedResult<EmployeeListItem>>()
        .RequireAuthorization(Permissions.PeopleRead);

        group.MapGet("/{id:guid}", async Task<IResult> (
            Guid id, EmployeeService service, CancellationToken ct) =>
        {
            var employee = await service.GetAsync(new EmployeeId(id), ct);
            return employee is null
                ? HttpResultsExtensions.Problem(new Error("employee.not_found", "Funcionario nao encontrado."))
                : TypedResults.Ok(employee);
        })
        .WithName("getEmployee")
        .Produces<EmployeeResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(Permissions.PeopleRead);

        group.MapPost("/", async Task<IResult> (
            CreateEmployeeRequest request,
            IValidator<CreateEmployeeRequest> validator,
            EmployeeService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.CreateAsync(request, ct);
            return result.ToHttpResult(created =>
                TypedResults.Created($"/api/v1/employees/{created.Id}", created));
        })
        .WithName("createEmployee")
        .Produces<EmployeeResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id,
            UpdateEmployeeRequest request,
            IValidator<UpdateEmployeeRequest> validator,
            EmployeeService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.UpdateAsync(new EmployeeId(id), request, ct);
            return result.ToHttpResult(value => TypedResults.Ok(value));
        })
        .WithName("updateEmployee")
        .Produces<EmployeeResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapPost("/{id:guid}/terminate", async Task<IResult> (
            Guid id,
            TerminateEmployeeRequest request,
            IValidator<TerminateEmployeeRequest> validator,
            EmployeeService service,
            CancellationToken ct) =>
        {
            var invalid = await validator.ValidateToProblemAsync(request, ct);
            if (invalid is not null)
                return invalid;

            var result = await service.TerminateAsync(new EmployeeId(id), request, ct);
            return result.ToHttpResult(value => TypedResults.Ok(value));
        })
        .WithName("terminateEmployee")
        .Produces<EmployeeResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        return app;
    }
}
