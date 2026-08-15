using Mamao.People.Contracts;

namespace Mamao.People.Domain.Missions;

/// <summary>
/// O histórico e a posição de uma pessoa para efeito de rodízio.
/// </summary>
/// <param name="Participations">Quantas vezes participou de missões do mesmo tipo na janela.</param>
/// <param name="LastParticipation">Quando foi a última. Nulo = nunca participou.</param>
/// <param name="PrecedenceOrder">
/// Ordem da graduação/cargo: menor é mais antigo. Nulo quando a empresa não ordena cargos —
/// aí a antiguidade cai só na data de admissão.
/// </param>
/// <param name="HiredOn">Data de admissão, o desempate de antiguidade dentro da mesma graduação.</param>
/// <param name="LastDutyEnd">
/// Fim do último serviço ou missão, de QUALQUER tipo. É daqui que sai o descanso mínimo —
/// quem acabou de sair de um plantão está cansado independentemente do tipo do próximo.
/// </param>
public sealed record RotationCandidate(
    EmployeeId EmployeeId,
    string Name,
    int Participations,
    DateOnly? LastParticipation,
    int? PrecedenceOrder = null,
    DateOnly? HiredOn = null,
    DateOnly? LastDutyEnd = null);

/// <param name="Reason">Por que entrou ou por que ficou de fora. A tela mostra isso.</param>
/// <param name="Resting">Está dentro do descanso mínimo da política.</param>
public sealed record RankedCandidate(
    RotationCandidate Candidate,
    int Position,
    string Reason,
    bool Resting = false);

/// <summary>
/// A ordem de quem entra na escala.
///
/// Determinístico de propósito, e não uma otimização: o gestor precisa conseguir explicar
/// para a pessoa que ficou de fora POR QUE ela ficou. "O sistema escolheu" não é resposta
/// numa seção pequena — vira desconfiança na segunda escala e abandono na terceira.
///
/// A ordem, sempre:
///   1. quem NÃO está descansando (quando a política pede descanso e manda só evitar);
///   2. quem participou MENOS vezes na janela;
///   3. quem está há MAIS TEMPO sem participar (nunca participou vem primeiro);
///   4. o desempate escolhido pela empresa (<see cref="RotationTiebreak"/>);
///   5. o nome, sempre por último.
///
/// O passo 5 não é enfeite: sem ele, dois candidatos idênticos sairiam em ordem indefinida
/// e a sugestão mudaria entre dois cliques — o que faz o gestor duvidar do resultado, com
/// razão.
///
/// O que NÃO está aqui: quem é impedido por ausência ou por restrição. Isso é elegibilidade
/// e acontece ANTES, no serviço — ordenar quem não pode ir seria trabalho jogado fora, e
/// pior, esconderia o motivo do impedimento no meio da lista.
/// </summary>
public static class RotationRanking
{
    /// <summary>Janela padrão do histórico. Três meses cobre o ciclo que as pessoas percebem.</summary>
    public const int JanelaPadraoEmDias = 90;

    /// <summary>
    /// Ordena os candidatos e explica cada posição. Devolve TODOS, não só os escolhidos:
    /// quem monta a escala precisa ver quem estava logo atrás para trocar à mão.
    /// </summary>
    /// <param name="policy">A política da empresa. Nulo usa o padrão.</param>
    /// <param name="seed">
    /// Semente do sorteio — use algo estável por missão (o id dela). Sem semente estável, a
    /// ordem mudaria a cada carregamento da tela e o sorteio viraria roleta.
    /// </param>
    public static IReadOnlyList<RankedCandidate> Rank(
        IEnumerable<RotationCandidate> candidatos,
        DateOnly referencia,
        RotationPolicy? policy = null,
        Guid seed = default)
    {
        var tiebreak = policy?.Tiebreak ?? RotationTiebreak.Antiguidade;
        var descansoMinimo = policy?.MinRestDays ?? 0;

        var comDescanso = candidatos
            .Select(c => (Candidato: c, Descansando: Descansando(c, referencia, descansoMinimo)))
            .ToList();

        // Quem está descansando vai para o fim, não para fora: a política diz "evitar", e
        // evitar com a pessoa visível é o que permite ao gestor furar num dia de falta.
        // Quando a política diz "impedir", quem filtra é o serviço, antes de chegar aqui.
        var ordenados = comDescanso
            .OrderBy(x => x.Descansando ? 1 : 0)
            .ThenBy(x => x.Candidato.Participations)
            .ThenBy(x => x.Candidato.LastParticipation ?? DateOnly.MinValue);

        ordenados = tiebreak switch
        {
            RotationTiebreak.Antiguidade => ordenados
                .ThenBy(x => x.Candidato.PrecedenceOrder ?? int.MaxValue)
                .ThenBy(x => x.Candidato.HiredOn ?? DateOnly.MaxValue),

            RotationTiebreak.Modernidade => ordenados
                .ThenByDescending(x => x.Candidato.PrecedenceOrder ?? int.MinValue)
                .ThenByDescending(x => x.Candidato.HiredOn ?? DateOnly.MinValue),

            RotationTiebreak.Sorteio => ordenados
                .ThenBy(x => Sorte(seed, x.Candidato.EmployeeId)),

            _ => ordenados,
        };

        var lista = ordenados
            .ThenBy(x => x.Candidato.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return
        [
            .. lista.Select((x, i) => new RankedCandidate(
                x.Candidato,
                i + 1,
                Motivo(x.Candidato, referencia, x.Descansando, descansoMinimo),
                x.Descansando)),
        ];
    }

    /// <summary>
    /// A pessoa saiu de serviço há menos dias do que a política exige?
    ///
    /// Conta a partir do FIM do último serviço, não do começo: quem entrou num plantão de
    /// três dias descansa depois que ele acaba, não depois que começou.
    /// </summary>
    public static bool Descansando(RotationCandidate c, DateOnly referencia, int descansoMinimo)
    {
        if (descansoMinimo <= 0 || c.LastDutyEnd is not { } fim)
            return false;

        return referencia.DayNumber - fim.DayNumber < descansoMinimo;
    }

    /// <summary>
    /// Sorteio estável: mesma semente e mesma pessoa dão sempre o mesmo número. Não usa
    /// Random — Random sorteia de novo a cada chamada, e a ordem mudaria entre dois cliques.
    ///
    /// <b>FNV-1a e não o `hash * 31 + b` de sempre.</b> Aquele é ADITIVO: o resultado vira
    /// "constante da semente + valor da pessoa", e somar a mesma constante em todo mundo
    /// não muda a ORDEM. Na prática o sorteio era uma ordem fixa disfarçada — medido, duas
    /// missões diferentes davam a mesma fila em 28% das vezes, e a mesma pessoa pegaria o
    /// serviço quase sempre. O xor do FNV quebra essa aditividade; com ele a repetição cai
    /// para 1 em 2000.
    /// </summary>
    private static int Sorte(Guid seed, EmployeeId pessoa)
    {
        Span<byte> bytes = stackalloc byte[32];
        seed.TryWriteBytes(bytes[..16]);
        pessoa.Value.TryWriteBytes(bytes[16..]);

        var hash = 2166136261u;

        foreach (var b in bytes)
        {
            hash ^= b;
            hash = unchecked(hash * 16777619u);
        }

        return unchecked((int)hash);
    }

    private static string Motivo(
        RotationCandidate c, DateOnly referencia, bool descansando, int descansoMinimo)
    {
        var historico = c.LastParticipation is null
            ? "nunca participou"
            : $"{Vezes(c.Participations)} · última {Quando(referencia, c.LastParticipation.Value)}";

        if (!descansando)
            return historico;

        var dias = referencia.DayNumber - c.LastDutyEnd!.Value.DayNumber;
        var descanso = dias <= 0
            ? $"saiu de serviço hoje (descanso de {descansoMinimo} dias)"
            : $"saiu de serviço {Quando(referencia, c.LastDutyEnd.Value)} (descanso de {descansoMinimo} dias)";

        return $"{descanso} · {historico}";
    }

    private static string Vezes(int quantas) => quantas == 1 ? "1 vez" : $"{quantas} vezes";

    private static string Quando(DateOnly referencia, DateOnly dia) =>
        (referencia.DayNumber - dia.DayNumber) switch
        {
            <= 0 => "hoje",
            1 => "ontem",
            var dias => $"há {dias} dias",
        };
}
