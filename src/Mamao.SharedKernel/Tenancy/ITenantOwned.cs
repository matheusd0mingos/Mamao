namespace Mamao.SharedKernel.Tenancy;

/// <summary>
/// Marca uma entidade como pertencente a um tenant. Toda entidade que implementa
/// esta interface ganha, por convencao: filtro global de consulta, indice iniciado
/// por TenantId e carimbo automatico no SaveChanges.
/// Ver docs/adr/0003-multi-tenancy.md.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
