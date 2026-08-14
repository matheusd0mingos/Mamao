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

    /// <summary>
    /// Amarra a conta ao funcionario. Chamado quando alguem aceita o convite: e o unico
    /// momento em que o sistema sabe, com certeza, que aquele usuario E aquela pessoa.
    ///
    /// Sem esse vinculo o funcionario nao consegue pedir as proprias ferias — o RH digita
    /// por todo mundo — e qualquer um com permissao de pedir poderia abrir pedido em nome
    /// de outro. Ver docs/adr/0020-usuario-pertence-a-empresa.md.
    /// </summary>
    Task LinkUserAsync(EmployeeId employeeId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Quem e este usuario, do lado de People. Nulo quando a conta nao esta amarrada.</summary>
    Task<EmployeeId?> FindByUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record EmployeeSummary(
    EmployeeId Id,
    string FullName,
    string PositionName,
    bool IsActive);
