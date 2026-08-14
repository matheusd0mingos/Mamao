using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mamao.Messaging;

public static class MessagingModule
{
    /// <summary>
    /// Registra a outbox. O publicador so entra no Worker: job longo competindo com
    /// request HTTP degrada latencia.
    /// </summary>
    public static IServiceCollection AddOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        bool runPublisher,
        params Assembly[] eventAssemblies)
    {
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName));

        services.AddDbContext<MessagingDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MessagingDbContext.Schema));
            options.UseSnakeCaseNamingConvention();
        });

        services.AddSingleton(new IntegrationEventRegistry(eventAssemblies));
        services.AddScoped<IEventDispatcher, EventDispatcher>();
        services.AddSingleton<OutboxMetrics>();

        if (runPublisher)
            services.AddHostedService<OutboxPublisher>();

        return services;
    }
}
