using Mamao.People.Application;
using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Missions;
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
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<AbsenceRequest> AbsenceRequests => Set<AbsenceRequest>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<MissionAssignment> MissionAssignments => Set<MissionAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration());
        modelBuilder.ApplyConfiguration(new DepartmentConfiguration());
        modelBuilder.ApplyConfiguration(new PositionConfiguration());
        modelBuilder.ApplyConfiguration(new EmploymentContractConfiguration());
        modelBuilder.ApplyConfiguration(new OccupancyConfiguration());
        modelBuilder.ApplyConfiguration(new AbsenceRequestConfiguration());
        modelBuilder.ApplyConfiguration(new MissionConfiguration());
        modelBuilder.ApplyConfiguration(new MissionAssignmentConfiguration());

        // Outbox e auditoria moram em schemas de outros donos (messaging e audit), que sao
        // quem gera as migrations delas. Aqui sao apenas MAPEADAS, para que o Enqueue e o
        // Record participem da MESMA transacao do dado de negocio. Auditoria em transacao
        // separada e auditoria que pode faltar no caso que importa.
        modelBuilder.Entity<Mamao.SharedKernel.Auditing.AuditEntry>(b =>
        {
            new Mamao.SharedKernel.Auditing.AuditEntryConfiguration().Configure(b);
            b.ToTable(
                Mamao.SharedKernel.Auditing.AuditEntry.Table,
                Mamao.SharedKernel.Auditing.AuditEntry.Schema,
                t => t.ExcludeFromMigrations());
        });

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
        configurationBuilder.Properties<OccupancyId>().HaveConversion<OccupancyIdConverter>();
        configurationBuilder.Properties<AbsenceRequestId>().HaveConversion<AbsenceRequestIdConverter>();
        configurationBuilder.Properties<MissionId>().HaveConversion<MissionIdConverter>();
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

public sealed class OccupancyIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<OccupancyId, Guid>(
        id => id.Value,
        value => new OccupancyId(value));

public sealed class AbsenceRequestIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<AbsenceRequestId, Guid>(
        id => id.Value,
        value => new AbsenceRequestId(value));

public sealed class OccupancyConfiguration : IEntityTypeConfiguration<Occupancy>
{
    public void Configure(EntityTypeBuilder<Occupancy> builder)
    {
        builder.ToTable("occupancies");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.Note).HasMaxLength(500);

        // A consulta que este modulo mais vai fazer e "quem esta ocupado neste intervalo".
        // Ela filtra por tenant e cruza datas, entao o indice segue essa ordem. Como toda
        // consulta multi-tenant comeca pelo tenant, todo indice tambem comeca por ele —
        // regra verificada por teste de arquitetura.
        builder.HasIndex(o => new { o.TenantId, o.StartsOn, o.EndsOn });

        // "Como esta o fulano?" — a tela da pessoa, do outro lado da mesma tabela.
        builder.HasIndex(o => new { o.TenantId, o.EmployeeId, o.StartsOn });

        // Achar o bloco que veio de uma solicitacao ou missao, para remove-lo quando a
        // origem for desfeita. Sem este indice seria varredura na tabela inteira.
        builder.HasIndex(o => new { o.TenantId, o.Source, o.SourceId });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(o => o.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AbsenceRequestConfiguration : IEntityTypeConfiguration<AbsenceRequest>
{
    public void Configure(EntityTypeBuilder<AbsenceRequest> builder)
    {
        builder.ToTable("absence_requests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(AbsenceRequest.MaxMotivo);
        builder.Property(r => r.DecisionNote).HasMaxLength(AbsenceRequest.MaxMotivo);
        builder.Property(r => r.DecidedByName).HasMaxLength(200);

        // A caixa de entrada do chefe: "o que esta pendente, mais antigo primeiro".
        builder.HasIndex(r => new { r.TenantId, r.Status, r.StartsOn });

        // O historico da pessoa, na tela dela.
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId, r.StartsOn });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MissionIdConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<MissionId, Guid>(
        id => id.Value,
        value => new MissionId(value));

public sealed class MissionConfiguration : IEntityTypeConfiguration<Mission>
{
    public void Configure(EntityTypeBuilder<Mission> builder)
    {
        builder.ToTable("missions");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).HasMaxLength(Mission.MaxNome).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(500);

        // "O que tem pela frente" e "quantas vezes o fulano foi nos ultimos 90 dias" sao as
        // duas consultas desta tabela, e as duas ordenam por data.
        builder.HasIndex(m => new { m.TenantId, m.On });

        builder.HasMany(m => m.Assignments)
            .WithOne()
            .HasForeignKey(a => a.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class MissionAssignmentConfiguration : IEntityTypeConfiguration<MissionAssignment>
{
    public void Configure(EntityTypeBuilder<MissionAssignment> builder)
    {
        builder.ToTable("mission_assignments");
        builder.HasKey(a => a.Id);

        // A mesma pessoa uma vez so por missao, garantido pelo banco.
        builder.HasIndex(a => new { a.TenantId, a.MissionId, a.EmployeeId }).IsUnique();

        // O historico de rodizio le por PESSOA: "quantas vezes o Pedro participou".
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
