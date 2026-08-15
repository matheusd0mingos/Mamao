namespace Mamao.People.Application.Documents;

/// <summary>
/// Onde o arquivo mora.
///
/// Interface e não acesso direto ao disco porque este é o ponto que mais provavelmente
/// muda: hoje é o volume do servidor, amanhã é um bucket. O resto do módulo não deveria
/// saber a diferença.
///
/// <b>O caminho vem SEMPRE do agregado</b> (<c>Document.StoragePath</c>), nunca do cliente
/// — a implementação confere isso de novo, porque uma barra a mais num caminho vira leitura
/// de arquivo fora da pasta.
/// </summary>
public interface IDocumentStorage
{
    Task SaveAsync(Guid tenantId, string storagePath, Stream content, CancellationToken ct);

    /// <summary>Nulo quando o arquivo não existe — a linha pode sobreviver ao arquivo num restore parcial.</summary>
    Task<Stream?> OpenAsync(Guid tenantId, string storagePath, CancellationToken ct);

    Task DeleteAsync(Guid tenantId, string storagePath, CancellationToken ct);
}
