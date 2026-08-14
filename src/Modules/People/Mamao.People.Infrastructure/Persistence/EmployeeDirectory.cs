using Mamao.People.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Infrastructure.Persistence;

/// <summary>
/// Implementacao in-process do contrato publico de People.
/// Ver docs/adr/0004-comunicacao-entre-modulos.md.
/// </summary>
public sealed class EmployeeDirectory(PeopleDbContext dbContext) : IEmployeeDirectory
{
    public async Task<EmployeeSummary?> GetAsync(EmployeeId id, CancellationToken cancellationToken)
        => await dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EmployeeSummary(e.Id, e.FullName, e.PositionName, e.TerminatedOn == null))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<EmployeeId, EmployeeSummary>> GetManyAsync(
        IReadOnlyCollection<EmployeeId> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new Dictionary<EmployeeId, EmployeeSummary>();

        var idList = ids.Distinct().ToList();

        var summaries = await dbContext.Employees.AsNoTracking()
            .Where(e => idList.Contains(e.Id))
            .Select(e => new EmployeeSummary(e.Id, e.FullName, e.PositionName, e.TerminatedOn == null))
            .ToListAsync(cancellationToken);

        return summaries.ToDictionary(s => s.Id);
    }
}
