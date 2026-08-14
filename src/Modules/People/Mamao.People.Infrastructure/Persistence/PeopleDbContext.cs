using Mamao.People.Application;
using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Mamao.People.Domain.Organization;
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
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<EmploymentContract> EmploymentContracts => Set<EmploymentContract>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration());
        modelBuilder.ApplyConfiguration(new DepartmentConfiguration());
        modelBuilder.ApplyConfiguration(new PositionConfiguration());
        modelBuilder.ApplyConfiguration(new EmploymentContractConfiguration());

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
        configurationBuilder.Properties<DepartmentId>().HaveConversion<DepartmentIdConverter>();
        configurationBuilder.Properties<PositionId>().HaveConversion<PositionIdConverter>();
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
        builder.Property(e => e.Code).HasMaxLength(50);
        builder.Property(e => e.Email).HasMaxLength(200);

        // Todo indice de tabela tenant-owned comeca por tenant_id, senao o Postgres varre
        // os dados de todos os tenants para filtrar depois.
        builder.HasIndex(e => new { e.TenantId, e.Code })
            .IsUnique()
            .HasFilter("code IS NOT NULL");

        // Unico quando informado, pelo mesmo motivo da matricula — e porque este endereco
        // vira a chave do convite de login. Dois funcionarios com o mesmo e-mail dariam
        // dois vinculos para um unico User.
        builder.HasIndex(e => new { e.TenantId, e.Email })
            .IsUnique()
            .HasFilter("email IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.FullName });
        builder.HasIndex(e => new { e.TenantId, e.PositionId });
        builder.HasIndex(e => new { e.TenantId, e.DepartmentId });
        builder.HasIndex(e => new { e.TenantId, e.ManagerId });

        // FK sem propriedade de navegacao: o agregado nao expoe Position nem Department
        // como objeto, senao um Include vira convite a carregar meia base. A integridade
        // referencial e do banco; a leitura junta por projecao, no servico.
        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(e => e.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(e => e.ManagerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class EmploymentContractConfiguration : IEntityTypeConfiguration<EmploymentContract>
{
    public void Configure(EntityTypeBuilder<EmploymentContract> builder)
    {
        builder.ToTable("employment_contracts");
        builder.HasKey(c => c.Id);

        // Enum como TEXTO no banco, nao inteiro: `regime = 'Militar'` e legivel num SELECT
        // de suporte, e reordenar o enum deixa de reescrever silenciosamente o significado
        // das linhas ja gravadas. Mesma razao do enum como texto no JSON.
        builder.Property(c => c.Regime).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.ScheduleType).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(c => c.WeeklyHours).HasPrecision(4, 1);
        builder.Property(c => c.Notes).HasMaxLength(500);

        // Um contrato por funcionario, garantido pelo banco e nao so pelo servico.
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId }).IsUnique();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(c => c.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Path).HasMaxLength(300).IsRequired();

        builder.HasIndex(d => new { d.TenantId, d.ParentId, d.Name }).IsUnique();

        // O indice que faz o caminho materializado valer a pena: "tudo abaixo de
        // Operacoes" vira `WHERE path LIKE '/op/%'`, que usa prefixo deste indice.
        builder.HasIndex(d => new { d.TenantId, d.Path });

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(d => d.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.NormalizedName).HasMaxLength(120).IsRequired();

        // A unicidade e sobre o nome DOBRADO: "Vigilante" e "vigilante " sao o mesmo cargo.
        builder.HasIndex(p => new { p.TenantId, p.NormalizedName }).IsUnique();
    }
}

/// <summary>Converte <see cref="EmployeeId"/> para uuid e de volta.</summary>
public sealed class EmployeeIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<EmployeeId, Guid>(
        id => id.Value,
        value => new EmployeeId(value));

public sealed class DepartmentIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DepartmentId, Guid>(
        id => id.Value,
        value => new DepartmentId(value));

public sealed class PositionIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<PositionId, Guid>(
        id => id.Value,
        value => new PositionId(value));
