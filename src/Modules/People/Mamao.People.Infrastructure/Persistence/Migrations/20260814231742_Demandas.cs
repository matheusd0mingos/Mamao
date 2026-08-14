using System;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Demandas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_items",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_items_departments_department_id",
                        column: x => x.department_id,
                        principalSchema: "people",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_items_employees_assignee_id",
                        column: x => x.assignee_id,
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_tenant_id",
                schema: "people",
                table: "work_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_tenant_id_assignee_id_status",
                schema: "people",
                table: "work_items",
                columns: new[] { "tenant_id", "assignee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_tenant_id_department_id_status",
                schema: "people",
                table: "work_items",
                columns: new[] { "tenant_id", "department_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_tenant_id_status_due_on",
                schema: "people",
                table: "work_items",
                columns: new[] { "tenant_id", "status", "due_on" });

            // Camada 3 do isolamento. Sem esta linha a tabela nasce sem policy: o filtro do
            // EF ainda protege as consultas do DbSet, mas um SQL cru ou um IgnoreQueryFilters
            // passaria por cima — e ninguem descobriria ate ser tarde.
            migrationBuilder.Sql(TenantRls.EnableFor("people", "work_items"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_items",
                schema: "people");
        }
    }
}
