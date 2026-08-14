using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mamao.People.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Camada 3 do isolamento. O SQL vem de TenantRls para que a policy seja a mesma
            // em toda tabela — divergir aqui daria uma protegida de um jeito e outra de outro.
            // Ver docs/adr/0003-multi-tenancy.md.
            migrationBuilder.Sql(TenantRls.EnableFor(PeopleDbContext.Schema, "employees"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TenantRls.DisableFor(PeopleDbContext.Schema, "employees"));
        }
    }
}
