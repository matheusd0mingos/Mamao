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
        RuleFor(x => x.PositionName).NotEmpty().MaximumLength(120)
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
        RuleFor(x => x.PositionName).NotEmpty().MaximumLength(120);
    }
}

public sealed class TerminateEmployeeRequestValidator : AbstractValidator<TerminateEmployeeRequest>
{
    public TerminateEmployeeRequestValidator()
        => RuleFor(x => x.TerminatedOn).NotEqual(default(DateOnly))
            .WithMessage("Informe a data de desligamento.");
}
