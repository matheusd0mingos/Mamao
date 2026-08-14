using FluentValidation;

namespace Mamao.People.Application.Employees;

/// <summary>
/// Validacao de forma da requisicao (obrigatorio, tamanho, formato). Regra de negocio de
/// verdade fica no dominio — duplicar regra aqui e no agregado e como acabam divergindo.
/// </summary>
public sealed class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200)
            .WithMessage("Informe o nome do funcionario.");
        RuleFor(x => x.PositionId).Must(id => id.Value != Guid.Empty)
            .WithMessage("Informe o cargo do funcionario.");
        RuleFor(x => x.Code).MaximumLength(50);
        RuleFor(x => x.HiredOn).NotEqual(default(DateOnly))
            .WithMessage("Informe a data de admissao.");
    }
}

public sealed class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PositionId).Must(id => id.Value != Guid.Empty)
            .WithMessage("Informe o cargo do funcionario.");
    }
}

public sealed class CreateDepartmentRequestValidator : AbstractValidator<Organization.CreateDepartmentRequest>
{
    public CreateDepartmentRequestValidator()
        => RuleFor(x => x.Name).NotEmpty().MaximumLength(120)
            .WithMessage("Informe o nome do setor.");
}

public sealed class UpdateDepartmentRequestValidator : AbstractValidator<Organization.UpdateDepartmentRequest>
{
    public UpdateDepartmentRequestValidator()
        => RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
}

public sealed class CreatePositionRequestValidator : AbstractValidator<Organization.CreatePositionRequest>
{
    public CreatePositionRequestValidator()
        => RuleFor(x => x.Name).NotEmpty().MaximumLength(120)
            .WithMessage("Informe o nome do cargo.");
}

public sealed class UpdatePositionRequestValidator : AbstractValidator<Organization.UpdatePositionRequest>
{
    public UpdatePositionRequestValidator()
        => RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
}

public sealed class TerminateEmployeeRequestValidator : AbstractValidator<TerminateEmployeeRequest>
{
    public TerminateEmployeeRequestValidator()
        => RuleFor(x => x.TerminatedOn).NotEqual(default(DateOnly))
            .WithMessage("Informe a data de desligamento.");
}
