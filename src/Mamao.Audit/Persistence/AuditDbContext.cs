using Mamao.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Audit.Persistence;

/// <summary>
/// Dono da migration do schema "audit". A tabela e mapeada tambem nos DbContexts dos
/// modulos (com ExcludeFromMigrations) para que o registro entre na MESMA transacao do
/// fato — mesmo arranjo da outbox, e pelo mesmo motivo.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = AuditEntry.Schema;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
