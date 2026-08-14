using FluentValidation;
using Mamao.People.Application;
using Mamao.People.Application.Employees;
using Mamao.People.Contracts;
using Mamao.People.Infrastructure.Persistence;
using Mamao.SharedKernel.Messaging;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mamao.People.Infrastructure;

/// <summary>
/// Ponto unico de registro do modulo. O Program.cs do host so precisa de uma linha por
/// modulo — ver docs/arquitetura/modulos-e-contratos.md.
/// </summary>
public static class PeopleModule
{
    public const string ConnectionStringName = "mamao";

    public static IServiceCollection AddPeopleModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PeopleDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PeopleDbContext.Schema));

            // snake_case em todo o banco: SQL cru, script de manutencao e ferramenta de BI
            // ficam legiveis sem aspas em todo identificador.
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        services.AddScoped<IPeopleDbContext>(sp => sp.GetRequiredService<PeopleDbContext>());
        services.AddScoped<IPeopleOutbox>(sp => new PeopleOutbox(sp.GetRequiredService<PeopleDbContext>()));
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<EmployeeService>();

        services.AddValidatorsFromAssemblyContaining<CreateEmployeeRequestValidator>(includeInternalTypes: true);

        return services;
    }
}

internal sealed class PeopleOutbox(PeopleDbContext dbContext) : OutboxWriter(dbContext), IPeopleOutbox;
