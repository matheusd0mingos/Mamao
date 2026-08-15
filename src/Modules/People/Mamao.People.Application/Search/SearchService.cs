using Mamao.People.Domain.Missions;
using Mamao.People.Domain.Work;
using Mamao.SharedKernel.Auditing;
using Mamao.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Search;

/// <summary>
/// O tipo do resultado. A tela decide o ícone e a rota a partir daqui — o servidor não
/// devolve URL, senão a API passaria a conhecer as rotas do frontend e um renomear de
/// rota viraria deploy dos dois lados.
/// </summary>
public enum SearchResultKind
{
    Pessoa,
    Missao,
    Demanda,
    Setor,
    Cargo,
}

/// <param name="Subtitle">O que distingue dois resultados de mesmo nome. Sem isto a lista mente.</param>
public sealed record SearchResult(
    SearchResultKind Kind,
    Guid Id,
    string Title,
    string? Subtitle);

public sealed record SearchResponse(string Query, IReadOnlyList<SearchResult> Results, bool Truncated);

/// <summary>
/// Busca global.
///
/// Existe porque, passada a primeira semana, ninguém navega por menu: a pessoa sabe o
/// nome do que procura e quer chegar lá. Sem isto, achar "Ana" custa Pessoas → filtro →
/// rolar; com isto, custa uma tecla.
///
/// <b>Respeita permissão por tipo.</b> Quem não pode ver escala não recebe missão no
/// resultado — uma busca que vaza o que a tela esconde é um vazamento com passo a passo.
/// </summary>
public sealed class SearchService(IPeopleDbContext dbContext, ICurrentActor actor)
{
    public const int MinimoDeCaracteres = 2;

    /// <summary>Por TIPO, não no total: senão trinta pessoas empurrariam missões e demandas para fora.</summary>
    private const int MaxPorTipo = 6;

    public async Task<SearchResponse> SearchAsync(string? query, CancellationToken ct)
    {
        var termo = (query ?? string.Empty).Trim();

        if (termo.Length < MinimoDeCaracteres)
            return new SearchResponse(termo, [], false);

        // Minusculas no C# e Contains no LINQ, que o EF traduz para LOWER(x) LIKE '%y%'
        // com o termo PARAMETRIZADO — entao "100%" e "a_b" sao texto, nao curinga.
        //
        // Nao usa ILIKE: ILIKE e do provider do Postgres, e esta camada nao referencia o
        // provider de proposito (ver a arquitetura do modulo). Trocar um LIKE por uma
        // dependencia de infraestrutura na camada de aplicacao seria caro pelo motivo
        // errado.
        //
        // Acento conta: "Joao" nao acha "Joao" com til. Resolver exige a extensao unaccent
        // no banco, que e decisao de infraestrutura — fica registrado como limite conhecido,
        // nao como esquecimento.
        var alvo = termo.ToLowerInvariant();

        var resultados = new List<SearchResult>();
        var truncado = false;

        if (actor.Can(Permissions.PeopleRead))
        {
            var pessoas = await dbContext.Employees.AsNoTracking()
                .Where(e => e.FullName.ToLower().Contains(alvo)
                    || (e.Code != null && e.Code.ToLower().Contains(alvo))
                    || (e.Email != null && e.Email.ToLower().Contains(alvo)))
                // Quem está na ativa primeiro: procurar alguém desligado é a exceção.
                .OrderBy(e => e.TerminatedOn != null)
                .ThenBy(e => e.FullName)
                .Take(MaxPorTipo + 1)
                .Select(e => new
                {
                    e.Id,
                    e.FullName,
                    e.TerminatedOn,
                    PositionName = dbContext.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault(),
                    DepartmentName = dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                })
                .ToListAsync(ct);

            truncado |= pessoas.Count > MaxPorTipo;

            resultados.AddRange(pessoas.Take(MaxPorTipo).Select(e => new SearchResult(
                SearchResultKind.Pessoa,
                e.Id.Value,
                e.FullName,
                e.TerminatedOn is not null
                    ? "desligado"
                    : string.Join(" · ", new[] { e.PositionName, e.DepartmentName }.Where(x => !string.IsNullOrEmpty(x))))));
        }

        if (actor.Can(Permissions.ScheduleRead))
        {
            var missoes = await dbContext.Missions.AsNoTracking()
                .Where(m => m.Name.ToLower().Contains(alvo))
                .OrderByDescending(m => m.On)
                .Take(MaxPorTipo + 1)
                .Select(m => new { m.Id, m.Name, m.On, m.Status, Escalados = m.Assignments.Count, m.RequiredPeople })
                .ToListAsync(ct);

            truncado |= missoes.Count > MaxPorTipo;

            resultados.AddRange(missoes.Take(MaxPorTipo).Select(m => new SearchResult(
                SearchResultKind.Missao,
                m.Id.Value,
                m.Name,
                $"{m.On:dd/MM/yyyy} · {m.Escalados}/{m.RequiredPeople} escaladas")));
        }

        if (actor.Can(Permissions.WorkRead))
        {
            var demandas = await dbContext.WorkItems.AsNoTracking()
                .Where(w => w.Title.ToLower().Contains(alvo))
                // Aberta antes de concluída: procurar o que já foi entregue é a exceção.
                .OrderBy(w => w.Status == WorkItemStatus.Concluida || w.Status == WorkItemStatus.Cancelada)
                .ThenBy(w => w.DueOn)
                .Take(MaxPorTipo + 1)
                .Select(w => new
                {
                    w.Id,
                    w.Title,
                    w.Status,
                    w.DueOn,
                    Responsavel = w.AssigneeId == null
                        ? null
                        : dbContext.Employees.Where(e => e.Id == w.AssigneeId).Select(e => e.FullName).FirstOrDefault(),
                })
                .ToListAsync(ct);

            truncado |= demandas.Count > MaxPorTipo;

            resultados.AddRange(demandas.Take(MaxPorTipo).Select(w => new SearchResult(
                SearchResultKind.Demanda,
                w.Id.Value,
                w.Title,
                string.Join(" · ", new[]
                {
                    WorkItemLabels.Of(w.Status),
                    w.Responsavel,
                    w.DueOn is { } prazo ? $"até {prazo:dd/MM}" : null,
                }.Where(x => !string.IsNullOrEmpty(x))))));
        }

        if (actor.Can(Permissions.PeopleRead))
        {
            var setores = await dbContext.Departments.AsNoTracking()
                .Where(d => d.Name.ToLower().Contains(alvo))
                .OrderBy(d => d.Name)
                .Take(MaxPorTipo)
                .Select(d => new { d.Id, d.Name, Pessoas = dbContext.Employees.Count(e => e.DepartmentId == d.Id && e.TerminatedOn == null) })
                .ToListAsync(ct);

            resultados.AddRange(setores.Select(d => new SearchResult(
                SearchResultKind.Setor, d.Id.Value, d.Name,
                d.Pessoas == 1 ? "1 pessoa" : $"{d.Pessoas} pessoas")));

            var cargos = await dbContext.Positions.AsNoTracking()
                .Where(p => p.Name.ToLower().Contains(alvo))
                .OrderBy(p => p.Name)
                .Take(MaxPorTipo)
                .Select(p => new { p.Id, p.Name, Pessoas = dbContext.Employees.Count(e => e.PositionId == p.Id && e.TerminatedOn == null) })
                .ToListAsync(ct);

            resultados.AddRange(cargos.Select(p => new SearchResult(
                SearchResultKind.Cargo, p.Id.Value, p.Name,
                p.Pessoas == 1 ? "1 pessoa" : $"{p.Pessoas} pessoas")));
        }

        return new SearchResponse(termo, resultados, truncado);
    }
}
