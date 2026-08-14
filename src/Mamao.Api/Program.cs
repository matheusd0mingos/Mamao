using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Mamao.Api;
using Mamao.Identity;
using Mamao.Identity.Endpoints;
using Mamao.Messaging;
using Mamao.Notifications;
using Mamao.People.Contracts.Events;
using Mamao.People.Infrastructure;
using Mamao.People.Infrastructure.Endpoints;
using Mamao.ServiceDefaults;
using Mamao.Audit;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Tenancy;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("mamao")
    ?? throw new InvalidOperationException("ConnectionStrings:mamao nao configurada.");

// Tenancy antes de tudo: nenhum modulo funciona sem tenant resolvido.
builder.Services.AddOptions<TenancyOptions>()
    .Bind(builder.Configuration.GetSection(TenancyOptions.SectionName));
builder.Services.AddScoped<ITenantContext, TenantContext>();

// Auditoria: o ator vem do token; a escrita vive no DbContext de cada modulo, para entrar
// na MESMA transacao do fato. Ver docs/arquitetura/multi-tenancy-e-seguranca.md#auditoria.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddScoped<TenantSaveChangesInterceptor>();
builder.Services.AddScoped<TenantRlsConnectionInterceptor>();

// As chaves do Data Protection assinam os tokens de recuperacao de senha do Identity.
// Sem persistir, cada restart do container invalida os links ja enviados — e o usuario
// que pediu a recuperacao dez minutos antes fica sem entender por que nao funciona.
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeysPath"] ?? "/var/mamao/keys"))
    .SetApplicationName("mamao");

builder.Services
    .AddNotifications(builder.Configuration, builder.Environment)
    .AddMamaoIdentity(builder.Configuration, connectionString)
    .AddPeopleModule(connectionString)
    .AddAudit(connectionString)
    .AddOutbox(
        builder.Configuration,
        connectionString,
        runPublisher: false, // publicacao e do Worker: job longo nao compete com request HTTP
        typeof(EmployeeHired).Assembly);

// Enum vai como TEXTO no JSON, nunca como numero. Um `status: 2` no contrato obriga o
// frontend a manter uma tabela de numeros e transforma reordenar o enum em quebra
// silenciosa; `status: "DuplicadaNoArquivo"` se explica sozinho no log e no DevTools.
// Vale para todo enum que vier depois (tipo de ausencia, turno, situacao de documento).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<StronglyTypedIdSchemaTransformer>());
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

app.Services.LogEmailConfiguration();

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
app.MapInviteEndpoints();
app.MapPeopleEndpoints();
app.MapOrganizationEndpoints();
app.MapAvailabilityEndpoints();

// Geracao do documento OpenAPI sem subir servidor nem tocar no banco. Usado pelo CI
// para gerar o cliente TypeScript. Ver docs/adr/0009-cliente-gerado-do-openapi.md.
if (await OpenApiDocumentWriter.TryWriteAndExitAsync(app, args))
    return;

app.Run();

/// <summary>Exposto para o WebApplicationFactory dos testes de integracao.</summary>
public partial class Program;
