using Mamao.Audit.Persistence;
using Mamao.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Mamao.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mamao.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAudit(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuditDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema));

            options.UseSnakeCaseNamingConvention();

            // Este contexto so escrevia via migrations, entao nunca precisou do interceptor.
            // Com a tela de auditoria ele passa a LER — e sem definir app.tenant_id a policy
            // do Postgres nao devolveria linha nenhuma. Melhor descobrir isto aqui do que
            // numa tela que abre vazia sem dizer por que.
            if (sp.GetRequiredService<IOptions<TenancyOptions>>().Value.EnableRls)
                options.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
        });

        services.AddScoped<AuditQueryService>();

        return services;
    }

    /// <summary>
    /// A tela de "quem fez o que". Uma rota so: o filtro vai na query string porque e assim
    /// que se compartilha um recorte de investigacao por link.
    /// </summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", async Task<IResult> (
            string? action, string? subjectId, string? search,
            DateOnly? from, DateOnly? to, int? page, int? pageSize,
            AuditQueryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(
                action, subjectId, search, from, to, page ?? 1, pageSize ?? 50, ct)))
        .WithName("listAudit")
        .WithTags("Audit")
        .Produces<AuditPage>()
        .RequireAuthorization(Permissions.AuditRead);

        return app;
    }
}
