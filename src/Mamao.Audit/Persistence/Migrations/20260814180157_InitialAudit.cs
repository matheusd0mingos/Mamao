using System;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.Audit.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Schema de auditoria. Ver docs/arquitetura/multi-tenancy-e-seguranca.md#auditoria.
    /// </summary>
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    subject_label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_occurred_at",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_subject_type_subject_id",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "subject_type", "subject_id" });

            // Tabela de tenant nasce com RLS, como toda outra.
            migrationBuilder.Sql(TenantRls.EnableFor(AuditEntry.Schema, AuditEntry.Table));

            // O grant append-only NAO fica aqui: ele e reaplicado a cada startup do Worker,
            // que e onde o role da aplicacao e criado. Ver TenantRls.GrantAppendOnlyTo.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");
        }
    }
}
