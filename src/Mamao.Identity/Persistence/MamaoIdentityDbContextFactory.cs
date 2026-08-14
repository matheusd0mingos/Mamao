using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mamao.Identity.Persistence;

/// <summary>Design-time apenas: `dotnet ef migrations add`.</summary>
public sealed class MamaoIdentityDbContextFactory : IDesignTimeDbContextFactory<MamaoIdentityDbContext>
{
    public MamaoIdentityDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<MamaoIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=mamao_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MamaoIdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options);
}
