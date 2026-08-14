using Mamao.People.Domain.Employees;
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
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Outbox ligada ao DbContext de People. Ver Mamao.SharedKernel.Messaging.IOutbox.</summary>
public interface IPeopleOutbox : IOutbox;
