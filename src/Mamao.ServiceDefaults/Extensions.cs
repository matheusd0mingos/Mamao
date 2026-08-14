using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Mamao.ServiceDefaults;

/// <summary>
/// Plumbing compartilhado por API e Worker: OpenTelemetry, health checks, service
/// discovery e resiliencia de HttpClient. Nada aqui e autoral — e exatamente o tipo de
/// coisa que o framework ja resolve. Ver docs/arquitetura/visao-geral.md.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public const string LivenessEndpoint = "/healthz";
    public const string ReadinessEndpoint = "/healthz/ready";

    /// <summary>Marca os health checks que entram no readiness (dependencias externas).</summary>
    public const string ReadyTag = "ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        // TimeProvider no lugar de um IClock proprio: o framework ja resolve, e ele e
        // substituivel em teste (FakeTimeProvider).
        builder.Services.AddSingleton(TimeProvider.System);

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Mamao.Outbox"))
            .WithTracing(tracing => tracing
                .AddSource("Mamao.*")
                .AddAspNetCoreInstrumentation(o =>
                    // Ruido puro: health check e sondagem de infraestrutura, nao trafego.
                    o.Filter = context => !context.Request.Path.StartsWithSegments("/healthz"))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(
                builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness so responde "o processo esta vivo". Nunca coloque dependencia externa
        // aqui: o container reinicia por causa de um servico de terceiro fora do ar.
        app.MapHealthChecks(LivenessEndpoint, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
        }).AllowAnonymous();

        app.MapHealthChecks(ReadinessEndpoint, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),

            // Degraded tem que reprovar o readiness. No padrao ele responde 200, e uma
            // checagem que devolve Degraded (ex.: migrations pendentes) deixaria o
            // processo receber trafego mesmo sem estar pronto.
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        return app;
    }
}
