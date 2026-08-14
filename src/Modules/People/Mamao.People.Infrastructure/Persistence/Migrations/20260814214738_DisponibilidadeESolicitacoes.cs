using System;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisponibilidadeESolicitacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absence_requests",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_absence_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "occupancies",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupancies", x => x.id);
                    table.ForeignKey(
                        name: "fk_occupancies_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absence_requests_tenant_id",
                schema: "people",
                table: "absence_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_absence_requests_tenant_id_employee_id_starts_on",
                schema: "people",
                table: "absence_requests",
                columns: new[] { "tenant_id", "employee_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_absence_requests_tenant_id_status_starts_on",
                schema: "people",
                table: "absence_requests",
                columns: new[] { "tenant_id", "status", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_occupancies_tenant_id",
                schema: "people",
                table: "occupancies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_occupancies_tenant_id_employee_id_starts_on",
                schema: "people",
                table: "occupancies",
                columns: new[] { "tenant_id", "employee_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_occupancies_tenant_id_source_source_id",
                schema: "people",
                table: "occupancies",
                columns: new[] { "tenant_id", "source", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_occupancies_tenant_id_starts_on_ends_on",
                schema: "people",
                table: "occupancies",
                columns: new[] { "tenant_id", "starts_on", "ends_on" });

            // Terceira camada de isolamento: mesmo com um filtro esquecido no codigo, o
            // Postgres nao devolve linha de outro cliente. Vale para as duas tabelas —
            // ocupacao diz onde a pessoa esta, e solicitacao carrega motivo de ausencia,
            // que as vezes e informacao de saude. Ver docs/adr/0003-multi-tenancy.md.
            migrationBuilder.Sql(TenantRls.EnableFor("people", "occupancies"));
            migrationBuilder.Sql(TenantRls.EnableFor("people", "absence_requests"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TenantRls.DisableFor("people", "absence_requests"));
            migrationBuilder.Sql(TenantRls.DisableFor("people", "occupancies"));

            migrationBuilder.DropTable(
                name: "absence_requests",
                schema: "people");

            migrationBuilder.DropTable(
                name: "occupancies",
                schema: "people");
        }
    }
}
