using Mamao.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Identity.Persistence;

/// <summary>
/// Schema "identity". Nao herda de TenantDbContext de proposito: User e Tenant sao
/// globais e Membership cruza os dois — filtro por tenant aqui impediria o login, que
/// acontece antes de existir tenant ativo.
/// </summary>
public sealed class MamaoIdentityDbContext(DbContextOptions<MamaoIdentityDbContext> options)
    : IdentityDbContext<MamaoUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);

        builder.Entity<MamaoUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
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

        builder.Entity<Membership>(b =>
        {
            b.ToTable("memberships");
            b.HasKey(m => m.Id);
            b.Property(m => m.Role).HasMaxLength(50).IsRequired();
            b.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();
            b.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
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
