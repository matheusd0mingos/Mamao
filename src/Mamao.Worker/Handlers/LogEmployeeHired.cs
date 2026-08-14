using Mamao.People.Contracts.Events;
using Mamao.SharedKernel.Messaging;

namespace Mamao.Worker.Handlers;

/// <summary>
/// Consumidor de esqueleto: prova o circuito outbox -> publisher -> handler de ponta a
/// ponta no Marco 0. Nos marcos seguintes ele da lugar aos consumidores de verdade —
/// Documents gerando as exigencias de documento e TimeOff criando o periodo aquisitivo.
///
/// Handler e idempotente por natureza aqui (so registra). Quando fizer escrita, lembre
/// que a entrega e at-least-once: duplicata vai acontecer.
/// </summary>
public sealed class LogEmployeeHired(ILogger<LogEmployeeHired> logger)
    : IIntegrationEventHandler<EmployeeHired>
{
    public Task HandleAsync(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "EmployeeHired consumido: {FullName} ({PositionName}), admissao {HiredOn}, tenant {TenantId}.",
            integrationEvent.FullName,
            integrationEvent.PositionName,
            integrationEvent.HiredOn,
            integrationEvent.TenantId);

        return Task.CompletedTask;
    }
}
