using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuscaSemAcento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_title",
                schema: "people",
                table: "work_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                schema: "people",
                table: "missions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                schema: "people",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Preenche as colunas dobradas nas linhas que ja existem.
            //
            // O NO FORCE nao e opcional: as tabelas tem FORCE ROW LEVEL SECURITY e a policy
            // le `app.tenant_id`, que nao existe durante a migration. Sem ele o UPDATE
            // enxerga ZERO linhas e nao reclama — a busca ficaria vazia e o rodizio pararia
            // de contar historico, os dois em silencio.
            //
            // A dobra tem que casar com Position.Normalize: minusculas, sem acento, so
            // letras e digitos, espaco unico.
            migrationBuilder.Sql("""
                ALTER TABLE people.employees   NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE people.missions    NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE people.work_items  NO FORCE ROW LEVEL SECURITY;

                UPDATE people.employees SET normalized_name = trim(regexp_replace(
                    regexp_replace(translate(lower(full_name),
                        'áàâãäéèêëíìîïóòôõöúùûüçñ', 'aaaaaeeeeiiiiooooouuuucn'),
                        '[^a-z0-9]+', ' ', 'g'), '\s+', ' ', 'g'))
                WHERE normalized_name = '';

                UPDATE people.missions SET normalized_name = trim(regexp_replace(
                    regexp_replace(translate(lower(name),
                        'áàâãäéèêëíìîïóòôõöúùûüçñ', 'aaaaaeeeeiiiiooooouuuucn'),
                        '[^a-z0-9]+', ' ', 'g'), '\s+', ' ', 'g'))
                WHERE normalized_name = '';

                UPDATE people.work_items SET normalized_title = trim(regexp_replace(
                    regexp_replace(translate(lower(title),
                        'áàâãäéèêëíìîïóòôõöúùûüçñ', 'aaaaaeeeeiiiiooooouuuucn'),
                        '[^a-z0-9]+', ' ', 'g'), '\s+', ' ', 'g'))
                WHERE normalized_title = '';

                ALTER TABLE people.employees   FORCE ROW LEVEL SECURITY;
                ALTER TABLE people.missions    FORCE ROW LEVEL SECURITY;
                ALTER TABLE people.work_items  FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_missions_tenant_id_normalized_name_on",
                schema: "people",
                table: "missions",
                columns: new[] { "tenant_id", "normalized_name", "on" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_normalized_name",
                schema: "people",
                table: "employees",
                columns: new[] { "tenant_id", "normalized_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_missions_tenant_id_normalized_name_on",
                schema: "people",
                table: "missions");

            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_normalized_name",
                schema: "people",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "normalized_title",
                schema: "people",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                schema: "people",
                table: "missions");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                schema: "people",
                table: "employees");
        }
    }
}
