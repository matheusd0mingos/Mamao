using Xunit;
using System.Reflection;
using Mamao.People.Application;
using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Mamao.People.Infrastructure;
using NetArchTest.Rules;
using Shouldly;

namespace Mamao.ArchitectureTests;

/// <summary>
/// Os testes mais baratos e mais valiosos do projeto: cada um substitui uma convencao que
/// ninguem lembra as 23h de sexta. Regra escrita nao segura ninguem; build quebrado segura.
/// Ver docs/arquitetura/modulos-e-contratos.md.
/// </summary>
public class FronteirasDeModuloTests
{
    private static readonly Assembly PeopleDomain = typeof(Employee).Assembly;
    private static readonly Assembly PeopleApplication = typeof(IPeopleDbContext).Assembly;
    private static readonly Assembly PeopleContracts = typeof(EmployeeId).Assembly;
    private static readonly Assembly PeopleInfrastructure = typeof(PeopleModule).Assembly;

    [Fact]
    public void Dominio_nao_conhece_infraestrutura()
    {
        var resultado = Types.InAssembly(PeopleDomain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql")
            .GetResult();

        FalharCom(resultado, "O dominio precisa ser testavel sem banco e sem HTTP.");
    }

    [Fact]
    public void Contracts_nao_depende_de_nada_alem_do_shared_kernel()
    {
        var resultado = Types.InAssembly(PeopleContracts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql",
                "FluentValidation")
            .GetResult();

        FalharCom(resultado,
            "Contracts e a superficie publica do modulo: no dia da extracao ela vira " +
            "cliente HTTP gerado, entao nao pode arrastar infraestrutura junto.");
    }

    [Fact]
    public void Application_nao_conhece_a_propria_infraestrutura()
    {
        var resultado = Types.InAssembly(PeopleApplication)
            .ShouldNot()
            .HaveDependencyOn(PeopleInfrastructure.GetName().Name)
            .GetResult();

        FalharCom(resultado, "Application depende de IPeopleDbContext, nunca do PeopleDbContext.");
    }

    [Fact]
    public void Modulo_so_enxerga_o_Contracts_de_outro_modulo()
    {
        // Quando existir um segundo modulo, esta lista cresce. A regra e a mesma:
        // Domain/Application/Infrastructure de outro modulo sao invisiveis.
        var proibidos = new[]
        {
            "Mamao.TimeOff.Domain", "Mamao.TimeOff.Application", "Mamao.TimeOff.Infrastructure",
            "Mamao.Work.Domain", "Mamao.Work.Application", "Mamao.Work.Infrastructure",
            "Mamao.Documents.Domain", "Mamao.Documents.Application", "Mamao.Documents.Infrastructure",
            "Mamao.Scheduling.Domain", "Mamao.Scheduling.Application", "Mamao.Scheduling.Infrastructure",
        };

        foreach (var assembly in new[] { PeopleDomain, PeopleApplication, PeopleInfrastructure })
        {
            var resultado = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(proibidos)
                .GetResult();

            FalharCom(resultado, $"{assembly.GetName().Name} atravessou a fronteira de outro modulo.");
        }
    }

    private static void FalharCom(NetArchTest.Rules.TestResult resultado, string porque)
    {
        var infratores = resultado.FailingTypeNames is null
            ? string.Empty
            : string.Join(", ", resultado.FailingTypeNames);

        resultado.IsSuccessful.ShouldBeTrue($"{porque}\nTipos: {infratores}");
    }
}
