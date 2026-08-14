using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Mamao.People.Application.Employees.Import;

/// <summary>
/// Le a planilha que o cliente ja tem, em vez de exigir que ele produza a planilha que
/// nos queremos. Cadastrar 30 pessoas num formulario e onde o trial morre — ver
/// docs/produto/mvp-e-posicionamento.md#p3.
///
/// Por isso o parser aceita arquivo sujo:
/// - colunas em QUALQUER ordem, identificadas pelo cabecalho;
/// - varios nomes possiveis por coluna ("cargo", "funcao", "posto");
/// - separador , ou ; (o Excel em portugues exporta com ;);
/// - acento e caixa no cabecalho, com ou sem;
/// - UTF-8 com ou sem BOM, e tambem Latin-1 (o outro jeito do Excel exportar);
/// - datas em dd/MM/aaaa, aaaa-MM-dd e dd-MM-aaaa;
/// - linhas em branco no meio.
///
/// Classe pura: nao toca em banco. As duplicidades contra o cadastro sao verificadas
/// depois, pelo EmployeeImportService.
/// </summary>
public sealed class EmployeeCsvParser
{
    /// <summary>Teto de linhas. Arquivo maior que isso quase sempre e a planilha errada.</summary>
    public const int MaxRows = 5_000;

    private static readonly string[] SeparadoresCandidatos = [";", ",", "\t"];

    /// <summary>
    /// Nomes aceitos por campo. A primeira entrada e a que sai no modelo para download.
    /// Crescer esta lista e mais barato que pedir para o cliente renomear coluna.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["fullName"] = ["nome", "nome completo", "funcionario", "colaborador", "nome do funcionario"],
        ["positionName"] = ["cargo", "funcao", "posto", "cargo/funcao", "ocupacao"],
        ["hiredOn"] = ["admissao", "data de admissao", "data admissao", "dt admissao", "entrada"],
        ["code"] = ["matricula", "codigo", "registro", "chapa", "id"],
    };

    private static readonly string[] Obrigatorios = ["fullName", "positionName", "hiredOn"];

    public EmployeeImportPreview Parse(Stream conteudo, DateOnly hoje)
    {
        var texto = LerTexto(conteudo, out var avisoDeCodificacao);
        var avisos = new List<string>();

        if (avisoDeCodificacao is not null)
            avisos.Add(avisoDeCodificacao);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return new EmployeeImportPreview([], [], new EmployeeImportSummary(0, 0, 0, 0),
                ["O arquivo está vazio."]);
        }

        var separador = DetectarSeparador(texto);

        var configuracao = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = separador,
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
        };

        using var leitor = new StringReader(texto);
        using var csv = new CsvReader(leitor, configuracao);

        if (!csv.Read() || !csv.ReadHeader())
        {
            return new EmployeeImportPreview([], [], new EmployeeImportSummary(0, 0, 0, 0),
                ["Não foi possível ler o cabeçalho. A primeira linha precisa ter os nomes das colunas."]);
        }

        var cabecalhos = csv.HeaderRecord ?? [];
        var mapeamento = Mapear(cabecalhos);

        var faltando = Obrigatorios
            .Where(campo => mapeamento.First(m => m.Field == campo).Header is null)
            .Select(RotuloDe)
            .ToList();

        if (faltando.Count > 0)
        {
            avisos.Add($"Não encontrei a(s) coluna(s): {string.Join(", ", faltando)}. " +
                       "Baixe o modelo para ver os nomes aceitos.");

            return new EmployeeImportPreview(mapeamento, [], new EmployeeImportSummary(0, 0, 0, 0), avisos);
        }

        var linhas = new List<EmployeeImportRow>();
        var matriculasVistas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        while (csv.Read())
        {
            // RawRow, nao um contador proprio: linha em branco no meio da planilha e
            // comum, e um contador de REGISTROS deslocaria todos os numeros seguintes.
            // O numero so serve para a pessoa achar a linha no Excel — se nao bate, nao serve.
            var numeroDaLinha = csv.Parser.RawRow;

            if (linhas.Count >= MaxRows)
            {
                avisos.Add($"O arquivo tem mais de {MaxRows:N0} linhas. " +
                           "Só as primeiras foram lidas — divida a planilha e importe em partes.");
                break;
            }

            var linha = LerLinha(csv, mapeamento, numeroDaLinha, hoje, matriculasVistas);
            if (linha is not null)
                linhas.Add(linha);
        }

        return new EmployeeImportPreview(mapeamento, linhas, Resumir(linhas), avisos);
    }

    private EmployeeImportRow? LerLinha(
        CsvReader csv,
        IReadOnlyList<EmployeeImportColumn> mapeamento,
        int numeroDaLinha,
        DateOnly hoje,
        Dictionary<string, int> matriculasVistas)
    {
        string? Campo(string nome)
        {
            var coluna = mapeamento.First(m => m.Field == nome).Header;
            if (coluna is null) return null;

            return csv.TryGetField<string>(coluna, out var valor) ? valor?.Trim() : null;
        }

        var nome = Campo("fullName") ?? string.Empty;
        var cargo = Campo("positionName") ?? string.Empty;
        var admissaoBruta = Campo("hiredOn") ?? string.Empty;
        var matricula = Campo("code");
        matricula = string.IsNullOrWhiteSpace(matricula) ? null : matricula.Trim();

        // Linha inteiramente vazia (comum no fim de planilha exportada) e ignorada em
        // silencio: reportar como erro so assustaria sem motivo.
        if (nome.Length == 0 && cargo.Length == 0 && admissaoBruta.Length == 0 && matricula is null)
            return null;

        var erros = new List<string>();

        if (nome.Length < 2)
            erros.Add("Nome vazio ou muito curto.");

        if (cargo.Length < 2)
            erros.Add("Cargo vazio ou muito curto.");

        DateOnly? admissao = null;

        if (admissaoBruta.Length == 0)
        {
            erros.Add("Data de admissão vazia.");
        }
        else if (!TentarLerData(admissaoBruta, out var data))
        {
            erros.Add($"Data de admissão \"{admissaoBruta}\" não reconhecida. Use dd/mm/aaaa.");
        }
        else if (data > hoje.AddYears(1))
        {
            // Mesma regra do dominio: admissao futura e legitima, ano digitado errado nao.
            erros.Add($"Data de admissão muito distante ({data:dd/MM/yyyy}). Confira o ano.");
        }
        else
        {
            admissao = data;
        }

        var situacao = erros.Count > 0
            ? EmployeeImportRowStatus.Invalida
            : EmployeeImportRowStatus.Pronta;

        // Duplicidade DENTRO do arquivo. A checagem contra o cadastro e feita depois,
        // no servico, que e quem tem banco.
        if (matricula is not null && situacao == EmployeeImportRowStatus.Pronta)
        {
            if (matriculasVistas.TryGetValue(matricula, out var linhaAnterior))
            {
                situacao = EmployeeImportRowStatus.DuplicadaNoArquivo;
                erros.Add($"Matrícula {matricula} já aparece na linha {linhaAnterior}.");
            }
            else
            {
                matriculasVistas[matricula] = numeroDaLinha;
            }
        }

        return new EmployeeImportRow(numeroDaLinha, matricula, nome, cargo, admissao, situacao, erros);
    }

    private static IReadOnlyList<EmployeeImportColumn> Mapear(IEnumerable<string> cabecalhos)
    {
        var normalizados = cabecalhos
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToDictionary(Normalizar, c => c, StringComparer.Ordinal);

        return Aliases.Select(par =>
        {
            var encontrado = par.Value
                .Select(alias => normalizados.GetValueOrDefault(Normalizar(alias)))
                .FirstOrDefault(v => v is not null);

            return new EmployeeImportColumn(
                Field: par.Key,
                Label: RotuloDe(par.Key),
                Header: encontrado,
                Required: Obrigatorios.Contains(par.Key),
                AcceptedNames: par.Value);
        }).ToList();
    }

    private static string RotuloDe(string campo) => campo switch
    {
        "fullName" => "Nome",
        "positionName" => "Cargo",
        "hiredOn" => "Admissão",
        "code" => "Matrícula",
        _ => campo,
    };

    /// <summary>
    /// Tabela explicita de dobra de acento. Deliberadamente NAO usa string.Normalize:
    /// normalizacao depende de ICU e, sem ele, vira no-op silencioso — o cabecalho
    /// "Admissão" simplesmente deixa de ser encontrado e o cliente ve "coluna faltando"
    /// num arquivo correto. Leitura de arquivo de cliente nao pode depender de como o
    /// host foi compilado. Sao 20 letras do portugues; a lista e a garantia.
    /// </summary>
    private const string ComAcento = "áàâãäéèêëíìîïóòôõöúùûüçñ";
    private const string SemAcento = "aaaaaeeeeiiiiooooouuuucn";

    /// <summary>Minusculas, sem acento e sem pontuacao: "Data de Admissão" casa com "data admissao".</summary>
    private static string Normalizar(string valor)
    {
        var destino = new StringBuilder(valor.Length);
        var espacoPendente = false;

        foreach (var caractere in valor.ToLowerInvariant())
        {
            var indice = ComAcento.IndexOf(caractere);
            var letra = indice >= 0 ? SemAcento[indice] : caractere;

            if (char.IsLetterOrDigit(letra))
            {
                if (espacoPendente && destino.Length > 0)
                    destino.Append(' ');

                espacoPendente = false;
                destino.Append(letra);
            }
            else
            {
                // Qualquer separador (espaco, barra, hifen, ponto) vira um unico espaco:
                // "Cargo/Função" e "cargo funcao" passam a ser a mesma coluna.
                espacoPendente = true;
            }
        }

        return destino.ToString();
    }

    /// <summary>
    /// Lista fechada de formatos, sem fallback dependente de cultura. Um "ultimo recurso"
    /// com CultureInfo("pt-BR") tornaria a leitura da data refem da configuracao do host —
    /// e a mesma planilha poderia ser lida diferente em dev e em producao.
    /// </summary>
    private static bool TentarLerData(string valor, out DateOnly data)
    {
        string[] formatos =
        [
            "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "dd-MM-yyyy", "d-M-yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
        ];

        return DateOnly.TryParseExact(valor, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }

    /// <summary>
    /// O Excel em portugues exporta CSV com ponto e virgula. Contamos as ocorrencias no
    /// cabecalho em vez de assumir: assumir virgula quebra o arquivo do cliente tipico.
    /// </summary>
    private static string DetectarSeparador(string texto)
    {
        var primeiraLinha = texto.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;

        return SeparadoresCandidatos
            .OrderByDescending(sep => primeiraLinha.Split(sep).Length)
            .First();
    }

    /// <summary>
    /// UTF-8 com ou sem BOM; caindo para Latin-1 quando os bytes nao formam UTF-8 valido —
    /// que e como o Excel salva em muita maquina. Sem isso, "João" chega como "Jo?o".
    /// </summary>
    private static string LerTexto(Stream conteudo, out string? aviso)
    {
        using var memoria = new MemoryStream();
        conteudo.CopyTo(memoria);
        var bytes = memoria.ToArray();

        aviso = null;

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(RemoverBom(bytes));
        }
        catch (DecoderFallbackException)
        {
            aviso = "O arquivo não estava em UTF-8; foi lido como Latin-1. " +
                    "Confira se os acentos ficaram corretos na prévia.";
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static byte[] RemoverBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    private static EmployeeImportSummary Resumir(IReadOnlyList<EmployeeImportRow> linhas) => new(
        Total: linhas.Count,
        Ready: linhas.Count(l => l.Status == EmployeeImportRowStatus.Pronta),
        Invalid: linhas.Count(l => l.Status == EmployeeImportRowStatus.Invalida),
        Duplicates: linhas.Count(l => l.Status is EmployeeImportRowStatus.DuplicadaNoArquivo
                                                or EmployeeImportRowStatus.DuplicadaNoCadastro));

    /// <summary>
    /// O formato esperado, para a tela explicar antes de o usuario escolher o arquivo.
    /// Sai daqui — e nao de um texto no frontend — para nao existirem duas verdades.
    /// </summary>
    public static EmployeeImportFormat DescribeFormat() =>
        new(Mapear([]), TemplateTexto, MaxRows);

    private const string TemplateTexto = """
        Nome;Cargo;Admissão;Matrícula
        Carlos Mendes;Vigilante;11/03/2024;V-001
        Ana Lima;Vigilante noturno;02/07/2023;V-002
        Beatriz Costa;Supervisora de operações;04/05/2021;

        """;

    /// <summary>
    /// Modelo para download. Sai com ponto e virgula e BOM porque e assim que o Excel em
    /// portugues abre sem estragar acento — e o cliente vai abrir no Excel.
    /// </summary>
    public static byte[] BuildTemplate() =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(TemplateTexto)];
}
