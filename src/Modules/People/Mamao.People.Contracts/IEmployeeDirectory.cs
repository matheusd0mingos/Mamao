namespace Mamao.People.Contracts;

/// <summary>
/// Unica porta de entrada de outros modulos para dados de funcionario.
/// Chamada in-process, sincrona. Ver docs/adr/0004-comunicacao-entre-modulos.md.
///
/// No dia em que People virar servico, a implementacao passa a ser um cliente HTTP
/// gerado do OpenAPI e nenhum consumidor muda.
/// </summary>
public interface IEmployeeDirectory
{
    Task<EmployeeSummary?> GetAsync(EmployeeId id, CancellationToken cancellationToken);

    /// <summary>
    /// Sempre em lote. A versao singular dentro de um laco gera N+1 no primeiro
    /// dashboard — por isso ela nao existe aqui.
    /// </summary>
    Task<IReadOnlyDictionary<EmployeeId, EmployeeSummary>> GetManyAsync(
        IReadOnlyCollection<EmployeeId> ids, CancellationToken cancellationToken);
}

public sealed record EmployeeSummary(
    EmployeeId Id,
    string FullName,
    string PositionName,
    bool IsActive);
