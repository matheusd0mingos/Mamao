using System.Net;
using System.Net.Http.Json;
using Mamao.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Mamao.IntegrationTests;

/// <summary>
/// Cobertura por caso de uso, nao por endpoint. O que importa provar aqui e que o dado de
/// negocio e o evento sao gravados na MESMA transacao — a garantia inteira do outbox.
/// </summary>
public class AdmissaoPontaAPontaTests(MamaoApiFactory factory) : IClassFixture<MamaoApiFactory>
{
    [Fact]
    public async Task Admitir_funcionario_grava_o_evento_na_mesma_transacao()
    {
        SkipSeSemDocker();

        var empresa = await factory.CreateTenantAsync("Alfa Outbox", "owner-outbox@teste.local");
        var cargo = await MamaoApiFactory.CreatePositionAsync(empresa.Client, "Tecnico de manutencao");

        var resposta = await empresa.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = "Carlos Mendes",
            positionId = cargo,
            hiredOn = "2026-02-01",
            code = "M-001",
        }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = factory.Services.CreateScope();
        var messaging = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

        var evento = await messaging.Outbox
            .Where(m => m.TenantId == empresa.TenantId && m.Type == "People.EmployeeHired.v1")
            .SingleOrDefaultAsync(Ct);

        evento.ShouldNotBeNull("Sem o evento na outbox, Documents e TimeOff nunca sabem da admissao.");
        evento.ProcessedAt.ShouldBeNull("Quem publica e o Worker, nao a API.");
        evento.Payload.ShouldContain("Carlos Mendes");
    }

    [Fact]
    public async Task Matricula_duplicada_e_recusada_com_erro_de_campo()
    {
        SkipSeSemDocker();

        var empresa = await factory.CreateTenantAsync("Alfa Matricula", "owner-matricula@teste.local");
        var cargo = await MamaoApiFactory.CreatePositionAsync(empresa.Client, "Vigilante");

        var primeiro = await empresa.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = "Ana Lima",
            positionId = cargo,
            hiredOn = "2026-02-01",
            code = "V-100",
        }, Ct);
        primeiro.StatusCode.ShouldBe(HttpStatusCode.Created);

        var segundo = await empresa.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = "Outra Pessoa",
            positionId = cargo,
            hiredOn = "2026-03-01",
            code = "V-100",
        }, Ct);

        segundo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var corpo = await segundo.Content.ReadAsStringAsync(Ct);
        corpo.ShouldContain("employee.duplicate_code");
        corpo.ShouldContain("fieldErrors");
    }

    [Fact]
    public async Task Mesma_matricula_em_empresas_diferentes_e_permitida()
    {
        SkipSeSemDocker();

        var empresaA = await factory.CreateTenantAsync("Alfa Cross", "owner-cross-a@teste.local");
        var empresaB = await factory.CreateTenantAsync("Beta Cross", "owner-cross-b@teste.local");

        // Um cargo POR EMPRESA, com o mesmo nome. Reaproveitar o id da Alfa na Beta nao
        // testaria a matricula: pararia antes, no cargo que nao existe la.
        var cargoA = await MamaoApiFactory.CreatePositionAsync(empresaA.Client, "Vigilante");
        var cargoB = await MamaoApiFactory.CreatePositionAsync(empresaB.Client, "Vigilante");

        (await empresaA.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = "Homonimo",
            positionId = cargoA,
            hiredOn = "2026-02-01",
            code = "X-1",
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // O indice unico e (tenant_id, code): unicidade e por empresa, nao global.
        (await empresaB.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = "Homonimo",
            positionId = cargoB,
            hiredOn = "2026-02-01",
            code = "X-1",
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Validacao_devolve_erro_por_campo_no_formato_que_o_formulario_consome()
    {
        SkipSeSemDocker();

        var empresa = await factory.CreateTenantAsync("Alfa Validacao", "owner-validacao@teste.local");

        var resposta = await empresa.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = "",
            positionId = Guid.Empty,
            hiredOn = "2026-02-01",
            code = (string?)null,
        }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        corpo.ShouldContain("fieldErrors");
        corpo.ShouldContain("fullName");
        corpo.ShouldContain("positionId");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void SkipSeSemDocker()
    {
        if (factory.SkipReason is not null)
            Assert.Skip(factory.SkipReason);
    }
}
