using Mamao.People.Contracts;

namespace Mamao.People.Application.Employees;

public sealed record CreateEmployeeRequest(
    string FullName,
    string PositionName,
    DateOnly HiredOn,
    string? Code);

public sealed record UpdateEmployeeRequest(
    string FullName,
    string PositionName);

public sealed record TerminateEmployeeRequest(DateOnly TerminatedOn);

public sealed record EmployeeResponse(
    EmployeeId Id,
    string? Code,
    string FullName,
    string PositionName,
    DateOnly HiredOn,
    DateOnly? TerminatedOn,
    bool IsActive,
    bool HasLogin);

public sealed record EmployeeListItem(
    EmployeeId Id,
    string? Code,
    string FullName,
    string PositionName,
    DateOnly HiredOn,
    bool IsActive);

/// <summary>Paginacao por offset: cadastro cresce devagar e a tela precisa de "pagina 3".</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
