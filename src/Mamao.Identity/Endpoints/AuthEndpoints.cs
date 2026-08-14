using System.Security.Claims;
using Mamao.Identity.Domain;
using Mamao.Identity.Persistence;
using Mamao.Identity.Tokens;
using Mamao.Notifications;
using Mamao.Notifications.Email;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mamao.Identity.Endpoints;

public sealed record RegisterCompanyRequest(string CompanyName, string FullName, string Email, string Password);
// Sem TenantId: um usuario pertence a uma empresa, entao nao ha o que escolher.
// Ver docs/adr/0020-usuario-pertence-a-empresa.md.
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    Guid TenantId,
    string TenantName,
    string Role,
    IReadOnlyList<string> Permissions);

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

            dbContext.Tenants.Add(tenant);
            await dbContext.SaveChangesAsync(ct);

            // A empresa nasce depois do usuario porque o Identity valida o e-mail primeiro;
            // por isso o vinculo e gravado num segundo passo, na mesma requisicao.
            user.TenantId = tenant.Id;
            user.Role = Roles.Owner;
            await dbContext.SaveChangesAsync(ct);

            var pair = await tokens.IssueAsync(user, ct);
            return TypedResults.Ok(ToResponse(pair, tenant, user.Role));
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

            var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);

            // Conta ou empresa desativada devolve a MESMA mensagem de credencial invalida:
            // dizer "sua conta esta desativada" confirma para quem tentou que o e-mail
            // existe e que a senha estava certa.
            if (!user.IsActive || tenant is null || !tenant.IsActive)
                return HttpResultsExtensions.Problem(new Error("auth.invalid_credentials", "E-mail ou senha invalidos."));

            var pair = await tokens.IssueAsync(user, ct);
            return TypedResults.Ok(ToResponse(pair, tenant, user.Role));
        })
        .WithName("login")
        .Produces<AuthResponse>()
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
                refreshed.Pair, refreshed.Tenant, refreshed.User.Role));
        })
        .WithName("refreshToken")
        .Produces<AuthResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous();

        group.MapPost("/forgot-password", async Task<IResult> (
            ForgotPasswordRequest request,
            UserManager<MamaoUser> userManager,
            IEmailSender emails,
            EmailTemplates templates,
            ILogger<MamaoUser> logger,
            CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await userManager.FindByEmailAsync(email);

            if (user is not null)
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                await emails.SendAsync(templates.PasswordReset(email, user.FullName, token), ct);
            }
            else
            {
                logger.LogInformation("Pedido de recuperacao para e-mail inexistente.");
            }

            // Sempre 204, exista o usuario ou nao: responder diferente transforma este
            // endpoint num verificador de quem tem conta no sistema.
            return TypedResults.NoContent();
        })
        .WithName("forgotPassword")
        .AllowAnonymous();

        group.MapPost("/reset-password", async Task<IResult> (
            ResetPasswordRequest request,
            UserManager<MamaoUser> userManager,
            TokenService tokens,
            IEmailSender emails,
            EmailTemplates templates,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await userManager.FindByEmailAsync(email);

            var invalido = HttpResultsExtensions.Problem(new Error(
                "auth.invalid_reset_token",
                "Este link expirou ou já foi usado. Peça um novo."));

            if (user is null)
                return invalido;

            var resultado = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

            if (!resultado.Succeeded)
            {
                var falha = resultado.Errors.First();

                // Senha fraca e erro do usuario e merece mensagem propria; token invalido
                // nao diz mais do que precisa.
                return falha.Code.Contains("Password", StringComparison.Ordinal)
                    ? HttpResultsExtensions.Problem(new Error(
                        $"auth.{falha.Code}", falha.Description, nameof(request.NewPassword)))
                    : invalido;
            }

            // Trocar a senha derruba as sessoes: se o acesso foi indevido, o intruso sai junto.
            await tokens.RevokeAllForUserAsync(user.Id, timeProvider.GetUtcNow(), ct);
            await emails.SendAsync(templates.PasswordChanged(email, user.FullName), ct);

            return TypedResults.NoContent();
        })
        .WithName("resetPassword")
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
