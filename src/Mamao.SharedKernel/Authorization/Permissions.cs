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

    public const string TimeOffRequest = "timeoff.request";
    public const string TimeOffApprove = "timeoff.approve";

    public const string DocumentsRead = "documents.read";
    public const string DocumentsUpload = "documents.upload";
    public const string DocumentsApprove = "documents.approve";

    public const string WorkRead = "work.read";
    public const string WorkAssign = "work.assign";

    public const string ScheduleRead = "schedule.read";
    public const string ScheduleWrite = "schedule.write";

    public const string AuditRead = "audit.read";
    public const string SettingsWrite = "settings.write";
    public const string BillingManage = "billing.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        PeopleRead, PeopleWrite, PeopleDelete,
        TimeOffRequest, TimeOffApprove,
        DocumentsRead, DocumentsUpload, DocumentsApprove,
        WorkRead, WorkAssign,
        ScheduleRead, ScheduleWrite,
        AuditRead, SettingsWrite, BillingManage,
    ];
}

/// <summary>
/// Papel e um agrupamento de permissoes, nao um valor verificado no codigo.
/// Papeis customizados por tenant entram na V2 — o modelo ja suporta, falta so a tela.
/// </summary>
public static class Roles
{
    public const string Owner = "Owner";
    public const string Hr = "RH";
    public const string Manager = "Gestor";
    public const string Employee = "Funcionario";

    public static IReadOnlyList<string> All { get; } = [Owner, Hr, Manager, Employee];

    public static IReadOnlyList<string> PermissionsOf(string role) => role switch
    {
        Owner => Permissions.All,
        Hr =>
        [
            Permissions.PeopleRead, Permissions.PeopleWrite,
            Permissions.DocumentsRead, Permissions.DocumentsUpload, Permissions.DocumentsApprove,
            Permissions.TimeOffRequest, Permissions.TimeOffApprove,
            Permissions.WorkRead, Permissions.ScheduleRead,
            Permissions.AuditRead,
        ],
        Manager =>
        [
            Permissions.PeopleRead,
            Permissions.DocumentsRead,
            Permissions.TimeOffRequest, Permissions.TimeOffApprove,
            Permissions.WorkRead, Permissions.WorkAssign,
            Permissions.ScheduleRead, Permissions.ScheduleWrite,
        ],
        Employee =>
        [
            Permissions.PeopleRead,
            Permissions.DocumentsRead, Permissions.DocumentsUpload,
            Permissions.TimeOffRequest,
            Permissions.WorkRead,
            Permissions.ScheduleRead,
        ],
        _ => [],
    };
}
