namespace Mamao.Notifications.Email;

/// <summary>
/// Envio de e-mail transacional. Uma interface porque existem duas implementacoes reais —
/// SMTP em producao e log em desenvolvimento — nao por precaucao.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <param name="To">Destinatario.</param>
/// <param name="Subject">Assunto, ja em pt-BR.</param>
/// <param name="HtmlBody">Corpo em HTML.</param>
/// <param name="TextBody">
/// Alternativa em texto puro. Obrigatoria: mensagem so-HTML pontua pior em filtro de spam,
/// e e-mail transacional que cai no spam e o mesmo que e-mail nao enviado.
/// </param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody)
{
    public string? ToName { get; init; }
}
