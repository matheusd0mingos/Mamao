using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Mamao.IntegrationTests;

/// <summary>
/// O teste mais importante da suite. Ele varre os endpoints de listagem por descoberta,
/// nao por lista escrita a mao — assim endpoint novo ja nasce coberto.
/// Ver docs/adr/0003-multi-tenancy.md.
/// </summary>
public class VazamentoEntreTenantsTests(MamaoApiFactory factory) : IClassFixture<MamaoApiFactory>
{
    [Fact]
    public async Task Nenhum_endpoint_de_listagem_devolve_dado_de_outro_tenant()
    {
        SkipSeSemDocker();

        var empresaA = await factory.CreateTenantAsync("Manutencao Alfa", "owner-a@teste.local");
        var empresaB = await factory.CreateTenantAsync("Seguranca Beta", "owner-b@teste.local");

        await CriarFuncionarioAsync(empresaA, "Carlos da Alfa", "Eletricista");
        var idDaBeta = await CriarFuncionarioAsync(empresaB, "Ana da Beta", "Vigilante");

        // Marcadores que NAO podem aparecer para a empresa A, em nenhuma resposta.
        var marcadores = new[] { idDaBeta, "Ana da Beta", empresaB.TenantId.ToString() };

        foreach (var rota in RotasDeListagem())
        {
            var resposta = await empresaA.Client.GetAsync(rota, Ct);

            if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                continue;

            resposta.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {rota} deveria responder para a empresa A.");

            var corpo = await resposta.Content.ReadAsStringAsync(Ct);

            foreach (var marcador in marcadores)
            {
                corpo.ShouldNotContain(marcador,
                    Case.Sensitive,
                    $"VAZAMENTO ENTRE TENANTS: GET {rota} devolveu '{marcador}', que pertence a outra empresa.");
            }
        }
    }

    [Fact]
    public async Task Buscar_funcionario_de_outro_tenant_por_id_responde_nao_encontrado()
    {
        SkipSeSemDocker();

        var empresaA = await factory.CreateTenantAsync("Alfa Detalhe", "owner-a2@teste.local");
        var empresaB = await factory.CreateTenantAsync("Beta Detalhe", "owner-b2@teste.local");

        var idDaBeta = await CriarFuncionarioAsync(empresaB, "Pedro da Beta", "Vigilante");

        var resposta = await empresaA.Client.GetAsync($"/api/v1/employees/{idDaBeta}", Ct);

        // 404 e nao 403: confirmar a existencia do recurso ja seria vazamento.
        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Requisicao_sem_token_nao_acessa_dado_de_ninguem()
    {
        SkipSeSemDocker();

        var anonimo = factory.CreateClient();

        var resposta = await anonimo.GetAsync("/api/v1/employees", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Descobre as rotas GET sem parametro — as "listagens" — a partir do proprio
    /// EndpointDataSource. E isso que faz o teste cobrir o endpoint que ainda nao existe.
    /// </summary>
    private IEnumerable<string> RotasDeListagem()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var metodos = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!metodos.Contains(HttpMethods.Get))
                continue;

            var rota = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(rota) || rota.Contains('{'))
                continue;

            if (!rota.StartsWith("/api/", StringComparison.Ordinal))
                continue;

            yield return rota;
        }
    }

    private static async Task<string> CriarFuncionarioAsync(
        MamaoApiFactory.TenantFixture empresa, string nome, string cargo)
    {
        var resposta = await empresa.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = nome,
            positionName = cargo,
            hiredOn = "2026-01-10",
            code = (string?)null,
        }, Ct);

        resposta.EnsureSuccessStatusCode();

        var criado = await resposta.Content.ReadFromJsonAsync<FuncionarioCriado>(Ct);
        return criado!.Id;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void SkipSeSemDocker()
    {
        if (factory.SkipReason is not null)
            Assert.Skip(factory.SkipReason);
    }

    private sealed record FuncionarioCriado(string Id);
}
