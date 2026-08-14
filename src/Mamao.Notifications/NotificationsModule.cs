using Mamao.Notifications.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mamao.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotifications(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations()
            // Cogitamos recusar o boot em Production sem SMTP — afinal, sem e-mail nao ha
            // recuperacao de senha. Mas derrubar o produto inteiro por causa do mailer e
            // pior que degradar so a recuperacao: um erro de digitacao no host tiraria a
            // operacao do cliente do ar. Fica um log CRITICAL no startup, que o deploy
            // mostra e o alerta pega. Ver LogEmailConfiguration.
            .Validate(
                o => string.IsNullOrWhiteSpace(o.ReplyTo) || o.ReplyTo.Contains('@'),
                "Smtp:ReplyTo, quando informado, precisa ser um e-mail.")
            .ValidateOnStart();

        var smtp = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();

        if (smtp.Configurado)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        services.AddSingleton<EmailTemplates>();

        return services;
    }

    /// <summary>
    /// Deixa registrado no startup como o e-mail vai se comportar. Sem isto, "o e-mail nao
    /// chegou" vira investigacao; com isto, e a primeira linha do log.
    /// </summary>
    public static void LogEmailConfiguration(this IServiceProvider services)
    {
        var smtp = services.GetRequiredService<IOptions<SmtpOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Mamao.Notifications");

        if (smtp.Configurado)
        {
            // Modo de TLS e "vai autenticar?" entram no log porque foram exatamente as duas
            // coisas invisiveis quando um envio falhou em producao: a porta 465 exige TLS
            // implicito, e usuario vazio faz o cliente nem tentar autenticar — provedor
            // nenhum aceita retransmitir assim. A senha, claro, nunca aparece.
            var modo = (smtp.UseSsl, smtp.Port, smtp.UseStartTls) switch
            {
                (true, _, _) or (_, 465, _) => "TLS implicito (SSL)",
                (_, _, true) => "STARTTLS",
                _ => "automatico",
            };

            logger.LogInformation(
                "E-mail por SMTP em {Host}:{Porta} · {Modo} · autenticacao {Autenticacao} · remetente {Remetente}.",
                smtp.Host, smtp.Port, modo,
                string.IsNullOrWhiteSpace(smtp.Username) ? "DESLIGADA (usuario vazio)" : $"como {smtp.Username}",
                smtp.FromAddress);

            if (string.IsNullOrWhiteSpace(smtp.Username))
            {
                logger.LogWarning(
                    "Smtp:Username vazio: nenhuma autenticacao sera feita. Quase todo provedor " +
                    "recusa retransmitir sem login, e a falha so aparece na hora de enviar.");
            }
        }
        else if (services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            logger.LogWarning(
                "SMTP nao configurado: os e-mails serao ESCRITOS NO LOG, nao enviados. " +
                "Normal em desenvolvimento — e assim que se testa a recuperacao de senha sem mailserver.");
        }
        else
        {
            logger.LogCritical(
                "SMTP NAO CONFIGURADO EM {Ambiente}. Nenhum e-mail sera entregue e a recuperacao " +
                "de senha nao funciona: quem esquecer a senha fica sem acesso. Defina Smtp:Host.",
                services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        }
    }
}
