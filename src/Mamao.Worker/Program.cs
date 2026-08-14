using Mamao.Identity;
using Mamao.Messaging;
using Mamao.People.Contracts.Events;
using Mamao.People.Infrastructure;
using Mamao.ServiceDefaults;
using Mamao.SharedKernel.Tenancy;
using Mamao.Worker;
using Mamao.Worker.Handlers;
using Mamao.SharedKernel.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("mamao")
    ?? throw new InvalidOperationException("ConnectionStrings:mamao nao configurada.");

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantSaveChangesInterceptor>();

builder.Services
    .AddMamaoIdentity(builder.Configuration, connectionString)
    .AddPeopleModule(connectionString);

// A ORDEM IMPORTA: servicos hospedados sobem na ordem de registro, e o DatabaseMigrator
// bloqueia no StartAsync ate as migrations terminarem. Registrar o publisher antes dele
// faz a primeira leitura da outbox acontecer com a tabela ainda inexistente — ele se
// recupera na batida seguinte, mas polui o log justamente no startup.
//
// Migrations rodam no Worker, com advisory lock — nao em passo separado do pipeline, que
// dessincroniza de codigo. Ver docs/arquitetura/infraestrutura-e-deploy.md.
builder.Services.AddHostedService<DatabaseMigrator>();

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
