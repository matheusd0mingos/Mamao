using Mamao.Identity.Persistence;
using Mamao.Messaging;
using Mamao.People.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mamao.Api;

/// <summary>
/// Readiness so fica verde quando o schema esta em dia.
///
/// Quem aplica as migrations e o Worker (com advisory lock), mas a API sobe em paralelo.
/// Sem esta checagem, a API se declara pronta com o schema atrasado, o Caddy manda trafego
/// e o usuario recebe erro de coluna inexistente — justamente durante o deploy, que e onde
/// ninguem esta olhando o log.
///
/// Com ela, o deploy.sh so considera a subida concluida quando as migrations terminaram.
/// </summary>
public sealed class PendingMigrationsHealthCheck(
    MamaoIdentityDbContext identity,
    MessagingDbContext messaging,
    PeopleDbContext people) : IHealthCheck
{
    public const string Name = "migrations";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pendentes = new Dictionary<string, int>();

            foreach (var (nome, dbContext) in new (string, DbContext)[]
                     {
                         (nameof(MamaoIdentityDbContext), identity),
                         (nameof(MessagingDbContext), messaging),
                         (nameof(PeopleDbContext), people),
                     })
            {
                var quantidade = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
                if (quantidade > 0)
                    pendentes[nome] = quantidade;
            }

            if (pendentes.Count == 0)
                return HealthCheckResult.Healthy("Schema em dia.");

            var detalhe = string.Join(", ", pendentes.Select(p => $"{p.Key}: {p.Value}"));

            // Degraded e nao Unhealthy: durante um deploy normal isso e transitorio, e o
            // Worker esta aplicando neste exato momento. O readiness ainda reprova.
            return HealthCheckResult.Degraded(
                $"Migrations pendentes ({detalhe}). Aguardando o Worker aplicar.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Nao foi possivel verificar as migrations.", ex);
        }
    }
}
