using System.Security.Claims;
using Mamao.Identity.Domain;
using Mamao.Identity.Persistence;
using Mamao.Identity.Tokens;
using Mamao.Notifications.Email;
using Mamao.Notifications;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Authorization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mamao.Identity.Endpoints;

public sealed record CreateInviteRequest(string Email, string FullName, string Role, Guid? EmployeeId);

public sealed record InviteResponse(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid? EmployeeId,
    InviteStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>O que a tela de aceite mostra antes de pedir a senha, para a pessoa saber onde está entrando.</summary>
public sealed record InvitePreview(string Email, string FullName, string CompanyName, string Role);

public sealed record AcceptInviteRequest(string Token, string Password);

/// <summary>
/// Convites. É o caminho de ativação: o RH importa a planilha, escolhe quem precisa
/// acessar e convida. Ver docs/roadmap.md, Marco 1.
///
/// Convidar 3 pessoas-chave é ativação; convidar o efetivo inteiro é dependência — e é
/// isso que o P1 protege. O produto segue 100% útil com o RH sozinho logado, mesmo que
/// ninguém aceite convite nenhum.
/// </summary>
public static class InviteEndpoints
{
    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invites")
            .WithTags("Invites")
            .RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
            ClaimsPrincipal principal,
            MamaoIdentityDbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var tenantId = TenantOf(principal);
            var agora = timeProvider.GetUtcNow();

            var convites = await dbContext.Invites
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(ct);

            return TypedResults.Ok(convites.ConvertAll(i => ToResponse(i, agora)));
        })
        .WithName("listInvites")
        .Produces<IReadOnlyList<InviteResponse>>()
        .RequireAuthorization(Permissions.UsersInvite);

        group.MapPost("/", async Task<IResult> (
            CreateInviteRequest request,
            ClaimsPrincipal principal,
            MamaoIdentityDbContext dbContext,
            UserManager<MamaoUser> userManager,
            IEmailSender emails,
            EmailTemplates templates,
            TimeProvider timeProvider,
            ILogger<Invite> logger,
            [FromKeyedServices("identity")] IAuditLog audit,
            CancellationToken ct) =>
        {
            var tenantId = TenantOf(principal);
            var agora = timeProvider.GetUtcNow();
            var endereco = request.Email.Trim().ToLowerInvariant();

            // O e-mail e unico no sistema inteiro (ADR-0020). A mensagem NAO pode dizer
            // onde ele ja esta em uso: revelar isso e a enumeracao que a propria decisao
            // de isolamento existe para evitar.
            if (await userManager.FindByEmailAsync(endereco) is not null)
            {
                return HttpResultsExtensions.Problem(new Error(
                    "invite.email_unavailable", "Este e-mail não está disponível.", nameof(request.Email)));
            }

            var pendente = await dbContext.Invites.FirstOrDefaultAsync(
                i => i.TenantId == tenantId && i.Email == endereco && i.AcceptedAt == null && i.RevokedAt == null, ct);

            var criacao = Invite.Create(
                tenantId, endereco, request.FullName, request.Role, request.EmployeeId, UserOf(principal), agora);

            if (criacao.IsFailure)
                return HttpResultsExtensions.Problem(criacao.Error!);

            var (convite, token) = criacao.Value;

            // Convite pendente para o mesmo endereco vira reenvio: dois links validos da
            // mesma conta circulando por e-mail e a receita de confusao e de suporte.
            if (pendente is not null)
            {
                token = pendente.Renew(agora);
                convite = pendente;
            }
            else
            {
                dbContext.Invites.Add(convite);
            }

            // Dar acesso ao sistema e das acoes com mais consequencia que existem: quem
            // convidou, quem foi convidado e com qual papel.
            audit.Record(
                AuditActions.InviteCreated, nameof(Invite), convite.Id.ToString(),
                $"{convite.FullName} <{convite.Email}>", new { convite.Role, Reenvio = pendente is not null });

            await dbContext.SaveChangesAsync(ct);
            await EnviarAsync(convite, token, dbContext, emails, templates, principal, logger, ct);

            return TypedResults.Ok(ToResponse(convite, agora));
        })
        .WithName("createInvite")
        .Produces<InviteResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.UsersInvite);

        group.MapPost("/{id:guid}/resend", async Task<IResult> (
            Guid id,
            ClaimsPrincipal principal,
            MamaoIdentityDbContext dbContext,
            IEmailSender emails,
            EmailTemplates templates,
            TimeProvider timeProvider,
            ILogger<Invite> logger,
            CancellationToken ct) =>
        {
            var tenantId = TenantOf(principal);
            var agora = timeProvider.GetUtcNow();

            var convite = await dbContext.Invites.FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId, ct);
            if (convite is null)
                return HttpResultsExtensions.Problem(new Error("invite.not_found", "Convite não encontrado."));

            if (convite.AcceptedAt is not null)
                return HttpResultsExtensions.Problem(new Error("invite.already_accepted", "Este convite já foi aceito."));

            var token = convite.Renew(agora);
            await dbContext.SaveChangesAsync(ct);
            await EnviarAsync(convite, token, dbContext, emails, templates, principal, logger, ct);

            return TypedResults.Ok(ToResponse(convite, agora));
        })
        .WithName("resendInvite")
        .Produces<InviteResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.UsersInvite);

        group.MapDelete("/{id:guid}", async Task<IResult> (
            Guid id,
            ClaimsPrincipal principal,
            MamaoIdentityDbContext dbContext,
            TimeProvider timeProvider,
            [FromKeyedServices("identity")] IAuditLog audit,
            CancellationToken ct) =>
        {
            var convite = await dbContext.Invites.FirstOrDefaultAsync(
                i => i.Id == id && i.TenantId == TenantOf(principal), ct);

            if (convite is null)
                return HttpResultsExtensions.Problem(new Error("invite.not_found", "Convite não encontrado."));

            var revogado = convite.Revoke(timeProvider.GetUtcNow());
            if (revogado.IsFailure)
                return HttpResultsExtensions.Problem(revogado.Error!);

            audit.Record(
                AuditActions.InviteRevoked, nameof(Invite), convite.Id.ToString(),
                $"{convite.FullName} <{convite.Email}>");

            await dbContext.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        })
        .WithName("revokeInvite")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAuthorization(Permissions.UsersInvite);

        MapAnonymous(app);
        return app;
    }

    /// <summary>
    /// Quem aceita o convite ainda NAO tem conta, entao estes dois sao anonimos — e por
    /// isso ficam sob o mesmo rate limit do login: o token e o unico segredo protegendo a
    /// criacao de uma conta.
    /// </summary>
    private static void MapAnonymous(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invites")
            .WithTags("Invites")
            .RequireRateLimiting(AuthEndpoints.AuthRateLimitPolicy);

        group.MapGet("/preview", async Task<IResult> (
            string token,
            MamaoIdentityDbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var convite = await BuscarPorTokenAsync(token, dbContext, ct);

            if (convite is null || !convite.CanAccept(timeProvider.GetUtcNow()))
            {
                return HttpResultsExtensions.Problem(new Error(
                    "invite.not_acceptable", "Convite expirado ou já utilizado. Peça um novo ao responsável."));
            }

            var empresa = await dbContext.Tenants
                .Where(t => t.Id == convite.TenantId)
                .Select(t => t.Name)
                .FirstAsync(ct);

            return TypedResults.Ok(new InvitePreview(convite.Email, convite.FullName, empresa, convite.Role));
        })
        .WithName("previewInvite")
        .Produces<InvitePreview>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous();

        group.MapPost("/accept", async Task<IResult> (
            AcceptInviteRequest request,
            MamaoIdentityDbContext dbContext,
            UserManager<MamaoUser> userManager,
            TokenService tokens,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var agora = timeProvider.GetUtcNow();
            var convite = await BuscarPorTokenAsync(request.Token, dbContext, ct);

            if (convite is null || !convite.CanAccept(agora))
            {
                return HttpResultsExtensions.Problem(new Error(
                    "invite.not_acceptable", "Convite expirado ou já utilizado. Peça um novo ao responsável."));
            }

            var empresa = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == convite.TenantId, ct);
            if (empresa is null || !empresa.IsActive)
                return HttpResultsExtensions.Problem(new Error("invite.not_acceptable", "Convite indisponível."));

            var user = new MamaoUser
            {
                Id = Guid.CreateVersion7(),
                UserName = convite.Email,
                Email = convite.Email,
                FullName = convite.FullName,
                TenantId = convite.TenantId,
                Role = convite.Role,
                CreatedAt = agora,
            };

            var criado = await userManager.CreateAsync(user, request.Password);
            if (!criado.Succeeded)
            {
                var primeiro = criado.Errors.First();
                return HttpResultsExtensions.Problem(new Error(
                    $"auth.{primeiro.Code}", primeiro.Description, nameof(request.Password)));
            }

            // Marcar aceito DEPOIS de criar o usuario: se a senha for recusada, o convite
            // continua valendo e a pessoa tenta de novo em vez de ficar sem caminho.
            convite.Accept(user.Id, agora);
            await dbContext.SaveChangesAsync(ct);

            var pair = await tokens.IssueAsync(user, ct);

            // Entra ja logado: mandar para a tela de login logo depois de escolher a senha
            // e um passo a mais sem nenhuma razao.
            return TypedResults.Ok(new AuthResponse(
                pair.AccessToken,
                pair.AccessTokenExpiresAt,
                pair.RefreshToken,
                empresa.Id,
                empresa.Name,
                user.Role,
                Roles.PermissionsOf(user.Role)));
        })
        .WithName("acceptInvite")
        .Produces<AuthResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous();
    }

    private static Task<Invite?> BuscarPorTokenAsync(
        string token, MamaoIdentityDbContext dbContext, CancellationToken ct)
    {
        var hash = Invite.HashToken(token ?? string.Empty);
        return dbContext.Invites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
    }

    private static async Task EnviarAsync(
        Invite convite,
        string token,
        MamaoIdentityDbContext dbContext,
        IEmailSender emails,
        EmailTemplates templates,
        ClaimsPrincipal principal,
        ILogger<Invite> logger,
        CancellationToken ct)
    {
        var empresa = await dbContext.Tenants
            .Where(t => t.Id == convite.TenantId)
            .Select(t => t.Name)
            .FirstAsync(ct);

        var quemConvidou = principal.FindFirstValue(ClaimTypes.Name) ?? "O responsável pela sua empresa";

        try
        {
            await emails.SendAsync(
                templates.Invite(convite.Email, convite.FullName, empresa, quemConvidou, token, Invite.ValidityDays), ct);
        }
        catch (Exception erro)
        {
            // O convite ja esta gravado: falha de SMTP nao pode desfazer o que o RH fez.
            // Ele reenvia pela tela, e o log diz o que aconteceu.
            logger.LogError(erro, "Convite {ConviteId} gravado, mas o e-mail nao saiu.", convite.Id);
        }
    }

    private static Guid TenantOf(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(TokenService.TenantClaim)!);

    private static Guid UserOf(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub")!);

    private static InviteResponse ToResponse(Invite i, DateTimeOffset agora) => new(
        i.Id, i.Email, i.FullName, i.Role, i.EmployeeId, i.StatusAt(agora), i.ExpiresAt, i.CreatedAt);
}
