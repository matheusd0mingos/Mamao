using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mamao.SharedKernel.Tenancy;

/// <summary>
/// Camada 3 do isolamento: Row-Level Security no PostgreSQL.
/// Ver docs/adr/0003-multi-tenancy.md.
///
/// As camadas 1 e 2 (contexto de tenant e filtro global do EF) cobrem as consultas que
/// passam pelo DbSet. Esta cobre o resto: SQL cru, script de manutencao, ferramenta de BI
/// e o dia em que alguem usar IgnoreQueryFilters() para destravar um bug.
///
/// Exige que a aplicacao conecte com um role SEM BYPASSRLS. Superusuario ignora policy em
/// silencio, que e a forma mais facil de achar que se esta protegido sem estar.
/// Ver deploy/init-db.sql.
/// </summary>
public static class TenantRls
{
    /// <summary>Nome da configuracao de sessao lida pelas policies.</summary>
    public const string SettingName = "app.tenant_id";

    /// <summary>
    /// Expressao do tenant corrente dentro da policy.
    ///
    /// O NULLIF e essencial: sem tenant definido, current_setting devolve string vazia, e
    /// ''::uuid **lanca erro** em vez de negar. Com NULLIF vira NULL, a comparacao da
    /// falso e a tabela simplesmente nao devolve linha — que e o comportamento seguro.
    /// </summary>
    private const string TenantAtual = $"NULLIF(current_setting('{SettingName}', true), '')::uuid";

    /// <summary>
    /// Habilita RLS numa tabela e cria a policy de isolamento.
    /// Use dentro de uma migration: <c>migrationBuilder.Sql(TenantRls.EnableFor("people", "employees"))</c>.
    ///
    /// FORCE faz a policy valer tambem para o dono da tabela — sem ele, o usuario que roda
    /// as migrations continuaria enxergando tudo.
    /// </summary>
    public static string EnableFor(string schema, string table)
    {
        var policy = $"{table}_tenant_isolation";

        return $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS "{policy}" ON "{schema}"."{table}";
            CREATE POLICY "{policy}" ON "{schema}"."{table}"
                USING      (tenant_id = {TenantAtual})
                WITH CHECK (tenant_id = {TenantAtual});
            """;
    }

    public static string DisableFor(string schema, string table) => $"""
        DROP POLICY IF EXISTS "{table}_tenant_isolation" ON "{schema}"."{table}";
        ALTER TABLE "{schema}"."{table}" NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE "{schema}"."{table}" DISABLE ROW LEVEL SECURITY;
        """;

    /// <summary>
    /// Concede ao role da aplicacao o acesso as tabelas de um schema, e define o padrao
    /// para as que ainda vao existir.
    ///
    /// Roda depois das migrations, no Worker: os schemas sao criados por elas, entao nao da
    /// para conceder antes. Idempotente.
    /// </summary>
    /// <summary>
    /// Cria (ou atualiza a senha do) role da aplicacao. Idempotente, roda a cada deploy.
    ///
    /// Fica aqui e nao num script de init do Postgres porque aquele roda uma unica vez, na
    /// criacao do volume: trocar a senha depois exigiria recriar o banco.
    ///
    /// NOSUPERUSER e NOBYPASSRLS nao sao detalhe — sao o que faz a policy valer.
    /// </summary>
    public static string EnsureRole(string role, string password) => $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                ALTER ROLE "{role}" WITH LOGIN PASSWORD '{password}'
                    NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            ELSE
                CREATE ROLE "{role}" WITH LOGIN PASSWORD '{password}'
                    NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            END IF;
        END
        $$;
        """;

    /// <summary>
    /// Acesso APPEND-ONLY: insere e le, nunca altera nem apaga.
    ///
    /// Fica aqui e nao na migration porque o grant e reaplicado a cada startup do Worker —
    /// se o REVOKE morasse so na migration, o proximo <see cref="GrantSchemaTo"/> devolveria
    /// UPDATE e DELETE em silencio, e ninguem perceberia ate precisar da auditoria como
    /// prova. Ver docs/arquitetura/multi-tenancy-e-seguranca.md#auditoria.
    /// </summary>
    public static string GrantAppendOnlyTo(string schema, string table, string role) => $"""
        GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
        GRANT SELECT, INSERT ON "{schema}"."{table}" TO "{role}";
        REVOKE UPDATE, DELETE, TRUNCATE ON "{schema}"."{table}" FROM "{role}";
        """;

    public static string GrantSchemaTo(string schema, string role) => $"""
        GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "{schema}" TO "{role}";
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA "{schema}" TO "{role}";
        ALTER DEFAULT PRIVILEGES IN SCHEMA "{schema}"
            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{role}";
        ALTER DEFAULT PRIVILEGES IN SCHEMA "{schema}"
            GRANT USAGE, SELECT ON SEQUENCES TO "{role}";
        """;
}

public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Liga o interceptor que define <c>app.tenant_id</c> na sessao. Desligado, as policies
    /// continuam ativas no banco e nao devolvem linha nenhuma — de proposito: e melhor a
    /// aplicacao parar de funcionar do que rodar sem a rede de seguranca sem ninguem notar.
    /// </summary>
    public bool EnableRls { get; set; } = true;

    /// <summary>Role sem BYPASSRLS que a API usa para conectar.</summary>
    public string ApplicationRole { get; set; } = "mamao_app";

    /// <summary>
    /// Senha do role, usada pelo Worker para cria-lo/atualiza-lo no startup.
    /// Vazio pula a criacao — util em desenvolvimento, onde se conecta como dono.
    /// </summary>
    public string ApplicationRolePassword { get; set; } = string.Empty;
}

/// <summary>
/// Define <c>app.tenant_id</c> a cada abertura de conexao.
///
/// Em toda abertura, e nao por transacao com is_local, porque o EF nem sempre abre
/// transacao explicita para leitura. E **sempre**, inclusive quando nao ha tenant
/// resolvido: o Npgsql reusa conexoes do pool, entao sair sem escrever deixaria o
/// tenant da requisicao anterior valendo para a proxima. Nesse caso gravamos string
/// vazia, que a policy traduz para "nenhuma linha".
/// </summary>
public sealed class TenantRlsConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        var tenant = tenantContext.IsResolved ? tenantContext.Current.ToString() : string.Empty;

        await using var command = connection.CreateCommand();

        // Parametro NOMEADO com placeholder nomeado. Misturar com o posicional $1 faz o
        // Npgsql nao ligar os dois e o comando falha com "bind message supplies 0
        // parameters" — e como isto roda na abertura de toda conexao, derruba tudo.
        command.CommandText = $"SELECT set_config('{TenantRls.SettingName}', @tenant, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenant;
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(ct);
    }
}
