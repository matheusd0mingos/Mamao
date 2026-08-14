using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Mamao.Notifications.Email;

/// <summary>
/// Envio por SMTP com MailKit. O <c>System.Net.Mail.SmtpClient</c> da BCL nao e recomendado
/// pela propria Microsoft para codigo novo; MailKit e MIT e lida com STARTTLS e
/// autenticacao moderna sem surpresa.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mensagem.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.To));
        mensagem.Subject = message.Subject;

        if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
            mensagem.ReplyTo.Add(MailboxAddress.Parse(_options.ReplyTo));

        mensagem.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        }.ToMessageBody();

        using var cliente = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };

        // A porta manda mais que a configuracao. 465 e TLS implicito: o servidor comeca a
        // falar TLS no primeiro byte e nunca anuncia STARTTLS. Pedir STARTTLS ali faz o
        // cliente esperar um anuncio que nunca vem, ate estourar o timeout — e o sintoma
        // ("o e-mail nao chegou") nao aponta para a porta em momento algum.
        // Como UseStartTls e true por padrao, quem so trocasse a porta para 465 cairia
        // exatamente nesse buraco.
        const int PortaTlsImplicito = 465;

        var seguranca = _options switch
        {
            { UseSsl: true } => SecureSocketOptions.SslOnConnect,
            { Port: PortaTlsImplicito } => SecureSocketOptions.SslOnConnect,
            { UseStartTls: true } => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.Auto,
        };

        await cliente.ConnectAsync(_options.Host, _options.Port, seguranca, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
            await cliente.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);

        await cliente.SendAsync(mensagem, cancellationToken);
        await cliente.DisconnectAsync(quit: true, cancellationToken);

        // Assunto e destinatario no log; corpo nunca — ele carrega token de recuperacao.
        logger.LogInformation("E-mail enviado para {Destinatario}: {Assunto}", message.To, message.Subject);
    }
}
