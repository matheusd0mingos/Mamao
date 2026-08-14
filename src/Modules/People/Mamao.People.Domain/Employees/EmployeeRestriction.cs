using Mamao.People.Contracts;
using Mamao.People.Domain.Organization;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Employees;

/// <summary>
/// Alguém que NÃO entra em certa atividade — por restrição médica, por função ou por
/// decisão da chefia.
///
/// É diferente de ausência: a ausência tem data e acaba; a restrição vale enquanto valer, e
/// nem sempre tem prazo. Modelar as duas na mesma tabela obrigaria a inventar um fim para
/// "não faz serviço armado", que é permanente até deixar de ser.
///
/// <b>O motivo é opcional de propósito.</b> Uma restrição médica pode revelar dado de saúde,
/// e nem todo mundo que monta escala precisa saber a doença de ninguém — precisa saber que a
/// pessoa não entra. Quem quiser registrar o motivo, registra; quem não quiser, não é
/// obrigado pelo formulário. Ver a política de privacidade.
/// </summary>
public sealed class EmployeeRestriction : ITenantOwned
{
    public const int MaxAtividade = 160;
    public const int MaxMotivo = 300;

    private EmployeeRestriction() { }

    public RestrictionId Id { get; private set; }
    public Guid TenantId { get; set; }
    public EmployeeId EmployeeId { get; private set; }

    /// <summary>
    /// Nome da atividade da qual a pessoa está fora, dobrado para comparação. Nulo = fora de
    /// TODA escala — o caso de quem está em restrição ampla.
    /// </summary>
    public string? ActivityKey { get; private set; }

    /// <summary>O nome como foi escrito, para a tela. Nulo quando vale para toda escala.</summary>
    public string? ActivityName { get; private set; }

    public string? Reason { get; private set; }

    public DateOnly StartsOn { get; private set; }

    /// <summary>Até quando. Nulo = sem prazo definido.</summary>
    public DateOnly? EndsOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedByName { get; private set; } = null!;

    public static Result<EmployeeRestriction> Create(
        EmployeeId employeeId,
        DateOnly startsOn,
        DateTimeOffset now,
        string createdByName,
        string? activityName = null,
        string? reason = null,
        DateOnly? endsOn = null)
    {
        activityName = activityName?.Trim();

        if (activityName is { Length: > MaxAtividade })
        {
            return Result.Failure<EmployeeRestriction>(new Error(
                "restriction.activity_too_long",
                $"O nome da atividade passa de {MaxAtividade} caracteres.",
                nameof(activityName)));
        }

        if (reason is { Length: > MaxMotivo })
        {
            return Result.Failure<EmployeeRestriction>(new Error(
                "restriction.reason_too_long",
                $"O motivo passa de {MaxMotivo} caracteres.",
                nameof(reason)));
        }

        if (endsOn is { } fim && fim < startsOn)
        {
            return Result.Failure<EmployeeRestriction>(new Error(
                "restriction.end_before_start",
                "A data final não pode ser anterior à inicial.",
                nameof(endsOn)));
        }

        var vazia = string.IsNullOrWhiteSpace(activityName);

        return Result.Success(new EmployeeRestriction
        {
            Id = RestrictionId.New(),
            EmployeeId = employeeId,
            ActivityKey = vazia ? null : Position.Normalize(activityName),
            ActivityName = vazia ? null : activityName,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim(),
            StartsOn = startsOn,
            EndsOn = endsOn,
            CreatedAt = now,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? "—" : createdByName.Trim(),
        });
    }

    /// <summary>
    /// Esta restrição impede a pessoa de entrar nesta atividade, neste dia?
    ///
    /// A comparação é sobre o nome DOBRADO, a mesma chave que o rodízio usa para contar
    /// histórico. "Serviço Armado" e "servico armado" são a mesma atividade — se não fossem,
    /// a restrição escaparia por uma diferença de acento.
    /// </summary>
    public bool Impede(string atividadeDobrada, DateOnly dia)
    {
        if (dia < StartsOn || (EndsOn is { } fim && dia > fim))
            return false;

        return ActivityKey is null || ActivityKey == atividadeDobrada;
    }

    /// <summary>Texto curto para a tela de escala, sem obrigar a expor o motivo.</summary>
    public string Rotulo() => ActivityName is null
        ? "restrição para toda escala"
        : $"restrição para {ActivityName}";
}
