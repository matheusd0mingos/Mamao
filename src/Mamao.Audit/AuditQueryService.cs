using Mamao.Audit.Persistence;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Audit;

public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string ActorName,
    string? ActorIp,
    string Action,
    string SubjectType,
    string SubjectId,
    string SubjectLabel,
    string? Metadata,
    string? CorrelationId);

/// <param name="Total">Quantos registros casam com o filtro, para a tela saber se há mais.</param>
public sealed record AuditPage(
    IReadOnlyList<AuditEntryResponse> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<string> Actions);

/// <summary>
/// Leitura da auditoria.
///
/// É a única forma de responder "quem fez isso, e quando" depois que o estado atual já não
/// conta a história — que é justamente a pergunta que aparece quando algo deu errado. Sem
/// esta tela, a permissão <c>audit.read</c> existia e não levava a lugar nenhum.
///
/// <b>Filtra por tenant explicitamente</b>, mesmo com a RLS ligada no banco. As duas coisas
/// juntas: a policy protege o SQL cru e o dia em que alguém usar IgnoreQueryFilters; o
/// filtro aqui protege o dia em que a aplicação conectar com um role privilegiado por
/// engano. Redundância é barata; vazar auditoria de outra empresa, não.
/// </summary>
public sealed class AuditQueryService(AuditDbContext dbContext, ITenantContext tenantContext)
{
    public const int MaxPageSize = 100;

    public async Task<AuditPage> ListAsync(
        string? action,
        string? subjectId,
        string? search,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? 50 : pageSize;

        var consulta = dbContext.Entries.AsNoTracking()
            .Where(e => e.TenantId == tenantContext.Current);

        if (!string.IsNullOrWhiteSpace(action))
            consulta = consulta.Where(e => e.Action == action);

        if (!string.IsNullOrWhiteSpace(subjectId))
            consulta = consulta.Where(e => e.SubjectId == subjectId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var alvo = search.Trim().ToLowerInvariant();
            consulta = consulta.Where(e =>
                e.ActorName.ToLower().Contains(alvo) || e.SubjectLabel.ToLower().Contains(alvo));
        }

        if (from is { } inicio)
        {
            var limite = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(e => e.OccurredAt >= limite);
        }

        if (to is { } fim)
        {
            // Fim do dia, não começo: filtrar "até 15/08" e perder o que aconteceu às 14h
            // do dia 15 é o tipo de detalhe que faz a pessoa achar que o registro sumiu.
            var limite = new DateTimeOffset(fim.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(e => e.OccurredAt < limite);
        }

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEntryResponse(
                e.Id, e.OccurredAt, e.ActorName, e.ActorIp, e.Action,
                e.SubjectType, e.SubjectId, e.SubjectLabel, e.Metadata, e.CorrelationId))
            .ToListAsync(ct);

        // As ações que ESTA empresa tem, não a lista de constantes: um filtro cheio de
        // opções que não devolvem nada faz a pessoa desconfiar da tela.
        var acoes = await dbContext.Entries.AsNoTracking()
            .Where(e => e.TenantId == tenantContext.Current)
            .Select(e => e.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);

        return new AuditPage(itens, total, page, pageSize, acoes);
    }
}
