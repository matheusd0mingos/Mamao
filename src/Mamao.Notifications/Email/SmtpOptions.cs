using System.ComponentModel.DataAnnotations;

namespace Mamao.Notifications.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>
    /// Vazio liga o remetente de desenvolvimento, que escreve o e-mail no log em vez de
    /// enviar. E o que permite testar o fluxo de recuperacao de senha sem servidor de
    /// e-mail — e o que impede um deploy de producao mal configurado passar despercebido,
    /// porque a aplicacao recusa subir em Production sem SMTP.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    /// <summary>587 com STARTTLS e o padrao. Use 465 com <c>UseSsl</c> se o provedor exigir.</summary>
    public bool UseStartTls { get; set; } = true;

    public bool UseSsl { get; set; }

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    [EmailAddress]
    public string FromAddress { get; set; } = "nao-responda@mamao.tech";

    public string FromName { get; set; } = "Mamão";

    /// <summary>
    /// Para onde vai a resposta quando o usuario responder mesmo assim — e ele responde.
    /// Opcional: sem [EmailAddress] porque o atributo reprova string vazia, e vazio aqui
    /// significa "nao definido". A validacao de formato vive no NotificationsModule.
    /// </summary>
    public string? ReplyTo { get; set; }

    public int TimeoutSeconds { get; set; } = 20;

    public bool Configurado => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// Endereco publico da aplicacao, usado para montar os links dos e-mails. Sem isso o
/// link de recuperacao de senha aponta para lugar nenhum.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string PublicOrigin { get; set; } = "http://localhost:4200";
    public string ProductName { get; set; } = "Mamão";
}
