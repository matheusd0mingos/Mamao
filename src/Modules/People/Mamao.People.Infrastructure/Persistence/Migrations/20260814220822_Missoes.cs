using System;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Missoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "missions",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    on = table.Column<DateOnly>(type: "date", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    required_people = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_missions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mission_assignments",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_mission_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mission_assignments_missions_mission_id",
                        column: x => x.mission_id,
                        principalSchema: "people",
                        principalTable: "missions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mission_assignments_tenant_id",
                schema: "people",
                table: "mission_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mission_assignments_tenant_id_employee_id",
                schema: "people",
                table: "mission_assignments",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mission_assignments_tenant_id_mission_id_employee_id",
                schema: "people",
                table: "mission_assignments",
                columns: new[] { "tenant_id", "mission_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_missions_tenant_id",
                schema: "people",
                table: "missions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_missions_tenant_id_on",
                schema: "people",
                table: "missions",
                columns: new[] { "tenant_id", "on" });

            migrationBuilder.Sql(TenantRls.EnableFor("people", "missions"));
            migrationBuilder.Sql(TenantRls.EnableFor("people", "mission_assignments"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TenantRls.DisableFor("people", "mission_assignments"));
            migrationBuilder.Sql(TenantRls.DisableFor("people", "missions"));

            migrationBuilder.DropTable(
                name: "mission_assignments",
                schema: "people");

            migrationBuilder.DropTable(
                name: "missions",
                schema: "people");
        }
    }
}
