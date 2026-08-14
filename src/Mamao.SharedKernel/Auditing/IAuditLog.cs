using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Mamao.SharedKernel.Auditing;

/// <summary>
/// Quem está agindo. Resolvido pelo host a partir do token; em job de background não há
/// usuário, e o nome do processo entra no lugar — auditoria sem ator é auditoria pela
/// metade.
/// </summary>
public interface ICurrentActor
{
    Guid? UserId { get; }
    string Name { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
}

/// <summary>
/// Escrita da auditoria.
///
/// <b>Não salva:</b> apenas enfileira no DbContext de quem chamou, e o SaveChanges do fato
/// grava os dois juntos. É a mesma escolha da outbox, pelo mesmo motivo — auditoria em
/// transação separada é auditoria que pode faltar exatamente no caso que importa.
/// Ver docs/arquitetura/multi-tenancy-e-seguranca.md#auditoria.
/// </summary>
public interface IAuditLog
{
    void Record(
        string action,
        string subjectType,
        string subjectId,
        string subjectLabel,
        object? metadata = null);
}

/// <summary>
/// Implementação sobre um DbContext qualquer. Cada módulo registra a sua ligada ao próprio
/// contexto, como acontece com a outbox.
/// </summary>
public class AuditLogWriter(
    DbContext dbContext,
    Tenancy.ITenantContext tenantContext,
    ICurrentActor actor,
    TimeProvider timeProvider) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(
        string action,
        string subjectType,
        string subjectId,
        string subjectLabel,
        object? metadata = null)
    {
        dbContext.Set<AuditEntry>().Add(AuditEntry.Create(
            tenantId: tenantContext.Current,
            actorUserId: actor.UserId,
            actorName: actor.Name,
            actorIp: actor.IpAddress,
            action: action,
            subjectType: subjectType,
            subjectId: subjectId,
            subjectLabel: subjectLabel,
            metadata: metadata is null ? null : JsonSerializer.Serialize(metadata, Json),
            correlationId: actor.CorrelationId,
            occurredAt: timeProvider.GetUtcNow()));
    }
}

/// <summary>
/// Nomes das ações. Constantes e não strings soltas: o valor vai para o banco e vira base
/// de relatório e de alerta, então renomear tem que doer no compilador.
/// </summary>
public static class AuditActions
{
    public const string EmployeeCreated = "employee.created";
    public const string EmployeeUpdated = "employee.updated";
    public const string EmployeeTerminated = "employee.terminated";
    public const string EmployeeImported = "employee.imported";

    public const string ContractSaved = "employee.contract.saved";

    public const string DepartmentDeleted = "department.deleted";
    public const string PositionDeleted = "position.deleted";

    // Disponibilidade: quem tirou quem de circulacao, e quem autorizou. E a pergunta que
    // aparece meses depois, quando falta gente na escala e ninguem lembra da decisao.
    public const string OccupancyCreated = "availability.occupancy.created";
    public const string OccupancyDeleted = "availability.occupancy.deleted";
    public const string AbsenceApproved = "availability.absence.approved";
    public const string AbsenceRejected = "availability.absence.rejected";

    public const string InviteCreated = "invite.created";
    public const string InviteAccepted = "invite.accepted";
    public const string InviteRevoked = "invite.revoked";
}
