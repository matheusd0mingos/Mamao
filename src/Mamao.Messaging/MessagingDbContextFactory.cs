using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mamao.Messaging;

/// <summary>Design-time apenas: `dotnet ef migrations add`.</summary>
public sealed class MessagingDbContextFactory : IDesignTimeDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<MessagingDbContext>()
            .UseNpgsql("Host=localhost;Database=mamao_design_time",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MessagingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options);
}
