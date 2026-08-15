using Mamao.People.Contracts;
using Mamao.People.Domain.Organization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Documents;

/// <summary>
/// Situação do documento em relação à validade. Derivada, nunca gravada: campo de status
/// depende de alguém lembrar de atualizar, e essa pessoa esquece na primeira semana — o
/// mesmo motivo pelo qual disponibilidade também é calculada.
/// </summary>
public enum DocumentStatus
{
    /// <summary>Sem data de validade. Vale até alguém dizer o contrário.</summary>
    SemValidade,

    Valido,

    /// <summary>Dentro da janela de aviso. É o estado que o produto existe para mostrar.</summary>
    Vencendo,

    Vencido,
}

/// <summary>
/// Um documento de uma pessoa, com validade.
///
/// É o único dos cinco objetos que gera valor sem exigir disciplina de ninguém: o
/// documento vence sozinho, e o sistema avisa. Os outros dependem de alguém registrar —
/// aqui basta subir o arquivo uma vez.
///
/// <b>O arquivo não fica no banco.</b> Blob em Postgres incha o dump, e o dump é o backup:
/// guardar dez anos de PDF ali transformaria a cópia diária num arquivo que não cabe na
/// janela da madrugada. O caminho aponta para o volume; a linha guarda os metadados.
/// </summary>
public sealed class Document : ITenantOwned
{
    public const int MaxTipo = 120;
    public const int MaxNome = 260;
    public const int MaxObservacao = 500;

    /// <summary>25 MB. Documento de RH é PDF ou foto de papel; acima disso é engano.</summary>
    public const long MaxBytes = 25 * 1024 * 1024;

    /// <summary>Quantos dias antes o documento entra em "vencendo".</summary>
    public const int JanelaDeAvisoEmDias = 30;

    private Document() { }

    public DocumentId Id { get; private set; }
    public Guid TenantId { get; set; }
    public EmployeeId EmployeeId { get; private set; }

    /// <summary>Ex.: "ASO", "CNH", "Carteira de vacinação". Texto livre — cada casa tem os seus.</summary>
    public string Kind { get; private set; } = null!;

    /// <summary>Tipo dobrado, para agrupar "ASO" e "aso" e para a busca.</summary>
    public string NormalizedKind { get; private set; } = null!;

    public DateOnly? IssuedOn { get; private set; }

    /// <summary>Até quando vale. Nulo = documento sem validade (diploma, certidão).</summary>
    public DateOnly? ExpiresOn { get; private set; }

    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    /// <summary>Caminho relativo dentro do volume. Nunca vem do cliente — é gerado aqui.</summary>
    public string StoragePath { get; private set; } = null!;

    public string? Notes { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }
    public string UploadedByName { get; private set; } = null!;

    /// <summary>
    /// Até quando já avisamos sobre este documento.
    ///
    /// Guardado para o aviso não repetir todo dia. Sem isto, o primeiro documento a vencer
    /// treina todo mundo a ignorar o e-mail — e aí o aviso que importa também é ignorado.
    /// </summary>
    public DateOnly? NotifiedFor { get; private set; }

    public static Result<Document> Create(
        EmployeeId employeeId,
        string? kind,
        string? fileName,
        string? contentType,
        long sizeBytes,
        DateTimeOffset now,
        string uploadedByName,
        DateOnly? issuedOn = null,
        DateOnly? expiresOn = null,
        string? notes = null)
    {
        kind = kind?.Trim() ?? string.Empty;

        if (kind.Length < 2)
            return Erro("document.kind_required", "Informe o tipo do documento.", nameof(kind));

        if (kind.Length > MaxTipo)
            return Erro("document.kind_too_long", $"O tipo passa de {MaxTipo} caracteres.", nameof(kind));

        fileName = SanitizarNome(fileName);

        if (fileName.Length == 0)
            return Erro("document.file_required", "Envie o arquivo.", nameof(fileName));

        if (sizeBytes <= 0)
            return Erro("document.file_empty", "O arquivo está vazio.", nameof(fileName));

        if (sizeBytes > MaxBytes)
        {
            return Erro(
                "document.file_too_large",
                $"O arquivo passa de {MaxBytes / (1024 * 1024)} MB.",
                nameof(fileName));
        }

        if (issuedOn is { } emissao && expiresOn is { } validade && validade < emissao)
            return Erro("document.expiry_before_issue", "A validade não pode ser anterior à emissão.", nameof(expiresOn));

        if (notes is { Length: > MaxObservacao })
            return Erro("document.notes_too_long", $"A observação passa de {MaxObservacao} caracteres.", nameof(notes));

        var id = DocumentId.New();

        return Result.Success(new Document
        {
            Id = id,
            EmployeeId = employeeId,
            Kind = kind,
            NormalizedKind = Position.Normalize(kind),
            IssuedOn = issuedOn,
            ExpiresOn = expiresOn,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            SizeBytes = sizeBytes,

            // O caminho é gerado, NUNCA derivado do nome enviado: "../../etc/passwd" como
            // nome de arquivo é o ataque mais velho que existe contra upload.
            StoragePath = $"{employeeId.Value:N}/{id.Value:N}",
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            UploadedAt = now,
            UploadedByName = string.IsNullOrWhiteSpace(uploadedByName) ? "—" : uploadedByName.Trim(),
        });
    }

    /// <summary>Situação hoje. Calculada, nunca gravada.</summary>
    public DocumentStatus StatusEm(DateOnly hoje)
    {
        if (ExpiresOn is not { } validade)
            return DocumentStatus.SemValidade;

        if (validade < hoje)
            return DocumentStatus.Vencido;

        return validade.DayNumber - hoje.DayNumber <= JanelaDeAvisoEmDias
            ? DocumentStatus.Vencendo
            : DocumentStatus.Valido;
    }

    /// <summary>Precisa de aviso hoje? Só uma vez por validade — trocar o documento reabre o aviso.</summary>
    public bool PrecisaAvisar(DateOnly hoje) =>
        ExpiresOn is not null
        && StatusEm(hoje) is DocumentStatus.Vencendo or DocumentStatus.Vencido
        && NotifiedFor != ExpiresOn;

    public void MarcarAvisado() => NotifiedFor = ExpiresOn;

    public Result Update(DateOnly? issuedOn, DateOnly? expiresOn, string? notes)
    {
        if (issuedOn is { } emissao && expiresOn is { } validade && validade < emissao)
            return Result.Failure("document.expiry_before_issue", "A validade não pode ser anterior à emissão.", nameof(expiresOn));

        if (notes is { Length: > MaxObservacao })
            return Result.Failure("document.notes_too_long", $"A observação passa de {MaxObservacao} caracteres.", nameof(notes));

        // Mudou a validade? O aviso volta a valer — senão renovar o documento deixaria o
        // sistema calado sobre a validade nova.
        if (expiresOn != ExpiresOn)
            NotifiedFor = null;

        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        return Result.Success();
    }

    /// <summary>
    /// Fica só o nome do arquivo, sem caminho e sem os caracteres que atrapalham no
    /// download. O nome enviado é do usuário e serve para ELE reconhecer o arquivo — não
    /// para o sistema achar nada.
    /// </summary>
    private static string SanitizarNome(string? nome)
    {
        nome = (nome ?? string.Empty).Replace('\\', '/');

        var barra = nome.LastIndexOf('/');
        if (barra >= 0)
            nome = nome[(barra + 1)..];

        nome = nome.Trim();

        foreach (var proibido in Path.GetInvalidFileNameChars())
            nome = nome.Replace(proibido, '_');

        return nome.Length > MaxNome ? nome[^MaxNome..] : nome;
    }

    private static Result<Document> Erro(string code, string message, string field) =>
        Result.Failure<Document>(new Error(code, message, field));
}
