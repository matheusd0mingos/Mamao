using Mamao.People.Contracts;
using Mamao.People.Domain.Documents;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Documents;

public sealed record UploadDocumentRequest(
    Guid EmployeeId,
    string Kind,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string? Notes);

public sealed record UpdateDocumentRequest(DateOnly? IssuedOn, DateOnly? ExpiresOn, string? Notes);

/// <param name="DaysToExpiry">Negativo quando já venceu. Nulo quando não há validade.</param>
public sealed record DocumentResponse(
    DocumentId Id,
    EmployeeId EmployeeId,
    string EmployeeName,
    string Kind,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    DocumentStatus Status,
    int? DaysToExpiry,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Notes,
    DateTimeOffset UploadedAt,
    string UploadedByName);

public sealed record DocumentFile(Stream Content, string FileName, string ContentType);

/// <summary>
/// Documentos com validade.
///
/// É o objeto que gera valor sem exigir disciplina: o documento vence sozinho e o sistema
/// avisa. Todo o resto do produto depende de alguém registrar alguma coisa; aqui basta
/// subir o arquivo uma vez.
///
/// O arquivo vai para o disco e a linha para o banco, na ordem certa — <b>arquivo primeiro</b>.
/// Se a gravação do arquivo falhar, não fica linha órfã apontando para o nada; se o banco
/// falhar depois, sobra um arquivo sem dono, que é barato e invisível.
/// </summary>
public sealed class DocumentService(
    IPeopleDbContext dbContext,
    IDocumentStorage storage,
    ITenantContext tenantContext,
    IAuditLog audit,
    ICurrentActor actor,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<DocumentResponse>> ListAsync(
        Guid? employeeId, bool somenteVencendo, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var limite = hoje.AddDays(Document.JanelaDeAvisoEmDias);

        var consulta = dbContext.Documents.AsNoTracking();

        if (employeeId is { } pessoa)
            consulta = consulta.Where(d => d.EmployeeId == new EmployeeId(pessoa));

        if (somenteVencendo)
            consulta = consulta.Where(d => d.ExpiresOn != null && d.ExpiresOn <= limite);

        var itens = await consulta
            // Sem validade por último: o que vence é o que a tela existe para mostrar.
            .OrderBy(d => d.ExpiresOn == null)
            .ThenBy(d => d.ExpiresOn)
            .ThenBy(d => d.Kind)
            .Take(300)
            .ToListAsync(ct);

        var nomes = await NomesAsync(itens, ct);

        return [.. itens.Select(d => ToResponse(d, hoje, nomes))];
    }

    public async Task<Result<DocumentResponse>> UploadAsync(
        UploadDocumentRequest request,
        Stream content,
        string? fileName,
        string? contentType,
        long sizeBytes,
        CancellationToken ct)
    {
        var pessoa = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == new EmployeeId(request.EmployeeId), ct);

        if (pessoa is null)
        {
            return Result.Failure<DocumentResponse>(new Error(
                "document.employee_not_found", "Funcionário não encontrado.", "employeeId"));
        }

        var criacao = Document.Create(
            pessoa.Id, request.Kind, fileName, contentType, sizeBytes,
            timeProvider.GetUtcNow(), actor.Name,
            request.IssuedOn, request.ExpiresOn, request.Notes);

        if (criacao.IsFailure)
            return Result.Failure<DocumentResponse>(criacao.Error!);

        var documento = criacao.Value;

        await storage.SaveAsync(tenantContext.Current, documento.StoragePath, content, ct);

        try
        {
            dbContext.Documents.Add(documento);

            // O tipo vai para a auditoria; a observação NÃO. Documento de saúde é o caso
            // mais comum de anexo com validade, e a auditoria é lida por mais gente.
            audit.Record(
                AuditActions.DocumentUploaded, nameof(Document), documento.Id.ToString(),
                $"{documento.Kind} de {pessoa.FullName}",
                new { documento.Kind, documento.ExpiresOn, documento.SizeBytes });

            await dbContext.SaveChangesAsync(ct);
        }
        catch
        {
            // Banco falhou depois do disco: tira o arquivo em vez de deixar lixo eterno.
            await storage.DeleteAsync(tenantContext.Current, documento.StoragePath, CancellationToken.None);
            throw;
        }

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Result.Success(ToResponse(documento, hoje, new() { [pessoa.Id] = pessoa.FullName }));
    }

    public async Task<Result<DocumentResponse>> UpdateAsync(
        DocumentId id, UpdateDocumentRequest request, CancellationToken ct)
    {
        var documento = await dbContext.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
            return Result.Failure<DocumentResponse>(new Error("document.not_found", "Documento não encontrado."));

        var alteracao = documento.Update(request.IssuedOn, request.ExpiresOn, request.Notes);
        if (alteracao.IsFailure)
            return Result.Failure<DocumentResponse>(alteracao.Error!);

        await dbContext.SaveChangesAsync(ct);

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Result.Success(ToResponse(documento, hoje, await NomesAsync([documento], ct)));
    }

    /// <summary>Abre o arquivo. Nulo quando a linha existe e o arquivo não — restore parcial acontece.</summary>
    public async Task<Result<DocumentFile>> DownloadAsync(DocumentId id, CancellationToken ct)
    {
        var documento = await dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
            return Result.Failure<DocumentFile>(new Error("document.not_found", "Documento não encontrado."));

        var conteudo = await storage.OpenAsync(tenantContext.Current, documento.StoragePath, ct);

        if (conteudo is null)
        {
            return Result.Failure<DocumentFile>(new Error(
                "document.file_missing",
                "O arquivo deste documento não está no servidor. Envie de novo."));
        }

        // Baixar documento É um acesso a dado sensível, e a política de privacidade promete
        // que todo acesso ao arquivo é auditado. Aqui é onde essa promessa vira verdade.
        audit.Record(
            AuditActions.DocumentDownloaded, nameof(Document), id.ToString(), documento.Kind);

        await dbContext.SaveChangesAsync(ct);

        return Result.Success(new DocumentFile(conteudo, documento.FileName, documento.ContentType));
    }

    public async Task<Result> DeleteAsync(DocumentId id, CancellationToken ct)
    {
        var documento = await dbContext.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (documento is null)
            return Result.Failure("document.not_found", "Documento não encontrado.");

        dbContext.Documents.Remove(documento);

        audit.Record(AuditActions.DocumentDeleted, nameof(Document), id.ToString(), documento.Kind);
        await dbContext.SaveChangesAsync(ct);

        // O arquivo sai DEPOIS do banco: se o banco falhar, o arquivo continua lá e a linha
        // também — consistente. Na ordem inversa, uma falha deixaria a linha apontando para
        // um arquivo que já não existe.
        await storage.DeleteAsync(tenantContext.Current, documento.StoragePath, CancellationToken.None);

        return Result.Success();
    }

    private async Task<Dictionary<EmployeeId, string>> NomesAsync(
        IReadOnlyCollection<Document> documentos, CancellationToken ct)
    {
        var ids = documentos.Select(d => d.EmployeeId).Distinct().ToList();

        return ids.Count == 0
            ? []
            : await dbContext.Employees.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
    }

    private static DocumentResponse ToResponse(
        Document d, DateOnly hoje, Dictionary<EmployeeId, string> nomes) => new(
        d.Id,
        d.EmployeeId,
        nomes.GetValueOrDefault(d.EmployeeId, "—"),
        d.Kind,
        d.IssuedOn,
        d.ExpiresOn,
        d.StatusEm(hoje),
        d.ExpiresOn is { } v ? v.DayNumber - hoje.DayNumber : null,
        d.FileName,
        d.ContentType,
        d.SizeBytes,
        d.Notes,
        d.UploadedAt,
        d.UploadedByName);
}
