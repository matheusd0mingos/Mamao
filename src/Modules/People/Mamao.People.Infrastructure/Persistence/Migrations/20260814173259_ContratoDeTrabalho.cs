using System;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContratoDeTrabalho : Migration
    {
        /// <summary>
        /// Puramente aditiva: cria a tabela e liga a RLS. Nao precisou de reescrita, ao
        /// contrario das duas anteriores — quando a migration so ACRESCENTA, o scaffold
        /// acerta; o perigo mora em apagar ou preencher.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employment_contracts",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regime = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    schedule_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    weekly_hours = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    compensation_agreement_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_employment_contracts_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "people",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employment_contracts_tenant_id",
                schema: "people",
                table: "employment_contracts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_contracts_tenant_id_employee_id",
                schema: "people",
                table: "employment_contracts",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            // Tabela nova de tenant NASCE com RLS. Esquecer aqui abriria um buraco
            // silencioso: o filtro do EF continuaria certo e o SQL cru veria tudo.
            migrationBuilder.Sql(TenantRls.EnableFor(PeopleDbContext.Schema, "employment_contracts"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TenantRls.DisableFor(PeopleDbContext.Schema, "employment_contracts"));
            migrationBuilder.DropTable(
                name: "employment_contracts",
                schema: "people");
        }
    }
}
