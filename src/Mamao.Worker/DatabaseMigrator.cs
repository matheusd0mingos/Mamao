using Mamao.Identity.Persistence;
using Mamao.Messaging;
using Mamao.People.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mamao.Worker;

/// <summary>
/// Aplica as migrations de todos os modulos no startup do WORKER — nao da API, para
/// evitar corrida entre replicas — protegido por advisory lock do Postgres.
/// Cada modulo tem sua propria cadeia de migrations e seu proprio historico.
/// Ver docs/arquitetura/modulos-e-contratos.md.
/// </summary>
public sealed class DatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<DatabaseMigrator> logger) : IHostedService
{
    /// <summary>Chave arbitraria e fixa. Duas instancias nunca migram ao mesmo tempo.</summary>
    private const long AdvisoryLockKey = 8_314_552_001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var identity = scope.ServiceProvider.GetRequiredService<MamaoIdentityDbContext>();
        var connectionString = identity.Database.GetConnectionString()!;

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);

        await using (var command = lockConnection.CreateCommand())
        {
            command.CommandText = "SELECT pg_advisory_lock($1)";
            command.Parameters.AddWithValue(AdvisoryLockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await MigrateAsync(identity, nameof(MamaoIdentityDbContext), cancellationToken);
            await MigrateAsync(
                scope.ServiceProvider.GetRequiredService<MessagingDbContext>(),
                nameof(MessagingDbContext), cancellationToken);
            await MigrateAsync(
                scope.ServiceProvider.GetRequiredService<PeopleDbContext>(),
                nameof(PeopleDbContext), cancellationToken);
        }
        catch (Exception ex)
        {
            // Subir com o schema errado e pior do que nao subir.
            logger.LogCritical(ex, "Falha ao aplicar migrations. Encerrando.");
            lifetime.StopApplication();
            throw;
        }
        finally
        {
            await using var unlock = lockConnection.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock($1)";
            unlock.Parameters.AddWithValue(AdvisoryLockKey);
            await unlock.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateAsync(DbContext context, string name, CancellationToken ct)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("{Context}: schema em dia.", name);
            return;
        }

        logger.LogInformation("{Context}: aplicando {Count} migration(s): {Migrations}",
            name, pending.Count, string.Join(", ", pending));

        await context.Database.MigrateAsync(ct);
    }
}
