using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Mamao.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mamao.Messaging;

/// <summary>
/// Resolve o tipo CLR a partir do nome versionado gravado na outbox.
/// O nome e o contrato; o tipo CLR pode ser renomeado sem quebrar nada.
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<string, Type> _byName = [];

    public IntegrationEventRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var type in assemblies.SelectMany(SafeTypes))
        {
            if (!typeof(IIntegrationEvent).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                continue;

            var attribute = type.GetCustomAttribute<IntegrationEventAttribute>();
            if (attribute is null)
                continue;

            if (_byName.TryGetValue(attribute.Name, out var existing) && existing != type)
            {
                throw new InvalidOperationException(
                    $"Dois tipos declaram o evento '{attribute.Name}': {existing.FullName} e {type.FullName}.");
            }

            _byName[attribute.Name] = type;
        }
    }

    public Type? Resolve(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyCollection<string> KnownNames => _byName.Keys;

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}

public interface IEventDispatcher
{
    /// <summary>
    /// Entrega o evento a todos os consumidores registrados. Cada consumidor roda em seu
    /// proprio escopo de DI e sua propria transacao: um handler que falha nao derruba os
    /// outros nem a publicacao.
    /// </summary>
    Task<DispatchOutcome> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed record DispatchOutcome(bool Success, string? Error);

public sealed class EventDispatcher(
    IServiceScopeFactory scopeFactory,
    IntegrationEventRegistry registry,
    ILogger<EventDispatcher> logger) : IEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

    public async Task<DispatchOutcome> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = registry.Resolve(message.Type);
        if (eventType is null)
        {
            // Evento desconhecido nao e sucesso silencioso: normalmente significa deploy
            // parcial ou um consumidor removido cedo demais.
            return new DispatchOutcome(false, $"Tipo de evento desconhecido: {message.Type}");
        }

        if (JsonSerializer.Deserialize(message.Payload, eventType, OutboxJson.Options)
            is not IIntegrationEvent integrationEvent)
        {
            return new DispatchOutcome(false, $"Payload invalido para {message.Type}");
        }

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var errors = new List<string>();

        await using var scope = scopeFactory.CreateAsyncScope();

        var tenantContext = scope.ServiceProvider.GetService<SharedKernel.Tenancy.ITenantContext>();
        tenantContext?.Set(message.TenantId);

        var handlers = scope.ServiceProvider.GetServices(handlerType).OfType<object>().ToList();

        if (handlers.Count == 0)
        {
            logger.LogDebug("Nenhum consumidor para {EventType}. Marcando como processado.", message.Type);
            return new DispatchOutcome(true, null);
        }

        var processedEvents = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

        foreach (var handler in handlers)
        {
            var consumerName = handler.GetType().FullName!;

            if (await AlreadyHandledAsync(processedEvents, message.Id, consumerName, cancellationToken))
                continue;

            try
            {
                var method = HandleMethods.GetOrAdd(handler.GetType(), t =>
                    t.GetInterfaceMap(handlerType).TargetMethods
                        .First(m => m.Name.EndsWith(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync),
                            StringComparison.Ordinal)));

                await (Task)method.Invoke(handler, [integrationEvent, cancellationToken])!;

                processedEvents.ProcessedEvents.Add(new ProcessedEvent
                {
                    EventId = message.Id,
                    Consumer = consumerName,
                    HandledAt = DateTimeOffset.UtcNow,
                });

                await processedEvents.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Consumidor {Consumer} falhou em {EventType} ({EventId}).",
                    consumerName, message.Type, message.Id);
                errors.Add($"{consumerName}: {ex.Message}");
            }
        }

        return errors.Count == 0
            ? new DispatchOutcome(true, null)
            : new DispatchOutcome(false, string.Join(" | ", errors));
    }

    private static async Task<bool> AlreadyHandledAsync(
        MessagingDbContext dbContext, Guid eventId, string consumer, CancellationToken ct)
        => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
            dbContext.ProcessedEvents, p => p.EventId == eventId && p.Consumer == consumer, ct);
}
