using System.Security.Claims;
using Mamao.Identity.Domain;
using Mamao.Identity.Persistence;
using Mamao.Identity.Tokens;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Identity.Endpoints;

public sealed record RegisterCompanyRequest(string CompanyName, string FullName, string Email, string Password);
public sealed record LoginRequest(string Email, string Password, Guid? TenantId);
public sealed record RefreshRequest(string RefreshToken);

public sealed record TenantOption(Guid TenantId, string Name, string Role);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    Guid TenantId,
    string TenantName,
    string Role,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Uma forma so para os dois desfechos do login. Anonimo aqui geraria contrato ruim no
/// OpenAPI e, por consequencia, no cliente TypeScript gerado.
/// </summary>
public sealed record LoginResponse(
    bool RequiresTenantSelection,
    IReadOnlyList<TenantOption>? Tenants,
    AuthResponse? Auth);

public sealed record MeResponse(
    Guid UserId,
    string FullName,
    string Email,
    Guid TenantId,
    string Role,
    IReadOnlyList<string> Permissions);

public static class AuthEndpoints
{
    public const string AuthRateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Rate limit no proprio grupo: login e refresh sao o alvo obvio de forca bruta.
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .RequireRateLimiting(AuthRateLimitPolicy);

        group.MapPost("/register-company", async Task<IResult> (
            RegisterCompanyRequest request,
            UserManager<MamaoUser> userManager,
            MamaoIdentityDbContext dbContext,
            TokenService tokens,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.CompanyName))
                return HttpResultsExtensions.Problem(new Error(
                    "auth.company_required", "Informe o nome da empresa.", nameof(request.CompanyName)));

            var now = timeProvider.GetUtcNow();
            var email = request.Email.Trim().ToLowerInvariant();

            var user = await userManager.FindByEmailAsync(email);
            if (user is not null)
            {
                // Nao revelamos se o e-mail existe: isso e enumeracao de usuario.
                return HttpResultsExtensions.Problem(new Error(
                    "auth.registration_failed",
                    "Nao foi possivel concluir o cadastro com esses dados.",
                    nameof(request.Email)));
            }

            user = new MamaoUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = request.FullName.Trim(),
                CreatedAt = now,
            };

            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                var first = created.Errors.First();
                return HttpResultsExtensions.Problem(new Error(
                    $"auth.{first.Code}", first.Description, nameof(request.Password)));
            }

            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                Name = request.CompanyName.Trim(),
                CreatedAt = now,
            };

            var membership = new Membership
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                TenantId = tenant.Id,
                Role = Roles.Owner,
                CreatedAt = now,
            };

            dbContext.Tenants.Add(tenant);
            dbContext.Memberships.Add(membership);
            await dbContext.SaveChangesAsync(ct);

            var pair = await tokens.IssueAsync(user, membership, ct);
            return TypedResults.Ok(ToResponse(pair, tenant, membership.Role));
        })
        .WithName("registerCompany")
        .Produces<AuthResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous();

        group.MapPost("/login", async Task<IResult> (
            LoginRequest request,
            UserManager<MamaoUser> userManager,
            MamaoIdentityDbContext dbContext,
            TokenService tokens,
            CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await userManager.FindByEmailAsync(email);

            if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
                return HttpResultsExtensions.Problem(new Error("auth.invalid_credentials", "E-mail ou senha invalidos."));

            var memberships = await dbContext.Memberships
                .Include(m => m.Tenant)
                .Where(m => m.UserId == user.Id && m.IsActive && m.Tenant.IsActive)
                .ToListAsync(ct);

            if (memberships.Count == 0)
                return HttpResultsExtensions.Problem(new Error(
                    "auth.no_membership", "Seu usuario nao esta vinculado a nenhuma empresa ativa."));

            var membership = request.TenantId is { } tenantId
                ? memberships.FirstOrDefault(m => m.TenantId == tenantId)
                : memberships.Count == 1 ? memberships[0] : null;

            if (membership is null)
            {
                // Mesma pessoa em varias empresas: o cliente escolhe e repete o login com
                // o tenantId. Ver docs/adr/0006-identidade.md.
                return TypedResults.Ok(new LoginResponse(
                    RequiresTenantSelection: true,
                    Tenants: memberships
                        .Select(m => new TenantOption(m.TenantId, m.Tenant.Name, m.Role))
                        .ToList(),
                    Auth: null));
            }

            var pair = await tokens.IssueAsync(user, membership, ct);
            return TypedResults.Ok(new LoginResponse(
                false, null, ToResponse(pair, membership.Tenant, membership.Role)));
        })
        .WithName("login")
        .Produces<LoginResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous();

        group.MapPost("/refresh", async Task<IResult> (
            RefreshRequest request,
            TokenService tokens,
            CancellationToken ct) =>
        {
            var refreshed = await tokens.RefreshAsync(request.RefreshToken, ct);
            if (refreshed is null)
                return HttpResultsExtensions.Problem(new Error(
                    "auth.invalid_refresh_token", "Sessao expirada. Entre novamente."));

            return TypedResults.Ok(ToResponse(
                refreshed.Pair, refreshed.Membership.Tenant, refreshed.Membership.Role));
        })
        .WithName("refreshToken")
        .Produces<AuthResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var permissions = principal.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList();

            return TypedResults.Ok(new MeResponse(
                UserId: Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub")!),
                FullName: principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                Email: principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email") ?? string.Empty,
                TenantId: Guid.Parse(principal.FindFirstValue(TokenService.TenantClaim)!),
                Role: principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
                Permissions: permissions));
        })
        .WithName("me")
        .Produces<MeResponse>()
        .RequireAuthorization();

        return app;
    }

    private static AuthResponse ToResponse(TokenPair pair, Tenant tenant, string role) => new(
        pair.AccessToken,
        pair.AccessTokenExpiresAt,
        pair.RefreshToken,
        tenant.Id,
        tenant.Name,
        role,
        Roles.PermissionsOf(role));
}
