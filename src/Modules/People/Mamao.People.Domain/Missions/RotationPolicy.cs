using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Missions;

/// <summary>
/// Como desempatar quando duas pessoas têm o mesmo histórico.
///
/// Existe porque a resposta certa muda por unidade. Num quartel, a escala de serviço
/// costuma cair no mais moderno; num escritório, o critério defensável é o sorteio. Nós
/// não temos como saber qual é o caso — só a seção sabe.
/// </summary>
public enum RotationTiebreak
{
    /// <summary>Mais antigo primeiro: graduação (ordem do cargo) e depois data de admissão.</summary>
    Antiguidade,

    /// <summary>Mais moderno primeiro. É o padrão de escala de serviço em unidade militar.</summary>
    Modernidade,

    /// <summary>Ordem alfabética. Previsível, e por isso injusta a longo prazo com quem tem nome no começo.</summary>
    Alfabetica,

    /// <summary>
    /// Sorteio. Estável para a mesma missão — sorteia uma vez, não a cada clique, senão a
    /// tela mudaria de resposta enquanto o gestor pensa.
    /// </summary>
    Sorteio,
}

/// <summary>
/// A política de rodízio da empresa.
///
/// Nasceu de quatro perguntas feitas à unidade que vai usar o sistema. As quatro respostas
/// foram "pode ter" e "depende da política" — e é exatamente isso que esta classe registra.
/// Chutar um padrão e escondê-lo no código faria a sugestão parecer arbitrária na primeira
/// escala em que contrariasse o costume da casa.
///
/// Uma por empresa. Os valores padrão são os menos surpreendentes: janela de 90 dias,
/// desempate por antiguidade, sem descanso mínimo.
/// </summary>
public sealed class RotationPolicy : ITenantOwned
{
    public const int MaxDescansoEmDias = 60;
    public const int MinJanelaEmDias = 7;
    public const int MaxJanelaEmDias = 730;

    private RotationPolicy() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }

    public RotationTiebreak Tiebreak { get; private set; }

    /// <summary>
    /// Quantos dias a pessoa fica de fora depois de sair de um serviço ou missão.
    /// Zero desliga a regra.
    /// </summary>
    public int MinRestDays { get; private set; }

    /// <summary>
    /// O descanso IMPEDE de escalar, ou apenas empurra para o fim da fila?
    ///
    /// Duas casas fazem diferente e as duas têm razão: onde o descanso é norma, ele impede;
    /// onde é costume, o gestor precisa poder furar em dia de falta. O padrão é evitar, que
    /// é o que perdoa o dia ruim.
    /// </summary>
    public bool RestBlocks { get; private set; }

    /// <summary>Janela do histórico considerado no rodízio.</summary>
    public int WindowDays { get; private set; }

    public static RotationPolicy Padrao() => new()
    {
        Id = Guid.CreateVersion7(),
        Tiebreak = RotationTiebreak.Antiguidade,
        MinRestDays = 0,
        RestBlocks = false,
        WindowDays = RotationRanking.JanelaPadraoEmDias,
    };

    public Result Update(RotationTiebreak tiebreak, int minRestDays, bool restBlocks, int windowDays)
    {
        if (minRestDays is < 0 or > MaxDescansoEmDias)
        {
            return Result.Failure(
                "rotation.invalid_rest",
                $"O descanso precisa estar entre 0 e {MaxDescansoEmDias} dias.",
                nameof(minRestDays));
        }

        if (windowDays is < MinJanelaEmDias or > MaxJanelaEmDias)
        {
            return Result.Failure(
                "rotation.invalid_window",
                $"A janela precisa estar entre {MinJanelaEmDias} e {MaxJanelaEmDias} dias.",
                nameof(windowDays));
        }

        Tiebreak = tiebreak;
        MinRestDays = minRestDays;
        RestBlocks = restBlocks;
        WindowDays = windowDays;

        return Result.Success();
    }
}
