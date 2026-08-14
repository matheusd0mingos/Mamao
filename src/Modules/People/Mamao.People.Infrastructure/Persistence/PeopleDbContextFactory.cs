using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mamao.People.Infrastructure.Persistence;

/// <summary>
/// Usado apenas pelo `dotnet ef` para gerar migrations. A connection string aqui nunca e
/// usada em execucao — o design-time so precisa do modelo, nao do banco.
/// </summary>
public sealed class PeopleDbContextFactory : IDesignTimeDbContextFactory<PeopleDbContext>
{
    public PeopleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql("Host=localhost;Database=mamao_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PeopleDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.Set(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        return new PeopleDbContext(options, tenantContext);
    }
}
