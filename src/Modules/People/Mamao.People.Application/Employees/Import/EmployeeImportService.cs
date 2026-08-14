using Mamao.People.Contracts.Events;
using Mamao.People.Application.Organization;
using Mamao.People.Domain.Employees;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Employees.Import;

/// <summary>
/// Importacao em dois passos: primeiro a pessoa VE o que vai acontecer, depois confirma.
///
/// A previa nao guarda nada no servidor — o arquivo e reenviado na confirmacao. Guardar
/// exigiria armazenamento temporario, expiracao e limpeza, para economizar um upload de
/// alguns KB. Nao vale.
/// </summary>
public sealed class EmployeeImportService(
    IPeopleDbContext dbContext,
    IPeopleOutbox outbox,
    ITenantContext tenantContext,
    PositionService positions,
    TimeProvider timeProvider)
{
    private readonly EmployeeCsvParser _parser = new();

    public async Task<EmployeeImportPreview> PreviewAsync(Stream arquivo, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var previa = _parser.Parse(arquivo, hoje);

        var linhas = await MarcarDuplicadasNoCadastroAsync(previa.Rows, ct);

        return previa with
        {
            Rows = linhas,
            Summary = new EmployeeImportSummary(
                linhas.Count,
                linhas.Count(l => l.Status == EmployeeImportRowStatus.Pronta),
                linhas.Count(l => l.Status == EmployeeImportRowStatus.Invalida),
                linhas.Count(l => l.Status is EmployeeImportRowStatus.DuplicadaNoArquivo
                                           or EmployeeImportRowStatus.DuplicadaNoCadastro)),
        };
    }

    /// <summary>
    /// Grava as linhas prontas e pula o resto.
    ///
    /// Parcial e nao tudo-ou-nada de proposito: uma linha ruim nao pode bloquear 200 boas.
    /// So e aceitavel porque a previa mostrou exatamente o que seria pulado — importacao
    /// parcial sem previa deixaria a pessoa sem saber em que estado ficou o cadastro.
    /// </summary>
    public async Task<EmployeeImportResult> ImportAsync(Stream arquivo, CancellationToken ct)
    {
        var previa = await PreviewAsync(arquivo, ct);
        var prontas = previa.Rows.Where(l => l.Status == EmployeeImportRowStatus.Pronta).ToList();

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var agora = timeProvider.GetUtcNow();
        var importadas = 0;

        // Cargo do arquivo e texto; aqui vira cargo de verdade, criando o que faltar.
        // Criar na importacao e o que permite trazer a planilha sem cadastrar cargo antes —
        // e a mesma tolerancia que faz o leitor aceitar arquivo sujo. O cache carrega os
        // existentes de uma vez: sem ele seria uma consulta por linha.
        var cargos = await positions.LoadCacheAsync(ct);

        foreach (var linha in prontas)
        {
            var cargo = await positions.ResolveOrCreateAsync(linha.PositionName, cargos, ct);
            if (cargo.IsFailure)
                continue;

            var criacao = Employee.Hire(linha.FullName, cargo.Value.Id, linha.HiredOn!.Value, hoje, linha.Code);

            // O dominio ja validou o mesmo na previa; se recusar aqui, e divergencia entre
            // os dois caminhos e nao pode passar em silencio.
            if (criacao.IsFailure)
                continue;

            var funcionario = criacao.Value;
            dbContext.Employees.Add(funcionario);

            outbox.Enqueue(new EmployeeHired(
                EventId: Guid.CreateVersion7(),
                TenantId: tenantContext.Current,
                OccurredAt: agora,
                EmployeeId: funcionario.Id,
                FullName: funcionario.FullName,
                PositionName: cargo.Value.Name,
                HiredOn: funcionario.HiredOn));

            importadas++;
        }

        // Uma transacao para o lote inteiro: ou entram todas as linhas validas, ou nenhuma.
        await dbContext.SaveChangesAsync(ct);

        return new EmployeeImportResult(importadas, previa.Rows.Count - importadas);
    }

    private async Task<IReadOnlyList<EmployeeImportRow>> MarcarDuplicadasNoCadastroAsync(
        IReadOnlyList<EmployeeImportRow> linhas, CancellationToken ct)
    {
        var matriculas = linhas
            .Where(l => l.Status == EmployeeImportRowStatus.Pronta && l.Code is not null)
            .Select(l => l.Code!)
            .Distinct()
            .ToList();

        if (matriculas.Count == 0)
            return linhas;

        // Uma consulta para o lote inteiro. Uma por linha viraria N+1 num arquivo de 300.
        var jaExistem = await dbContext.Employees
            .Where(e => e.Code != null && matriculas.Contains(e.Code))
            .Select(e => e.Code!)
            .ToListAsync(ct);

        if (jaExistem.Count == 0)
            return linhas;

        var conjunto = jaExistem.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return linhas.Select(linha =>
            linha.Status == EmployeeImportRowStatus.Pronta && linha.Code is not null && conjunto.Contains(linha.Code)
                ? linha with
                {
                    Status = EmployeeImportRowStatus.DuplicadaNoCadastro,
                    Errors = [.. linha.Errors, $"Já existe um funcionário com a matrícula {linha.Code}."],
                }
                : linha).ToList();
    }
}
