namespace Mamao.SharedKernel.Authorization;

/// <summary>
/// Permissoes granulares. A verificacao e sempre por policy com este nome, nunca por
/// papel: <c>[Authorize(Policy = Permissions.TimeOffApprove)]</c> sobrevive a criacao de
/// papel customizado por cliente; <c>if (role == "RH")</c> nao.
/// Ver docs/adr/0007-autorizacao.md.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "perm";

    public const string PeopleRead = "people.read";
    public const string PeopleWrite = "people.write";
    public const string PeopleDelete = "people.delete";

    /// <summary>
    /// Ver quem esta fora e por que: calendario, disponibilidade e o painel de hoje.
    ///
    /// Separada de <see cref="PeopleRead"/> de proposito. "Quem trabalha aqui" e "quem esta
    /// de afastamento medico" nao sao a mesma sensibilidade — a segunda pode revelar dado de
    /// saude. Sem esta separacao, dar acesso ao sistema para quem cuida das CONTAS obrigaria
    /// a entregar junto a agenda medica da empresa inteira.
    /// </summary>
    public const string AvailabilityRead = "availability.read";

    public const string TimeOffRequest = "timeoff.request";
    public const string TimeOffApprove = "timeoff.approve";

    public const string DocumentsRead = "documents.read";
    public const string DocumentsUpload = "documents.upload";
    public const string DocumentsApprove = "documents.approve";

    public const string WorkRead = "work.read";
    public const string WorkAssign = "work.assign";

    public const string ScheduleRead = "schedule.read";
    public const string ScheduleWrite = "schedule.write";

    /// <summary>Convidar pessoas para acessar o sistema, reenviar e cancelar convite.</summary>
    public const string UsersInvite = "users.invite";

    public const string AuditRead = "audit.read";
    public const string SettingsWrite = "settings.write";
    public const string BillingManage = "billing.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        PeopleRead, PeopleWrite, PeopleDelete,
        AvailabilityRead,
        TimeOffRequest, TimeOffApprove,
        DocumentsRead, DocumentsUpload, DocumentsApprove,
        WorkRead, WorkAssign,
        ScheduleRead, ScheduleWrite,
        UsersInvite, AuditRead, SettingsWrite, BillingManage,
    ];
}

/// <summary>
/// Papel e um agrupamento de permissoes, nao um valor verificado no codigo.
/// Papeis customizados por tenant entram na V2 — o modelo ja suporta, falta so a tela.
/// </summary>
public static class Roles
{
    // Codigo em ingles, como todo identificador persistido (ADR-0012). O rotulo em
    // portugues e responsabilidade da UI — traduzir o valor guardado misturaria dado
    // com apresentacao e quebraria na primeira mudanca de texto.
    public const string Owner = "Owner";
    public const string Hr = "Hr";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    /// <summary>
    /// Quem cuida do sistema: contas, configuracao e auditoria. Nao cuida de gente.
    ///
    /// Existe porque as duas responsabilidades sao de pessoas diferentes ate numa empresa
    /// pequena — quem instala o computador do novo funcionario nao e quem aprova as ferias
    /// dele. E o unico papel que NAO enxerga disponibilidade: dar conta a alguem nao exige
    /// saber quem esta de afastamento medico.
    /// </summary>
    public const string ItManager = "ItManager";

    public static IReadOnlyList<string> All { get; } = [Owner, Hr, Manager, ItManager, Employee];

    public static IReadOnlyList<string> PermissionsOf(string role) => role switch
    {
        Owner => Permissions.All,
        ItManager =>
        [
            // Ve o quadro de pessoas para saber a quem dar acesso, e nada da agenda delas.
            Permissions.PeopleRead,
            Permissions.UsersInvite,
            Permissions.AuditRead,
            Permissions.SettingsWrite,
        ],
        Hr =>
        [
            Permissions.PeopleRead, Permissions.PeopleWrite,
            Permissions.AvailabilityRead,
            Permissions.DocumentsRead, Permissions.DocumentsUpload, Permissions.DocumentsApprove,
            Permissions.TimeOffRequest, Permissions.TimeOffApprove,
            Permissions.WorkRead, Permissions.ScheduleRead,
            Permissions.UsersInvite,
            Permissions.AuditRead,
        ],
        Manager =>
        [
            Permissions.PeopleRead,
            Permissions.AvailabilityRead,
            Permissions.DocumentsRead,
            Permissions.TimeOffRequest, Permissions.TimeOffApprove,
            Permissions.WorkRead, Permissions.WorkAssign,
            Permissions.ScheduleRead, Permissions.ScheduleWrite,
        ],
        Employee =>
        [
            Permissions.PeopleRead,
            Permissions.AvailabilityRead,
            Permissions.DocumentsRead, Permissions.DocumentsUpload,
            Permissions.TimeOffRequest,
            Permissions.WorkRead,
            Permissions.ScheduleRead,
        ],
        _ => [],
    };
}
