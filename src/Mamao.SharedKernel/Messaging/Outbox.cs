using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Mamao.SharedKernel.Messaging;

/// <summary>
/// Enfileira um integration event na MESMA transacao do fato de negocio.
/// Nao publica nada: apenas adiciona a linha ao change tracker do modulo. A publicacao
/// e do <c>OutboxPublisher</c>, no Worker.
///
/// Cada modulo expoe seu proprio marcador (ex.: IPeopleOutbox) para que o DI saiba qual
/// DbContext usar. E o preco de manter um DbContext por modulo — barato e explicito.
/// </summary>
public interface IOutbox
{
    void Enqueue(IIntegrationEvent integrationEvent, string? correlationId = null);
}

public class OutboxWriter(DbContext dbContext) : IOutbox
{
    public void Enqueue(IIntegrationEvent integrationEvent, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            TenantId = integrationEvent.TenantId,
            OccurredAt = integrationEvent.OccurredAt,
            Type = IntegrationEventTypeName.Of(integrationEvent.GetType()),
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), OutboxJson.Options),
            CorrelationId = correlationId,
            NextAttemptAt = integrationEvent.OccurredAt,
            Attempts = 0,
        });
    }
}

public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
