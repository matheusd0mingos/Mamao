using Mamao.People.Application.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mamao.People.Infrastructure.Storage;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "Documents";

    /// <summary>
    /// Raiz no disco. Em produção é um volume do Docker; em desenvolvimento, uma pasta local.
    ///
    /// Fica dentro do volume `uploads` que a API e o Worker já compartilham — não um volume
    /// novo. Um volume a mais é mais uma coisa para lembrar de incluir no backup.
    /// </summary>
    public string RootPath { get; set; } = "/var/mamao/uploads/documentos";
}

/// <summary>
/// Arquivo no disco, uma pasta por empresa.
///
/// Não é o banco de propósito: blob em Postgres incha o dump, e o dump é o backup —
/// guardar anos de PDF ali transformaria a cópia diária num arquivo que não cabe na janela
/// da madrugada. O banco guarda os metadados; o disco guarda o arquivo.
///
/// <b>Consequência que precisa estar escrita:</b> o backup do banco NÃO leva os arquivos.
/// Restaurar só o dump devolve a lista de documentos com os arquivos faltando. Por isso o
/// backup copia a pasta também — ver deploy/backup.sh.
/// </summary>
public sealed class FileSystemDocumentStorage(
    IOptions<DocumentStorageOptions> options,
    ILogger<FileSystemDocumentStorage> logger) : IDocumentStorage
{
    private readonly string _raiz = Path.GetFullPath(options.Value.RootPath);

    public async Task SaveAsync(Guid tenantId, string storagePath, Stream content, CancellationToken ct)
    {
        var destino = Resolver(tenantId, storagePath);

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        // FileMode.CreateNew: se o arquivo já existe, alguém reusou um id — melhor estourar
        // do que sobrescrever documento de outra pessoa em silêncio.
        await using var arquivo = new FileStream(
            destino, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        await content.CopyToAsync(arquivo, ct);
    }

    public Task<Stream?> OpenAsync(Guid tenantId, string storagePath, CancellationToken ct)
    {
        var origem = Resolver(tenantId, storagePath);

        if (!File.Exists(origem))
        {
            // Acontece de verdade: restaurar o banco sem restaurar a pasta. A tela precisa
            // dizer "arquivo não encontrado" em vez de estourar.
            logger.LogWarning("Documento {Caminho} está no banco mas não no disco.", storagePath);
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(
            origem, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task DeleteAsync(Guid tenantId, string storagePath, CancellationToken ct)
    {
        var alvo = Resolver(tenantId, storagePath);

        if (File.Exists(alvo))
            File.Delete(alvo);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Monta o caminho absoluto e CONFERE que ele ficou dentro da raiz.
    ///
    /// O caminho já vem do agregado, então em tese não há entrada de usuário aqui. A
    /// conferência existe porque "em tese" é o que sempre falha: basta um dia alguém aceitar
    /// o caminho de um parâmetro para "../.." virar leitura de qualquer arquivo do servidor.
    /// </summary>
    private string Resolver(Guid tenantId, string storagePath)
    {
        var completo = Path.GetFullPath(Path.Combine(_raiz, tenantId.ToString("N"), storagePath));

        if (!completo.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Caminho de documento fora da raiz: {storagePath}");

        return completo;
    }
}
