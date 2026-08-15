using Mamao.People.Application.Search;
using Mamao.People.Application.Today;
using Mamao.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Mamao.People.Infrastructure.Endpoints;

/// <summary>
/// As duas rotas que abrem o sistema: o painel de hoje e a busca.
/// </summary>
public static class TodayEndpoints
{
    public static IEndpointRouteBuilder MapTodayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/today", async Task<IResult> (
            Guid? departmentId, bool? all, TodayService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(departmentId, all ?? false, ct)))
        .WithName("today")
        .WithTags("Today")
        .Produces<TodayPanel>()
        .RequireAuthorization(Permissions.PeopleRead);

        // A busca nao exige permissao propria: ela filtra POR TIPO com as permissoes que a
        // pessoa ja tem. Uma permissao "search" separada daria a falsa impressao de que
        // negá-la protege alguma coisa.
        app.MapGet("/api/v1/search", async Task<IResult> (
            string? q, SearchService service, CancellationToken ct) =>
            TypedResults.Ok(await service.SearchAsync(q, ct)))
        .WithName("search")
        .WithTags("Search")
        .Produces<SearchResponse>()
        .RequireAuthorization();

        return app;
    }
}
