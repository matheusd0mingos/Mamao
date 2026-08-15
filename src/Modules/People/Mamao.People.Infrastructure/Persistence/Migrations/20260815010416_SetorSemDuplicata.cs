using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SetorSemDuplicata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_departments_tenant_id_parent_id_name",
                schema: "people",
                table: "departments");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "people",
                table: "departments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "created_by_name",
                schema: "people",
                table: "departments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                schema: "people",
                table: "departments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            // Preenche o nome dobrado ANTES do indice unico. Sem isto toda linha existente
            // ficaria com string vazia.
            //
            // ATENCAO — vale para TODA migration de dados neste banco: as tabelas tem
            // FORCE ROW LEVEL SECURITY, e a policy le `app.tenant_id`, que nao existe
            // durante a migration. Sem desligar o FORCE, este UPDATE enxerga ZERO linhas e
            // nao atualiza nada, EM SILENCIO — o DDL passa, o dado nao muda, e so se
            // descobre olhando a tabela. Aconteceu na primeira versao desta migration.
            //
            // A dobra tem que casar com Position.Normalize (minusculas, sem acento, so
            // letras e digitos, espaco unico). Limitacao conhecida: letra acentuada fora da
            // tabela abaixo e descartada aqui e mantida no C#; para pt-BR a tabela cobre.
            migrationBuilder.Sql("""
                ALTER TABLE people.departments NO FORCE ROW LEVEL SECURITY;

                UPDATE people.departments
                SET normalized_name = trim(regexp_replace(
                        regexp_replace(
                            translate(lower(name),
                                'áàâãäéèêëíìîïóòôõöúùûüçñ',
                                'aaaaaeeeeiiiiooooouuuucn'),
                            '[^a-z0-9]+', ' ', 'g'),
                        '\s+', ' ', 'g')),
                    created_by_name = '—'
                WHERE normalized_name = '';

                ALTER TABLE people.departments FORCE ROW LEVEL SECURITY;
                """);

            // NULLS NOT DISTINCT, e nao um CreateIndex comum.
            //
            // No Postgres, NULL e distinto de NULL num indice unico — e setor de primeiro
            // nivel tem parent_id NULO. Sem esta clausula, dois setores RAIZ com o mesmo
            // nome passariam pelo indice sem reclamar, que e justamente o caso mais comum:
            // o dono cria "Seção Técnica" e o TI cria "Secao Tecnica", os dois na raiz.
            // O indice antigo tinha o mesmo furo.
            //
            // Se ja existirem dois setores que so o acento distinguia, o indice recusa e a
            // migration PARA. E o comportamento certo: fundir setor por conta propria
            // moveria gente de escala sem ninguem decidir.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_departments_tenant_id_parent_id_normalized_name
                    ON people.departments (tenant_id, parent_id, normalized_name)
                    NULLS NOT DISTINCT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_departments_tenant_id_parent_id_normalized_name",
                schema: "people",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "people",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "created_by_name",
                schema: "people",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                schema: "people",
                table: "departments");

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_parent_id_name",
                schema: "people",
                table: "departments",
                columns: new[] { "tenant_id", "parent_id", "name" },
                unique: true);
        }
    }
}
