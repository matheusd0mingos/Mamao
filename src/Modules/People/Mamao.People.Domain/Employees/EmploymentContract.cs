using Mamao.People.Contracts;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Employees;

/// <summary>
/// Regime de vínculo. Decide QUAL LEI se aplica a férias, licenças e jornada — não é
/// campo de cadastro. Fica no contrato e não no tenant porque prefeitura tem estatutário e
/// celetista na mesma folha, e corporação tem militar e civil.
/// Ver docs/adr/0017-regime-de-vinculo.md.
/// </summary>
public enum EmploymentRegime
{
    Clt,
    EstatutarioFederal,
    EstatutarioLocal,
    Militar,

    /// <summary>PJ, estágio, voluntário, terceirizado. Desliga o cálculo de férias em vez de fingir um número.</summary>
    Outro,
}

/// <summary>
/// Padrão de jornada. <see cref="Rodizio"/> existe porque, no segmento escolhido, boa parte
/// da operação não tem padrão semanal fixo: o serviço aparece e o efetivo é escalado por vez.
/// Ver docs/adr/0019-escala-por-rodizio.md.
/// </summary>
public enum WorkScheduleType
{
    /// <summary>Segunda a sexta, horário comercial.</summary>
    Administrativo,
    CincoDois,
    SeisUm,
    DozePorTrintaESeis,

    /// <summary>Sem padrão semanal: entra na escala de serviço quando é a vez.</summary>
    Rodizio,
    Outro,
}

/// <summary>
/// Contrato de trabalho do funcionário: regime, jornada e carga semanal.
///
/// É a base do cálculo de capacidade e o fallback da disponibilidade quando não há escala
/// publicada para o dia. Ver docs/produto/modelo-de-dominio.md#people.
///
/// Um contrato por funcionário, atualizado no lugar. Versionar por vigência é recuperável
/// depois — a migração transformaria a linha atual na versão vigente, com início na data de
/// admissão — então não vale pagar por histórico antes de alguém precisar dele.
/// </summary>
public sealed class EmploymentContract : ITenantOwned
{
    /// <summary>Teto da CLT (art. 58). Usado só para ALERTAR, e só em regime CLT.</summary>
    public const decimal CltWeeklyHoursLimit = 44m;

    private EmploymentContract() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public EmployeeId EmployeeId { get; private set; }

    public EmploymentRegime Regime { get; private set; }
    public WorkScheduleType ScheduleType { get; private set; }

    /// <summary>Carga contratual. No 12×36 é a média, não o que se cumpre numa semana isolada.</summary>
    public decimal WeeklyHours { get; private set; }

    public DateOnly StartsOn { get; private set; }

    /// <summary>
    /// Data do acordo individual escrito ou da norma coletiva que autoriza a compensação.
    /// Nulo significa "não registrado", que é diferente de "não existe" — o sistema alerta,
    /// não acusa.
    /// </summary>
    public DateOnly? CompensationAgreementOn { get; private set; }

    public string? Notes { get; private set; }

    public static Result<EmploymentContract> Create(
        EmployeeId employeeId,
        EmploymentRegime regime,
        WorkScheduleType scheduleType,
        decimal weeklyHours,
        DateOnly startsOn,
        DateOnly? compensationAgreementOn = null,
        string? notes = null)
    {
        var contrato = new EmploymentContract
        {
            Id = Guid.CreateVersion7(),
            EmployeeId = employeeId,
        };

        var alteracao = contrato.Change(regime, scheduleType, weeklyHours, startsOn, compensationAgreementOn, notes);

        return alteracao.IsFailure
            ? Result.Failure<EmploymentContract>(alteracao.Error!)
            : Result.Success(contrato);
    }

    public Result Change(
        EmploymentRegime regime,
        WorkScheduleType scheduleType,
        decimal weeklyHours,
        DateOnly startsOn,
        DateOnly? compensationAgreementOn,
        string? notes)
    {
        // Zero e negativo são erro de digitação; 84h é mais que qualquer jornada legal em
        // qualquer regime. Entre os dois extremos, quem julga é o alerta e não o bloqueio —
        // a operação real tem exceção e travar o cadastro não corrige nenhuma delas.
        if (weeklyHours <= 0 || weeklyHours > 84)
        {
            return Result.Failure(
                "contract.invalid_weekly_hours",
                "Carga semanal fora do razoável. Informe as horas contratuais da semana.",
                nameof(WeeklyHours));
        }

        if (startsOn == default)
            return Result.Failure("contract.start_required", "Informe o início da vigência.", nameof(StartsOn));

        Regime = regime;
        ScheduleType = scheduleType;
        WeeklyHours = decimal.Round(weeklyHours, 1);
        StartsOn = startsOn;
        CompensationAgreementOn = compensationAgreementOn;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        return Result.Success();
    }

    /// <summary>
    /// Teto semanal do estatutário federal (Lei 8.112/90, art. 19). Regra própria, com
    /// número próprio — não é a da CLT com outro nome.
    /// </summary>
    public const decimal EstatutarioFederalWeeklyHoursLimit = 40m;

    /// <summary>
    /// Teto semanal por regime, ou <c>null</c> quando o regime não tem um.
    ///
    /// <b>Militar não tem teto semanal e isso é resposta, não omissão:</b> o Estatuto dos
    /// Militares trata o serviço como dedicação integral, regulado por escala de cada Força
    /// — não por jornada semanal. Alertar com número inventado seria pior que calar.
    ///
    /// Estatutário estadual/municipal varia por ente federativo, então o padrão é não
    /// alertar; quem sabe o número é o cliente, e ele configura
    /// (ver <see href="../../../docs/adr/0017-regime-de-vinculo.md"/>).
    /// </summary>
    private decimal? TetoSemanal => Regime switch
    {
        EmploymentRegime.Clt => CltWeeklyHoursLimit,
        EmploymentRegime.EstatutarioFederal => EstatutarioFederalWeeklyHoursLimit,
        _ => null,
    };

    private string TetoSemanalFundamento => Regime switch
    {
        EmploymentRegime.Clt => "CLT, art. 58",
        EmploymentRegime.EstatutarioFederal => "Lei 8.112/90, art. 19",
        _ => string.Empty,
    };

    /// <summary>
    /// Alertas do contrato — nunca bloqueios. É a primeira aparição concreta do modo alerta
    /// decidido em P4b: o sistema aponta o que parece errado e deixa a pessoa decidir, porque
    /// a operação real tem exceção e porque nós não conhecemos a norma interna dela.
    ///
    /// <b>O regime não determina a jornada.</b> Militar pode cumprir expediente
    /// administrativo, e celetista pode estar em rodízio — os dois campos são independentes
    /// de propósito. O que o regime decide é QUAIS REGRAS se aplicam à jornada escolhida.
    /// Ver docs/adr/0017-regime-de-vinculo.md.
    /// </summary>
    public IReadOnlyList<ContractWarning> Warnings()
    {
        var alertas = new List<ContractWarning>();

        // Acordo de compensação do 12×36 é exigência da CLT (art. 59-A) e só dela.
        // Estatutário e militar têm normas próprias de escala, sem esse instrumento.
        if (Regime == EmploymentRegime.Clt
            && ScheduleType == WorkScheduleType.DozePorTrintaESeis
            && CompensationAgreementOn is null)
        {
            alertas.Add(new ContractWarning(
                "contract.missing_12x36_agreement",
                "Jornada 12×36 sem acordo registrado. A CLT (art. 59-A) pede acordo individual escrito "
                + "ou norma coletiva. Registre a data do acordo para o alerta sumir."));
        }

        if (TetoSemanal is { } teto && WeeklyHours > teto)
        {
            alertas.Add(new ContractWarning(
                "contract.weekly_hours_above_limit",
                $"Carga semanal de {WeeklyHours:0.#}h acima das {teto:0}h previstas ({TetoSemanalFundamento}). "
                + "Confira se a carga está correta ou se há instrumento que autorize."));
        }

        return alertas;
    }
}

/// <param name="Code">Estável, para a tela decidir; a mensagem pode mudar sem quebrar nada.</param>
public sealed record ContractWarning(string Code, string Message);
