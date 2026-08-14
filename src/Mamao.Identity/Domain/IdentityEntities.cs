using Microsoft.AspNetCore.Identity;

namespace Mamao.Identity.Domain;

/// <summary>
/// Usuario. PERTENCE a uma empresa: e ela que cria, gerencia e, ao ser excluida, apaga.
/// Ver docs/adr/0020-usuario-pertence-a-empresa.md.
///
/// O e-mail continua unico no sistema INTEIRO, e as duas coisas nao se contradizem:
/// unico global nao significa usuario global. A linha pertence a uma empresa; o indice
/// so garante que o endereco aponta para uma pessoa so — e e o que permite o login
/// continuar sendo e-mail + senha, sem subdominio nem campo de empresa.
/// </summary>
public sealed class MamaoUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = null!;

    /// <summary>Empresa dona da conta. Nunca muda: trocar de empresa e criar outra conta.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Papel nesta empresa. Ver Mamao.SharedKernel.Authorization.Roles.</summary>
    public string Role { get; set; } = null!;

    /// <summary>Desativar preserva historico e auditoria; excluir nao.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
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

// Membership foi removida na ADR-0020. Um usuario tem UM papel em UMA empresa, entao a
// tabela de ligacao nao descrevia mais nada que MamaoUser nao diga. Se um dia um cliente
// pedir acesso a duas empresas com o mesmo login, o caminho esta escrito na propria ADR —
// e nao passa por trazer esta tabela de volta.

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
