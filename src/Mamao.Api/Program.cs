using System.Threading.RateLimiting;
using Mamao.Api;
using Mamao.Identity;
using Mamao.Identity.Endpoints;
using Mamao.Messaging;
using Mamao.People.Contracts.Events;
using Mamao.People.Infrastructure;
using Mamao.People.Infrastructure.Endpoints;
using Mamao.ServiceDefaults;
using Mamao.SharedKernel.Tenancy;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("mamao")
    ?? throw new InvalidOperationException("ConnectionStrings:mamao nao configurada.");

// Tenancy antes de tudo: nenhum modulo funciona sem tenant resolvido.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantSaveChangesInterceptor>();

builder.Services
    .AddMamaoIdentity(builder.Configuration, connectionString)
    .AddPeopleModule(connectionString)
    .AddOutbox(
        builder.Configuration,
        connectionString,
        runPublisher: false, // publicacao e do Worker: job longo nao compete com request HTTP
        typeof(EmployeeHired).Assembly);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Mamao.Identity.Persistence.MamaoIdentityDbContext>(
        name: "postgres", tags: [ServiceDefaultsExtensions.ReadyTag])
    .AddCheck<PendingMigrationsHealthCheck>(
        PendingMigrationsHealthCheck.Name, tags: [ServiceDefaultsExtensions.ReadyTag]);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login e refresh sao o alvo obvio de forca bruta.
    options.AddPolicy(AuthEndpoints.AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

const string CorsPolicy = "mamao-web";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapAuthEndpoints();
app.MapPeopleEndpoints();

// Geracao do documento OpenAPI sem subir servidor nem tocar no banco. Usado pelo CI
// para gerar o cliente TypeScript. Ver docs/adr/0009-cliente-gerado-do-openapi.md.
if (await OpenApiDocumentWriter.TryWriteAndExitAsync(app, args))
    return;

app.Run();

/// <summary>Exposto para o WebApplicationFactory dos testes de integracao.</summary>
public partial class Program;
