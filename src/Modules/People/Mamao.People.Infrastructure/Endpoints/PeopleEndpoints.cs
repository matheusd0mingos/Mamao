using FluentValidation;
using Mamao.People.Application.Employees;
using Mamao.People.Application.Employees.Import;
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
            Guid? departmentId,
            EmployeeService service,
            CancellationToken ct) =>
        {
            var result = await service.ListAsync(
                search, includeInactive ?? false, page ?? 1, pageSize ?? 25,
                departmentId is { } d ? new DepartmentId(d) : null, ct);

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

        group.MapGet("/{id:guid}/contract", async Task<IResult> (
            Guid id, ContractService service, CancellationToken ct) =>
        {
            var contrato = await service.GetAsync(new EmployeeId(id), ct);
            return contrato is null
                ? HttpResultsExtensions.Problem(new Error("contract.not_found", "Contrato ainda não preenchido."))
                : TypedResults.Ok(contrato);
        })
        .WithName("getEmploymentContract")
        .Produces<ContractResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(Permissions.PeopleRead);

        group.MapPut("/{id:guid}/contract", async Task<IResult> (
            Guid id, SaveContractRequest request, ContractService service, CancellationToken ct) =>
        {
            var result = await service.SaveAsync(new EmployeeId(id), request, ct);
            return result.ToHttpResult(value => TypedResults.Ok(value));
        })
        .WithName("saveEmploymentContract")
        .Produces<ContractResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.PeopleWrite);

        MapImportEndpoints(group);

        return app;
    }

    /// <summary>
    /// Importacao em dois passos: /import/preview mostra o que vai acontecer, /import grava.
    /// Ver docs/produto/mvp-e-posicionamento.md#p3.
    /// </summary>
    private static void MapImportEndpoints(RouteGroupBuilder group)
    {
        // 5 MB cobre com folga uma planilha de milhares de linhas. O limite existe para
        // que um arquivo errado (um PDF, um ZIP) pare aqui e nao no parser.
        const long TamanhoMaximo = 5 * 1024 * 1024;

        group.MapGet("/import/format", () => TypedResults.Ok(EmployeeCsvParser.DescribeFormat()))
            .WithName("getImportFormat")
            .Produces<EmployeeImportFormat>()
            .RequireAuthorization(Permissions.PeopleRead);

        group.MapGet("/import/template", () =>
        {
            // Ponto e virgula e BOM: e assim que o Excel em portugues abre sem estragar
            // acento, e o cliente vai abrir no Excel.
            return TypedResults.File(
                EmployeeCsvParser.BuildTemplate(),
                contentType: "text/csv; charset=utf-8",
                fileDownloadName: "modelo-funcionarios-mamao.csv");
        })
        .WithName("downloadImportTemplate")
        .RequireAuthorization(Permissions.PeopleRead);

        group.MapPost("/import/preview", async Task<IResult> (
            IFormFile arquivo,
            EmployeeImportService service,
            CancellationToken ct) =>
        {
            var invalido = ValidarArquivo(arquivo, TamanhoMaximo);
            if (invalido is not null)
                return invalido;

            await using var conteudo = arquivo.OpenReadStream();
            return TypedResults.Ok(await service.PreviewAsync(conteudo, ct));
        })
        .WithName("previewEmployeeImport")
        .Produces<EmployeeImportPreview>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .DisableAntiforgery()
        .RequireAuthorization(Permissions.PeopleWrite);

        group.MapPost("/import", async Task<IResult> (
            IFormFile arquivo,
            EmployeeImportService service,
            CancellationToken ct) =>
        {
            var invalido = ValidarArquivo(arquivo, TamanhoMaximo);
            if (invalido is not null)
                return invalido;

            await using var conteudo = arquivo.OpenReadStream();
            return TypedResults.Ok(await service.ImportAsync(conteudo, ct));
        })
        .WithName("importEmployees")
        .Produces<EmployeeImportResult>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .DisableAntiforgery()
        .RequireAuthorization(Permissions.PeopleWrite);
    }

    private static IResult? ValidarArquivo(IFormFile arquivo, long tamanhoMaximo)
    {
        if (arquivo.Length == 0)
            return HttpResultsExtensions.Problem(new Error("import.empty_file", "O arquivo está vazio."));

        if (arquivo.Length > tamanhoMaximo)
        {
            return HttpResultsExtensions.Problem(new Error(
                "import.file_too_large",
                $"Arquivo acima de {tamanhoMaximo / 1024 / 1024} MB. Divida a planilha e importe em partes."));
        }

        var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();

        // XLSX ganha mensagem propria: mandar um .xlsx e o engano mais provavel, e
        // "arquivo invalido" nao diria o que fazer.
        if (extensao is ".xlsx" or ".xls")
        {
            return HttpResultsExtensions.Problem(new Error(
                "import.excel_not_supported",
                "Por enquanto aceitamos CSV. No Excel: Arquivo → Salvar como → CSV."));
        }

        if (extensao is not (".csv" or ".txt" or ""))
        {
            return HttpResultsExtensions.Problem(new Error(
                "import.unsupported_format", $"Formato {extensao} não suportado. Envie um arquivo CSV."));
        }

        return null;
    }
}
