using Mamao.People.Contracts;
using Mamao.People.Domain.Employees;
using Mamao.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Employees;

public sealed record SaveContractRequest(
    EmploymentRegime Regime,
    WorkScheduleType ScheduleType,
    decimal WeeklyHours,
    DateOnly StartsOn,
    DateOnly? CompensationAgreementOn,
    string? Notes);

/// <param name="Warnings">Alertas, nunca bloqueios. A tela mostra; quem decide é a pessoa.</param>
public sealed record ContractResponse(
    EmployeeId EmployeeId,
    EmploymentRegime Regime,
    WorkScheduleType ScheduleType,
    decimal WeeklyHours,
    DateOnly StartsOn,
    DateOnly? CompensationAgreementOn,
    string? Notes,
    IReadOnlyList<ContractWarning> Warnings);

/// <summary>
/// Contrato do funcionário. Um por pessoa, criado ou atualizado pelo mesmo caminho —
/// a tela não deveria precisar saber se é a primeira vez.
/// </summary>
public sealed class ContractService(IPeopleDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ContractResponse?> GetAsync(EmployeeId employeeId, CancellationToken ct)
    {
        var contrato = await dbContext.EmploymentContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmployeeId == employeeId, ct);

        return contrato is null ? null : ToResponse(contrato);
    }

    /// <summary>
    /// Cria ou atualiza. O início da vigência, quando não informado, é a data de admissão:
    /// é o que ele é na esmagadora maioria dos casos, e perguntar duas vezes a mesma data
    /// no cadastro é atrito sem retorno.
    /// </summary>
    public async Task<Result<ContractResponse>> SaveAsync(
        EmployeeId employeeId, SaveContractRequest request, CancellationToken ct)
    {
        var funcionario = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);

        if (funcionario is null)
            return Result.Failure<ContractResponse>(new Error("employee.not_found", "Funcionário não encontrado."));

        var inicio = request.StartsOn == default ? funcionario.HiredOn : request.StartsOn;

        if (inicio < funcionario.HiredOn)
        {
            return Result.Failure<ContractResponse>(new Error(
                "contract.starts_before_hire",
                $"A vigência não pode começar antes da admissão ({funcionario.HiredOn:dd/MM/yyyy}).",
                nameof(request.StartsOn)));
        }

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (request.CompensationAgreementOn is { } acordo && acordo > hoje)
        {
            return Result.Failure<ContractResponse>(new Error(
                "contract.agreement_in_future",
                "A data do acordo de compensação não pode estar no futuro.",
                nameof(request.CompensationAgreementOn)));
        }

        var contrato = await dbContext.EmploymentContracts.FirstOrDefaultAsync(c => c.EmployeeId == employeeId, ct);

        if (contrato is null)
        {
            var criacao = EmploymentContract.Create(
                employeeId, request.Regime, request.ScheduleType, request.WeeklyHours,
                inicio, request.CompensationAgreementOn, request.Notes);

            if (criacao.IsFailure)
                return Result.Failure<ContractResponse>(criacao.Error!);

            contrato = criacao.Value;
            dbContext.EmploymentContracts.Add(contrato);
        }
        else
        {
            var alteracao = contrato.Change(
                request.Regime, request.ScheduleType, request.WeeklyHours,
                inicio, request.CompensationAgreementOn, request.Notes);

            if (alteracao.IsFailure)
                return Result.Failure<ContractResponse>(alteracao.Error!);
        }

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(contrato));
    }

    private static ContractResponse ToResponse(EmploymentContract c) => new(
        c.EmployeeId, c.Regime, c.ScheduleType, c.WeeklyHours, c.StartsOn,
        c.CompensationAgreementOn, c.Notes, c.Warnings());
}
