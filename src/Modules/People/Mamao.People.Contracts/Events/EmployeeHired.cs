using Mamao.SharedKernel.Messaging;

namespace Mamao.People.Contracts.Events;

/// <summary>
/// Funcionario admitido. Consumido por Documents (gera as exigencias de documento),
/// TimeOff (cria o primeiro periodo aquisitivo) e Notifications.
/// </summary>
[IntegrationEvent("People.EmployeeHired.v1")]
public sealed record EmployeeHired(
    Guid EventId,
    Guid TenantId,
    DateTimeOffset OccurredAt,
    EmployeeId EmployeeId,
    string FullName,
    string PositionName,
    DateOnly HiredOn) : IIntegrationEvent;
