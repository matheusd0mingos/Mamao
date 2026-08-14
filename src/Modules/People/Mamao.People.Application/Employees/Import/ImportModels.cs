namespace Mamao.People.Application.Employees.Import;

/// <summary>Como cada campo do Mamao foi (ou nao foi) encontrado no arquivo enviado.</summary>
/// <param name="Header">Nome da coluna no arquivo do cliente. Nulo = nao encontrada.</param>
/// <param name="AcceptedNames">Nomes que reconhecemos — a tela mostra isso quando falta a coluna.</param>
public sealed record EmployeeImportColumn(
    string Field,
    string Label,
    string? Header,
    bool Required,
    IReadOnlyList<string> AcceptedNames);

public enum EmployeeImportRowStatus
{
    Pronta,
    Invalida,
    DuplicadaNoArquivo,
    DuplicadaNoCadastro,
}

/// <param name="LineNumber">Linha no arquivo, contando o cabecalho. E o que a pessoa procura no Excel.</param>
public sealed record EmployeeImportRow(
    int LineNumber,
    string? Code,
    string FullName,
    string PositionName,
    DateOnly? HiredOn,
    EmployeeImportRowStatus Status,
    IReadOnlyList<string> Errors);

public sealed record EmployeeImportSummary(int Total, int Ready, int Invalid, int Duplicates);

public sealed record EmployeeImportPreview(
    IReadOnlyList<EmployeeImportColumn> Columns,
    IReadOnlyList<EmployeeImportRow> Rows,
    EmployeeImportSummary Summary,
    IReadOnlyList<string> Warnings);

/// <param name="Imported">Linhas gravadas.</param>
/// <param name="Skipped">Linhas puladas por erro ou duplicidade — nunca em silencio.</param>
public sealed record EmployeeImportResult(int Imported, int Skipped);

/// <summary>
/// O formato esperado, servido pela API para a tela explicar ANTES de o usuario escolher o
/// arquivo. Poderia estar escrito no HTML, mas ai os nomes aceitos existiriam em dois
/// lugares e um dia divergiriam — e o cliente veria uma coluna anunciada que o parser recusa.
/// </summary>
/// <param name="Example">Conteudo do modelo, exibido na tela e baixavel em /import/template.</param>
public sealed record EmployeeImportFormat(
    IReadOnlyList<EmployeeImportColumn> Columns,
    string Example,
    int MaxRows);
