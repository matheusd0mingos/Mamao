using Xunit;
using Mamao.People.Domain.Employees;
using Shouldly;

namespace Mamao.People.UnitTests;

/// <summary>
/// Testes nomeados em portugues de proposito: a suite de regra de negocio precisa ser
/// legivel por quem nao programa. Nos marcos de ferias e escala, e ela que voce mostra
/// ao contador. Ver docs/arquitetura/testes.md.
/// </summary>
public class AdmissaoDeFuncionarioTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 14);

    [Fact]
    public void Admite_funcionario_com_dados_validos()
    {
        var resultado = Employee.Hire("Carlos Mendes", "Tecnico de manutencao", Hoje, Hoje);

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.FullName.ShouldBe("Carlos Mendes");
        resultado.Value.PositionName.ShouldBe("Tecnico de manutencao");
        resultado.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Remove_espacos_em_volta_do_nome_e_do_cargo()
    {
        var resultado = Employee.Hire("  Ana Lima  ", "  Vigilante  ", Hoje, Hoje, "  A-12  ");

        resultado.Value.FullName.ShouldBe("Ana Lima");
        resultado.Value.PositionName.ShouldBe("Vigilante");
        resultado.Value.Code.ShouldBe("A-12");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void Recusa_admissao_sem_nome_utilizavel(string nome)
    {
        var resultado = Employee.Hire(nome, "Vigilante", Hoje, Hoje);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.name_required");
    }

    [Fact]
    public void Recusa_admissao_sem_cargo()
    {
        var resultado = Employee.Hire("Ana Lima", "", Hoje, Hoje);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.position_required");
    }

    [Fact]
    public void Aceita_admissao_futura_porque_contratacao_ja_acertada_e_legitima()
    {
        var resultado = Employee.Hire("Ana Lima", "Vigilante", Hoje.AddDays(30), Hoje);

        resultado.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Recusa_admissao_distante_demais_porque_costuma_ser_erro_de_ano()
    {
        var resultado = Employee.Hire("Ana Lima", "Vigilante", Hoje.AddYears(3), Hoje);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.hired_on_too_far");
    }

    [Fact]
    public void Codigo_em_branco_vira_nulo_para_nao_colidir_no_indice_unico()
    {
        var resultado = Employee.Hire("Ana Lima", "Vigilante", Hoje, Hoje, "   ");

        resultado.Value.Code.ShouldBeNull();
    }

    [Fact]
    public void Funcionario_nasce_sem_login_e_isso_e_permanente()
    {
        // O produto precisa ser util com um unico usuario logado.
        // Ver docs/produto/mvp-e-posicionamento.md#p1.
        var funcionario = Employee.Hire("Ana Lima", "Vigilante", Hoje, Hoje).Value;

        funcionario.UserId.ShouldBeNull();
    }
}

public class DesligamentoDeFuncionarioTests
{
    private static readonly DateOnly Admissao = new(2026, 1, 10);

    private static Employee Admitido() =>
        Employee.Hire("Carlos Mendes", "Vigilante", Admissao, Admissao).Value;

    [Fact]
    public void Desliga_funcionario_ativo()
    {
        var funcionario = Admitido();

        var resultado = funcionario.Terminate(Admissao.AddMonths(6));

        resultado.IsSuccess.ShouldBeTrue();
        funcionario.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Recusa_desligamento_anterior_a_admissao()
    {
        var funcionario = Admitido();

        var resultado = funcionario.Terminate(Admissao.AddDays(-1));

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.termination_before_hire");
        funcionario.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Recusa_desligar_duas_vezes()
    {
        var funcionario = Admitido();
        funcionario.Terminate(Admissao.AddMonths(6));

        var resultado = funcionario.Terminate(Admissao.AddMonths(7));

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.already_terminated");
    }
}
