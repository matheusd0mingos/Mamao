namespace Mamao.SharedKernel.Messaging;

/// <summary>
/// Fato consumado que cruza a fronteira de um modulo. Contrato publico e versionado —
/// mudanca incompativel cria uma nova versao, nao altera esta.
/// Ver docs/arquitetura/eventos-e-outbox.md.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid TenantId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Nome estavel e versionado do evento, gravado na coluna <c>type</c> da outbox.
/// Formato: <c>Modulo.Evento.vN</c>, por exemplo <c>People.EmployeeHired.v1</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class IntegrationEventAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public static class IntegrationEventTypeName
{
    public static string Of(Type type) =>
        type.GetCustomAttributes(typeof(IntegrationEventAttribute), inherit: false)
            .OfType<IntegrationEventAttribute>()
            .FirstOrDefault()?.Name
        ?? throw new InvalidOperationException(
            $"{type.Name} precisa de [IntegrationEvent(\"Modulo.Evento.v1\")]. " +
            "O nome do tipo CLR nao serve como contrato: renomear a classe quebraria " +
            "os consumidores.");

    public static string Of<TEvent>() where TEvent : IIntegrationEvent => Of(typeof(TEvent));
}

/// <summary>
/// Reage a um fato de outro modulo. Executa no Worker, fora da transacao original.
/// Precisa ser idempotente: a entrega e at-least-once.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
