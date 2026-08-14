using System.Diagnostics;
using System.Security.Claims;
using Mamao.SharedKernel.Auditing;
using Microsoft.AspNetCore.Http;

namespace Mamao.SharedKernel.Web;

/// <summary>
/// Quem esta agindo, resolvido do token e da requisicao.
///
/// Sem requisicao (job de background, migration) o nome vira "sistema" em vez de vazio:
/// registro sem ator e registro que nao responde "quem fez", que e metade da pergunta.
/// </summary>
public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public Guid? UserId
    {
        get
        {
            var valor = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? Principal?.FindFirstValue("sub");

            return Guid.TryParse(valor, out var id) ? id : null;
        }
    }

    public string Name =>
        Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue(ClaimTypes.Email)
        ?? "sistema";

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// O mesmo id que amarra log, trace e auditoria. Investigar um incidente sem isso e
    /// juntar tres fontes na mao.
    /// </summary>
    public string? CorrelationId => Activity.Current?.TraceId.ToString();

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
}
