using Mamao.People.Application;
using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Mamao.SharedKernel.Messaging;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mamao.People.Infrastructure.Persistence;

/// <summary>
/// DbContext do modulo People, restrito ao schema "people".
/// O schema proprio e o que impede o JOIN entre modulos por acidente: sem DbSet do outro
/// modulo aqui, o atalho exige SQL cru e explicito. Ver docs/adr/0002-schema-por-modulo.md.
/// </summary>
public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext), IPeopleDbContext
{
    public const string Schema = "people";

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration());

        // A outbox mora no schema "messaging" e pertence ao MessagingDbContext, que e quem
        // gera a migration dela. Aqui ela e apenas mapeada para que o Enqueue participe da
        // MESMA transacao do dado de negocio.
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            new OutboxMessageConfiguration().Configure(b);
            b.ToTable(OutboxMessageConfiguration.Table, OutboxMessageConfiguration.Schema,
                t => t.ExcludeFromMigrations());
        });

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<EmployeeId>().HaveConversion<EmployeeIdConverter>();
        base.ConfigureConventions(configurationBuilder);
    }

    Task<int> IPeopleDbContext.SaveChangesAsync(CancellationToken cancellationToken)
        => SaveChangesAsync(cancellationToken);
}

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PositionName).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Code).HasMaxLength(50);

        // Todo indice de tabela tenant-owned comeca por tenant_id, senao o Postgres varre
        // os dados de todos os tenants para filtrar depois.
        builder.HasIndex(e => new { e.TenantId, e.Code })
            .IsUnique()
            .HasFilter("code IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.FullName });
    }
}

/// <summary>Converte <see cref="EmployeeId"/> para uuid e de volta.</summary>
public sealed class EmployeeIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<EmployeeId, Guid>(
        id => id.Value,
        value => new EmployeeId(value));
