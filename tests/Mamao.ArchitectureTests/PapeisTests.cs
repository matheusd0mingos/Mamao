using Mamao.SharedKernel.Authorization;
using Shouldly;
using Xunit;

namespace Mamao.ArchitectureTests;

/// <summary>
/// Os papéis e o que cada um alcança.
///
/// Ficam num teste porque a lista de permissões de um papel é o tipo de coisa que alguém
/// "só completa" ao adicionar uma permissão nova — e aí o gerente de TI ganha acesso à
/// agenda médica da empresa sem ninguém decidir isso.
/// </summary>
public class PapeisTests
{
    [Fact]
    public void Todo_papel_conhecido_tem_alguma_permissao()
    {
        foreach (var papel in Roles.All)
            Roles.PermissionsOf(papel).ShouldNotBeEmpty($"o papel {papel} não concede nada");
    }

    [Fact]
    public void Papel_desconhecido_nao_concede_nada()
    {
        // Falhar fechado: um papel gravado por engano no banco não pode virar acesso total.
        Roles.PermissionsOf("Diretor").ShouldBeEmpty();
        Roles.PermissionsOf("").ShouldBeEmpty();
    }

    [Fact]
    public void So_o_proprietario_mexe_em_cobranca()
    {
        foreach (var papel in Roles.All.Where(p => p != Roles.Owner))
            Roles.PermissionsOf(papel).ShouldNotContain(Permissions.BillingManage);
    }

    // ── gerente de TI ────────────────────────────────────────────────────────────

    [Fact]
    public void Gerente_de_TI_cuida_de_conta_configuracao_e_auditoria()
    {
        var permissoes = Roles.PermissionsOf(Roles.ItManager);

        permissoes.ShouldContain(Permissions.UsersInvite);
        permissoes.ShouldContain(Permissions.SettingsWrite);
        permissoes.ShouldContain(Permissions.AuditRead);

        // Precisa do quadro de pessoas para saber a quem dar acesso.
        permissoes.ShouldContain(Permissions.PeopleRead);
    }

    [Fact]
    public void Gerente_de_TI_nao_ve_disponibilidade()
    {
        // É a razão de a permissão existir separada: dar conta a alguém não exige saber
        // quem está de afastamento médico. Se este teste falhar, a separação foi desfeita.
        Roles.PermissionsOf(Roles.ItManager).ShouldNotContain(Permissions.AvailabilityRead);
    }

    [Fact]
    public void Gerente_de_TI_nao_decide_sobre_gente()
    {
        var permissoes = Roles.PermissionsOf(Roles.ItManager);

        permissoes.ShouldNotContain(Permissions.TimeOffApprove);
        permissoes.ShouldNotContain(Permissions.PeopleWrite);
        permissoes.ShouldNotContain(Permissions.PeopleDelete);
        permissoes.ShouldNotContain(Permissions.ScheduleWrite);
        permissoes.ShouldNotContain(Permissions.WorkAssign);
    }

    // ── quem opera a equipe ──────────────────────────────────────────────────────

    [Fact]
    public void Quem_opera_a_equipe_ve_disponibilidade()
    {
        foreach (var papel in new[] { Roles.Owner, Roles.Hr, Roles.Manager, Roles.Employee })
        {
            Roles.PermissionsOf(papel)
                .ShouldContain(Permissions.AvailabilityRead, $"{papel} precisa ver quem está fora");
        }
    }

    [Fact]
    public void Funcionario_pede_ferias_mas_nao_aprova()
    {
        var permissoes = Roles.PermissionsOf(Roles.Employee);

        permissoes.ShouldContain(Permissions.TimeOffRequest);
        permissoes.ShouldNotContain(Permissions.TimeOffApprove);
    }

    [Fact]
    public void Proprietario_alcanca_tudo()
    {
        Roles.PermissionsOf(Roles.Owner).ShouldBe(Permissions.All);
    }

    [Fact]
    public void Nenhum_papel_concede_permissao_que_nao_existe()
    {
        // Uma permissão com erro de digitação nunca casaria com a policy, e o papel
        // pareceria funcionar até alguém reclamar de um 403 sem explicação.
        foreach (var papel in Roles.All)
        {
            foreach (var permissao in Roles.PermissionsOf(papel))
                Permissions.All.ShouldContain(permissao, $"{papel} concede \"{permissao}\", que não existe");
        }
    }
}
