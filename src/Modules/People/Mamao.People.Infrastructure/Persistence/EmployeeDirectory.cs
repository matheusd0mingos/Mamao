using Mamao.People.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Infrastructure.Persistence;

/// <summary>
/// Implementacao in-process do contrato publico de People.
/// Ver docs/adr/0004-comunicacao-entre-modulos.md.
///
/// O contrato expoe o cargo como TEXTO e continua assim depois de Position virar entidade:
/// outro modulo nao tem o que fazer com um PositionId — ele quer escrever "Vigilante" numa
/// notificacao. A juncao fica deste lado da fronteira, que e onde ela pertence.
/// </summary>
public sealed class EmployeeDirectory(PeopleDbContext dbContext) : IEmployeeDirectory
{
    public async Task<EmployeeSummary?> GetAsync(EmployeeId id, CancellationToken cancellationToken)
        => await dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EmployeeSummary(
                e.Id,
                e.FullName,
                dbContext.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault()!,
                e.TerminatedOn == null))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<EmployeeId, EmployeeSummary>> GetManyAsync(
        IReadOnlyCollection<EmployeeId> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new Dictionary<EmployeeId, EmployeeSummary>();

        var idList = ids.Distinct().ToList();

        var summaries = await dbContext.Employees.AsNoTracking()
            .Where(e => idList.Contains(e.Id))
            .Select(e => new EmployeeSummary(
                e.Id,
                e.FullName,
                dbContext.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault()!,
                e.TerminatedOn == null))
            .ToListAsync(cancellationToken);

        return summaries.ToDictionary(s => s.Id);
    }

    public async Task LinkUserAsync(EmployeeId employeeId, Guid userId, CancellationToken cancellationToken)
    {
        var funcionario = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Funcionario {employeeId} nao encontrado no tenant atual. " +
                "Quem chama precisa ter o tenant resolvido: o filtro global esconde tudo sem ele.");

        funcionario.LinkUser(userId);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmployeeId?> FindByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var id = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => (EmployeeId?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id;
    }
}
