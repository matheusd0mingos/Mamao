using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Mamao.SharedKernel.Tenancy;

/// <summary>
/// Base dos DbContexts de modulo. Aplica, por convencao, o filtro global de tenant e
/// o indice em TenantId para toda entidade <see cref="ITenantOwned"/>.
///
/// Isto e a camada 2 do isolamento descrito em docs/adr/0003-multi-tenancy.md.
/// A camada 3 (RLS no PostgreSQL) e independente e vive em <see cref="TenantRls"/>.
/// </summary>
public abstract class TenantDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    protected TenantDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Lido pelos filtros globais a cada consulta. O EF Core substitui a referencia ao
    /// contexto pela instancia atual, entao o valor acompanha o escopo da requisicao
    /// mesmo com o modelo em cache.
    /// </summary>
    public Guid CurrentTenantId => _tenantContext.Current;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyTenancyConventions(modelBuilder);
    }

    private void ApplyTenancyConventions(ModelBuilder modelBuilder)
    {
        var apply = typeof(TenantDbContext).GetMethod(
            nameof(ApplyTenantFilter),
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{nameof(ApplyTenantFilter)} nao encontrado.");

        var tenantOwned = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .ToList();

        foreach (var entityType in tenantOwned)
        {
            apply.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(ITenantOwned.TenantId));
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
