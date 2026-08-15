using Mamao.Identity;
using Mamao.Messaging;
using Mamao.Notifications;
using Mamao.People.Contracts.Events;
using Mamao.People.Infrastructure;
using Mamao.ServiceDefaults;
using Mamao.Audit;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Tenancy;
using Mamao.Worker;
using Mamao.Worker.Handlers;
using Mamao.SharedKernel.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("mamao")
    ?? throw new InvalidOperationException("ConnectionStrings:mamao nao configurada.");

builder.Services.AddOptions<TenancyOptions>()
    .Bind(builder.Configuration.GetSection(TenancyOptions.SectionName));
builder.Services.AddScoped<ITenantContext, TenantContext>();

// No Worker nao ha requisicao nem usuario: o ator e o proprio processo. Registrar isso
// explicitamente evita que uma acao de background grave auditoria sem autor.
builder.Services.AddSingleton<ICurrentActor, SystemActor>();
builder.Services.AddScoped<TenantSaveChangesInterceptor>();
builder.Services.AddScoped<TenantRlsConnectionInterceptor>();

// AddNotifications antes dos modulos que dependem dele. O modulo People registra
// servicos que injetam IEmailSender; sem este registro o Worker nem constroi o container
// em Development, e em Production quebraria so na hora de usar — que e pior, porque
// passa no deploy e falha no cliente.
builder.Services.AddNotifications(builder.Configuration, builder.Environment);

builder.Services
    .AddMamaoIdentity(builder.Configuration, connectionString)
    .AddPeopleModule(connectionString)
    .AddAudit(connectionString);

// A ORDEM IMPORTA: servicos hospedados sobem na ordem de registro, e o DatabaseMigrator
// bloqueia no StartAsync ate as migrations terminarem. Registrar o publisher antes dele
// faz a primeira leitura da outbox acontecer com a tabela ainda inexistente — ele se
// recupera na batida seguinte, mas polui o log justamente no startup.
//
// Migrations rodam no Worker, com advisory lock — nao em passo separado do pipeline, que
// dessincroniza de codigo. Ver docs/arquitetura/infraestrutura-e-deploy.md.
builder.Services.AddHostedService<DatabaseMigrator>();

// Depois do migrator, pelo mesmo motivo de ordem: ele so tem o que ler quando o schema
// existe. O proprio job espera dois minutos antes da primeira passada.
builder.Services.AddHostedService<DocumentExpiryNotifier>();

builder.Services.AddOutbox(
    builder.Configuration,
    connectionString,
    runPublisher: true,
    typeof(EmployeeHired).Assembly);

// Consumidores de integration event. Registrados aqui, no processo que publica.
builder.Services.AddScoped<IIntegrationEventHandler<EmployeeHired>, LogEmployeeHired>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<Mamao.Messaging.MessagingDbContext>(
        name: "postgres", tags: [ServiceDefaultsExtensions.ReadyTag]);

var app = builder.Build();

app.MapDefaultEndpoints();
app.Run();


/// <summary>Ator dos processos de background: nao ha usuario, e o autor e o Worker.</summary>
internal sealed class SystemActor : ICurrentActor
{
    public Guid? UserId => null;
    public string Name => "Mamão (processo automático)";
    public string? IpAddress => null;
    public string? CorrelationId => System.Diagnostics.Activity.Current?.TraceId.ToString();

    /// <summary>
    /// O processo de fundo nao carrega permissao de ninguem. Responder "sim" aqui daria ao
    /// Worker um poder que nenhum usuario tem — e a regra que ele driblasse passaria a ser
    /// contornavel por qualquer job novo.
    /// </summary>
    public bool Can(string permission) => false;
}
