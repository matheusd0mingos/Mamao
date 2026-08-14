using System.Security.Claims;
using System.Text;
using Mamao.Identity.Domain;
using Mamao.Identity.Persistence;
using Mamao.Identity.Tokens;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Mamao.SharedKernel.Auditing;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Mamao.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddMamaoIdentity(
        this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) &&
                           Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Jwt:SigningKey ausente ou com menos de 32 bytes.")
            .ValidateOnStart();

        services.AddDbContext<MamaoIdentityDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MamaoIdentityDbContext.Schema));
            options.UseSnakeCaseNamingConvention();

            // O contexto do Identity nao filtra por tenant (o login acontece antes de
            // existir tenant), mas ELE ESCREVE na auditoria, que tem RLS. O interceptor so
            // define app.tenant_id na sessao; tabelas sem policy — users, tenants, invites —
            // ignoram o valor. Sem isto, todo convite quebrava com 42501 ao gravar o
            // registro de auditoria.
            if (sp.GetRequiredService<IOptions<TenancyOptions>>().Value.EnableRls)
                options.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
        });

        services.AddIdentityCore<MamaoUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 8;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MamaoIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<TokenService>();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jwt.SigningKey)
                            ? new string('0', 32)
                            : jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(TokenService.TenantClaim)
                .Build());

        // Uma policy por permissao. Endpoint verifica permissao, nunca papel —
        // ver docs/adr/0007-autorizacao.md.
        foreach (var permission in Permissions.All)
        {
            services.AddAuthorizationBuilder().AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(TokenService.TenantClaim)
                .RequireClaim(Permissions.ClaimType, permission));
        }

        // Auditoria ligada ao DbContext do Identity, pelo mesmo motivo do People: o
        // registro do convite entra na mesma transacao do convite.
        services.AddKeyedScoped<IAuditLog>("identity", (sp, _) => new AuditLogWriter(
            sp.GetRequiredService<Persistence.MamaoIdentityDbContext>(),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ICurrentActor>(),
            sp.GetRequiredService<TimeProvider>()));


        return services;
    }

    /// <summary>
    /// Resolve o tenant a partir do claim do token. Nunca de rota, query ou header —
    /// aceitar do cliente seria oferecer o vazamento em bandeja.
    /// </summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var claim = context.User.FindFirst(TokenService.TenantClaim)?.Value;

            if (Guid.TryParse(claim, out var tenantId))
                context.RequestServices.GetRequiredService<ITenantContext>().Set(tenantId);

            await next(context);
        });
}
