using Mamao.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Messaging;

/// <summary>
/// Dono do schema "messaging" e das migrations da outbox. Os DbContexts dos modulos
/// mapeiam a mesma tabela com ExcludeFromMigrations — assim o Enqueue participa da
/// transacao do modulo, mas so um contexto gera o DDL.
/// </summary>
public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public const string Schema = OutboxMessageConfiguration.Schema;

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
