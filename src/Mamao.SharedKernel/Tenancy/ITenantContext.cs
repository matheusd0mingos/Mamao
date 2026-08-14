namespace Mamao.SharedKernel.Tenancy;

/// <summary>
/// Tenant ativo na requisicao. Resolvido a partir do claim do token — nunca de
/// parametro de rota, query string ou header. Ver docs/adr/0003-multi-tenancy.md.
/// </summary>
public interface ITenantContext
{
    /// <summary>Tenant ativo. Lanca se ainda nao foi resolvido.</summary>
    Guid Current { get; }

    bool IsResolved { get; }

    /// <summary>
    /// Define o tenant. Usado pelo middleware da API e, explicitamente, por jobs de
    /// background que processam um tenant por vez.
    /// </summary>
    void Set(Guid tenantId);
}

public sealed class TenantContext : ITenantContext
{
    private Guid? _current;

    public Guid Current => _current
        ?? throw new InvalidOperationException(
            "Tenant nao resolvido. Toda operacao sobre dados de tenant precisa de um " +
            "tenant ativo — em jobs de background, chame ITenantContext.Set explicitamente.");

    public bool IsResolved => _current.HasValue;

    public void Set(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));

        _current = tenantId;
    }
}
