using Mamao.Audit.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mamao.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAudit(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuditDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema));

            options.UseSnakeCaseNamingConvention();
        });

        return services;
    }
}
