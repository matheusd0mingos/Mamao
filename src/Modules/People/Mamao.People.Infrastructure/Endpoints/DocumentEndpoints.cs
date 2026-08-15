using Mamao.People.Application.Documents;
using Mamao.People.Contracts;
using Mamao.People.Domain.Documents;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// Documentos com validade. O upload e multipart porque arquivo em JSON (base64) cresce um
/// terco e obriga a carregar tudo na memoria dos dois lados.
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/documents")
            .WithTags("Documents")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
            Guid? employeeId, bool? expiring, DocumentService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(employeeId, expiring ?? false, ct)))
        .WithName("listDocuments")
        .Produces<IReadOnlyList<DocumentResponse>>()
        .RequireAuthorization(Permissions.DocumentsRead);

        group.MapPost("/", async Task<IResult> (
            HttpRequest request, DocumentService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return HttpResultsExtensions.Problem(new Error("document.not_multipart", "Envie o arquivo como formulário."));

            var form = await request.ReadFormAsync(ct);
            var arquivo = form.Files.GetFile("file");

            if (arquivo is null || arquivo.Length == 0)
                return HttpResultsExtensions.Problem(new Error("document.file_required", "Envie o arquivo.", "file"));

            if (!Guid.TryParse(form["employeeId"], out var employeeId))
                return HttpResultsExtensions.Problem(new Error("document.employee_required", "Informe o funcionário.", "employeeId"));

            var pedido = new UploadDocumentRequest(
                employeeId,
                form["kind"].ToString(),
                Data(form["issuedOn"]),
                Data(form["expiresOn"]),
                form["notes"].ToString());

            await using var conteudo = arquivo.OpenReadStream();

            var result = await service.UploadAsync(
                pedido, conteudo, arquivo.FileName, arquivo.ContentType, arquivo.Length, ct);

            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("uploadDocument")
        .Produces<DocumentResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .DisableAntiforgery()
        .RequireAuthorization(Permissions.DocumentsUpload);

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id, UpdateDocumentRequest request, DocumentService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(new DocumentId(id), request, ct);
            return result.ToHttpResult(TypedResults.Ok);
        })
        .WithName("updateDocument")
        .Produces<DocumentResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.DocumentsUpload);

        group.MapGet("/{id:guid}/file", async Task<IResult> (
            Guid id, DocumentService service, CancellationToken ct) =>
        {
            var result = await service.DownloadAsync(new DocumentId(id), ct);

            if (result.IsFailure)
                return result.ToHttpResult();

            var arquivo = result.Value;

            // Attachment e nao inline: PDF e imagem abertos no navegador dentro da origem da
            // aplicacao sao um vetor conhecido. Documento e para baixar.
            return TypedResults.File(arquivo.Content, arquivo.ContentType, arquivo.FileName);
        })
        .WithName("downloadDocument")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.DocumentsRead);

        group.MapDelete("/{id:guid}", async Task<IResult> (
            Guid id, DocumentService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(new DocumentId(id), ct);
            return result.ToHttpResult();
        })
        .WithName("deleteDocument")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.DocumentsApprove);

        return app;
    }

    private static DateOnly? Data(string? valor) =>
        DateOnly.TryParse(valor, out var data) ? data : null;
}
