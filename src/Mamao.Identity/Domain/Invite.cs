using System.Security.Cryptography;
using Mamao.SharedKernel.Results;

namespace Mamao.Identity.Domain;

/// <summary>
/// Convite de acesso. O RH importa a planilha, escolhe quem precisa entrar no sistema e
/// convida — e o caminho de ativacao definido para os pilotos.
///
/// Guardamos o HASH do token, nunca o valor, pelo mesmo motivo do refresh token: vazamento
/// do banco nao pode virar acesso. O valor existe uma vez so, no link que vai por e-mail.
///
/// <see cref="EmployeeId"/> e um Guid solto, sem chave estrangeira: `employees` vive no
/// schema de outro modulo e uma FK entre schemas seria justamente o atalho que o
/// schema-por-modulo existe para impedir (ADR-0002). O registro do convite tambem e o que
/// permite, na V1.5, ligar funcionario a usuario sem adivinhar por e-mail.
/// </summary>
public sealed class Invite
{
    /// <summary>Prazo do convite. Curto o bastante para um link vazado envelhecer, longo para caber uma semana de trabalho.</summary>
    public const int ValidityDays = 7;

    private Invite() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Para onde o convite foi. Minusculas, como em todo lugar que compara e-mail.</summary>
    public string Email { get; private set; } = null!;

    public string FullName { get; private set; } = null!;
    public string Role { get; private set; } = null!;

    /// <summary>Funcionario que este convite representa, quando veio da lista de pessoas.</summary>
    public Guid? EmployeeId { get; private set; }

    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Usuario criado ao aceitar. Fecha o rastro de quem virou conta a partir de qual convite.</summary>
    public Guid? AcceptedUserId { get; private set; }

    public InviteStatus StatusAt(DateTimeOffset now) =>
        AcceptedAt is not null ? InviteStatus.Aceito
        : RevokedAt is not null ? InviteStatus.Cancelado
        : ExpiresAt <= now ? InviteStatus.Expirado
        : InviteStatus.Pendente;

    public bool CanAccept(DateTimeOffset now) => StatusAt(now) == InviteStatus.Pendente;

    /// <summary>Cria o convite e devolve o token EM CLARO, que existe só neste retorno.</summary>
    public static Result<(Invite Invite, string Token)> Create(
        Guid tenantId,
        string? email,
        string? fullName,
        string role,
        Guid? employeeId,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        var endereco = (email ?? string.Empty).Trim().ToLowerInvariant();
        var nome = (fullName ?? string.Empty).Trim();

        if (!EmailPlausivel(endereco))
        {
            return Result.Failure<(Invite, string)>(new Error(
                "invite.invalid_email", "Informe um e-mail válido para enviar o convite.", nameof(Email)));
        }

        if (nome.Length < 2)
        {
            return Result.Failure<(Invite, string)>(new Error(
                "invite.name_required", "Informe o nome de quem vai receber o convite.", nameof(FullName)));
        }

        if (!SharedKernel.Authorization.Roles.All.Contains(role))
        {
            return Result.Failure<(Invite, string)>(new Error(
                "invite.invalid_role", "Papel inválido.", nameof(Role)));
        }

        var token = GenerateToken();

        return Result.Success((new Invite
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Email = endereco,
            FullName = nome,
            Role = role,
            EmployeeId = employeeId,
            TokenHash = HashToken(token),
            ExpiresAt = now.AddDays(ValidityDays),
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
        }, token));
    }

    /// <summary>
    /// Reenviar gera token novo e invalida o anterior no mesmo movimento. Se o antigo
    /// continuasse valendo, dois links da mesma conta ficariam circulando por e-mail.
    /// </summary>
    public string Renew(DateTimeOffset now)
    {
        var token = GenerateToken();
        TokenHash = HashToken(token);
        ExpiresAt = now.AddDays(ValidityDays);
        RevokedAt = null;
        return token;
    }

    public Result Revoke(DateTimeOffset now)
    {
        if (AcceptedAt is not null)
            return Result.Failure("invite.already_accepted", "Este convite já foi aceito.");

        RevokedAt = now;
        return Result.Success();
    }

    public Result Accept(Guid userId, DateTimeOffset now)
    {
        if (!CanAccept(now))
            return Result.Failure("invite.not_acceptable", "Convite expirado ou já utilizado.");

        AcceptedAt = now;
        AcceptedUserId = userId;
        return Result.Success();
    }

    /// <summary>256 bits de aleatoriedade criptografica, em base64url para caber numa URL.</summary>
    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static bool EmailPlausivel(string valor)
    {
        var partes = valor.Split('@');

        return partes.Length == 2
            && partes[0].Length > 0
            && partes[1].Length > 2
            && partes[1].Contains('.', StringComparison.Ordinal)
            && !valor.Any(char.IsWhiteSpace);
    }
}

public enum InviteStatus
{
    Pendente,
    Aceito,
    Expirado,
    Cancelado,
}
