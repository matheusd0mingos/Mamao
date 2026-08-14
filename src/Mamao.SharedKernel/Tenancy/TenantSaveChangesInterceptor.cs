using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mamao.SharedKernel.Tenancy;

/// <summary>
/// Carimba o TenantId em toda entidade criada e recusa alteracao de entidade de outro
/// tenant. Elimina, de uma vez, a classe de bug "esqueci de setar o tenant ao criar".
/// </summary>
public sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null || !tenantContext.IsResolved)
            return;

        var tenantId = tenantContext.Current;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.TenantId = tenantId;
                    break;

                case EntityState.Modified or EntityState.Deleted:
                    if (entry.Entity.TenantId != tenantId)
                    {
                        // O tipo e o estado entram na mensagem porque, sem eles, a unica
                        // pista e um GUID zerado — e descobrir QUAL entidade escapou da
                        // marcacao vira arqueologia no ChangeTracker.
                        throw new InvalidOperationException(
                            $"Tentativa de {entry.State} em {entry.Entity.GetType().Name} do tenant " +
                            $"{entry.Entity.TenantId} a partir do tenant {tenantId}. Operacao bloqueada.");
                    }

                    break;
            }
        }
    }
}
