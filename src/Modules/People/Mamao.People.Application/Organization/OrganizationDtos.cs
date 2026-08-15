using Mamao.People.Contracts;

namespace Mamao.People.Application.Organization;

public sealed record CreateDepartmentRequest(string Name, DepartmentId? ParentId);

public sealed record UpdateDepartmentRequest(string Name, EmployeeId? ManagerId);

public sealed record MoveDepartmentRequest(DepartmentId? ParentId);

/// <param name="Depth">0 para raiz. A tela indenta por este número em vez de montar a árvore no cliente.</param>
/// <param name="EmployeeCount">Só do setor. O acumulado da subárvore é <paramref name="SubtreeEmployeeCount"/>.</param>
public sealed record DepartmentNode(
    DepartmentId Id,
    string Name,
    DepartmentId? ParentId,
    int Depth,
    EmployeeId? ManagerId,
    string? ManagerName,
    int EmployeeCount,
    int SubtreeEmployeeCount,
    string CreatedByName,
    DateTimeOffset CreatedAt);

/// <param name="PrecedenceOrder">
/// Ordem de precedencia: MENOR e mais antigo/superior. Nulo deixa o cargo fora da
/// ordenacao — e o que o desempate por antiguidade do rodizio le.
/// </param>
public sealed record CreatePositionRequest(string Name, int? PrecedenceOrder = null);

public sealed record UpdatePositionRequest(string Name, int? PrecedenceOrder = null);

/// <param name="EmployeeCount">Quantas pessoas ocupam o cargo. A tela usa para não deixar excluir o que está em uso.</param>
public sealed record PositionResponse(PositionId Id, string Name, int EmployeeCount, int? PrecedenceOrder);
