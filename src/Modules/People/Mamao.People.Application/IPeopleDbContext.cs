using Mamao.People.Domain.Availability;
using Mamao.People.Domain.Documents;
using Mamao.People.Domain.Missions;
using Mamao.People.Domain.Employees;
using Mamao.People.Domain.Organization;
using Mamao.People.Domain.Work;
using Mamao.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application;

/// <summary>
/// O DbContext do modulo, visto pela camada de aplicacao. Existe para que Application
/// nao dependa de Infrastructure — e nao para "abstrair o EF". Nao ha repositorio por
/// cima do DbSet: o proprio DbContext ja e unidade de trabalho e repositorio.
/// Ver docs/arquitetura/visao-geral.md.
/// </summary>
public interface IPeopleDbContext
{
    DbSet<Employee> Employees { get; }
    DbSet<Department> Departments { get; }
    DbSet<Position> Positions { get; }
    DbSet<EmploymentContract> EmploymentContracts { get; }
    DbSet<Occupancy> Occupancies { get; }
    DbSet<AbsenceRequest> AbsenceRequests { get; }
    DbSet<Mission> Missions { get; }
    DbSet<MissionAssignment> MissionAssignments { get; }
    DbSet<WorkItem> WorkItems { get; }
    DbSet<RotationPolicy> RotationPolicies { get; }
    DbSet<EmployeeRestriction> EmployeeRestrictions { get; }
    DbSet<Document> Documents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Outbox ligada ao DbContext de People. Ver Mamao.SharedKernel.Messaging.IOutbox.</summary>
public interface IPeopleOutbox : IOutbox;
