using Mamao.People.Contracts;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Availability;

/// <summary>
/// Estados de uma solicitação. Quatro, e as transições só saem de <see cref="Pendente"/>:
/// decisão tomada não se desfaz — se mudou de ideia, o caminho é uma solicitação nova, que
/// preserva o histórico de quem decidiu o quê.
/// </summary>
public enum AbsenceRequestStatus
{
    Pendente,
    Aprovada,
    Recusada,

    /// <summary>Desistência de quem pediu, antes da decisão.</summary>
    Cancelada,
}

/// <summary>
/// Alguém pede, alguém decide.
///
/// Um conceito só para férias, folga, dispensa e justificativa de falta — porque é sempre
/// a mesma forma, e seis funcionalidades separadas seriam seis telas, seis histórias de
/// notificação e seis lugares para a regra divergir. Ver docs/adr/0021-solicitacao-e-ocupacao.md.
///
/// Aprovar é o que escreve na <see cref="Occupancy"/>: a solicitação é o processo humano, a
/// ocupação é o fato operacional, e a aprovação é a única ponte entre os dois. É por isso
/// que o algoritmo de escala nunca precisa saber o que é "férias".
/// </summary>
public sealed class AbsenceRequest : ITenantOwned
{
    public const int MaxMotivo = 500;

    private AbsenceRequest() { }

    public AbsenceRequestId Id { get; private set; }
    public Guid TenantId { get; set; }

    /// <summary>De quem é a ausência. Nem sempre é quem digitou: o RH lança pelo funcionário.</summary>
    public EmployeeId EmployeeId { get; private set; }

    public OccupancyKind Kind { get; private set; }

    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public TimeOnly? StartsAt { get; private set; }
    public TimeOnly? EndsAt { get; private set; }

    public string? Reason { get; private set; }

    public AbsenceRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Quem decidiu, guardado por id E por nome. O nome é cópia de propósito: em dois anos
    /// a pessoa pode não estar mais aqui, e "aprovado por (excluído)" não serve para nada
    /// numa discussão sobre férias.
    /// </summary>
    public Guid? DecidedByUserId { get; private set; }

    public string? DecidedByName { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Justificativa da recusa. Recusar sem dizer por quê devolve a pessoa para o mesmo lugar.</summary>
    public string? DecisionNote { get; private set; }

    public int Dias => EndsOn.DayNumber - StartsOn.DayNumber + 1;

    public static Result<AbsenceRequest> Create(
        EmployeeId employeeId,
        OccupancyKind kind,
        DateOnly startsOn,
        DateOnly endsOn,
        DateTimeOffset now,
        TimeOnly? startsAt = null,
        TimeOnly? endsAt = null,
        string? reason = null)
    {
        // A ocupação é quem sabe validar um intervalo. Repetir a regra aqui criaria duas
        // versões dela, e um dia elas discordariam — provavelmente na borda que importa.
        var esboco = Occupancy.Create(employeeId, kind, startsOn, endsOn, now, startsAt, endsAt);
        if (esboco.IsFailure)
            return Result.Failure<AbsenceRequest>(esboco.Error!);

        if (reason is { Length: > MaxMotivo })
        {
            return Result.Failure<AbsenceRequest>(new Error(
                "absence_request.reason_too_long",
                $"O motivo passa de {MaxMotivo} caracteres.",
                nameof(reason)));
        }

        return Result.Success(new AbsenceRequest
        {
            Id = AbsenceRequestId.New(),
            EmployeeId = employeeId,
            Kind = kind,
            StartsOn = startsOn,
            EndsOn = endsOn,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Status = AbsenceRequestStatus.Pendente,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Aprova e devolve a ocupação correspondente — que o chamador grava na MESMA transação.
    /// Aprovar sem bloquear a agenda deixaria a escala escalando quem está de férias.
    /// </summary>
    public Result<Occupancy> Approve(Guid userId, string userName, DateTimeOffset now)
    {
        if (Status != AbsenceRequestStatus.Pendente)
            return Result.Failure<Occupancy>(JaDecidida());

        var ocupacao = Occupancy.Create(
            EmployeeId, Kind, StartsOn, EndsOn, now, StartsAt, EndsAt,
            OccupancySource.Solicitacao, Id.Value);

        if (ocupacao.IsFailure)
            return ocupacao;

        Status = AbsenceRequestStatus.Aprovada;
        Decidir(userId, userName, now, null);

        return ocupacao;
    }

    public Result Reject(Guid userId, string userName, DateTimeOffset now, string? note)
    {
        if (Status != AbsenceRequestStatus.Pendente)
            return Result.Failure(JaDecidida());

        Status = AbsenceRequestStatus.Recusada;
        Decidir(userId, userName, now, note);
        return Result.Success();
    }

    /// <summary>Desistência de quem pediu. Só antes da decisão — depois, o caminho é pedir de novo.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status != AbsenceRequestStatus.Pendente)
            return Result.Failure(JaDecidida());

        Status = AbsenceRequestStatus.Cancelada;
        DecidedAt = now;
        return Result.Success();
    }

    private void Decidir(Guid userId, string userName, DateTimeOffset now, string? note)
    {
        DecidedByUserId = userId;
        DecidedByName = userName;
        DecidedAt = now;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private Error JaDecidida() => new(
        "absence_request.not_pending",
        $"Esta solicitação já está como {Status.ToString().ToLowerInvariant()} e não pode mais mudar.");
}
