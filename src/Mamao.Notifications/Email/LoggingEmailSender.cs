using Microsoft.Extensions.Logging;

namespace Mamao.Notifications.Email;

/// <summary>
/// Remetente de desenvolvimento: escreve o e-mail no log em vez de enviar.
///
/// Existe para que o fluxo de recuperacao de senha seja testavel sem servidor SMTP — e por
/// isso ele imprime o corpo em texto puro, incluindo o link com o token. Aceitavel em
/// desenvolvimento, inaceitavel em producao: por isso a aplicacao recusa subir em Production
/// sem SMTP configurado. Ver NotificationsModule.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            """
            ── E-MAIL NAO ENVIADO (SMTP nao configurado) ─────────────────────────
            Para:    {Destinatario}
            Assunto: {Assunto}

            {Corpo}
            ──────────────────────────────────────────────────────────────────────
            """,
            message.To, message.Subject, message.TextBody);

        return Task.CompletedTask;
    }
}
