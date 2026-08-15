using Mamao.Identity.Persistence;
using Mamao.Notifications;
using Mamao.Notifications.Email;
using Mamao.People.Domain.Documents;
using Mamao.People.Infrastructure.Persistence;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Mamao.Worker;

/// <summary>
/// Avisa quem cuida de documento que algo está para vencer.
///
/// É o que faz o objeto "documento" valer sem exigir disciplina de ninguém: o documento
/// vence sozinho e o sistema fala primeiro. Todo o resto do produto depende de alguém
/// registrar alguma coisa.
///
/// <b>Uma vez por documento, por validade.</b> Repetir todo dia treina a pessoa a ignorar o
/// e-mail — e aí o aviso que importa também é ignorado. Renovar a validade zera a marca e o
/// aviso volta a valer.
///
/// Roda no Worker e não na API porque é trabalho de fundo: a API não deveria ter um laço
/// que varre empresas, e o Worker já é quem tem o resto dos jobs.
/// </summary>
public sealed class DocumentExpiryNotifier(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DocumentExpiryNotifier> logger) : BackgroundService
{
    /// <summary>Uma vez por dia basta: validade se mede em dias, não em minutos.</summary>
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    /// <summary>Espera o startup assentar — migrations primeiro, e-mail depois.</summary>
    private static readonly TimeSpan Atraso = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Atraso, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RodarAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Falhar aqui não pode derrubar o Worker: ele também publica a outbox.
                logger.LogError(ex, "Falha ao avisar sobre documentos vencendo.");
            }

            try
            {
                await Task.Delay(Intervalo, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RodarAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var identity = scope.ServiceProvider.GetRequiredService<MamaoIdentityDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var emails = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var templates = scope.ServiceProvider.GetRequiredService<EmailTemplates>();

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var limite = hoje.AddDays(Document.JanelaDeAvisoEmDias);

        var empresas = await identity.Tenants.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var empresa in empresas)
        {
            // Cada empresa no seu escopo: o tenant vai para a sessão do banco e as policies
            // fazem o resto. Varrer tudo com o filtro desligado seria a forma mais rápida de
            // mandar o documento de uma empresa para o RH de outra.
            tenantContext.Set(empresa);

            await using var doTenant = scopeFactory.CreateAsyncScope();
            var contextoDoTenant = doTenant.ServiceProvider.GetRequiredService<ITenantContext>();
            contextoDoTenant.Set(empresa);

            var people = doTenant.ServiceProvider.GetRequiredService<PeopleDbContext>();

            var vencendo = await people.Documents
                .Where(d => d.ExpiresOn != null && d.ExpiresOn <= limite)
                .Where(d => d.NotifiedFor == null || d.NotifiedFor != d.ExpiresOn)
                .ToListAsync(ct);

            if (vencendo.Count == 0)
                continue;

            var nomes = await NomesAsync(people, vencendo, ct);

            var itens = vencendo
                .Where(d => d.PrecisaAvisar(hoje))
                .Select(d => (
                    Tipo: d.Kind,
                    Pessoa: nomes.GetValueOrDefault(d.EmployeeId, "—"),
                    Dias: d.ExpiresOn!.Value.DayNumber - hoje.DayNumber))
                .OrderBy(i => i.Dias)
                .ToList();

            if (itens.Count == 0)
                continue;

            var destinatarios = await DestinatariosAsync(identity, empresa, ct);

            if (destinatarios.Count == 0)
            {
                // Sem ninguém com permissão de documento, o aviso não tem para quem ir. Não
                // marca como avisado: no dia em que alguém ganhar a permissão, o aviso sai.
                logger.LogWarning(
                    "Empresa {Empresa} tem {Quantos} documento(s) vencendo e ninguém com permissão de documentos.",
                    empresa, itens.Count);
                continue;
            }

            foreach (var (email, nome) in destinatarios)
                await emails.SendAsync(templates.ExpiringDocuments(email, nome, itens), ct);

            foreach (var documento in vencendo.Where(d => d.PrecisaAvisar(hoje)))
                documento.MarcarAvisado();

            await people.SaveChangesAsync(ct);

            logger.LogInformation(
                "Avisei {Pessoas} pessoa(s) sobre {Documentos} documento(s) da empresa {Empresa}.",
                destinatarios.Count, itens.Count, empresa);
        }
    }

    private static async Task<Dictionary<Mamao.People.Contracts.EmployeeId, string>> NomesAsync(
        PeopleDbContext people, IReadOnlyCollection<Document> documentos, CancellationToken ct)
    {
        var ids = documentos.Select(d => d.EmployeeId).Distinct().ToList();

        return await people.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
    }

    /// <summary>
    /// Quem recebe: as contas ativas da empresa que podem aprovar documento.
    ///
    /// Não é "todo mundo" nem "o dono": é quem tem a permissão de cuidar disso. Mandar para
    /// quem não pode agir é o começo do e-mail ignorado.
    /// </summary>
    private static async Task<IReadOnlyList<(string Email, string Nome)>> DestinatariosAsync(
        MamaoIdentityDbContext identity, Guid empresa, CancellationToken ct)
    {
        var papeis = Roles.All
            .Where(p => Roles.PermissionsOf(p).Contains(Permissions.DocumentsApprove))
            .ToList();

        return await identity.Users.AsNoTracking()
            .Where(u => u.TenantId == empresa && u.IsActive && u.Email != null && papeis.Contains(u.Role))
            .Select(u => new ValueTuple<string, string>(u.Email!, u.FullName))
            .ToListAsync(ct);
    }
}
