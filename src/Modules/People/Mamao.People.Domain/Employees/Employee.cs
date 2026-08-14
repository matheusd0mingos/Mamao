using Mamao.People.Contracts;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Employees;

/// <summary>
/// Funcionario: o objeto central do sistema.
///
/// Nao tem UserId obrigatorio, e isso e permanente — o produto precisa ser util com um
/// unico usuario logado. Ver docs/produto/mvp-e-posicionamento.md#p1.
/// </summary>
public sealed class Employee : ITenantOwned
{
    private Employee() { }

    public EmployeeId Id { get; private set; }
    public Guid TenantId { get; set; }

    /// <summary>Matricula/codigo interno. Unico por tenant quando informado.</summary>
    public string? Code { get; private set; }

    public string FullName { get; private set; } = null!;
    public string PositionName { get; private set; } = null!;
    public DateOnly HiredOn { get; private set; }
    public DateOnly? TerminatedOn { get; private set; }

    /// <summary>Login e opcional e vem depois, por convite. Nunca obrigatorio.</summary>
    public Guid? UserId { get; private set; }

    public bool IsActive => TerminatedOn is null;

    public static Result<Employee> Hire(
        string fullName,
        string positionName,
        DateOnly hiredOn,
        DateOnly today,
        string? code = null)
    {
        fullName = fullName?.Trim() ?? string.Empty;
        positionName = positionName?.Trim() ?? string.Empty;
        code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();

        if (fullName.Length < 2)
            return Result.Failure<Employee>(new Error(
                "employee.name_required", "Informe o nome do funcionario.", nameof(FullName)));

        if (positionName.Length < 2)
            return Result.Failure<Employee>(new Error(
                "employee.position_required", "Informe o cargo do funcionario.", nameof(PositionName)));

        // Admissao futura e legitima (contratacao ja acertada); um limite evita erro de
        // digitacao de ano, que e o engano real.
        if (hiredOn > today.AddYears(1))
            return Result.Failure<Employee>(new Error(
                "employee.hired_on_too_far", "Data de admissao muito distante. Confira o ano.", nameof(HiredOn)));

        return Result.Success(new Employee
        {
            Id = EmployeeId.New(),
            FullName = fullName,
            PositionName = positionName,
            HiredOn = hiredOn,
            Code = code,
        });
    }

    public Result Rename(string fullName)
    {
        fullName = fullName?.Trim() ?? string.Empty;

        if (fullName.Length < 2)
            return Result.Failure("employee.name_required", "Informe o nome do funcionario.", nameof(FullName));

        FullName = fullName;
        return Result.Success();
    }

    public Result ChangePosition(string positionName)
    {
        positionName = positionName?.Trim() ?? string.Empty;

        if (positionName.Length < 2)
            return Result.Failure("employee.position_required", "Informe o cargo do funcionario.", nameof(PositionName));

        PositionName = positionName;
        return Result.Success();
    }

    public Result Terminate(DateOnly terminatedOn)
    {
        if (TerminatedOn is not null)
            return Result.Failure("employee.already_terminated", "Funcionario ja esta desligado.");

        if (terminatedOn < HiredOn)
            return Result.Failure(
                "employee.termination_before_hire",
                "A data de desligamento nao pode ser anterior a admissao.",
                nameof(TerminatedOn));

        TerminatedOn = terminatedOn;
        return Result.Success();
    }

    /// <summary>Vincula um login ao funcionario. Opcional, feito por convite.</summary>
    public void LinkUser(Guid userId) => UserId = userId;
}
