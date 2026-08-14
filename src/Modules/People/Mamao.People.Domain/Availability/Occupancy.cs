using Mamao.People.Contracts;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Availability;

/// <summary>
/// O que tira a pessoa de circulação. Um tipo por MOTIVO, não por módulo — quem consulta
/// disponibilidade não deveria precisar saber de onde o bloco veio.
/// </summary>
public enum OccupancyKind
{
    Ferias,
    Folga,

    /// <summary>Não compareceu. Registrado porque afeta a operação, não para punir ninguém.</summary>
    Falta,

    /// <summary>Licença médica, luto, licença-paternidade: ausência amparada e normalmente longa.</summary>
    Afastamento,

    /// <summary>Serviço, plantão, sobreaviso — a pessoa está trabalhando, e por isso indisponível para outra coisa.</summary>
    Servico,

    /// <summary>Escalado numa missão ou evento.</summary>
    Missao,

    /// <summary>Curso, viagem, atividade externa. Existe para não forçar um motivo errado.</summary>
    Outro,
}

/// <summary>De onde o bloco veio. Decide quem pode apagá-lo e o que acontece se a origem sumir.</summary>
public enum OccupancySource
{
    /// <summary>Lançado direto por alguém do RH ou da chefia.</summary>
    Manual,

    /// <summary>Nasceu da aprovação de uma solicitação. Cancelar a solicitação remove o bloco.</summary>
    Solicitacao,

    /// <summary>Nasceu de uma escalação em missão. Tirar a pessoa da missão remove o bloco.</summary>
    Missao,
}

/// <summary>
/// Um bloco de tempo em que a pessoa NÃO está disponível.
///
/// É o registro único de onde sai toda resposta sobre disponibilidade. Férias, folga,
/// serviço e missão escrevem aqui; ninguém consulta as origens uma a uma. É o que faz
/// "quem está livre amanhã" ser uma consulta só, e o que faz um tipo novo de ausência
/// não tocar no algoritmo de escala.
///
/// Disponibilidade NÃO é um campo em Employee. É o que sobra quando nada ocupa o
/// intervalo — calculada, porque campo depende de alguém lembrar de atualizar, e essa
/// pessoa esquece na primeira semana.
/// </summary>
public sealed class Occupancy : ITenantOwned
{
    /// <summary>Um bloco muito longo quase sempre é erro de digitação no ano.</summary>
    public const int MaxDias = 400;

    private Occupancy() { }

    public OccupancyId Id { get; private set; }
    public Guid TenantId { get; set; }
    public EmployeeId EmployeeId { get; private set; }

    public OccupancyKind Kind { get; private set; }
    public OccupancySource Source { get; private set; }

    /// <summary>Id da solicitação ou missão que originou. Nulo quando lançado à mão.</summary>
    public Guid? SourceId { get; private set; }

    /// <summary>Primeiro dia ocupado.</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>Último dia ocupado, INCLUSIVE. Um dia só tem início e fim iguais.</summary>
    public DateOnly EndsOn { get; private set; }

    /// <summary>
    /// Recorte de horário dentro dos dias. Nulo significa DIA INTEIRO.
    ///
    /// Férias e afastamento são sempre dia inteiro; missão às 07h não é. Sem esse recorte,
    /// quem tem uma atividade às 14h seria excluído de uma formatura às 07h — e a
    /// funcionalidade que sustenta o produto passaria a mentir logo na primeira escala.
    /// </summary>
    public TimeOnly? StartsAt { get; private set; }

    public TimeOnly? EndsAt { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool DiaInteiro => StartsAt is null || EndsAt is null;

    public static Result<Occupancy> Create(
        EmployeeId employeeId,
        OccupancyKind kind,
        DateOnly startsOn,
        DateOnly endsOn,
        DateTimeOffset now,
        TimeOnly? startsAt = null,
        TimeOnly? endsAt = null,
        OccupancySource source = OccupancySource.Manual,
        Guid? sourceId = null,
        string? note = null)
    {
        if (endsOn < startsOn)
        {
            return Result.Failure<Occupancy>(new Error(
                "occupancy.end_before_start", "A data final não pode ser anterior à inicial.", nameof(endsOn)));
        }

        if (endsOn.DayNumber - startsOn.DayNumber + 1 > MaxDias)
        {
            return Result.Failure<Occupancy>(new Error(
                "occupancy.too_long",
                $"O período passa de {MaxDias} dias. Confira o ano das datas.",
                nameof(endsOn)));
        }

        // Horário só faz sentido em par: um lado só não define recorte nenhum, e aceitar
        // isso criaria um bloco que ninguém sabe interpretar depois.
        if (startsAt is null != endsAt is null)
        {
            return Result.Failure<Occupancy>(new Error(
                "occupancy.partial_time",
                "Informe os dois horários ou nenhum — só um lado não define o intervalo.",
                nameof(startsAt)));
        }

        if (startsAt is { } inicio && endsAt is { } fim && fim <= inicio && startsOn == endsOn)
        {
            return Result.Failure<Occupancy>(new Error(
                "occupancy.end_time_before_start",
                "O horário final precisa ser depois do inicial.",
                nameof(endsAt)));
        }

        return Result.Success(new Occupancy
        {
            Id = OccupancyId.New(),
            EmployeeId = employeeId,
            Kind = kind,
            Source = source,
            SourceId = sourceId,
            StartsOn = startsOn,
            EndsOn = endsOn,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Este bloco cobre o dia informado?
    ///
    /// Sem horário na pergunta, qualquer bloco daquele dia conta — é o comportamento certo
    /// para "quem está de férias hoje". Com horário, um bloco de dia inteiro sempre conflita
    /// e dois recortes só conflitam se de fato se cruzarem.
    /// </summary>
    public bool Cobre(DateOnly dia, TimeOnly? inicio = null, TimeOnly? fim = null)
    {
        if (dia < StartsOn || dia > EndsOn)
            return false;

        if (DiaInteiro || inicio is null || fim is null)
            return true;

        // Num bloco de vários dias, o recorte de horário vale para os dias das pontas; os
        // dias do meio estão ocupados por inteiro. Tratar o meio como recorte deixaria uma
        // pessoa "livre de manhã" no meio de uma viagem de três dias.
        var recorteVale = dia == StartsOn || dia == EndsOn;
        if (!recorteVale)
            return true;

        return inicio < EndsAt!.Value && StartsAt!.Value < fim;
    }

    /// <summary>Os dois blocos disputam a mesma pessoa em algum momento?</summary>
    public bool Conflita(Occupancy outro)
    {
        if (outro.EndsOn < StartsOn || EndsOn < outro.StartsOn)
            return false;

        if (DiaInteiro || outro.DiaInteiro)
            return true;

        return outro.StartsAt!.Value < EndsAt!.Value && StartsAt!.Value < outro.EndsAt!.Value;
    }
}
