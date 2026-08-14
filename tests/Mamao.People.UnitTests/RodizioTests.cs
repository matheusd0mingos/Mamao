using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Missions;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// A regra de quem entra na escala.
///
/// E o teste que mais importa no produto: se a sugestao parecer injusta na primeira
/// formatura, ninguem usa na segunda. E "parecer injusta" nao e opiniao — e a ordem
/// contradizer o historico que esta na propria tela.
/// </summary>
public class RodizioTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 14);

    private static RotationCandidate Pessoa(string nome, int participacoes, int? diasDesdeAUltima)
        => new(
            EmployeeId.New(),
            nome,
            participacoes,
            diasDesdeAUltima is { } d ? Hoje.AddDays(-d) : null);

    private static IReadOnlyList<string> Ordem(params RotationCandidate[] candidatos)
        => [.. RotationRanking.Rank(candidatos, Hoje).Select(r => r.Candidate.Name)];

    [Fact]
    public void Quem_participou_menos_vem_primeiro()
    {
        var ordem = Ordem(
            Pessoa("Pedro", 4, 7),
            Pessoa("Carlos", 2, 30),
            Pessoa("Joao", 1, 45));

        ordem.ShouldBe(["Joao", "Carlos", "Pedro"]);
    }

    [Fact]
    public void Quem_nunca_participou_vem_antes_de_todo_mundo()
    {
        var ordem = Ordem(
            Pessoa("Joao", 1, 45),
            Pessoa("Andre", 0, null));

        ordem[0].ShouldBe("Andre");
    }

    [Fact]
    public void Empate_no_numero_desempata_por_quem_esta_ha_mais_tempo_sem_participar()
    {
        // Os dois participaram duas vezes; entra quem participou ha mais tempo.
        var ordem = Ordem(
            Pessoa("Recente", 2, 5),
            Pessoa("Antigo", 2, 60));

        ordem.ShouldBe(["Antigo", "Recente"]);
    }

    [Fact]
    public void Empate_total_desempata_por_nome_para_a_sugestao_nao_mudar_entre_dois_cliques()
    {
        // Sem o terceiro criterio, dois candidatos identicos sairiam em ordem indefinida —
        // e a sugestao mudaria a cada clique, o que faz duvidar do resultado com razao.
        var primeira = Ordem(Pessoa("Zulmira", 1, 10), Pessoa("Ana", 1, 10));
        var segunda = Ordem(Pessoa("Ana", 1, 10), Pessoa("Zulmira", 1, 10));

        primeira.ShouldBe(["Ana", "Zulmira"]);
        segunda.ShouldBe(primeira);
    }

    [Fact]
    public void Participar_menos_pesa_mais_que_ter_participado_ha_mais_tempo()
    {
        // Quem foi 4 vezes, mesmo que a ultima tenha sido ha muito tempo, entra DEPOIS de
        // quem foi uma vez. O criterio principal e a carga, nao a data.
        var ordem = Ordem(
            Pessoa("MuitasVezes", 4, 80),
            Pessoa("UmaVez", 1, 3));

        ordem.ShouldBe(["UmaVez", "MuitasVezes"]);
    }

    [Fact]
    public void Devolve_todo_mundo_e_nao_so_os_escolhidos()
    {
        // Quem monta a escala precisa ver quem estava logo atras para trocar a mao.
        var ranking = RotationRanking.Rank(
            [Pessoa("A", 0, null), Pessoa("B", 1, 10), Pessoa("C", 2, 20)], Hoje);

        ranking.Count.ShouldBe(3);
        ranking.Select(r => r.Position).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Explica_a_posicao_de_cada_um_em_texto_que_o_gestor_mostra_para_quem_ficou_de_fora()
    {
        var ranking = RotationRanking.Rank(
            [Pessoa("Andre", 0, null), Pessoa("Pedro", 4, 7), Pessoa("Unico", 1, 1)], Hoje);

        ranking.Single(r => r.Candidate.Name == "Andre").Reason.ShouldBe("nunca participou");
        ranking.Single(r => r.Candidate.Name == "Pedro").Reason.ShouldBe("4 vezes · última há 7 dias");
        ranking.Single(r => r.Candidate.Name == "Unico").Reason.ShouldBe("1 vez · última ontem");
    }

    // ── missao ───────────────────────────────────────────────────────────────────
    private static readonly DateTimeOffset Agora = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private static Mission Formatura(int necessarias = 20)
    {
        var criacao = Mission.Create(
            "Formatura", new(2026, 8, 15), necessarias, Agora, new(7, 0), new(9, 0));

        criacao.IsSuccess.ShouldBeTrue(criacao.Error?.Message);
        return criacao.Value;
    }

    [Fact]
    public void Missao_sem_nome_e_recusada()
    {
        Mission.Create("  ", new(2026, 8, 15), 20, Agora).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Missao_com_zero_pessoas_e_recusada()
    {
        Mission.Create("Formatura", new(2026, 8, 15), 0, Agora).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Escalar_a_mesma_pessoa_duas_vezes_conta_uma()
    {
        var missao = Formatura();
        var joao = EmployeeId.New();

        missao.Assign([joao, joao, EmployeeId.New()]);

        missao.AssignedCount.ShouldBe(2);
    }

    [Fact]
    public void Escala_incompleta_e_visivel_sem_impedir_de_salvar()
    {
        // 17 de 20 e um estado legitimo: o gestor salva o que tem e completa depois. Bloquear
        // aqui obrigaria a montar tudo de uma vez ou perder o trabalho.
        var missao = Formatura(20);
        missao.Assign(Enumerable.Range(0, 17).Select(_ => EmployeeId.New()));

        missao.AssignedCount.ShouldBe(17);
        missao.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Confirmar_bloqueia_a_agenda_de_cada_escalado_no_horario_da_missao()
    {
        var missao = Formatura(2);
        missao.Assign([EmployeeId.New(), EmployeeId.New()]);

        var confirmacao = missao.Confirm(Agora);

        confirmacao.IsSuccess.ShouldBeTrue();
        confirmacao.Value.Count.ShouldBe(2);

        var ocupacao = confirmacao.Value[0];
        ocupacao.Kind.ShouldBe(OccupancyKind.Missao);
        ocupacao.Source.ShouldBe(OccupancySource.Missao);
        ocupacao.SourceId.ShouldBe(missao.Id.Value);
        ocupacao.StartsOn.ShouldBe(new DateOnly(2026, 8, 15));
        ocupacao.StartsAt.ShouldBe(new TimeOnly(7, 0));
        missao.Status.ShouldBe(MissionStatus.Confirmada);
    }

    [Fact]
    public void Confirmar_sem_ninguem_escalado_e_recusado()
    {
        Formatura().Confirm(Agora).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Missao_confirmada_nao_aceita_remontagem_sem_cancelar()
    {
        var missao = Formatura(1);
        missao.Assign([EmployeeId.New()]);
        missao.Confirm(Agora);

        missao.Assign([EmployeeId.New()]).IsFailure.ShouldBeTrue();
    }
}
