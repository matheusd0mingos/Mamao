var builder = DistributedApplication.CreateBuilder(args);

// Postgres com volume: os dados sobrevivem ao restart do container, senao cada F5 recomeca
// o cadastro do zero.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("mamao-pgdata")
    .WithPgAdmin();

var database = postgres.AddDatabase("mamao");

var api = builder.AddProject<Projects.Mamao_Api>("api")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.Mamao_Worker>("worker")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();

// O Angular NAO sobe aqui: o pacote de hosting de Node ainda nao acompanha esta versao do
// Aspire. Rode `npm start` em web/mamao-web — ele aponta para a API pelo proxy do dev
// server. Ver docs/adr/0011-aspire-e-deploy.md.
//
// Producao nao usa este arquivo: la e docker-compose escrito a mao, em deploy/.
_ = api;
