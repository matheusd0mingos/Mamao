using Mamao.Identity.Persistence;
using Mamao.Messaging;
using Mamao.People.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Mamao.Audit.Persistence;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Tenancy;
using Microsoft.Extensions.Options;
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
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
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
            await MigrateAsync(audit, nameof(AuditDbContext), cancellationToken);
            await MigrateAsync(
                scope.ServiceProvider.GetRequiredService<MessagingDbContext>(),
                nameof(MessagingDbContext), cancellationToken);
            await MigrateAsync(
                scope.ServiceProvider.GetRequiredService<PeopleDbContext>(),
                nameof(PeopleDbContext), cancellationToken);

            await ConcederAcessoAoRoleDaAplicacaoAsync(scope.ServiceProvider, lockConnection, cancellationToken);
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

    /// <summary>
    /// Concede acesso ao role da aplicacao depois das migrations.
    ///
    /// Tem que ser depois: os schemas sao criados pelas proprias migrations, entao nao ha
    /// o que conceder antes. E tem que ser aqui e nao no init-db.sql porque aquele roda uma
    /// vez so, na criacao do volume — um modulo novo, com schema novo, ficaria sem acesso.
    /// </summary>
    private async Task ConcederAcessoAoRoleDaAplicacaoAsync(
        IServiceProvider services, NpgsqlConnection connection, CancellationToken ct)
    {
        var tenancy = services.GetRequiredService<IOptions<TenancyOptions>>().Value;
        var role = tenancy.ApplicationRole;

        if (string.IsNullOrWhiteSpace(tenancy.ApplicationRolePassword))
        {
            // Em desenvolvimento e comum conectar como dono e nao ter role separado.
            // Avisar e seguir: bloquear aqui travaria o `dotnet run`.
            logger.LogWarning(
                "Tenancy:ApplicationRolePassword vazio: o role '{Role}' nao foi criado e a RLS " +
                "nao tem efeito. Normal em desenvolvimento; em producao e o que protege os dados.",
                role);
            return;
        }

        await using (var criacao = connection.CreateCommand())
        {
            criacao.CommandText = TenantRls.EnsureRole(role, tenancy.ApplicationRolePassword);
            await criacao.ExecuteNonQueryAsync(ct);
        }

        foreach (var schema in new[]
                 {
                     MamaoIdentityDbContext.Schema, MessagingDbContext.Schema, PeopleDbContext.Schema,
                 })
        {
            await using var grant = connection.CreateCommand();
            grant.CommandText = TenantRls.GrantSchemaTo(schema, role);
            await grant.ExecuteNonQueryAsync(ct);
        }

        // Auditoria NAO entra no laco acima: ela recebe insert e select, e tem update e
        // delete revogados. Auditoria que a aplicacao consegue reescrever nao serve de prova.
        await using (var auditoria = connection.CreateCommand())
        {
            auditoria.CommandText = TenantRls.GrantAppendOnlyTo(
                AuditEntry.Schema, AuditEntry.Table, role);
            await auditoria.ExecuteNonQueryAsync(ct);
        }

        logger.LogInformation(
            "Role '{Role}' garantido (sem superusuario, sem BYPASSRLS), com acesso aos schemas " +
            "e com auditoria em modo append-only.", role);
    }

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
