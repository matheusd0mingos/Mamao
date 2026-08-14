using Xunit;
using Mamao.People.Contracts;
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
    private static readonly PositionId Vigilante = PositionId.New();

    [Fact]
    public void Admite_funcionario_com_dados_validos()
    {
        var resultado = Employee.Hire("Carlos Mendes", Vigilante, Hoje, Hoje);

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.FullName.ShouldBe("Carlos Mendes");
        resultado.Value.PositionId.ShouldBe(Vigilante);
        resultado.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Remove_espacos_em_volta_do_nome_e_do_cargo()
    {
        var resultado = Employee.Hire("  Ana Lima  ", Vigilante, Hoje, Hoje, "  A-12  ");

        resultado.Value.FullName.ShouldBe("Ana Lima");
        resultado.Value.Code.ShouldBe("A-12");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void Recusa_admissao_sem_nome_utilizavel(string nome)
    {
        var resultado = Employee.Hire(nome, Vigilante, Hoje, Hoje);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.name_required");
    }

    [Fact]
    public void Recusa_admissao_sem_cargo()
    {
        // Cargo vazio agora e um PositionId zerado — o texto foi embora, a exigencia nao.
        var resultado = Employee.Hire("Ana Lima", default, Hoje, Hoje);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.position_required");
    }

    [Fact]
    public void Aceita_admissao_futura_porque_contratacao_ja_acertada_e_legitima()
    {
        var resultado = Employee.Hire("Ana Lima", Vigilante, Hoje.AddDays(30), Hoje);

        resultado.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Recusa_admissao_distante_demais_porque_costuma_ser_erro_de_ano()
    {
        var resultado = Employee.Hire("Ana Lima", Vigilante, Hoje.AddYears(3), Hoje);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("employee.hired_on_too_far");
    }

    [Fact]
    public void Codigo_em_branco_vira_nulo_para_nao_colidir_no_indice_unico()
    {
        var resultado = Employee.Hire("Ana Lima", Vigilante, Hoje, Hoje, "   ");

        resultado.Value.Code.ShouldBeNull();
    }

    [Fact]
    public void Funcionario_nasce_sem_login_e_isso_e_permanente()
    {
        // O produto precisa ser util com um unico usuario logado.
        // Ver docs/produto/mvp-e-posicionamento.md#p1.
        var funcionario = Employee.Hire("Ana Lima", Vigilante, Hoje, Hoje).Value;

        funcionario.UserId.ShouldBeNull();
    }
}

public class EmailDoFuncionarioTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 14);
    private static readonly PositionId Cargo = PositionId.New();

    private static Employee Admitido(string? email) =>
        Employee.Hire("Carlos Mendes", Cargo, Hoje, Hoje, null, null, email).Value;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_email_e_o_caso_normal_e_vira_nulo(string? entrada)
    {
        // Boa parte do efetivo deste segmento nao tem e-mail. Exigir travaria o cadastro
        // na primeira pessoa. Vazio precisa virar NULO para nao colidir no indice unico.
        Admitido(entrada).Email.ShouldBeNull();
    }

    [Fact]
    public void Guarda_em_minusculas_e_sem_espaco_porque_e_a_chave_do_convite()
    {
        Admitido("  Carlos.Mendes@Empresa.COM.br  ").Email.ShouldBe("carlos.mendes@empresa.com.br");
    }

    [Theory]
    [InlineData("carlos")]
    [InlineData("carlos@")]
    [InlineData("@empresa.com")]
    [InlineData("carlos@empresa")]
    [InlineData("carlos @empresa.com")]
    public void Recusa_endereco_que_nao_tem_como_ser_email(string entrada)
    {
        var resultado = Employee.Hire("Carlos Mendes", Cargo, Hoje, Hoje, null, null, entrada);

        resultado.IsFailure.ShouldBeTrue($"Deveria recusar: {entrada}");
        resultado.Error!.Code.ShouldBe("employee.invalid_email");
    }

    [Theory]
    [InlineData("carlos+turno.a@empresa.com.br")]
    [InlineData("c.mendes_2@sub.dominio.gov.br")]
    [InlineData("41999998888@operadora.com")]
    public void Aceita_endereco_incomum_porque_recusar_valido_e_pior(string entrada)
    {
        // Regex "completa" de e-mail recusa endereco real. Quem diz se existe e a entrega.
        Admitido(entrada).Email.ShouldBe(entrada);
    }

    [Fact]
    public void Trocar_por_vazio_limpa_o_email()
    {
        var funcionario = Admitido("carlos@empresa.com");

        funcionario.ChangeEmail("  ").IsSuccess.ShouldBeTrue();

        funcionario.Email.ShouldBeNull();
    }
}

public class DesligamentoDeFuncionarioTests
{
    private static readonly DateOnly Admissao = new(2026, 1, 10);

    private static Employee Admitido() =>
        Employee.Hire("Carlos Mendes", PositionId.New(), Admissao, Admissao).Value;

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
