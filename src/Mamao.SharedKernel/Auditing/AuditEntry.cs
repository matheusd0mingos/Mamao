using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mamao.SharedKernel.Auditing;

/// <summary>
/// Registro de auditoria. Append-only, no schema "audit".
/// Ver docs/arquitetura/multi-tenancy-e-seguranca.md#auditoria.
///
/// <b>Append-only não é comentário:</b> o role da aplicação recebe INSERT e SELECT, e tem
/// UPDATE e DELETE revogados no banco. Auditoria que a aplicação pode reescrever não é
/// auditoria — é log.
/// </summary>
public sealed class AuditEntry
{
    public const string Schema = "audit";
    public const string Table = "entries";

    private AuditEntry() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>
    /// Nome de quem agiu, desnormalizado. A auditoria precisa continuar legível depois que
    /// o usuário for excluído — senão o histórico vira uma lista de GUIDs justamente quando
    /// alguém precisa dele.
    /// </summary>
    public string ActorName { get; private set; } = null!;

    public string? ActorIp { get; private set; }

    /// <summary>Ex.: <c>employee.terminated</c>. Estável: relatório e alerta dependem dele.</summary>
    public string Action { get; private set; } = null!;

    public string SubjectType { get; private set; } = null!;
    public string SubjectId { get; private set; } = null!;

    /// <summary>Ex.: "Ana Lima (V-002)". Legível sem join, pelo mesmo motivo do ActorName.</summary>
    public string SubjectLabel { get; private set; } = null!;

    /// <summary>Antes/depois quando relevante. JSON livre — o que importa varia por ação.</summary>
    public string? Metadata { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditEntry Create(
        Guid tenantId,
        Guid? actorUserId,
        string actorName,
        string? actorIp,
        string action,
        string subjectType,
        string subjectId,
        string subjectLabel,
        string? metadata,
        string? correlationId,
        DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        OccurredAt = occurredAt,
        ActorUserId = actorUserId,
        ActorName = Limitar(actorName, 200),
        ActorIp = actorIp,
        Action = action,
        SubjectType = subjectType,
        SubjectId = subjectId,
        SubjectLabel = Limitar(subjectLabel, 300),
        Metadata = metadata,
        CorrelationId = correlationId,
    };

    /// <summary>Corta em vez de estourar: perder o fim de um rótulo é melhor que perder o registro.</summary>
    private static string Limitar(string valor, int tamanho) =>
        valor.Length <= tamanho ? valor : valor[..tamanho];
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable(AuditEntry.Table, AuditEntry.Schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ActorIp).HasMaxLength(45);
        builder.Property(e => e.Action).HasMaxLength(80).IsRequired();
        builder.Property(e => e.SubjectType).HasMaxLength(80).IsRequired();
        builder.Property(e => e.SubjectId).HasMaxLength(80).IsRequired();
        builder.Property(e => e.SubjectLabel).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Metadata).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        // A consulta real é sempre "o que aconteceu nesta empresa, do mais recente para o
        // mais antigo", e depois filtra. Por isso o índice já nasce nessa ordem.
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(e => new { e.TenantId, e.SubjectType, e.SubjectId });
    }
}
