using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Mamao.People.Domain.Missions;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// A política de rodízio da empresa.
///
/// Nasceu de quatro perguntas feitas à unidade. As quatro respostas foram "pode ter" e
/// "depende da política" — então o que estes testes garantem não é UMA regra certa, e sim
/// que cada casa consegue configurar a sua e que a sugestão obedece.
/// </summary>
public class PoliticaDeRodizioTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 14);
    private static readonly DateTimeOffset Agora = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private static RotationPolicy Politica(
        RotationTiebreak desempate = RotationTiebreak.Antiguidade,
        int descanso = 0,
        bool descansoImpede = false,
        int janela = 90)
    {
        var p = RotationPolicy.Padrao();
        p.Update(desempate, descanso, descansoImpede, janela).IsSuccess.ShouldBeTrue();
        return p;
    }

    private static RotationCandidate Pessoa(
        string nome,
        int participacoes = 0,
        int? diasDesdeAUltima = null,
        int? graduacao = null,
        int? anosDeCasa = null,
        int? diasDesdeOServico = null)
        => new(
            EmployeeId.New(),
            nome,
            participacoes,
            diasDesdeAUltima is { } d ? Hoje.AddDays(-d) : null,
            graduacao,
            anosDeCasa is { } a ? Hoje.AddYears(-a) : null,
            diasDesdeOServico is { } s ? Hoje.AddDays(-s) : null);

    private static IReadOnlyList<string> Ordem(RotationPolicy politica, params RotationCandidate[] c)
        => [.. RotationRanking.Rank(c, Hoje, politica).Select(r => r.Candidate.Name)];

    // ── o núcleo continua sendo o rodízio ────────────────────────────────────────

    [Fact]
    public void O_desempate_so_vale_no_empate()
    {
        // O mais moderno tem MENOS participações, então entra primeiro mesmo com a política
        // de antiguidade: quem participou menos vem antes de qualquer critério de ordem.
        var ordem = Ordem(
            Politica(RotationTiebreak.Antiguidade),
            Pessoa("Coronel", participacoes: 3, diasDesdeAUltima: 10, graduacao: 1),
            Pessoa("Soldado", participacoes: 0, graduacao: 9));

        ordem.ShouldBe(["Soldado", "Coronel"]);
    }

    // ── antiguidade e modernidade ────────────────────────────────────────────────

    [Fact]
    public void Antiguidade_poe_a_graduacao_mais_alta_primeiro()
    {
        var ordem = Ordem(
            Politica(RotationTiebreak.Antiguidade),
            Pessoa("Sargento", graduacao: 5),
            Pessoa("Capitao", graduacao: 2),
            Pessoa("Soldado", graduacao: 9));

        ordem.ShouldBe(["Capitao", "Sargento", "Soldado"]);
    }

    [Fact]
    public void Modernidade_inverte_a_ordem()
    {
        // É o padrão de escala de serviço em unidade militar: o serviço cai no mais moderno.
        var ordem = Ordem(
            Politica(RotationTiebreak.Modernidade),
            Pessoa("Sargento", graduacao: 5),
            Pessoa("Capitao", graduacao: 2),
            Pessoa("Soldado", graduacao: 9));

        ordem.ShouldBe(["Soldado", "Sargento", "Capitao"]);
    }

    [Fact]
    public void Dentro_da_mesma_graduacao_vale_o_tempo_de_casa()
    {
        var ordem = Ordem(
            Politica(RotationTiebreak.Antiguidade),
            Pessoa("Recente", graduacao: 5, anosDeCasa: 1),
            Pessoa("Veterano", graduacao: 5, anosDeCasa: 12));

        ordem.ShouldBe(["Veterano", "Recente"]);
    }

    [Fact]
    public void Sem_graduacao_cadastrada_a_antiguidade_cai_na_admissao()
    {
        // A empresa que não ordena cargos não fica sem desempate — só usa o que tem.
        var ordem = Ordem(
            Politica(RotationTiebreak.Antiguidade),
            Pessoa("Novo", anosDeCasa: 2),
            Pessoa("Antigo", anosDeCasa: 9));

        ordem.ShouldBe(["Antigo", "Novo"]);
    }

    [Fact]
    public void Quem_nao_tem_graduacao_nao_fura_a_fila_dos_que_tem()
    {
        var ordem = Ordem(
            Politica(RotationTiebreak.Antiguidade),
            Pessoa("SemCargoOrdenado", anosDeCasa: 20),
            Pessoa("ComCargo", graduacao: 7, anosDeCasa: 1));

        ordem.ShouldBe(["ComCargo", "SemCargoOrdenado"]);
    }

    // ── sorteio ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sorteio_da_sempre_a_mesma_ordem_para_a_mesma_missao()
    {
        var pessoas = new[] { Pessoa("Ana"), Pessoa("Bruno"), Pessoa("Carlos"), Pessoa("Diego") };
        var missao = new Guid("33333333-3333-3333-3333-333333333333");
        var politica = Politica(RotationTiebreak.Sorteio);

        var primeira = RotationRanking.Rank(pessoas, Hoje, politica, missao).Select(r => r.Candidate.Name);
        var segunda = RotationRanking.Rank(pessoas, Hoje, politica, missao).Select(r => r.Candidate.Name);

        // Sorteio que muda entre dois cliques não é sorteio: é a tela mudando de resposta
        // enquanto o gestor pensa.
        segunda.ShouldBe(primeira);
    }

    [Fact]
    public void Sorteio_de_missoes_diferentes_nao_e_a_mesma_fila()
    {
        // MEDE a propriedade em vez de confiar em duas sementes. A versão anterior deste
        // teste comparava dois sorteios e passava por sorte: a função somava uma constante
        // por semente, o que NÃO muda a ordem, e duas missões davam a mesma fila em 28% das
        // vezes. Passava isolada, falhava no conjunto, e o defeito era do produto — a mesma
        // pessoa pegaria o serviço quase sempre.
        var pessoas = Enumerable.Range(1, 8).Select(i => Pessoa($"Pessoa {i:00}")).ToArray();
        var politica = Politica(RotationTiebreak.Sorteio);

        var filas = new HashSet<string>();
        var primeiros = new HashSet<string>();

        for (var i = 0; i < 200; i++)
        {
            var ranking = RotationRanking.Rank(pessoas, Hoje, politica, Semente(i));
            filas.Add(string.Join(",", ranking.Select(r => r.Candidate.Name)));
            primeiros.Add(ranking[0].Candidate.Name);
        }

        // Com 200 missões e 8 pessoas, uma função que embaralha de verdade produz muitas
        // filas distintas e faz quase todo mundo abrir a lista alguma vez.
        filas.Count.ShouldBeGreaterThan(150);
        primeiros.Count.ShouldBeGreaterThanOrEqualTo(6);
    }

    /// <summary>Sementes fixas e distintas: um teste que sorteia a propria entrada falha sozinho um dia.</summary>
    private static Guid Semente(int i) => new($"{i:D8}-0000-0000-0000-000000000000");

    // ── descanso mínimo ──────────────────────────────────────────────────────────

    [Fact]
    public void Quem_saiu_de_servico_ontem_vai_para_o_fim_da_fila()
    {
        var ordem = Ordem(
            Politica(descanso: 3),
            Pessoa("Cansado", participacoes: 0, diasDesdeOServico: 1),
            Pessoa("Descansado", participacoes: 5, diasDesdeAUltima: 3));

        // Mesmo tendo participado menos, quem acabou de sair de serviço cede a vez.
        ordem.ShouldBe(["Descansado", "Cansado"]);
    }

    [Fact]
    public void Descanso_cumprido_devolve_a_vez()
    {
        var ordem = Ordem(
            Politica(descanso: 3),
            Pessoa("Ja descansou", participacoes: 0, diasDesdeOServico: 3),
            Pessoa("Outro", participacoes: 5, diasDesdeAUltima: 3));

        ordem.ShouldBe(["Ja descansou", "Outro"]);
    }

    [Fact]
    public void Sem_descanso_configurado_a_regra_nao_existe()
    {
        var ordem = Ordem(
            Politica(descanso: 0),
            Pessoa("Saiu ontem", participacoes: 0, diasDesdeOServico: 1),
            Pessoa("Outro", participacoes: 5, diasDesdeAUltima: 3));

        ordem.ShouldBe(["Saiu ontem", "Outro"]);
    }

    [Fact]
    public void O_motivo_explica_o_descanso_para_quem_ficou_de_fora()
    {
        var ranking = RotationRanking.Rank(
            [Pessoa("Cansado", participacoes: 1, diasDesdeAUltima: 2, diasDesdeOServico: 2)],
            Hoje,
            Politica(descanso: 5));

        // Sem o texto, "por que eu não entrei" não tem resposta na tela.
        ranking[0].Resting.ShouldBeTrue();
        ranking[0].Reason.ShouldBe("saiu de serviço há 2 dias (descanso de 5 dias) · 1 vez · última há 2 dias");
    }

    [Fact]
    public void Descanso_conta_do_fim_do_servico_nao_do_comeco()
    {
        // Plantão de três dias que acabou hoje: o descanso começa hoje, não há três dias.
        var c = Pessoa("Plantonista", diasDesdeOServico: 0);

        RotationRanking.Descansando(c, Hoje, 2).ShouldBeTrue();
    }

    // ── validação da política ────────────────────────────────────────────────────

    [Fact]
    public void Descanso_absurdo_e_recusado()
    {
        var p = RotationPolicy.Padrao();

        p.Update(RotationTiebreak.Antiguidade, 999, false, 90).IsFailure.ShouldBeTrue();
        p.Update(RotationTiebreak.Antiguidade, -1, false, 90).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Janela_curta_demais_e_recusada()
    {
        // Janela de um dia faria todo mundo aparecer com zero participações, e o rodízio
        // deixaria de existir sem ninguém perceber.
        RotationPolicy.Padrao().Update(RotationTiebreak.Antiguidade, 0, false, 1)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void O_padrao_e_o_menos_surpreendente()
    {
        var p = RotationPolicy.Padrao();

        p.Tiebreak.ShouldBe(RotationTiebreak.Antiguidade);
        p.MinRestDays.ShouldBe(0);
        p.RestBlocks.ShouldBeFalse();
        p.WindowDays.ShouldBe(RotationRanking.JanelaPadraoEmDias);
    }

    // ── restrições ───────────────────────────────────────────────────────────────

    private static EmployeeRestriction Restricao(string? atividade, int? terminaEm = null)
        => EmployeeRestriction.Create(
            EmployeeId.New(), Hoje.AddDays(-30), Agora, "RH",
            activityName: atividade,
            endsOn: terminaEm is { } d ? Hoje.AddDays(d) : null).Value;

    [Fact]
    public void Restricao_de_uma_atividade_nao_impede_outra()
    {
        var r = Restricao("Serviço Armado");

        r.Impede("servico armado", Hoje).ShouldBeTrue();
        r.Impede("formatura", Hoje).ShouldBeFalse();
    }

    [Fact]
    public void Restricao_sem_atividade_impede_toda_escala()
    {
        var r = Restricao(null);

        r.Impede("formatura", Hoje).ShouldBeTrue();
        r.Impede("qualquer coisa", Hoje).ShouldBeTrue();
        r.Rotulo().ShouldBe("restrição para toda escala");
    }

    [Fact]
    public void Restricao_vencida_para_de_impedir()
    {
        var r = Restricao("Formatura", terminaEm: -1);

        r.Impede("formatura", Hoje).ShouldBeFalse();
    }

    [Fact]
    public void Acento_e_caixa_nao_furam_a_restricao()
    {
        // Se "Serviço Armado" e "servico armado" fossem atividades diferentes, a restrição
        // escaparia por uma diferença de digitação — e alguém entraria numa escala da qual
        // está proibido.
        var r = Restricao("SERVIÇO   armado");

        r.Impede("servico armado", Hoje).ShouldBeTrue();
    }

    [Fact]
    public void O_motivo_da_restricao_e_opcional()
    {
        // Restrição médica pode revelar dado de saúde, e quem monta escala precisa saber que
        // a pessoa não entra — não precisa saber a doença.
        var r = EmployeeRestriction.Create(EmployeeId.New(), Hoje, Agora, "RH", "Formatura");

        r.IsSuccess.ShouldBeTrue();
        r.Value.Reason.ShouldBeNull();
    }
}
