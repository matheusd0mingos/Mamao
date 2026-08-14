using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.Identity.Persistence.Migrations
{
    /// <summary>
    /// O usuario passa a pertencer a uma empresa; `memberships` deixa de existir.
    /// Ver docs/adr/0020-usuario-pertence-a-empresa.md.
    ///
    /// REESCRITA a mao. A versao gerada apagava `memberships` no primeiro comando e so
    /// depois criava `tenant_id` com uuid zerado — ou seja, jogava fora o unico lugar que
    /// sabia de qual empresa era cada usuario, e deixava todos apontando para um tenant
    /// inexistente. A ordem correta e: criar colunas anulaveis, copiar, conferir, travar,
    /// e so entao apagar a origem.
    /// </summary>
    public partial class UsuarioPertenceAEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Anulaveis por enquanto: NOT NULL agora exigiria um valor padrao falso, que e
            // como se perde a integridade sem ninguem perceber.
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                schema: "identity",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Ativo por padrao, e nao inativo: o scaffold trazia `false`, o que desligaria
            // todo mundo que ja existe.
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // --- Conferencia ANTES de copiar --------------------------------------
            // Usuario com mais de um vinculo nao tem traducao possivel para este modelo, e
            // escolher um por conta propria seria decidir de quem e a conta de alguem.
            // Usuario sem vinculo nenhum e dado orfao. Nos dois casos a migration para e a
            // transacao volta inteira — falhar alto e mais barato que descobrir depois.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicados int;
                    orfaos int;
                BEGIN
                    SELECT count(*) INTO duplicados FROM (
                        SELECT user_id FROM identity.memberships
                        GROUP BY user_id HAVING count(*) > 1
                    ) AS t;

                    IF duplicados > 0 THEN
                        RAISE EXCEPTION
                            '% usuario(s) pertencem a mais de uma empresa. Decida manualmente de quem e cada conta antes de migrar (ver ADR-0020).',
                            duplicados;
                    END IF;

                    SELECT count(*) INTO orfaos
                    FROM identity.users u
                    WHERE NOT EXISTS (SELECT 1 FROM identity.memberships m WHERE m.user_id = u.id);

                    IF orfaos > 0 THEN
                        RAISE EXCEPTION
                            '% usuario(s) sem vinculo com empresa nenhuma. Apague ou vincule antes de migrar.',
                            orfaos;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("""
                UPDATE identity.users AS u
                SET tenant_id = m.tenant_id,
                    role      = m.role,
                    is_active = m.is_active
                FROM identity.memberships AS m
                WHERE m.user_id = u.id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                schema: "identity",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false);

            // Origem so agora, com o destino ja preenchido e travado.
            migrationBuilder.DropTable(
                name: "memberships",
                schema: "identity");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_is_active",
                schema: "identity",
                table: "users",
                columns: new[] { "tenant_id", "is_active" });

            // Cascade: excluir a empresa apaga os usuarios dela, que e o que torna a
            // exclusao de conta pela LGPD uma operacao so.
            migrationBuilder.AddForeignKey(
                name: "fk_users_tenants_tenant_id",
                schema: "identity",
                table: "users",
                column: "tenant_id",
                principalSchema: "identity",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "identity",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Recompoe o vinculo a partir do usuario ANTES de derrubar as colunas: um Down
            // que perde dado nao e rollback, e um segundo incidente.
            migrationBuilder.Sql("""
                INSERT INTO identity.memberships (id, tenant_id, user_id, created_at, is_active, role)
                SELECT gen_random_uuid(), u.tenant_id, u.id, u.created_at, u.is_active, u.role
                FROM identity.users AS u;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_users_tenants_tenant_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_is_active",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "identity",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id",
                schema: "identity",
                table: "memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id_tenant_id",
                schema: "identity",
                table: "memberships",
                columns: new[] { "user_id", "tenant_id" },
                unique: true);
        }
    }
}
