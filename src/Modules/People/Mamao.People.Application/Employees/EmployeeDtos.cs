using Mamao.People.Contracts;

namespace Mamao.People.Application.Employees;

public sealed record CreateEmployeeRequest(
    string FullName,
    PositionId PositionId,
    DateOnly HiredOn,
    string? Code,
    DepartmentId? DepartmentId,
    EmployeeId? ManagerId,
    string? Email);

public sealed record UpdateEmployeeRequest(
    string FullName,
    PositionId PositionId,
    DepartmentId? DepartmentId,
    EmployeeId? ManagerId,
    string? Email);

public sealed record TerminateEmployeeRequest(DateOnly TerminatedOn);

/// <summary>
/// O nome do cargo e do setor vem junto de proposito: a tela precisa dos dois para
/// mostrar, e obrigar o frontend a cruzar tres listas para desenhar uma linha seria
/// empurrar trabalho de junção para o navegador.
/// </summary>
public sealed record EmployeeResponse(
    EmployeeId Id,
    string? Code,
    string FullName,
    PositionId PositionId,
    string PositionName,
    DepartmentId? DepartmentId,
    string? DepartmentName,
    EmployeeId? ManagerId,
    string? ManagerName,
    string? Email,
    DateOnly HiredOn,
    DateOnly? TerminatedOn,
    bool IsActive,
    bool HasLogin);

/// <param name="Email">Na listagem porque a tela de acessos decide por ele quem dá para convidar.</param>
public sealed record EmployeeListItem(
    EmployeeId Id,
    string? Code,
    string FullName,
    string PositionName,
    string? DepartmentName,
    string? Email,
    DateOnly HiredOn,
    bool IsActive);

/// <summary>Paginacao por offset: cadastro cresce devagar e a tela precisa de "pagina 3".</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <param name="DepartmentId">Nulo tira a pessoa de qualquer setor.</param>
public sealed record MoveEmployeeRequest(Guid? DepartmentId);
