using System.Net.Http.Json;
using Mamao.Identity.Persistence;
using Mamao.Messaging;
using Mamao.People.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Mamao.IntegrationTests;

/// <summary>
/// Postgres de verdade via Testcontainers. Sem mock de banco e sem SQLite in-memory: eles
/// divergem do Postgres exatamente onde importa (RLS, jsonb, tipos de data, colacao).
/// Ver docs/arquitetura/testes.md.
///
/// Requer Docker. Sem daemon disponivel os testes se marcam como skipped em vez de falhar:
/// o CI roda com Docker, a maquina de desenvolvimento nem sempre. O container e construido
/// dentro do try porque o proprio Build() ja falha quando nao ha daemon.
/// </summary>
public sealed class MamaoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public string? SkipReason { get; private set; }

    /// <summary>Conexao do DONO das tabelas. Os testes de RLS derivam dela o role sem BYPASSRLS.</summary>
    public string ConnectionString =>
        _postgres?.GetConnectionString() ?? "Host=localhost;Database=sem_docker";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("mamao")
                .Build();

            await _postgres.StartAsync(Ct);
        }
        catch (Exception ex)
        {
            // Pular sem Docker e conveniente na maquina de desenvolvimento e PERIGOSO em
            // qualquer lugar que decida por esta suite: "Skipped: 10, Failed: 0" se le como
            // verde, e a rede de seguranca desaparece sem ninguem perceber. Foi assim que um
            // cadastro quebrado chegou em producao — o teste que o pegaria estava aqui,
            // escrito, e nao rodou uma unica vez.
            if (Environment.GetEnvironmentVariable("CI") is not null)
            {
                throw new InvalidOperationException(
                    "Docker indisponivel no CI. Os testes de integracao nao podem ser pulados aqui: " +
                    "sem eles a suite passa a aprovar qualquer coisa.", ex);
            }

            SkipReason = $"Docker indisponivel neste ambiente: {ex.Message}";
            _postgres = null;
            return;
        }

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MamaoIdentityDbContext>().Database.MigrateAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<MessagingDbContext>().Database.MigrateAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<PeopleDbContext>().Database.MigrateAsync(Ct);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:mamao", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "chave-de-teste-com-mais-de-trinta-e-dois-bytes");
        builder.UseSetting("Jwt:Issuer", "mamao-tests");
        builder.UseSetting("Jwt:Audience", "mamao-tests");
    }

    /// <summary>Cria uma empresa e devolve um cliente ja autenticado como Owner dela.</summary>
    public async Task<TenantFixture> CreateTenantAsync(string companyName, string email)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register-company", new
        {
            companyName,
            fullName = $"Owner de {companyName}",
            email,
            password = "SenhaDeTeste123",
        }, Ct);

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthPayload>(Ct)
            ?? throw new InvalidOperationException("Resposta de cadastro vazia.");

        var authenticated = CreateClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return new TenantFixture(auth.TenantId, authenticated);
    }

    public sealed record TenantFixture(Guid TenantId, HttpClient Client);

    private sealed record AuthPayload(string AccessToken, Guid TenantId);
}
