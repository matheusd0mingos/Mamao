using System.Net;
using Mamao.Notifications.Email;
using Microsoft.Extensions.Options;

namespace Mamao.Notifications;

/// <summary>
/// Templates em C# mesmo, enquanto sao dois. Motor de template (Razor, Fluid) entra quando
/// o RH quiser editar texto sem deploy — nao antes.
///
/// HTML de e-mail e tabela e estilo inline de proposito: cliente de e-mail nao suporta
/// flexbox, grid nem folha de estilo externa. Aqui as regras do design system nao valem.
/// </summary>
public sealed class EmailTemplates(IOptions<AppOptions> options)
{
    private readonly AppOptions _app = options.Value;

    public EmailMessage PasswordReset(string destinatario, string nome, string token)
    {
        var link = $"{_app.PublicOrigin.TrimEnd('/')}/redefinir-senha" +
                   $"?email={WebUtility.UrlEncode(destinatario)}&token={WebUtility.UrlEncode(token)}";

        var texto = $"""
            Olá, {nome}.

            Recebemos um pedido para redefinir a sua senha no {_app.ProductName}.
            Abra o endereço abaixo para escolher uma nova senha:

            {link}

            O link vale por algumas horas e só pode ser usado uma vez.

            Se não foi você quem pediu, ignore esta mensagem: sua senha continua a mesma.

            {_app.ProductName} — gestão sem complicação
            """;

        return new EmailMessage(
            destinatario,
            $"Redefinir sua senha no {_app.ProductName}",
            Layout(
                titulo: "Redefinir sua senha",
                corpo: $"""
                    <p>Olá, {Escapar(nome)}.</p>
                    <p>Recebemos um pedido para redefinir a sua senha no {Escapar(_app.ProductName)}.</p>
                    {Botao(link, "Escolher nova senha")}
                    <p style="color:#5b6661;font-size:14px">
                      O link vale por algumas horas e só pode ser usado uma vez.<br>
                      Se não foi você quem pediu, ignore esta mensagem: sua senha continua a mesma.
                    </p>
                    <p style="color:#5b6661;font-size:13px;word-break:break-all">
                      Se o botão não funcionar, copie e cole este endereço:<br>{Escapar(link)}
                    </p>
                    """),
            texto)
        { ToName = nome };
    }

    public EmailMessage PasswordChanged(string destinatario, string nome)
    {
        var texto = $"""
            Olá, {nome}.

            A senha da sua conta no {_app.ProductName} acabou de ser alterada.

            Se não foi você, procure o responsável pela conta da sua empresa imediatamente.

            {_app.ProductName} — gestão sem complicação
            """;

        // Aviso de senha alterada e controle de seguranca, nao cortesia: e assim que a
        // pessoa descobre um acesso indevido.
        return new EmailMessage(
            destinatario,
            $"Sua senha no {_app.ProductName} foi alterada",
            Layout(
                titulo: "Sua senha foi alterada",
                corpo: $"""
                    <p>Olá, {Escapar(nome)}.</p>
                    <p>A senha da sua conta no {Escapar(_app.ProductName)} acabou de ser alterada.</p>
                    <p style="color:#a32020">
                      Se não foi você, procure o responsável pela conta da sua empresa imediatamente.
                    </p>
                    """),
            texto)
        { ToName = nome };
    }

    private string Layout(string titulo, string corpo) => $"""
        <!doctype html>
        <html lang="pt-BR">
          <body style="margin:0;padding:24px;background:#f7f3ea;font-family:Helvetica,Arial,sans-serif;color:#16211d">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              <tr>
                <td align="center">
                  <table role="presentation" width="520" cellpadding="0" cellspacing="0"
                         style="max-width:520px;background:#ffffff;border:1px solid #e6e6e1;border-radius:10px">
                    <tr>
                      <td style="padding:24px 28px;border-bottom:1px solid #e6e6e1">
                        <span style="color:#f2b233;font-size:26px;font-family:Georgia,serif">ã</span>
                        <span style="font-size:20px;font-family:Georgia,serif;margin-left:6px">{Escapar(_app.ProductName)}</span>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:28px">
                        <h1 style="margin:0 0 16px;font-size:20px;font-family:Georgia,serif;font-weight:400">{Escapar(titulo)}</h1>
                        {corpo}
                      </td>
                    </tr>
                  </table>
                  <p style="color:#5b6661;font-size:12px;margin-top:16px">
                    {Escapar(_app.ProductName)} — gestão sem complicação
                  </p>
                </td>
              </tr>
            </table>
          </body>
        </html>
        """;

    private static string Botao(string url, string texto) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:20px 0">
          <tr>
            <td style="background:#11362d;border-radius:6px">
              <a href="{Escapar(url)}"
                 style="display:inline-block;padding:12px 20px;color:#f7f3ea;text-decoration:none;font-weight:600">
                {Escapar(texto)}
              </a>
            </td>
          </tr>
        </table>
        """;

    /// <summary>Nome de usuario entra no HTML: sem escape, vira injecao no cliente de e-mail.</summary>
    private static string Escapar(string valor) => WebUtility.HtmlEncode(valor);
}
