using Mamao.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Identity.Persistence;

/// <summary>
/// Schema "identity". NAO herda de TenantDbContext, e a excecao e consciente: o login
/// precisa achar o usuario pelo e-mail ANTES de existir tenant, e um filtro global (ou
/// RLS) aqui impediria exatamente isso.
///
/// O que compensa: esta tabela guarda e-mail, hash de senha e nome — nunca dado de
/// funcionario, documento ou escala, que vivem em modulos com RLS. E toda consulta que
/// lista usuarios filtra por TenantId explicitamente.
/// Ver docs/adr/0020-usuario-pertence-a-empresa.md.
/// </summary>
public sealed class MamaoIdentityDbContext(DbContextOptions<MamaoIdentityDbContext> options)
    : IdentityDbContext<MamaoUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invite> Invites => Set<Invite>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);

        builder.Entity<MamaoUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.Property(u => u.Role).HasMaxLength(50).IsRequired();

            // Cascade: excluir a empresa apaga os usuarios dela. E o que torna a exclusao
            // de conta pela LGPD uma operacao so, sem ressalva.
            b.HasOne(u => u.Tenant)
                .WithMany()
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(u => new { u.TenantId, u.IsActive });
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("aspnet_roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("aspnet_user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("aspnet_user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("aspnet_user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("aspnet_user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("aspnet_role_claims");

        builder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
            b.Property(t => t.TimeZoneId).HasMaxLength(60).IsRequired();
        });

        // Mesma tabela de auditoria dos outros modulos, mapeada aqui para que o registro
        // do convite entre na mesma transacao do convite.
        builder.Entity<Mamao.SharedKernel.Auditing.AuditEntry>(b =>
        {
            new Mamao.SharedKernel.Auditing.AuditEntryConfiguration().Configure(b);
            b.ToTable(
                Mamao.SharedKernel.Auditing.AuditEntry.Table,
                Mamao.SharedKernel.Auditing.AuditEntry.Schema,
                t => t.ExcludeFromMigrations());
        });

        builder.Entity<Invite>(b =>
        {
            b.ToTable("invites");
            b.HasKey(i => i.Id);
            b.Property(i => i.Email).HasMaxLength(200).IsRequired();
            b.Property(i => i.FullName).HasMaxLength(200).IsRequired();
            b.Property(i => i.Role).HasMaxLength(50).IsRequired();
            b.Property(i => i.TokenHash).HasMaxLength(120).IsRequired();

            // Busca do aceite e por hash: precisa ser unica e indexada.
            b.HasIndex(i => i.TokenHash).IsUnique();
            b.HasIndex(i => new { i.TenantId, i.Email });

            b.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.TokenHash).HasMaxLength(120).IsRequired();
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => new { t.UserId, t.ExpiresAt });
        });
    }
}
