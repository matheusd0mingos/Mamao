using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // O EF cria, por convencao, um indice de coluna unica para cada chave estrangeira.
        // Num banco multi-tenant esse indice e pior que inutil: toda consulta filtra por
        // tenant_id primeiro, entao o Postgres prefere o indice composto — e o de coluna
        // unica fica so custando escrita e espaco. Pior, ele burla a regra "todo indice
        // comeca por tenant_id", que existe para o banco nao varrer dados de todos os
        // clientes. Cada FK que precisa de indice ganha um explicito, com tenant_id na
        // frente. Ver docs/adr/0003-multi-tenancy.md.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
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
