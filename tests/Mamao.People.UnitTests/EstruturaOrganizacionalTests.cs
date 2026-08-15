using Mamao.People.Domain.Organization;
using Shouldly;
using Xunit;

namespace Mamao.People.UnitTests;

/// <summary>
/// O setor e arvore, e arvore com caminho materializado tem duas armadilhas: ciclo e
/// caminho desatualizado. As duas quebram em silencio — a consulta continua rodando e
/// devolve gente a menos. Por isso estao cobertas aqui.
/// </summary>
public class SetoresTests
{
    private static Department Raiz(string nome = "Operações") => Department.Create(nome, null).Value;

    [Fact]
    public void Setor_raiz_nasce_no_nivel_zero_com_caminho_proprio()
    {
        var setor = Raiz();

        setor.Depth.ShouldBe(0);
        setor.ParentId.ShouldBeNull();
        setor.Path.ShouldBe($"/{setor.Id.Value}/");
    }

    [Fact]
    public void Filho_herda_o_caminho_do_pai()
    {
        var pai = Raiz();
        var filho = Department.Create("Turno A", pai).Value;

        filho.Depth.ShouldBe(1);
        filho.ParentId.ShouldBe(pai.Id);
        filho.Path.ShouldBe($"{pai.Path}{filho.Id.Value}/");

        // E o que faz "tudo abaixo de Operacoes" ser um LIKE de prefixo.
        filho.Path.ShouldStartWith(pai.Path);
    }

    [Fact]
    public void Profundidade_lida_do_caminho_bate_com_a_guardada()
    {
        var raiz = Raiz();
        var filho = Department.Create("Turno A", raiz).Value;
        var neto = Department.Create("Portaria", filho).Value;

        Department.DepthFromPath(raiz.Path).ShouldBe(raiz.Depth);
        Department.DepthFromPath(filho.Path).ShouldBe(filho.Depth);
        Department.DepthFromPath(neto.Path).ShouldBe(2);
    }

    [Fact]
    public void Recusa_mover_setor_para_dentro_de_si_mesmo()
    {
        var pai = Raiz();
        var filho = Department.Create("Turno A", pai).Value;

        var resultado = pai.MoveTo(filho);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error!.Code.ShouldBe("department.cycle");
    }

    [Fact]
    public void Recusa_setor_como_pai_de_si_mesmo()
    {
        var setor = Raiz();

        setor.MoveTo(setor).Error!.Code.ShouldBe("department.cycle");
    }

    [Fact]
    public void Mover_para_a_raiz_volta_o_setor_para_o_nivel_zero()
    {
        var pai = Raiz();
        var filho = Department.Create("Turno A", pai).Value;

        filho.MoveTo(null).IsSuccess.ShouldBeTrue();

        filho.Depth.ShouldBe(0);
        filho.ParentId.ShouldBeNull();
        filho.Path.ShouldBe($"/{filho.Id.Value}/");
    }

    [Fact]
    public void Recusa_aninhar_alem_do_limite()
    {
        // MaxDepth conta NIVEIS, e a raiz e o nivel 0: com 6, o mais fundo permitido e 5.
        var atual = Raiz();

        for (var nivel = 1; nivel < Department.MaxDepth; nivel++)
            atual = Department.Create($"Nível {nivel}", atual).Value;

        atual.Depth.ShouldBe(Department.MaxDepth - 1);

        var estouro = Department.Create("Fundo demais", atual);

        estouro.IsFailure.ShouldBeTrue();
        estouro.Error!.Code.ShouldBe("department.too_deep");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    public void Recusa_setor_sem_nome_utilizavel(string nome)
        => Department.Create(nome, null).Error!.Code.ShouldBe("department.name_required");
}

public class CargosTests
{
    [Theory]
    [InlineData("Vigilante", "vigilante")]
    [InlineData("  VIGILANTE  ", "vigilante")]
    [InlineData("Supervisora de Operações", "supervisora de operacoes")]
    [InlineData("Cargo/Função", "cargo funcao")]
    [InlineData("Técnico   de    Manutenção", "tecnico de manutencao")]
    public void Dobra_o_nome_para_comparar(string escrito, string esperado)
    {
        // A dobra e o que impede "Vigilante", "vigilante" e "Vigilante " de virarem tres
        // cargos — e sem tres cargos distintos a pergunta "quantos vigilantes neste turno?"
        // do Marco 4 passa a ter resposta.
        Position.Create(escrito).Value.NormalizedName.ShouldBe(esperado);
    }

    [Fact]
    public void Guarda_o_nome_como_o_cliente_escreveu()
    {
        var cargo = Position.Create("  Supervisora de Operações  ").Value;

        cargo.Name.ShouldBe("Supervisora de Operações", "A tela mostra o nome dele, não o nosso.");
        cargo.NormalizedName.ShouldBe("supervisora de operacoes");
    }

    [Fact]
    public void Renomear_atualiza_a_chave_de_comparacao()
    {
        var cargo = Position.Create("Vigilante").Value;

        cargo.Rename("Agente de Portaria").IsSuccess.ShouldBeTrue();

        cargo.NormalizedName.ShouldBe("agente de portaria");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    public void Recusa_cargo_sem_nome_utilizavel(string nome)
        => Position.Create(nome).Error!.Code.ShouldBe("position.name_required");

    // ── nome dobrado ─────────────────────────────────────────────────────────────

    [Fact]
    public void Acento_e_caixa_nao_criam_dois_setores()
    {
        // Duas pessoas mexem na estrutura — o dono e quem administra o sistema. Sem a
        // dobra, "Secao Tecnica" e "Seção Técnica" viram dois setores, a equipe se divide
        // entre eles e a escala de um nao enxerga a gente do outro.
        var a = Department.Create("Seção Técnica", null).Value;
        var b = Department.Create("  secao   TECNICA ", null).Value;

        b.NormalizedName.ShouldBe(a.NormalizedName);
    }

    [Fact]
    public void Renomear_atualiza_o_nome_dobrado()
    {
        // Se o dobrado ficasse para tras, o indice unico passaria a proteger o nome antigo.
        var setor = Department.Create("Operações", null).Value;
        setor.Rename("Seção de Operações");

        setor.NormalizedName.ShouldBe("secao de operacoes");
    }

    [Fact]
    public void Setores_diferentes_continuam_diferentes()
    {
        var a = Department.Create("Seção Técnica", null).Value;
        var b = Department.Create("Seção Jurídica", null).Value;

        b.NormalizedName.ShouldNotBe(a.NormalizedName);
    }
}
