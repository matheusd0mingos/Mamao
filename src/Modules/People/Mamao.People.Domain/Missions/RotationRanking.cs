using Mamao.People.Contracts;

namespace Mamao.People.Domain.Missions;

/// <summary>
/// O histórico de uma pessoa para efeito de rodízio, dentro da janela considerada.
/// </summary>
/// <param name="Participations">Quantas vezes participou de missões do mesmo tipo na janela.</param>
/// <param name="LastParticipation">Quando foi a última. Nulo = nunca participou.</param>
public sealed record RotationCandidate(
    EmployeeId EmployeeId,
    string Name,
    int Participations,
    DateOnly? LastParticipation);

/// <param name="Reason">Por que entrou ou por que ficou de fora. A tela mostra isso.</param>
public sealed record RankedCandidate(
    RotationCandidate Candidate,
    int Position,
    string Reason);

/// <summary>
/// A ordem de quem entra na escala.
///
/// Determinístico de propósito, e não uma otimização: o gestor precisa conseguir explicar
/// para a pessoa que ficou de fora POR QUE ela ficou. "O sistema escolheu" não é resposta
/// numa seção pequena — vira desconfiança na segunda escala e abandono na terceira.
///
/// A regra padrão, na ordem:
///   1. quem participou MENOS vezes na janela;
///   2. empate: quem está há MAIS TEMPO sem participar (nunca participou vem primeiro);
///   3. empate: ordem alfabética.
///
/// O terceiro critério existe por um motivo prático: sem ele, dois candidatos idênticos
/// sairiam em ordem indefinida e a sugestão mudaria entre dois cliques, o que faz o
/// gestor duvidar do resultado com razão.
///
/// Regra dura de unidade — antiguidade, descanso mínimo depois de serviço, quem nunca
/// entra em certa escala — ainda não está aqui. Quando vier, entra como filtro ANTES da
/// ordenação (elegibilidade) ou como critério novo, não substituindo estes.
/// </summary>
public static class RotationRanking
{
    /// <summary>Janela padrão do histórico. Três meses cobre o ciclo que as pessoas percebem.</summary>
    public const int JanelaPadraoEmDias = 90;

    /// <summary>
    /// Ordena os candidatos e explica cada posição. Devolve TODOS, não só os escolhidos:
    /// quem monta a escala precisa ver quem estava logo atrás para trocar à mão.
    /// </summary>
    public static IReadOnlyList<RankedCandidate> Rank(
        IEnumerable<RotationCandidate> candidatos, DateOnly referencia)
    {
        var ordenados = candidatos
            .OrderBy(c => c.Participations)
            .ThenBy(c => c.LastParticipation ?? DateOnly.MinValue)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return [.. ordenados.Select((c, i) => new RankedCandidate(c, i + 1, Motivo(c, referencia)))];
    }

    private static string Motivo(RotationCandidate c, DateOnly referencia)
    {
        if (c.LastParticipation is null)
            return "nunca participou";

        var dias = referencia.DayNumber - c.LastParticipation.Value.DayNumber;

        var vezes = c.Participations == 1 ? "1 vez" : $"{c.Participations} vezes";
        var desde = dias switch
        {
            <= 0 => "hoje",
            1 => "ontem",
            _ => $"há {dias} dias",
        };

        return $"{vezes} · última {desde}";
    }
}
