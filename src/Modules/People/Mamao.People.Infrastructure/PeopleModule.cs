using FluentValidation;
using Mamao.People.Application;
using Mamao.People.Application.Availability;
using Mamao.People.Application.Missions;
using Mamao.People.Application.Search;
using Mamao.People.Application.Today;
using Mamao.People.Application.Work;
using Mamao.People.Application.Employees;
using Mamao.People.Application.Employees.Import;
using Mamao.People.Application.Organization;
using Mamao.People.Contracts;
using Mamao.People.Infrastructure.Persistence;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Messaging;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

            // Camada 3: define app.tenant_id na sessao para as policies do Postgres.
            if (sp.GetRequiredService<IOptions<TenancyOptions>>().Value.EnableRls)
                options.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
        });

        services.AddScoped<IPeopleDbContext>(sp => sp.GetRequiredService<PeopleDbContext>());

        // Auditoria ligada ao DbContext DESTE modulo: e o que faz o registro entrar na
        // mesma transacao do dado de negocio.
        services.AddScoped<IAuditLog>(sp => new AuditLogWriter(
            sp.GetRequiredService<PeopleDbContext>(),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ICurrentActor>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IPeopleOutbox>(sp => new PeopleOutbox(sp.GetRequiredService<PeopleDbContext>()));
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<ContractService>();
        services.AddScoped<EmployeeImportService>();
        services.AddScoped<DepartmentService>();
        services.AddScoped<PositionService>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<AbsenceRequestService>();
        services.AddScoped<MissionService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<RestrictionService>();
        services.AddScoped<TodayService>();
        services.AddScoped<SearchService>();

        services.AddValidatorsFromAssemblyContaining<CreateEmployeeRequestValidator>(includeInternalTypes: true);

        return services;
    }
}

internal sealed class PeopleOutbox(PeopleDbContext dbContext) : OutboxWriter(dbContext), IPeopleOutbox;
