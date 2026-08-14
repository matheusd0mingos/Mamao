using Xunit;
using Mamao.People.Infrastructure.Persistence;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace Mamao.ArchitectureTests;

/// <summary>
/// Guarda das camadas 2 e 4 de docs/adr/0003-multi-tenancy.md. Endpoint novo e entidade
/// nova ja nascem cobertos porque estes testes varrem o modelo, nao uma lista escrita a mao.
/// </summary>
public class IsolamentoDeTenantTests
{
    private static PeopleDbContext CriarContexto()
    {
        var tenantContext = new TenantContext();
        tenantContext.Set(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        var options = new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql("Host=localhost;Database=arquitetura_apenas_modelo")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PeopleDbContext(options, tenantContext);
    }

    private static IReadOnlyList<IEntityType> EntidadesDeTenant(DbContext context) =>
        context.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .ToList();

    [Fact]
    public void Toda_entidade_de_tenant_tem_filtro_global()
    {
        using var context = CriarContexto();
        var entidades = EntidadesDeTenant(context);

        entidades.ShouldNotBeEmpty("O teste so protege se houver entidade para proteger.");

        foreach (var entidade in entidades)
        {
            entidade.GetDeclaredQueryFilters().ShouldNotBeEmpty(
                $"{entidade.ClrType.Name} implementa ITenantOwned mas ficou sem filtro global. " +
                "Uma consulta a essa tabela devolveria dado de outra empresa.");
        }
    }

    [Fact]
    public void Todo_indice_de_entidade_de_tenant_comeca_por_tenant_id()
    {
        using var context = CriarContexto();

        foreach (var entidade in EntidadesDeTenant(context))
        {
            foreach (var indice in entidade.GetIndexes())
            {
                indice.Properties[0].Name.ShouldBe(
                    nameof(ITenantOwned.TenantId),
                    $"O indice ({string.Join(", ", indice.Properties.Select(p => p.Name))}) de " +
                    $"{entidade.ClrType.Name} nao comeca por TenantId — o Postgres vai varrer os " +
                    "dados de todos os tenants para filtrar depois.");
            }
        }
    }

    [Fact]
    public void Outbox_nao_e_filtrada_por_tenant()
    {
        // Proposital: o publicador precisa ler as mensagens de todos os tenants.
        // Se alguem "corrigir" isso implementando ITenantOwned, a fila para de ser drenada.
        using var context = CriarContexto();

        var outbox = context.Model.GetEntityTypes()
            .Single(t => t.ClrType == typeof(SharedKernel.Messaging.OutboxMessage));

        outbox.GetDeclaredQueryFilters().ShouldBeEmpty();
    }
}

public class ContextoDeTenantTests
{
    [Fact]
    public void Acessar_tenant_nao_resolvido_falha_alto_em_vez_de_devolver_vazio()
    {
        var context = new TenantContext();

        Should.Throw<InvalidOperationException>(() => context.Current);
    }

    [Fact]
    public void Recusa_tenant_vazio()
    {
        var context = new TenantContext();

        Should.Throw<ArgumentException>(() => context.Set(Guid.Empty));
    }
}
