using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mamao.SharedKernel.Tenancy;

/// <summary>
/// Camada 3 do isolamento: Row-Level Security no PostgreSQL.
/// Ver docs/adr/0003-multi-tenancy.md.
///
/// Estado no Marco 0: o SQL e o interceptor existem e sao testaveis, mas ficam
/// desligados por padrao (Tenancy:EnableRls). Ligar exige que a aplicacao conecte com
/// um role SEM BYPASSRLS — superusuario ignora policy silenciosamente, que e a forma
/// mais facil de achar que se esta protegido sem estar. Ver deploy/init-db.sql.
/// </summary>
public static class TenantRls
{
    /// <summary>Nome da configuracao de sessao lida pelas policies.</summary>
    public const string SettingName = "app.tenant_id";

    /// <summary>
    /// SQL que habilita RLS numa tabela e cria a policy de isolamento.
    /// Use dentro de uma migration: <c>migrationBuilder.Sql(TenantRls.EnableFor("people", "employees"))</c>.
    /// FORCE faz a policy valer tambem para o dono da tabela.
    /// </summary>
    public static string EnableFor(string schema, string table)
    {
        var policy = $"{table}_tenant_isolation";
        return $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS "{policy}" ON "{schema}"."{table}";
            CREATE POLICY "{policy}" ON "{schema}"."{table}"
                USING      (tenant_id = current_setting('{SettingName}', true)::uuid)
                WITH CHECK (tenant_id = current_setting('{SettingName}', true)::uuid);
            """;
    }

    public static string DisableFor(string schema, string table) => $"""
        DROP POLICY IF EXISTS "{table}_tenant_isolation" ON "{schema}"."{table}";
        ALTER TABLE "{schema}"."{table}" NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE "{schema}"."{table}" DISABLE ROW LEVEL SECURITY;
        """;
}

/// <summary>
/// Define <c>app.tenant_id</c> a cada abertura de conexao. Precisa ser em toda abertura
/// (e nao por transacao com is_local) porque o Npgsql reusa conexoes do pool e o EF nem
/// sempre abre transacao explicita para leitura.
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
        if (!tenantContext.IsResolved)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT set_config('{TenantRls.SettingName}', @tenant, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tenant";
        parameter.Value = tenantContext.Current.ToString();
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(ct);
    }
}
