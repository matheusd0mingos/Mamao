using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mamao.SharedKernel.Messaging;

/// <summary>
/// Linha da outbox transacional. Gravada na MESMA transacao do fato de negocio — e
/// dessa atomicidade, e nao do broker, que vem a garantia.
///
/// Deliberadamente NAO implementa ITenantOwned: o publicador precisa ler as mensagens
/// de todos os tenants. O TenantId aqui e carga util, nao filtro.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Registro de "este consumidor ja tratou este evento". Sustenta a idempotencia exigida
/// pela entrega at-least-once.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = null!;
    public DateTimeOffset HandledAt { get; set; }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const string Schema = "messaging";
    public const string Table = "outbox";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(Table, Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.Error).HasMaxLength(4000);

        // Indice parcial: so o que falta processar. Mantem a fila rapida mesmo com
        // milhoes de linhas ja publicadas.
        builder.HasIndex(x => new { x.NextAttemptAt, x.OccurredAt })
            .HasDatabaseName("ix_outbox_pending")
            .HasFilter("processed_at IS NULL");
    }
}

public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_event", OutboxMessageConfiguration.Schema);
        builder.HasKey(x => new { x.EventId, x.Consumer });
        builder.Property(x => x.Consumer).HasMaxLength(300);
    }
}
