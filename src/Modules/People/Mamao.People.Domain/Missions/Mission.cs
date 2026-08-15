using Mamao.People.Contracts;
using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Organization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Missions;

public enum MissionStatus
{
    /// <summary>Sendo montada. Ninguém foi avisado e nada bloqueia agenda.</summary>
    Rascunho,

    /// <summary>Escala confirmada: os escalados ficam ocupados e entram no histórico.</summary>
    Confirmada,

    Cancelada,
}

/// <summary>
/// Uma atividade que precisa de VÁRIAS pessoas num momento determinado: formatura,
/// inspeção, visita, atividade externa.
///
/// Diferente de uma tarefa, que tem responsável e prazo e anda por estados. Aqui o que
/// importa é quantos e quando — e por isso é esta que gera escala e histórico de rodízio.
/// </summary>
public sealed class Mission : ITenantOwned
{
    public const int MaxNome = 160;
    public const int MaxPessoas = 500;

    private readonly List<MissionAssignment> _assignments = [];

    private Mission() { }

    public MissionId Id { get; private set; }
    public Guid TenantId { get; set; }

    public string Name { get; private set; } = null!;

    /// <summary>
    /// Nome dobrado. Serve a busca e — mais importante — ao historico de rodizio: contar
    /// "quantas formaturas o Pedro fez" comparava o nome dobrado EM MEMORIA, carregando o
    /// historico inteiro para descartar a maior parte. Com a coluna, o filtro vai ao banco.
    /// </summary>
    public string NormalizedName { get; private set; } = null!;

    public DateOnly On { get; private set; }
    public TimeOnly? StartsAt { get; private set; }
    public TimeOnly? EndsAt { get; private set; }

    /// <summary>Quantas pessoas a missão precisa. É o alvo da montagem, não um limite rígido.</summary>
    public int RequiredPeople { get; private set; }

    /// <summary>Restrição opcional de setor. Nulo = qualquer pessoa da empresa.</summary>
    public DepartmentId? DepartmentId { get; private set; }

    public string? Notes { get; private set; }
    public MissionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<MissionAssignment> Assignments => _assignments;

    public int AssignedCount => _assignments.Count;
    public bool Complete => _assignments.Count >= RequiredPeople;

    public static Result<Mission> Create(
        string name,
        DateOnly on,
        int requiredPeople,
        DateTimeOffset now,
        TimeOnly? startsAt = null,
        TimeOnly? endsAt = null,
        DepartmentId? departmentId = null,
        string? notes = null)
    {
        name = name?.Trim() ?? string.Empty;

        if (name.Length == 0)
            return Result.Failure<Mission>(new Error("mission.name_required", "Informe o nome da missão.", nameof(name)));

        if (name.Length > MaxNome)
            return Result.Failure<Mission>(new Error("mission.name_too_long", $"O nome passa de {MaxNome} caracteres.", nameof(name)));

        if (requiredPeople is < 1 or > MaxPessoas)
        {
            return Result.Failure<Mission>(new Error(
                "mission.invalid_headcount",
                $"A quantidade precisa estar entre 1 e {MaxPessoas}.",
                nameof(requiredPeople)));
        }

        if (startsAt is null != endsAt is null)
        {
            return Result.Failure<Mission>(new Error(
                "mission.partial_time",
                "Informe os dois horários ou nenhum.",
                nameof(startsAt)));
        }

        if (startsAt is { } inicio && endsAt is { } fim && fim <= inicio)
        {
            return Result.Failure<Mission>(new Error(
                "mission.end_time_before_start", "O horário final precisa ser depois do inicial.", nameof(endsAt)));
        }

        return Result.Success(new Mission
        {
            Id = MissionId.New(),
            Name = name,
            NormalizedName = Organization.Position.Normalize(name),
            On = on,
            StartsAt = startsAt,
            EndsAt = endsAt,
            RequiredPeople = requiredPeople,
            DepartmentId = departmentId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = MissionStatus.Rascunho,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Define quem está escalado. Substitui a lista inteira porque montar escala é uma
    /// operação de conjunto: o gestor mexe em várias linhas e salva uma vez.
    /// </summary>
    public Result Assign(IEnumerable<EmployeeId> employees)
    {
        if (Status == MissionStatus.Confirmada)
        {
            return Result.Failure(
                "mission.already_confirmed",
                "Esta missão já foi confirmada. Cancele para remontar a escala.");
        }

        var distintos = employees.Distinct().ToList();

        if (distintos.Count > MaxPessoas)
            return Result.Failure("mission.too_many", $"Máximo de {MaxPessoas} pessoas.");

        _assignments.Clear();
        _assignments.AddRange(distintos.Select(e => MissionAssignment.For(Id, e)));

        return Result.Success();
    }

    /// <summary>
    /// Confirma e devolve as ocupações dos escalados — gravadas na MESMA transação.
    ///
    /// Confirmar sem bloquear a agenda deixaria a próxima missão do mesmo dia escalar as
    /// mesmas pessoas, sem ninguém perceber até o dia.
    /// </summary>
    public Result<IReadOnlyList<Occupancy>> Confirm(DateTimeOffset now)
    {
        if (Status == MissionStatus.Confirmada)
            return Result.Failure<IReadOnlyList<Occupancy>>(new Error("mission.already_confirmed", "Esta missão já está confirmada."));

        if (_assignments.Count == 0)
            return Result.Failure<IReadOnlyList<Occupancy>>(new Error("mission.no_one_assigned", "Escale pelo menos uma pessoa antes de confirmar."));

        var ocupacoes = new List<Occupancy>(_assignments.Count);

        foreach (var atribuicao in _assignments)
        {
            var criacao = Occupancy.Create(
                atribuicao.EmployeeId, OccupancyKind.Missao, On, On, now,
                StartsAt, EndsAt, OccupancySource.Missao, Id.Value, Name);

            if (criacao.IsFailure)
                return Result.Failure<IReadOnlyList<Occupancy>>(criacao.Error!);

            ocupacoes.Add(criacao.Value);
        }

        Status = MissionStatus.Confirmada;
        return Result.Success<IReadOnlyList<Occupancy>>(ocupacoes);
    }

    /// <summary>Cancela. Quem chama remove as ocupações que esta missão gerou.</summary>
    public Result Cancel()
    {
        if (Status == MissionStatus.Cancelada)
            return Result.Failure("mission.already_cancelled", "Esta missão já está cancelada.");

        Status = MissionStatus.Cancelada;
        return Result.Success();
    }
}

/// <summary>
/// Quem está escalado. Linha própria e não uma lista de ids porque é ELA que vira o
/// histórico de rodízio: "quantas formaturas o Pedro fez nos últimos 90 dias" é uma
/// contagem aqui, e por isso não existe uma tabela separada de histórico.
/// </summary>
public sealed class MissionAssignment : ITenantOwned
{
    private MissionAssignment() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public MissionId MissionId { get; private set; }
    public EmployeeId EmployeeId { get; private set; }

    internal static MissionAssignment For(MissionId missionId, EmployeeId employeeId) => new()
    {
        Id = Guid.CreateVersion7(),
        MissionId = missionId,
        EmployeeId = employeeId,
    };
}
