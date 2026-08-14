using System.Text;
using Mamao.People.Application.Employees.Import;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// O parser existe para ler a planilha que o cliente JA TEM. Cada teste aqui e um jeito
/// real de a planilha chegar torta — e aceitar isso e o que faz o trial nao morrer no
/// cadastro. Ver docs/produto/mvp-e-posicionamento.md#p3.
/// </summary>
public class ImportacaoDePlanilhaTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 14);
    private readonly EmployeeCsvParser _parser = new();

    private EmployeeImportPreview Ler(string conteudo, Encoding? codificacao = null)
        => _parser.Parse(new MemoryStream((codificacao ?? Encoding.UTF8).GetBytes(conteudo)), Hoje);

    [Fact]
    public void Le_o_arquivo_no_formato_do_modelo()
    {
        var previa = Ler("""
            Nome;Cargo;Admissão;Matrícula
            Carlos Mendes;Vigilante;11/03/2024;V-001
            Ana Lima;Vigilante noturno;02/07/2023;V-002
            """);

        previa.Summary.Total.ShouldBe(2);
        previa.Summary.Ready.ShouldBe(2);
        previa.Rows[0].FullName.ShouldBe("Carlos Mendes");
        previa.Rows[0].HiredOn.ShouldBe(new DateOnly(2024, 3, 11));
        previa.Rows[0].Code.ShouldBe("V-001");
    }

    [Fact]
    public void Aceita_colunas_em_qualquer_ordem()
    {
        var previa = Ler("""
            Matrícula;Admissão;Cargo;Nome
            V-001;11/03/2024;Vigilante;Carlos Mendes
            """);

        previa.Summary.Ready.ShouldBe(1);
        previa.Rows[0].FullName.ShouldBe("Carlos Mendes");
        previa.Rows[0].PositionName.ShouldBe("Vigilante");
    }

    [Theory]
    [InlineData("Nome;Função;Data de Admissão")]
    [InlineData("NOME;CARGO;ADMISSAO")]
    [InlineData("nome completo;posto;dt admissao")]
    [InlineData("Colaborador;Ocupação;Entrada")]
    public void Reconhece_os_varios_nomes_que_a_planilha_do_cliente_usa(string cabecalho)
    {
        var previa = Ler($"{cabecalho}\nCarlos Mendes;Vigilante;11/03/2024");

        previa.Summary.Ready.ShouldBe(1, $"Cabecalho nao reconhecido: {cabecalho}");
    }

    [Theory]
    [InlineData("Nome;Cargo/Função;Admissão")]
    [InlineData("Nome;Cargo-Função;Data de Admissão")]
    [InlineData("  Nome  ;  CARGO  ;  Admissão  ")]
    public void Casa_cabecalho_com_acento_pontuacao_e_espaco_sobrando(string cabecalho)
    {
        // Regressao: enquanto InvariantGlobalization estava ligado, string.Normalize era
        // no-op e TODA planilha com "Admissão" era recusada como "coluna faltando". Hoje a
        // dobra de acento e uma tabela explicita, entao este teste nao depende de ICU.
        var previa = Ler($"{cabecalho}\nCarlos Mendes;Vigilante;11/03/2024");

        previa.Warnings.ShouldBeEmpty($"Cabecalho recusado: {cabecalho}");
        previa.Summary.Ready.ShouldBe(1);
    }

    [Fact]
    public void Aceita_virgula_como_separador()
    {
        // O Excel em portugues exporta com ponto e virgula; o resto do mundo com virgula.
        var previa = Ler("""
            Nome,Cargo,Admissão
            Carlos Mendes,Vigilante,11/03/2024
            """);

        previa.Summary.Ready.ShouldBe(1);
        previa.Rows[0].PositionName.ShouldBe("Vigilante");
    }

    [Theory]
    [InlineData("11/03/2024")]
    [InlineData("2024-03-11")]
    [InlineData("11-03-2024")]
    [InlineData("11.03.2024")]
    public void Aceita_os_formatos_de_data_que_aparecem_na_pratica(string data)
    {
        var previa = Ler($"Nome;Cargo;Admissão\nCarlos;Vigilante;{data}");

        previa.Rows[0].HiredOn.ShouldBe(new DateOnly(2024, 3, 11), $"Data nao lida: {data}");
    }

    [Fact]
    public void Le_acentos_de_arquivo_salvo_em_latin1()
    {
        // Sem isso, "João" chega como "Jo?o" e o cadastro nasce sujo.
        var previa = Ler("Nome;Cargo;Admissão\nJoão Conceição;Eletricista;11/03/2024", Encoding.Latin1);

        previa.Rows[0].FullName.ShouldBe("João Conceição");
        previa.Warnings.ShouldContain(a => a.Contains("Latin-1"));
    }

    [Fact]
    public void Ignora_linhas_em_branco_no_meio_e_no_fim()
    {
        var previa = Ler("""
            Nome;Cargo;Admissão
            Carlos Mendes;Vigilante;11/03/2024

            Ana Lima;Vigilante;02/07/2023
            ;;
            """);

        previa.Summary.Total.ShouldBe(2, "Linha vazia de planilha exportada nao e erro, e ruido.");
    }

    [Fact]
    public void Linha_em_branco_no_meio_nao_desloca_a_numeracao()
    {
        // A pessoa vai abrir o Excel e ir na linha que dissemos. Se um contador de
        // REGISTROS fosse usado, tudo depois da linha vazia apontaria para a linha errada.
        var previa = Ler("""
            Nome;Cargo;Admissão
            Carlos Mendes;Vigilante;11/03/2024

            Ana Lima;Vigilante;sem data
            """);

        previa.Rows[0].LineNumber.ShouldBe(2);
        previa.Rows[1].LineNumber.ShouldBe(4);
    }

    [Fact]
    public void Aponta_a_linha_exata_do_erro()
    {
        var previa = Ler("""
            Nome;Cargo;Admissão
            Carlos Mendes;Vigilante;11/03/2024
            ;Vigilante;02/07/2023
            Ana Lima;Vigilante;data invalida
            """);

        previa.Summary.Ready.ShouldBe(1);
        previa.Summary.Invalid.ShouldBe(2);

        // O numero da linha e o que a pessoa procura no Excel — precisa bater com o que
        // ela ve la, cabecalho incluido.
        previa.Rows[1].LineNumber.ShouldBe(3);
        previa.Rows[1].Errors.ShouldContain(e => e.Contains("Nome"));
        previa.Rows[2].LineNumber.ShouldBe(4);
        previa.Rows[2].Errors.ShouldContain(e => e.Contains("não reconhecida"));
    }

    [Fact]
    public void Marca_matricula_repetida_dentro_do_proprio_arquivo()
    {
        var previa = Ler("""
            Nome;Cargo;Admissão;Matrícula
            Carlos Mendes;Vigilante;11/03/2024;V-001
            Outro Carlos;Vigilante;02/07/2023;V-001
            """);

        previa.Rows[1].Status.ShouldBe(EmployeeImportRowStatus.DuplicadaNoArquivo);
        previa.Rows[1].Errors.ShouldContain(e => e.Contains("linha 2"));
    }

    [Fact]
    public void Avisa_qual_coluna_faltou_em_vez_de_so_recusar()
    {
        var previa = Ler("Nome;Matrícula\nCarlos Mendes;V-001");

        previa.Summary.Total.ShouldBe(0);
        previa.Warnings.ShouldContain(a => a.Contains("Cargo") && a.Contains("Admissão"));

        // A tela usa isto para mostrar quais nomes de coluna sao aceitos.
        previa.Columns.First(c => c.Field == "positionName").Header.ShouldBeNull();
        previa.Columns.First(c => c.Field == "positionName").AcceptedNames.ShouldContain("cargo");
    }

    [Fact]
    public void Recusa_admissao_com_ano_absurdo()
    {
        var previa = Ler("Nome;Cargo;Admissão\nCarlos;Vigilante;11/03/2224");

        previa.Rows[0].Status.ShouldBe(EmployeeImportRowStatus.Invalida);
        previa.Rows[0].Errors.ShouldContain(e => e.Contains("Confira o ano"));
    }

    [Fact]
    public void Arquivo_vazio_devolve_aviso_e_nao_estoura()
    {
        var previa = Ler(string.Empty);

        previa.Summary.Total.ShouldBe(0);
        previa.Warnings.ShouldNotBeEmpty();
    }

    [Fact]
    public void Formato_anunciado_na_tela_e_o_mesmo_que_o_parser_aceita()
    {
        // A tela explica o formato com ESTE objeto. Se ele divergisse do parser,
        // anunciariamos uma coluna que recusamos na hora de importar.
        var formato = EmployeeCsvParser.DescribeFormat();

        formato.Columns.Select(c => c.Field)
            .ShouldBe(["fullName", "positionName", "hiredOn", "code"], ignoreOrder: true);
        formato.Columns.Where(c => c.Required).Select(c => c.Label)
            .ShouldBe(["Nome", "Cargo", "Admissão"], ignoreOrder: true);
        formato.Columns.ShouldAllBe(c => c.Header == null && c.AcceptedNames.Count > 0);

        var previa = Ler(formato.Example);
        previa.Warnings.ShouldBeEmpty();
        previa.Summary.Ready.ShouldBe(3);
    }

    [Fact]
    public void Modelo_para_download_e_lido_pelo_proprio_parser()
    {
        // Se o modelo que entregamos nao passasse pelo nosso proprio parser, estariamos
        // ensinando o cliente a montar um arquivo que recusamos.
        var previa = _parser.Parse(new MemoryStream(EmployeeCsvParser.BuildTemplate()), Hoje);

        previa.Summary.Total.ShouldBe(3);
        previa.Summary.Ready.ShouldBe(3);
        previa.Warnings.ShouldBeEmpty();
    }
}
