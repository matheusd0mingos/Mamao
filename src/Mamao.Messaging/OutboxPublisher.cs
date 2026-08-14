using System.Diagnostics.Metrics;
using Mamao.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mamao.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 100;

    /// <summary>Depois disso a mensagem vira falha permanente e gera alerta, nao silencio.</summary>
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>
/// Le a outbox e entrega aos consumidores in-process.
///
/// Enquanto a aplicacao for um processo, e isto que substitui o broker: a garantia vem da
/// escrita atomica com o dado de negocio, nao do transporte. Trocar por RabbitMQ no dia da
/// primeira extracao e trocar o corpo de <see cref="PublishAsync"/>.
/// Ver docs/adr/0005-outbox-e-mensageria.md.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    OutboxMetrics metrics,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds), timeProvider);

        logger.LogInformation("OutboxPublisher iniciado (intervalo {Seconds}s, lote {Batch}).",
            _options.PollSeconds, _options.BatchSize);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PublishAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Falha no ciclo nao pode derrubar o worker: a proxima batida tenta de novo.
                logger.LogError(ex, "Ciclo do OutboxPublisher falhou.");
            }
        }
    }

    private async Task PublishAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

        var now = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // FOR UPDATE SKIP LOCKED: permite mais de um worker sem coordenacao externa e sem
        // entrega duplicada no mesmo ciclo.
        var batch = await dbContext.Outbox
            .FromSql($"""
                SELECT * FROM messaging.outbox
                WHERE processed_at IS NULL
                  AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                ORDER BY occurred_at
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        foreach (var message in batch)
        {
            var outcome = await dispatcher.DispatchAsync(message, ct);

            if (outcome.Success)
            {
                message.ProcessedAt = now;
                message.Error = null;
                metrics.Published();
                continue;
            }

            message.Attempts++;
            message.Error = outcome.Error?[..Math.Min(outcome.Error.Length, 4000)];

            if (message.Attempts >= _options.MaxAttempts)
            {
                // Mensagem morta silenciosa e pior que erro. Marca como processada para nao
                // travar a fila, mas registra em nivel critico para virar alerta.
                message.ProcessedAt = now;
                metrics.Failed();
                logger.LogCritical(
                    "Evento {EventType} ({EventId}) do tenant {TenantId} falhou {Attempts} vezes e foi abandonado: {Error}",
                    message.Type, message.Id, message.TenantId, message.Attempts, message.Error);
            }
            else
            {
                // Backoff exponencial simples, com teto.
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, message.Attempts), 300));
                message.NextAttemptAt = now.Add(delay);
            }
        }

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

/// <summary>
/// Metricas que valem desde o dia 1: fila crescendo significa worker parado, e isso
/// precisa gerar alerta. Ver docs/arquitetura/infraestrutura-e-deploy.md.
/// </summary>
public sealed class OutboxMetrics
{
    public const string MeterName = "Mamao.Outbox";

    private readonly Counter<long> _published;
    private readonly Counter<long> _failed;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _published = meter.CreateCounter<long>("mamao.outbox.published");
        _failed = meter.CreateCounter<long>("mamao.outbox.failed");
    }

    public void Published() => _published.Add(1);
    public void Failed() => _failed.Add(1);
}
