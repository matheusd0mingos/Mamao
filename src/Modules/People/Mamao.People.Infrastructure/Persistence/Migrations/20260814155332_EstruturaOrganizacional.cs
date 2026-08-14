using System;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Cargo deixa de ser texto no funcionario e vira entidade; setor entra como arvore.
    ///
    /// A migration gerada pelo EF foi REESCRITA a mao, e o motivo importa: ela apagava
    /// position_name antes de existir qualquer cargo e preenchia position_id com um uuid
    /// zerado — em qualquer banco com dados, a chave estrangeira quebraria na criacao, e
    /// se nao quebrasse o cadastro teria perdido o cargo de todo mundo. Aqui a ordem e:
    /// criar tabelas → semear cargos a partir do texto existente → ligar → so entao apagar.
    ///
    /// A ordem tambem e o que permite rodar isto com o sistema no ar: nenhum passo deixa a
    /// tabela num estado que a versao anterior da aplicacao nao saiba ler.
    /// </summary>
    public partial class EstruturaOrganizacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "departments",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_departments_departments_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "people",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                });

            // NULLABLE por enquanto. Vira NOT NULL depois do backfill — declarar NOT NULL
            // agora exigiria um valor padrao falso, que e como se perde a integridade.
            migrationBuilder.AddColumn<Guid>(
                name: "position_id",
                schema: "people",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                schema: "people",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_id",
                schema: "people",
                table: "employees",
                type: "uuid",
                nullable: true);

            // --- Backfill -----------------------------------------------------------
            // Um cargo por (tenant, nome dobrado). A dobra em SQL espelha Position.Normalize:
            // minusculas, acento traduzido caractere a caractere, o que nao for letra ou
            // digito vira espaco, espacos colapsados. translate() em vez de unaccent()
            // porque unaccent e extensao, e o banco de producao nao instala extensao.
            migrationBuilder.Sql("""
                INSERT INTO people.positions (id, tenant_id, name, normalized_name)
                SELECT DISTINCT ON (e.tenant_id, folded.value)
                       gen_random_uuid(), e.tenant_id, btrim(e.position_name), folded.value
                FROM people.employees AS e
                CROSS JOIN LATERAL (
                    SELECT btrim(regexp_replace(
                        translate(lower(e.position_name),
                                  'áàâãäéèêëíìîïóòôõöúùûüçñ',
                                  'aaaaaeeeeiiiiooooouuuucn'),
                        '[^a-z0-9]+', ' ', 'g')) AS value
                ) AS folded
                WHERE btrim(e.position_name) <> ''
                ORDER BY e.tenant_id, folded.value, e.position_name;
                """);

            migrationBuilder.Sql("""
                UPDATE people.employees AS e
                SET position_id = p.id
                FROM people.positions AS p
                WHERE p.tenant_id = e.tenant_id
                  AND p.normalized_name = btrim(regexp_replace(
                        translate(lower(e.position_name),
                                  'áàâãäéèêëíìîïóòôõöúùûüçñ',
                                  'aaaaaeeeeiiiiooooouuuucn'),
                        '[^a-z0-9]+', ' ', 'g'));
                """);

            // Rede de seguranca: se alguma linha sobrou sem cargo, a migration PARA aqui,
            // com a transacao inteira desfeita. Deixar passar significaria descobrir o
            // problema so quando o NOT NULL abaixo falhasse, com mensagem pior.
            migrationBuilder.Sql("""
                DO $$
                DECLARE orfaos int;
                BEGIN
                    SELECT count(*) INTO orfaos FROM people.employees WHERE position_id IS NULL;
                    IF orfaos > 0 THEN
                        RAISE EXCEPTION 'Backfill de cargo deixou % funcionario(s) sem cargo.', orfaos;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "position_id",
                schema: "people",
                table: "employees",
                type: "uuid",
                nullable: false);

            migrationBuilder.DropColumn(
                name: "position_name",
                schema: "people",
                table: "employees");

            // --- Indices e chaves ---------------------------------------------------
            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_department_id",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_manager_id",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "manager_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_position_id",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "position_id" });

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id",
                schema: "people",
                table: "departments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_parent_id_name",
                schema: "people",
                table: "departments",
                columns: new[] { "tenant_id", "parent_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_path",
                schema: "people",
                table: "departments",
                columns: new[] { "tenant_id", "path" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id",
                schema: "people",
                table: "positions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_normalized_name",
                schema: "people",
                table: "positions",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_departments_department_id",
                schema: "people",
                table: "employees",
                column: "department_id",
                principalSchema: "people",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_employees_manager_id",
                schema: "people",
                table: "employees",
                column: "manager_id",
                principalSchema: "people",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_positions_position_id",
                schema: "people",
                table: "employees",
                column: "position_id",
                principalSchema: "people",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // --- Isolamento ---------------------------------------------------------
            // Tabela nova que pertence a tenant NASCE com RLS. Esquecer aqui abriria um
            // buraco silencioso: o filtro do EF continuaria certo e o SQL cru veria tudo.
            migrationBuilder.Sql(TenantRls.EnableFor(PeopleDbContext.Schema, "departments"));
            migrationBuilder.Sql(TenantRls.EnableFor(PeopleDbContext.Schema, "positions"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TenantRls.DisableFor(PeopleDbContext.Schema, "positions"));
            migrationBuilder.Sql(TenantRls.DisableFor(PeopleDbContext.Schema, "departments"));

            migrationBuilder.AddColumn<string>(
                name: "position_name",
                schema: "people",
                table: "employees",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            // Volta o texto para a coluna antes de derrubar as tabelas: um Down que perde
            // dado nao e rollback, e um segundo incidente.
            migrationBuilder.Sql("""
                UPDATE people.employees AS e
                SET position_name = p.name
                FROM people.positions AS p
                WHERE p.id = e.position_id;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_employees_departments_department_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_employees_manager_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_positions_position_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropTable(
                name: "departments",
                schema: "people");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "people");

            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_department_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_manager_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_position_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "department_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "manager_id",
                schema: "people",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "position_id",
                schema: "people",
                table: "employees");
        }
    }
}
