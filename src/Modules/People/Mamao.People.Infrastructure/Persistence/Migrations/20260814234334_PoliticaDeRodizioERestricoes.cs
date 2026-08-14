using System;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoliticaDeRodizioERestricoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "precedence_order",
                schema: "people",
                table: "positions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_restrictions",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    activity_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_restrictions", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_restrictions_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rotation_policies",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tiebreak = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    min_rest_days = table.Column<int>(type: "integer", nullable: false),
                    rest_blocks = table.Column<bool>(type: "boolean", nullable: false),
                    window_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotation_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_restrictions_tenant_id",
                schema: "people",
                table: "employee_restrictions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_restrictions_tenant_id_employee_id",
                schema: "people",
                table: "employee_restrictions",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_restrictions_tenant_id_starts_on",
                schema: "people",
                table: "employee_restrictions",
                columns: new[] { "tenant_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_rotation_policies_tenant_id",
                schema: "people",
                table: "rotation_policies",
                column: "tenant_id",
                unique: true);
            // Camada 3 do isolamento, sempre junto com a tabela: politica de rodizio e
            // restricao sao dados de uma empresa so, e restricao ainda pode carregar
            // informacao de saude no motivo.
            migrationBuilder.Sql(TenantRls.EnableFor("people", "rotation_policies"));
            migrationBuilder.Sql(TenantRls.EnableFor("people", "employee_restrictions"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_restrictions",
                schema: "people");

            migrationBuilder.DropTable(
                name: "rotation_policies",
                schema: "people");

            migrationBuilder.DropColumn(
                name: "precedence_order",
                schema: "people",
                table: "positions");
        }
    }
}
