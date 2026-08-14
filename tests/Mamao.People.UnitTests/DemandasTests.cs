using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Work;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// A demanda e o alerta do cartão.
///
/// O alerta é a razão de o quadro morar dentro do Mamão: qualquer ferramenta de tarefas
/// mostra "vence sexta"; só esta sabe que quem responde entra de férias na quarta. Se o
/// texto do cartão estiver errado, o gestor aprende a ignorá-lo — e aí o quadro vira um
/// Trello pior.
/// </summary>
public class DemandasTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 14);
    private static readonly DateTimeOffset Agora = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmployeeId Marcos = EmployeeId.New();

    private static WorkItem Demanda(
        DateOnly? prazo = null,
        EmployeeId? responsavel = null,
        WorkItemStatus status = WorkItemStatus.Aberta)
    {
        var item = WorkItem.Create(
            "Fechar o relatório", Agora, "Ana",
            assigneeId: responsavel, dueOn: prazo).Value;

        if (status != WorkItemStatus.Aberta)
            item.MoveTo(status, Agora);

        return item;
    }

    private static Occupancy Ausencia(OccupancyKind tipo, DateOnly de, DateOnly ate)
        => Occupancy.Create(Marcos, tipo, de, ate, Agora).Value;

    // ── criação ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Demanda_sem_titulo_nao_e_criada()
    {
        var r = WorkItem.Create("   ", Agora, "Ana");

        r.IsFailure.ShouldBeTrue();
        r.Error!.Code.ShouldBe("work.title_required");
    }

    [Fact]
    public void Demanda_nasce_aberta_e_sem_dono_obrigatorio()
    {
        var item = WorkItem.Create("Comprar material", Agora, "Ana").Value;

        item.Status.ShouldBe(WorkItemStatus.Aberta);
        item.AssigneeId.ShouldBeNull();
        item.DueOn.ShouldBeNull();
        item.Aberta.ShouldBeTrue();
    }

    // ── movimento entre colunas ──────────────────────────────────────────────────

    [Fact]
    public void Concluir_grava_a_hora_e_reabrir_apaga()
    {
        var item = Demanda();

        item.MoveTo(WorkItemStatus.Concluida, Agora);
        item.CompletedAt.ShouldBe(Agora);

        item.MoveTo(WorkItemStatus.EmAndamento, Agora.AddHours(1));
        item.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Comecar_de_novo_nao_reinicia_o_inicio()
    {
        var item = Demanda();

        item.MoveTo(WorkItemStatus.EmAndamento, Agora);
        item.MoveTo(WorkItemStatus.Aberta, Agora.AddDays(1));
        item.MoveTo(WorkItemStatus.EmAndamento, Agora.AddDays(2));

        // Senão o tempo de ciclo mediria só a última tentativa, e sempre pareceria curto.
        item.StartedAt.ShouldBe(Agora);
    }

    // ── o alerta ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sem_prazo_e_com_dono_disponivel_nao_ha_alerta()
    {
        var alerta = WorkAlertRule.Avaliar(Demanda(responsavel: Marcos), Hoje, "Marcos Silva", []);

        alerta.ShouldBeNull();
    }

    [Fact]
    public void Demanda_sem_responsavel_avisa()
    {
        var alerta = WorkAlertRule.Avaliar(Demanda(prazo: Hoje.AddDays(3)), Hoje, null, []);

        alerta!.Kind.ShouldBe(WorkAlertKind.SemResponsavel);
        alerta.Message.ShouldBe("Sem responsável.");
    }

    [Fact]
    public void Prazo_vencido_avisa_ha_quantos_dias()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(-3), responsavel: Marcos), Hoje, "Marcos Silva", []);

        alerta!.Kind.ShouldBe(WorkAlertKind.Atrasada);
        alerta.Message.ShouldBe("Atrasada há 3 dias.");
    }

    [Fact]
    public void Venceu_ontem_fala_ontem()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(-1), responsavel: Marcos), Hoje, "Marcos Silva", []);

        // "Atrasada há 1 dias" é o tipo de texto que faz o usuário desconfiar do resto.
        alerta!.Message.ShouldBe("Venceu ontem.");
    }

    [Fact]
    public void Atraso_vem_antes_de_qualquer_outro_aviso()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(-2), responsavel: Marcos),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Ferias, Hoje.AddDays(-5), Hoje.AddDays(10))]);

        alerta!.Kind.ShouldBe(WorkAlertKind.Atrasada);
    }

    [Fact]
    public void Responsavel_fora_hoje_diz_quando_volta()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(20), responsavel: Marcos),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Ferias, Hoje.AddDays(-2), Hoje.AddDays(5))]);

        alerta!.Kind.ShouldBe(WorkAlertKind.ResponsavelAusente);
        alerta.Message.ShouldBe("Marcos está de férias, volta em 20/08.");
    }

    [Fact]
    public void Responsavel_que_volta_depois_do_prazo_diz_isso_na_cara()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(3), responsavel: Marcos),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Ferias, Hoje.AddDays(-2), Hoje.AddDays(10))]);

        // É a diferença entre "espera ele voltar" e "muda o responsável ou o prazo".
        alerta!.Kind.ShouldBe(WorkAlertKind.ResponsavelAusente);
        alerta.Message.ShouldBe("Marcos está de férias até 24/08 — depois do prazo (17/08).");
    }

    [Fact]
    public void Responsavel_que_sai_antes_do_prazo_avisa_com_antecedencia()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(5), responsavel: Marcos),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Ferias, Hoje.AddDays(2), Hoje.AddDays(20))]);

        alerta!.Kind.ShouldBe(WorkAlertKind.ResponsavelSai);
        alerta.Message.ShouldBe("Marcos entra de férias em 16/08, antes do prazo (19/08).");
    }

    [Fact]
    public void Ausencia_depois_do_prazo_nao_e_problema()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(2), responsavel: Marcos),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Ferias, Hoje.AddDays(10), Hoje.AddDays(30))]);

        // Entregar antes de sair é o caso normal. Avisar aqui seria ruído.
        alerta.ShouldBeNull();
    }

    [Fact]
    public void Servico_tambem_conta_como_ausencia()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(4), responsavel: Marcos),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Servico, Hoje.AddDays(1), Hoje.AddDays(2))]);

        // Quem está de serviço está trabalhando — em outra coisa. O prazo continua correndo.
        alerta!.Kind.ShouldBe(WorkAlertKind.ResponsavelSai);
        alerta.Message.ShouldContain("entra de serviço");
    }

    [Fact]
    public void Demanda_concluida_nao_alerta_nada()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(-10), responsavel: Marcos, status: WorkItemStatus.Concluida),
            Hoje,
            "Marcos Silva",
            [Ausencia(OccupancyKind.Ferias, Hoje.AddDays(-5), Hoje.AddDays(5))]);

        // Cobrar prazo de coisa entregue é a forma mais rápida de o gestor ignorar o quadro.
        alerta.ShouldBeNull();
    }

    [Fact]
    public void Nome_desconhecido_nao_vira_frase_quebrada()
    {
        var alerta = WorkAlertRule.Avaliar(
            Demanda(prazo: Hoje.AddDays(5), responsavel: Marcos),
            Hoje,
            null,
            [Ausencia(OccupancyKind.Folga, Hoje.AddDays(1), Hoje.AddDays(1))]);

        alerta!.Message.ShouldStartWith("O responsável entra de folga");
    }
}
