using Microsoft.AspNetCore.Identity;

namespace Mamao.Identity.Domain;

/// <summary>
/// Usuario, global por e-mail. Nao pertence a um tenant: a mesma pessoa pode atender
/// varias empresas (contador, consultor, socio de duas empresas) com uma senha so.
/// Corrigir isso depois seria migracao com merge de contas.
/// Ver docs/adr/0006-identidade.md.
/// </summary>
public sealed class MamaoUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Empresa cliente.</summary>
public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Fuso da empresa. "Hoje" no dashboard e o hoje do cliente, nao o do servidor.</summary>
    public string TimeZoneId { get; set; } = "America/Sao_Paulo";

    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Vinculo entre usuario e empresa, com o papel naquela empresa.</summary>
public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Role { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public MamaoUser User { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

/// <summary>
/// Refresh token com rotacao. Guardamos o hash, nunca o valor — vazamento de banco nao
/// pode virar sessao valida.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Token que substituiu este. Uso de token ja rotacionado indica roubo.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
