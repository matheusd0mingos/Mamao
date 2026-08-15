using Mamao.People.Contracts;
using Mamao.People.Domain.Organization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Work;

/// <summary>
/// As colunas do quadro. Três e não sete: com cinco pessoas na seção, uma coluna a mais é
/// uma coluna que ninguém move. "Cancelada" existe mas sai do quadro — é o registro de que
/// a demanda deixou de existir, e não uma quarta pilha para acumular.
/// </summary>
public enum WorkItemStatus
{
    Aberta,
    EmAndamento,
    Concluida,
    Cancelada,
}

public enum WorkItemPriority
{
    Baixa,
    Normal,
    Alta,
}

public static class WorkItemLabels
{
    public static string Of(WorkItemStatus status) => status switch
    {
        WorkItemStatus.Aberta => "a fazer",
        WorkItemStatus.EmAndamento => "em andamento",
        WorkItemStatus.Concluida => "concluída",
        _ => "cancelada",
    };

    public static string Of(WorkItemPriority priority) => priority switch
    {
        WorkItemPriority.Alta => "alta",
        WorkItemPriority.Baixa => "baixa",
        _ => "normal",
    };
}

/// <summary>
/// Uma demanda: algo que UMA pessoa precisa entregar até uma data.
///
/// É o par da <c>Mission</c>, não o mesmo objeto: missão é "preciso de vinte pessoas
/// amanhã às sete" e gera escala; demanda é "o Marcos entrega o relatório até sexta" e
/// anda por estados. Misturar as duas daria um objeto com dois donos e nenhuma regra.
///
/// O que faz este quadro valer mais que um Trello está fora desta classe: o cartão é
/// lido junto com a disponibilidade do responsável, então o gestor vê que o Marcos entra
/// de férias antes de sexta sem precisar abrir outra tela. Aqui ficam só os fatos; o
/// cruzamento é do serviço, porque depende de dado que este agregado não possui.
/// </summary>
public sealed class WorkItem : ITenantOwned
{
    public const int MaxTitulo = 200;
    public const int MaxDetalhes = 4000;

    private WorkItem() { }

    public WorkItemId Id { get; private set; }
    public Guid TenantId { get; set; }

    public string Title { get; private set; } = null!;

    /// <summary>Titulo dobrado, so para busca. Mesmo motivo do nome do funcionario.</summary>
    public string NormalizedTitle { get; private set; } = null!;
    public string? Details { get; private set; }

    public WorkItemStatus Status { get; private set; }
    public WorkItemPriority Priority { get; private set; }

    /// <summary>Quem entrega. Nulo é permitido — demanda sem dono é justamente o que o gestor precisa enxergar.</summary>
    public EmployeeId? AssigneeId { get; private set; }

    /// <summary>Até quando. Nulo é permitido, mas sem prazo não há alerta possível.</summary>
    public DateOnly? DueOn { get; private set; }

    /// <summary>Setor responsável. Serve ao filtro do quadro, não à permissão.</summary>
    public DepartmentId? DepartmentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedByName { get; private set; } = null!;

    /// <summary>Quando entrou em andamento pela primeira vez. Não é reiniciado ao reabrir.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Está no quadro? Cancelada e concluída antiga saem da visão padrão.</summary>
    public bool Aberta => Status is WorkItemStatus.Aberta or WorkItemStatus.EmAndamento;

    public static Result<WorkItem> Create(
        string title,
        DateTimeOffset now,
        string createdByName,
        string? details = null,
        WorkItemPriority priority = WorkItemPriority.Normal,
        EmployeeId? assigneeId = null,
        DateOnly? dueOn = null,
        DepartmentId? departmentId = null)
    {
        title = title?.Trim() ?? string.Empty;

        if (title.Length == 0)
            return Result.Failure<WorkItem>(new Error("work.title_required", "Informe o que precisa ser feito.", nameof(title)));

        if (title.Length > MaxTitulo)
            return Result.Failure<WorkItem>(new Error("work.title_too_long", $"O título passa de {MaxTitulo} caracteres.", nameof(title)));

        if (details is { Length: > MaxDetalhes })
            return Result.Failure<WorkItem>(new Error("work.details_too_long", $"A descrição passa de {MaxDetalhes} caracteres.", nameof(details)));

        return Result.Success(new WorkItem
        {
            Id = WorkItemId.New(),
            Title = title,
            NormalizedTitle = Organization.Position.Normalize(title),
            Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
            Status = WorkItemStatus.Aberta,
            Priority = priority,
            AssigneeId = assigneeId,
            DueOn = dueOn,
            DepartmentId = departmentId,
            CreatedAt = now,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? "—" : createdByName.Trim(),
        });
    }

    public Result Update(
        string title,
        string? details,
        WorkItemPriority priority,
        EmployeeId? assigneeId,
        DateOnly? dueOn,
        DepartmentId? departmentId)
    {
        title = title?.Trim() ?? string.Empty;

        if (title.Length == 0)
            return Result.Failure("work.title_required", "Informe o que precisa ser feito.", nameof(title));

        if (title.Length > MaxTitulo)
            return Result.Failure("work.title_too_long", $"O título passa de {MaxTitulo} caracteres.", nameof(title));

        if (details is { Length: > MaxDetalhes })
            return Result.Failure("work.details_too_long", $"A descrição passa de {MaxDetalhes} caracteres.", nameof(details));

        Title = title;
        NormalizedTitle = Organization.Position.Normalize(title);
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        Priority = priority;
        AssigneeId = assigneeId;
        DueOn = dueOn;
        DepartmentId = departmentId;

        return Result.Success();
    }

    /// <summary>
    /// Move de coluna.
    ///
    /// Qualquer transição é permitida de propósito: num quadro, arrastar de volta é o gesto
    /// normal quando alguém moveu por engano ou a entrega voltou. O que a regra garante é a
    /// coerência das datas — concluir grava a hora, reabrir apaga, e <see cref="StartedAt"/>
    /// guarda a PRIMEIRA vez que o trabalho começou, senão o tempo de ciclo mente.
    /// </summary>
    public Result MoveTo(WorkItemStatus status, DateTimeOffset now)
    {
        if (status == Status)
            return Result.Success();

        Status = status;

        if (status == WorkItemStatus.EmAndamento)
            StartedAt ??= now;

        CompletedAt = status == WorkItemStatus.Concluida ? now : null;

        return Result.Success();
    }
}
