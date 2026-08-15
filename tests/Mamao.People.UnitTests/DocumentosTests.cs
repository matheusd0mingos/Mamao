using Mamao.People.Contracts;
using Mamao.People.Domain.Documents;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// Documento com validade.
///
/// É o único objeto do produto que gera valor sem exigir disciplina: o documento vence
/// sozinho e o sistema avisa. Por isso as duas regras que importam são a situação (derivada,
/// nunca gravada) e o "avisa uma vez só" — um aviso que repete todo dia treina a pessoa a
/// ignorar o e-mail, e aí o aviso que importa também é ignorado.
/// </summary>
public class DocumentosTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 15);
    private static readonly DateTimeOffset Agora = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly EmployeeId Alguem = EmployeeId.New();

    private static Document Doc(int? venceEm = null, string tipo = "ASO", string arquivo = "aso.pdf")
        => Document.Create(
            Alguem, tipo, arquivo, "application/pdf", 1024, Agora, "RH",
            expiresOn: venceEm is { } d ? Hoje.AddDays(d) : null).Value;

    // ── situação ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sem_validade_nao_vence()
    {
        Doc().StatusEm(Hoje).ShouldBe(DocumentStatus.SemValidade);
    }

    [Fact]
    public void Longe_do_vencimento_e_valido()
    {
        Doc(venceEm: 90).StatusEm(Hoje).ShouldBe(DocumentStatus.Valido);
    }

    [Fact]
    public void Dentro_da_janela_esta_vencendo()
    {
        Doc(venceEm: Document.JanelaDeAvisoEmDias).StatusEm(Hoje).ShouldBe(DocumentStatus.Vencendo);
        Doc(venceEm: 1).StatusEm(Hoje).ShouldBe(DocumentStatus.Vencendo);
    }

    [Fact]
    public void No_dia_ainda_vale()
    {
        // Documento que vence hoje vale hoje. Marcar como vencido no proprio dia tiraria a
        // pessoa da escala um dia antes da hora.
        Doc(venceEm: 0).StatusEm(Hoje).ShouldBe(DocumentStatus.Vencendo);
    }

    [Fact]
    public void Depois_do_dia_esta_vencido()
    {
        Doc(venceEm: -1).StatusEm(Hoje).ShouldBe(DocumentStatus.Vencido);
    }

    // ── aviso ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Avisa_uma_vez_so()
    {
        var d = Doc(venceEm: 10);

        d.PrecisaAvisar(Hoje).ShouldBeTrue();
        d.MarcarAvisado();
        d.PrecisaAvisar(Hoje).ShouldBeFalse();
    }

    [Fact]
    public void Renovar_a_validade_faz_o_aviso_valer_de_novo()
    {
        var d = Doc(venceEm: 10);
        d.MarcarAvisado();

        // Trocar o documento por um novo, com validade nova, e justamente quando o aviso
        // precisa voltar — senao o sistema fica calado sobre o vencimento seguinte.
        d.Update(null, Hoje.AddDays(20), null);

        d.PrecisaAvisar(Hoje).ShouldBeTrue();
    }

    [Fact]
    public void Documento_sem_validade_nunca_avisa()
    {
        Doc().PrecisaAvisar(Hoje).ShouldBeFalse();
    }

    [Fact]
    public void Documento_vencido_continua_avisando_ate_alguem_resolver()
    {
        // Vencer nao encerra o assunto: o aviso sai enquanto a validade for a mesma.
        Doc(venceEm: -5).PrecisaAvisar(Hoje).ShouldBeTrue();
    }

    // ── arquivo ──────────────────────────────────────────────────────────────────

    [Fact]
    public void O_caminho_no_disco_nunca_vem_do_nome_enviado()
    {
        // "../../../etc/passwd" como nome de arquivo e o ataque mais velho contra upload.
        var d = Doc(arquivo: "../../../etc/passwd");

        d.FileName.ShouldBe("passwd");
        d.StoragePath.ShouldNotContain("..");
        d.StoragePath.ShouldNotContain("passwd");
    }

    [Fact]
    public void Nome_com_barra_invertida_tambem_e_cortado()
    {
        Doc(arquivo: @"C:\Users\rh\aso.pdf").FileName.ShouldBe("aso.pdf");
    }

    [Fact]
    public void Arquivo_grande_demais_e_recusado()
    {
        var r = Document.Create(
            Alguem, "ASO", "grande.pdf", "application/pdf",
            Document.MaxBytes + 1, Agora, "RH");

        r.IsFailure.ShouldBeTrue();
        r.Error!.Code.ShouldBe("document.file_too_large");
    }

    [Fact]
    public void Arquivo_vazio_e_recusado()
    {
        Document.Create(Alguem, "ASO", "vazio.pdf", "application/pdf", 0, Agora, "RH")
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validade_antes_da_emissao_e_recusada()
    {
        var r = Document.Create(
            Alguem, "ASO", "aso.pdf", "application/pdf", 100, Agora, "RH",
            issuedOn: Hoje, expiresOn: Hoje.AddDays(-1));

        r.IsFailure.ShouldBeTrue();
        r.Error!.Code.ShouldBe("document.expiry_before_issue");
    }

    [Fact]
    public void Tipo_dobrado_agrupa_aso_e_ASO()
    {
        Doc(tipo: "  aso  ").NormalizedKind.ShouldBe(Doc(tipo: "ASO").NormalizedKind);
    }
}
