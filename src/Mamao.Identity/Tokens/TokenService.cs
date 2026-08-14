using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Mamao.Identity.Domain;
using Mamao.Identity.Persistence;
using Mamao.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mamao.Identity.Tokens;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "mamao";
    public string Audience { get; set; } = "mamao";

    /// <summary>Minimo de 32 bytes. Em producao vem de secret, nunca do appsettings versionado.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Curto de proposito: revogacao de acesso (leia-se demissao) precisa valer rapido.
    /// Cenario frequente neste produto, nao hipotetico.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public sealed record RefreshResult(TokenPair Pair, Membership Membership);

/// <summary>
/// Emissao de tokens. O tenant ativo vai NO TOKEN — trocar de empresa emite token novo,
/// nao muda uma flag de sessao. Ver docs/adr/0003-multi-tenancy.md e 0006-identidade.md.
/// </summary>
public sealed class TokenService(
    MamaoIdentityDbContext dbContext,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
{
    public const string TenantClaim = "tenant_id";
    public const string EmployeeClaim = "employee_id";

    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(MamaoUser user, Membership membership)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(TenantClaim, membership.TenantId.ToString()),
            new(ClaimTypes.Role, membership.Role),
        };

        claims.AddRange(Roles.PermissionsOf(membership.Role)
            .Select(permission => new Claim(Permissions.ClaimType, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public async Task<TokenPair> IssueAsync(MamaoUser user, Membership membership, CancellationToken ct)
    {
        var access = CreateAccessToken(user, membership);
        var refresh = await CreateRefreshTokenAsync(user.Id, membership.TenantId, replacing: null, ct);
        return new TokenPair(access.Token, access.ExpiresAt, refresh);
    }

    /// <summary>
    /// Rotaciona o refresh token. Reuso de um token ja rotacionado revoga toda a cadeia do
    /// usuario: e o sinal classico de token roubado.
    /// </summary>
    public async Task<RefreshResult?> RefreshAsync(string presentedToken, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = Hash(presentedToken);

        var stored = await dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
            return null;

        if (!stored.IsActive(now))
        {
            // Token ja rotacionado sendo reapresentado: sinal classico de roubo.
            // Derruba a cadeia inteira do usuario.
            if (stored.ReplacedByTokenId is not null)
                await RevokeAllForUserAsync(stored.UserId, now, ct);

            return null;
        }

        var membership = await dbContext.Memberships
            .Include(m => m.User)
            .Include(m => m.Tenant)
            .FirstOrDefaultAsync(
                m => m.UserId == stored.UserId && m.TenantId == stored.TenantId && m.IsActive, ct);

        if (membership is null || !membership.Tenant.IsActive)
            return null;

        var newToken = await CreateRefreshTokenAsync(stored.UserId, stored.TenantId, stored, ct);
        var access = CreateAccessToken(membership.User, membership);

        return new RefreshResult(
            new TokenPair(access.Token, access.ExpiresAt, newToken), membership);
    }

    public async Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private async Task<string> CreateRefreshTokenAsync(
        Guid userId, Guid tenantId, RefreshToken? replacing, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        var entity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TenantId = tenantId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        };

        dbContext.RefreshTokens.Add(entity);

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedByTokenId = entity.Id;
        }

        await dbContext.SaveChangesAsync(ct);
        return raw;
    }

    private static string Hash(string value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
