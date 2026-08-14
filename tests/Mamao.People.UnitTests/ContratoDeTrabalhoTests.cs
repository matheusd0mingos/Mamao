using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// O contrato decide QUAL LEI se aplica à pessoa. Estes testes existem para travar a
/// decisão que mais custa se estiver errada: alerta de CLT não pode aparecer para militar
/// nem para estatutário. Ver docs/adr/0017-regime-de-vinculo.md.
/// </summary>
public class ContratoDeTrabalhoTests
{
    private static readonly EmployeeId Funcionario = EmployeeId.New();
    private static readonly DateOnly Inicio = new(2024, 3, 11);

    private static EmploymentContract Contrato(
        EmploymentRegime regime = EmploymentRegime.Clt,
        WorkScheduleType jornada = WorkScheduleType.CincoDois,
        decimal horas = 44m,
        DateOnly? acordo = null)
        => EmploymentContract.Create(Funcionario, regime, jornada, horas, Inicio, acordo).Value;

    [Fact]
    public void Contrato_comum_nao_gera_alerta()
        => Contrato().Warnings().ShouldBeEmpty();

    [Fact]
    public void Alerta_12x36_sem_acordo_registrado()
    {
        var alertas = Contrato(jornada: WorkScheduleType.DozePorTrintaESeis, horas: 42m).Warnings();

        alertas.ShouldHaveSingleItem();
        alertas[0].Code.ShouldBe("contract.missing_12x36_agreement");
        alertas[0].Message.ShouldContain("59-A", customMessage: "A pessoa precisa saber qual regra está sendo citada.");
    }

    [Fact]
    public void Registrar_o_acordo_faz_o_alerta_sumir()
    {
        var alertas = Contrato(
            jornada: WorkScheduleType.DozePorTrintaESeis,
            horas: 42m,
            acordo: new DateOnly(2024, 3, 1)).Warnings();

        alertas.ShouldBeEmpty();
    }

    [Fact]
    public void Alerta_carga_semanal_acima_do_teto_da_clt()
    {
        var alertas = Contrato(horas: 48m).Warnings();

        alertas.ShouldContain(a => a.Code == "contract.weekly_hours_above_clt");
    }

    [Theory]
    [InlineData(EmploymentRegime.Militar)]
    [InlineData(EmploymentRegime.EstatutarioFederal)]
    [InlineData(EmploymentRegime.EstatutarioLocal)]
    [InlineData(EmploymentRegime.Outro)]
    public void Regime_que_nao_e_clt_nao_recebe_alerta_de_clt(EmploymentRegime regime)
    {
        // Alertar aqui seria pior que calar: ensina o cliente a ignorar alerta, e aí o
        // alerta que importa também passa despercebido.
        var alertas = Contrato(regime, WorkScheduleType.DozePorTrintaESeis, horas: 60m).Warnings();

        alertas.ShouldBeEmpty($"Regra da CLT não se aplica a {regime}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-8)]
    [InlineData(120)]
    public void Recusa_carga_semanal_fora_do_razoavel(decimal horas)
    {
        // Zero e negativo são digitação; 120h não existe em regime nenhum. Entre os
        // extremos quem julga é o alerta — travar o cadastro não corrige exceção real.
        var resultado = EmploymentContract.Create(
            Funcionario, EmploymentRegime.Clt, WorkScheduleType.CincoDois, horas, Inicio);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("contract.invalid_weekly_hours");
    }

    [Fact]
    public void Aceita_carga_alta_dentro_do_razoavel_com_alerta_em_vez_de_recusa()
    {
        var resultado = EmploymentContract.Create(
            Funcionario, EmploymentRegime.Clt, WorkScheduleType.DozePorTrintaESeis, 60m, Inicio);

        resultado.IsSuccess.ShouldBeTrue("60h é fora do comum, não impossível — a operação real tem exceção.");
        resultado.Value.Warnings().ShouldContain(a => a.Code == "contract.weekly_hours_above_clt");
    }

    [Fact]
    public void Rodizio_e_jornada_valida_e_nao_gera_alerta_de_jornada_semanal()
    {
        // No segmento escolhido boa parte da operação não tem padrão semanal: entra na
        // escala quando é a vez. Ver docs/adr/0019-escala-por-rodizio.md.
        var alertas = Contrato(jornada: WorkScheduleType.Rodizio, horas: 40m).Warnings();

        alertas.ShouldBeEmpty();
    }

    [Fact]
    public void Alterar_o_contrato_recalcula_os_alertas()
    {
        var contrato = Contrato(jornada: WorkScheduleType.DozePorTrintaESeis, horas: 42m);
        contrato.Warnings().ShouldNotBeEmpty();

        contrato.Change(
            EmploymentRegime.Clt, WorkScheduleType.CincoDois, 40m, Inicio, null, null).IsSuccess.ShouldBeTrue();

        contrato.Warnings().ShouldBeEmpty();
    }
}
