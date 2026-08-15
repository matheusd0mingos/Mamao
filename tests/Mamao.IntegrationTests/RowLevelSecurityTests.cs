using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Tenancy;
using System.Net.Http.Json;
using Npgsql;
using Shouldly;
using Xunit;

namespace Mamao.IntegrationTests;

/// <summary>
/// Camada 3 do isolamento: prova que o PostgreSQL barra o acesso cruzado mesmo quando a
/// aplicacao erra — SQL cru, script de manutencao, IgnoreQueryFilters().
///
/// Os outros testes exercitam a API, que passa pelo filtro global do EF (camada 2). Este
/// vai direto ao banco, com o mesmo role que a aplicacao usa em producao, justamente para
/// pular a camada 2 e testar so o que sobra.
///
/// Ver docs/adr/0003-multi-tenancy.md.
/// </summary>
public class RowLevelSecurityTests(MamaoApiFactory factory) : IClassFixture<MamaoApiFactory>
{
    private const string RoleDaAplicacao = "mamao_app";
    private const string SenhaDoRole = "senha-de-teste";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Postgres_barra_acesso_cruzado_mesmo_com_sql_cru()
    {
        SkipSeSemDocker();

        var alfa = await factory.CreateTenantAsync("Alfa RLS", "owner-rls-a@teste.local");
        var beta = await factory.CreateTenantAsync("Beta RLS", "owner-rls-b@teste.local");

        await CriarFuncionarioAsync(alfa, "Carlos da Alfa");
        await CriarFuncionarioAsync(beta, "Ana da Beta");

        await PrepararRoleDaAplicacaoAsync();

        await using var conexao = new NpgsqlConnection(ConnectionStringDoRole());
        await conexao.OpenAsync(Ct);

        // Sem tenant na sessao — o caso do job de background que esquece de definir.
        // Falha FECHADA: nenhuma linha, em vez de todas.
        (await ContarAsync(conexao, null, filtro: null))
            .ShouldBe(0, "Sem tenant definido a tabela nao pode devolver linha nenhuma.");

        // Com o tenant da Alfa, pedindo tudo: so o que e da Alfa aparece.
        (await ContarAsync(conexao, alfa.TenantId, filtro: null))
            .ShouldBe(1);

        // Pedindo explicitamente os dados da Beta, ainda como Alfa.
        (await ContarAsync(conexao, alfa.TenantId, filtro: beta.TenantId))
            .ShouldBe(0, "VAZAMENTO: a policy deixou ler dado de outra empresa por SQL cru.");
    }

    [Fact]
    public async Task Postgres_recusa_escrita_para_outro_tenant()
    {
        SkipSeSemDocker();

        var alfa = await factory.CreateTenantAsync("Alfa Escrita", "owner-rls-c@teste.local");
        var beta = await factory.CreateTenantAsync("Beta Escrita", "owner-rls-d@teste.local");

        await PrepararRoleDaAplicacaoAsync();

        await using var conexao = new NpgsqlConnection(ConnectionStringDoRole());
        await conexao.OpenAsync(Ct);
        await DefinirTenantAsync(conexao, alfa.TenantId);

        await using var insercao = new NpgsqlCommand(
            """
            INSERT INTO people.employees (id, tenant_id, full_name, normalized_name, hired_on)
            VALUES (gen_random_uuid(), $1, 'Invasor', 'invasor', '2024-01-01')
            """, conexao);
        insercao.Parameters.AddWithValue(beta.TenantId);

        // O WITH CHECK da policy e o que impede gravar para outra empresa. Sem ele, ler
        // estaria protegido e escrever nao — o pior dos dois mundos.
        var erro = await Should.ThrowAsync<PostgresException>(() => insercao.ExecuteNonQueryAsync(Ct));
        erro.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Role_da_aplicacao_nao_pode_ignorar_a_policy()
    {
        SkipSeSemDocker();

        await PrepararRoleDaAplicacaoAsync();

        await using var conexao = new NpgsqlConnection(factory.ConnectionString);
        await conexao.OpenAsync(Ct);

        await using var comando = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = $1", conexao);
        comando.Parameters.AddWithValue(RoleDaAplicacao);

        await using var leitor = await comando.ExecuteReaderAsync(Ct);
        (await leitor.ReadAsync(Ct)).ShouldBeTrue($"O role {RoleDaAplicacao} deveria existir.");

        // Superusuario e BYPASSRLS ignoram policy EM SILENCIO. Se alguem "consertar" a
        // conexao de producao apontando para o dono das tabelas, a camada 3 some sem aviso.
        leitor.GetBoolean(0).ShouldBeFalse("O role da aplicacao nao pode ser superusuario.");
        leitor.GetBoolean(1).ShouldBeFalse("O role da aplicacao nao pode ter BYPASSRLS.");
    }

    private string ConnectionStringDoRole() =>
        new NpgsqlConnectionStringBuilder(factory.ConnectionString)
        {
            Username = RoleDaAplicacao,
            Password = SenhaDoRole,
        }.ConnectionString;

    /// <summary>
    /// Cria o role sem BYPASSRLS e concede o acesso. Em producao isso e feito por
    /// deploy/init-db.sql e pelo DatabaseMigrator; aqui reproduzimos o mesmo estado.
    /// </summary>
    private async Task PrepararRoleDaAplicacaoAsync()
    {
        await using var conexao = new NpgsqlConnection(factory.ConnectionString);
        await conexao.OpenAsync(Ct);

        await using (var criacao = new NpgsqlCommand(
            $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RoleDaAplicacao}') THEN
                    CREATE ROLE {RoleDaAplicacao} LOGIN PASSWORD '{SenhaDoRole}'
                        NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
            END
            $$;
            """, conexao))
        {
            await criacao.ExecuteNonQueryAsync(Ct);
        }

        foreach (var schema in new[] { "identity", "messaging", "people" })
        {
            await using var concessao = new NpgsqlCommand(
                TenantRls.GrantSchemaTo(schema, RoleDaAplicacao), conexao);
            await concessao.ExecuteNonQueryAsync(Ct);
        }

        // Auditoria fora do laco, exatamente como o Worker faz em producao: insert e select,
        // com update e delete revogados. Se este teste concedesse o schema inteiro, ele
        // estaria provando a RLS contra um banco mais permissivo que o de verdade.
        await using var auditoria = new NpgsqlCommand(
            TenantRls.GrantAppendOnlyTo(AuditEntry.Schema, AuditEntry.Table, RoleDaAplicacao), conexao);
        await auditoria.ExecuteNonQueryAsync(Ct);
    }

    private async Task<long> ContarAsync(NpgsqlConnection conexao, Guid? tenant, Guid? filtro)
    {
        await DefinirTenantAsync(conexao, tenant);

        var sql = filtro is null
            ? "SELECT count(*) FROM people.employees"
            : "SELECT count(*) FROM people.employees WHERE tenant_id = $1";

        await using var comando = new NpgsqlCommand(sql, conexao);
        if (filtro is { } valor)
            comando.Parameters.AddWithValue(valor);

        return (long)(await comando.ExecuteScalarAsync(Ct))!;
    }

    private async Task DefinirTenantAsync(NpgsqlConnection conexao, Guid? tenant)
    {
        await using var comando = new NpgsqlCommand(
            $"SELECT set_config('{TenantRls.SettingName}', $1, false)", conexao);
        comando.Parameters.AddWithValue(tenant?.ToString() ?? string.Empty);
        await comando.ExecuteNonQueryAsync(Ct);
    }

    private static async Task CriarFuncionarioAsync(MamaoApiFactory.TenantFixture empresa, string nome)
    {
        var cargo = await MamaoApiFactory.CreatePositionAsync(empresa.Client, "Vigilante");

        var resposta = await empresa.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            fullName = nome,
            positionId = cargo,
            hiredOn = "2024-01-10",
            code = (string?)null,
        }, Ct);

        resposta.EnsureSuccessStatusCode();
    }

    private void SkipSeSemDocker()
    {
        if (factory.SkipReason is not null)
            Assert.Skip(factory.SkipReason);
    }
}
