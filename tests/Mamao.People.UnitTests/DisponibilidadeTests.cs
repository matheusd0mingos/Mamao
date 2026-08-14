using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// As regras de sobreposicao sao a base da escala: e delas que sai "quem pode amanha as
/// 07h". Se elas erram, a sugestao de escala erra junto — e erra parecendo certa, que e o
/// pior jeito de errar. Por isso este arquivo cobre as bordas, nao so o caminho feliz.
/// </summary>
public class DisponibilidadeTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly EmployeeId Fulano = EmployeeId.New();

    private static Occupancy Bloco(
        string inicio, string fim, TimeOnly? de = null, TimeOnly? ate = null,
        OccupancyKind tipo = OccupancyKind.Servico)
    {
        var criacao = Occupancy.Create(
            Fulano, tipo, DateOnly.Parse(inicio), DateOnly.Parse(fim), Agora, de, ate);

        criacao.IsSuccess.ShouldBeTrue(criacao.Error?.Message);
        return criacao.Value;
    }

    private static TimeOnly H(int hora) => new(hora, 0);

    // ── intervalo ────────────────────────────────────────────────────────────────
    [Fact]
    public void Fim_antes_do_inicio_e_recusado()
    {
        var criacao = Occupancy.Create(
            Fulano, OccupancyKind.Ferias, new(2026, 9, 21), new(2026, 9, 12), Agora);

        criacao.IsFailure.ShouldBeTrue();
        criacao.Error!.Code.ShouldBe("occupancy.end_before_start");
    }

    [Fact]
    public void Periodo_absurdamente_longo_e_recusado_porque_quase_sempre_e_ano_digitado_errado()
    {
        var criacao = Occupancy.Create(
            Fulano, OccupancyKind.Ferias, new(2026, 9, 12), new(2036, 9, 21), Agora);

        criacao.IsFailure.ShouldBeTrue();
        criacao.Error!.Code.ShouldBe("occupancy.too_long");
    }

    [Fact]
    public void Um_horario_so_e_recusado_porque_nao_define_intervalo()
    {
        var criacao = Occupancy.Create(
            Fulano, OccupancyKind.Missao, new(2026, 8, 15), new(2026, 8, 15), Agora, startsAt: H(7));

        criacao.IsFailure.ShouldBeTrue();
        criacao.Error!.Code.ShouldBe("occupancy.partial_time");
    }

    [Fact]
    public void Um_dia_so_tem_inicio_e_fim_iguais()
    {
        var bloco = Bloco("2026-08-15", "2026-08-15");

        bloco.Cobre(new(2026, 8, 15)).ShouldBeTrue();
        bloco.Cobre(new(2026, 8, 14)).ShouldBeFalse();
        bloco.Cobre(new(2026, 8, 16)).ShouldBeFalse();
    }

    // ── cobertura por dia ────────────────────────────────────────────────────────
    [Fact]
    public void Ferias_cobrem_todos_os_dias_do_periodo_inclusive_as_pontas()
    {
        var ferias = Bloco("2026-09-12", "2026-09-21", tipo: OccupancyKind.Ferias);

        ferias.Cobre(new(2026, 9, 12)).ShouldBeTrue();
        ferias.Cobre(new(2026, 9, 17)).ShouldBeTrue();
        ferias.Cobre(new(2026, 9, 21)).ShouldBeTrue();
        ferias.Cobre(new(2026, 9, 11)).ShouldBeFalse();
        ferias.Cobre(new(2026, 9, 22)).ShouldBeFalse();
    }

    [Fact]
    public void Pergunta_sem_horario_encontra_qualquer_bloco_do_dia()
    {
        // "Quem esta ocupado no dia 15?" tem que achar tambem quem so tem uma hora tomada.
        var reuniao = Bloco("2026-08-15", "2026-08-15", H(14), H(16));

        reuniao.Cobre(new(2026, 8, 15)).ShouldBeTrue();
    }

    // ── o caso que sustenta a escala ─────────────────────────────────────────────
    [Fact]
    public void Atividade_a_tarde_nao_impede_formatura_de_manha()
    {
        // Sem recorte de horario, esta pessoa seria excluida da formatura das 07h por causa
        // de um compromisso as 14h — e a sugestao de escala perderia gente elegivel.
        var aTarde = Bloco("2026-08-15", "2026-08-15", H(14), H(16));

        aTarde.Cobre(new(2026, 8, 15), H(7), H(9)).ShouldBeFalse();
    }

    [Fact]
    public void Atividade_no_mesmo_horario_impede()
    {
        var deManha = Bloco("2026-08-15", "2026-08-15", H(6), H(10));

        deManha.Cobre(new(2026, 8, 15), H(7), H(9)).ShouldBeTrue();
    }

    [Fact]
    public void Encostar_no_horario_nao_conta_como_conflito()
    {
        // Termina 07h, a formatura comeca 07h: a pessoa esta livre.
        var antes = Bloco("2026-08-15", "2026-08-15", H(5), H(7));

        antes.Cobre(new(2026, 8, 15), H(7), H(9)).ShouldBeFalse();
    }

    [Fact]
    public void Ferias_impedem_qualquer_horario()
    {
        var ferias = Bloco("2026-09-12", "2026-09-21", tipo: OccupancyKind.Ferias);

        ferias.Cobre(new(2026, 9, 15), H(7), H(9)).ShouldBeTrue();
    }

    [Fact]
    public void No_meio_de_um_bloco_de_varios_dias_a_pessoa_esta_ocupada_o_dia_inteiro()
    {
        // Viagem que sai as 18h do dia 10 e volta as 08h do dia 13: o dia 11 e 12 estao
        // tomados por inteiro. Aplicar o recorte de horario ao miolo deixaria a pessoa
        // "livre de manha" no meio da viagem.
        var viagem = Bloco("2026-08-10", "2026-08-13", H(18), H(8));

        viagem.Cobre(new(2026, 8, 11), H(7), H(9)).ShouldBeTrue();
        viagem.Cobre(new(2026, 8, 12), H(14), H(16)).ShouldBeTrue();
    }

    // ── conflito entre blocos ────────────────────────────────────────────────────
    [Fact]
    public void Blocos_em_dias_diferentes_nao_conflitam()
    {
        Bloco("2026-08-15", "2026-08-15").Conflita(Bloco("2026-08-16", "2026-08-16")).ShouldBeFalse();
    }

    [Fact]
    public void Bloco_de_dia_inteiro_conflita_com_qualquer_recorte_no_mesmo_dia()
    {
        var ferias = Bloco("2026-08-10", "2026-08-20", tipo: OccupancyKind.Ferias);
        var formatura = Bloco("2026-08-15", "2026-08-15", H(7), H(9), OccupancyKind.Missao);

        ferias.Conflita(formatura).ShouldBeTrue();
        formatura.Conflita(ferias).ShouldBeTrue();   // a regra vale nos dois sentidos
    }

    [Fact]
    public void Dois_recortes_no_mesmo_dia_so_conflitam_se_realmente_se_cruzarem()
    {
        var manha = Bloco("2026-08-15", "2026-08-15", H(7), H(9));
        var tarde = Bloco("2026-08-15", "2026-08-15", H(14), H(18));
        var meio = Bloco("2026-08-15", "2026-08-15", H(8), H(15));

        manha.Conflita(tarde).ShouldBeFalse();
        manha.Conflita(meio).ShouldBeTrue();
        tarde.Conflita(meio).ShouldBeTrue();
    }

    // ── solicitacao ──────────────────────────────────────────────────────────────
    private static AbsenceRequest Pedido(string inicio = "2026-09-12", string fim = "2026-09-21")
    {
        var criacao = AbsenceRequest.Create(
            Fulano, OccupancyKind.Ferias, DateOnly.Parse(inicio), DateOnly.Parse(fim), Agora);

        criacao.IsSuccess.ShouldBeTrue(criacao.Error?.Message);
        return criacao.Value;
    }

    [Fact]
    public void Solicitacao_nasce_pendente()
    {
        Pedido().Status.ShouldBe(AbsenceRequestStatus.Pendente);
    }

    [Fact]
    public void Solicitacao_herda_a_validacao_de_intervalo_da_ocupacao()
    {
        var criacao = AbsenceRequest.Create(
            Fulano, OccupancyKind.Ferias, new(2026, 9, 21), new(2026, 9, 12), Agora);

        criacao.IsFailure.ShouldBeTrue();
        criacao.Error!.Code.ShouldBe("occupancy.end_before_start");
    }

    [Fact]
    public void Aprovar_devolve_a_ocupacao_com_o_mesmo_periodo_e_a_origem_certa()
    {
        var pedido = Pedido();

        var ocupacao = pedido.Approve(Guid.NewGuid(), "Chefe da Seção", Agora);

        ocupacao.IsSuccess.ShouldBeTrue();
        ocupacao.Value.Kind.ShouldBe(OccupancyKind.Ferias);
        ocupacao.Value.StartsOn.ShouldBe(new DateOnly(2026, 9, 12));
        ocupacao.Value.EndsOn.ShouldBe(new DateOnly(2026, 9, 21));
        ocupacao.Value.Source.ShouldBe(OccupancySource.Solicitacao);
        ocupacao.Value.SourceId.ShouldBe(pedido.Id.Value);

        pedido.Status.ShouldBe(AbsenceRequestStatus.Aprovada);
        pedido.DecidedByName.ShouldBe("Chefe da Seção");
        pedido.DecidedAt.ShouldBe(Agora);
    }

    [Fact]
    public void Aprovar_duas_vezes_e_recusado_para_nao_gerar_duas_ocupacoes()
    {
        var pedido = Pedido();
        pedido.Approve(Guid.NewGuid(), "Chefe", Agora);

        var segunda = pedido.Approve(Guid.NewGuid(), "Outro Chefe", Agora);

        segunda.IsFailure.ShouldBeTrue();
        segunda.Error!.Code.ShouldBe("absence_request.not_pending");
        pedido.DecidedByName.ShouldBe("Chefe");   // a primeira decisao permanece
    }

    [Fact]
    public void Recusar_guarda_quem_recusou_e_por_que()
    {
        var pedido = Pedido();

        var recusa = pedido.Reject(Guid.NewGuid(), "Chefe", Agora, "Equipe desfalcada nessa semana.");

        recusa.IsSuccess.ShouldBeTrue();
        pedido.Status.ShouldBe(AbsenceRequestStatus.Recusada);
        pedido.DecisionNote.ShouldBe("Equipe desfalcada nessa semana.");
    }

    [Fact]
    public void Recusada_nao_pode_ser_aprovada_depois()
    {
        var pedido = Pedido();
        pedido.Reject(Guid.NewGuid(), "Chefe", Agora, null);

        pedido.Approve(Guid.NewGuid(), "Chefe", Agora).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Cancelar_so_vale_antes_da_decisao()
    {
        var pendente = Pedido();
        pendente.Cancel(Agora).IsSuccess.ShouldBeTrue();

        var aprovada = Pedido();
        aprovada.Approve(Guid.NewGuid(), "Chefe", Agora);
        aprovada.Cancel(Agora).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Conta_os_dias_incluindo_as_duas_pontas()
    {
        Pedido("2026-09-12", "2026-09-21").Dias.ShouldBe(10);
        Pedido("2026-09-12", "2026-09-12").Dias.ShouldBe(1);
    }
}
