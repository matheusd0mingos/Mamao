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

    /// <summary>Cargo. Referencia, nao texto: o Marco 4 pergunta "quantos vigilantes neste turno?".</summary>
    public PositionId PositionId { get; private set; }

    /// <summary>
    /// Setor. OPCIONAL, e isso diverge do modelo de dominio original de proposito: exigir
    /// que o cliente monte a taxonomia antes de cadastrar a primeira pessoa e atrito no
    /// exato momento em que ele esta avaliando o produto. Ele organiza depois, quando o
    /// setor passa a servir para alguma coisa (cobertura, filtro, aprovacao).
    /// </summary>
    public DepartmentId? DepartmentId { get; private set; }

    /// <summary>Gestor direto. Opcional pelo mesmo motivo do setor.</summary>
    public EmployeeId? ManagerId { get; private set; }

    public DateOnly HiredOn { get; private set; }
    public DateOnly? TerminatedOn { get; private set; }

    /// <summary>Login e opcional e vem depois, por convite. Nunca obrigatorio.</summary>
    public Guid? UserId { get; private set; }

    public bool IsActive => TerminatedOn is null;

    public static Result<Employee> Hire(
        string fullName,
        PositionId positionId,
        DateOnly hiredOn,
        DateOnly today,
        string? code = null,
        DepartmentId? departmentId = null)
    {
        fullName = fullName?.Trim() ?? string.Empty;
        code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();

        if (fullName.Length < 2)
            return Result.Failure<Employee>(new Error(
                "employee.name_required", "Informe o nome do funcionario.", nameof(FullName)));

        if (positionId.Value == Guid.Empty)
            return Result.Failure<Employee>(new Error(
                "employee.position_required", "Informe o cargo do funcionario.", nameof(PositionId)));

        // Admissao futura e legitima (contratacao ja acertada); um limite evita erro de
        // digitacao de ano, que e o engano real.
        if (hiredOn > today.AddYears(1))
            return Result.Failure<Employee>(new Error(
                "employee.hired_on_too_far", "Data de admissao muito distante. Confira o ano.", nameof(HiredOn)));

        return Result.Success(new Employee
        {
            Id = EmployeeId.New(),
            FullName = fullName,
            PositionId = positionId,
            DepartmentId = departmentId,
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

    public Result ChangePosition(PositionId positionId)
    {
        if (positionId.Value == Guid.Empty)
            return Result.Failure("employee.position_required", "Informe o cargo do funcionario.", nameof(PositionId));

        PositionId = positionId;
        return Result.Success();
    }

    public void MoveToDepartment(DepartmentId? departmentId) => DepartmentId = departmentId;

    public Result AssignManager(EmployeeId? managerId)
    {
        // Chefe de si mesmo quebra qualquer travessia de hierarquia depois — e o unico
        // ciclo que da para barrar sem carregar a cadeia inteira. Ciclo mais longo
        // (A chefia B, B chefia A) e verificado no servico, que enxerga os dois.
        if (managerId == Id)
            return Result.Failure("employee.self_manager", "Uma pessoa não pode ser gestora de si mesma.", nameof(ManagerId));

        ManagerId = managerId;
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
