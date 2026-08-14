using Mamao.People.Contracts;
using Mamao.People.Contracts.Events;
using Mamao.People.Domain.Employees;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Mamao.People.Application.Employees;

/// <summary>
/// Casos de uso de funcionario. Uma classe por agregado enquanto couber; quebra em
/// handlers separados quando comecar a doer — nao antes.
/// </summary>
public sealed class EmployeeService(
    IPeopleDbContext dbContext,
    IPeopleOutbox outbox,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    public async Task<PagedResult<EmployeeListItem>> ListAsync(
        string? search, bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // O filtro por tenant e global e vem do TenantDbContext. Nao ha Where(TenantId)
        // aqui de proposito: repetir o filtro por endpoint e o caminho para esquecer dele
        // em algum. Ver docs/adr/0003-multi-tenancy.md.
        var query = dbContext.Employees.AsNoTracking();

        if (!includeInactive)
            query = query.Where(e => e.TerminatedOn == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Like sobre valor em minusculas em vez do ILIKE do Npgsql: mantem Application
            // sem dependencia do provider. No Postgres, um indice de expressao em
            // lower(full_name) atende quando a busca comecar a doer.
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            query = query.Where(e =>
                EF.Functions.Like(e.FullName.ToLower(), term) ||
                (e.Code != null && EF.Functions.Like(e.Code.ToLower(), term)) ||
                EF.Functions.Like(e.PositionName.ToLower(), term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeListItem(
                e.Id, e.Code, e.FullName, e.PositionName, e.HiredOn, e.TerminatedOn == null))
            .ToListAsync(ct);

        return new PagedResult<EmployeeListItem>(items, total, page, pageSize);
    }

    public async Task<EmployeeResponse?> GetAsync(EmployeeId id, CancellationToken ct)
    {
        var employee = await dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        return employee is null ? null : ToResponse(employee);
    }

    public async Task<Result<EmployeeResponse>> CreateAsync(CreateEmployeeRequest request, CancellationToken ct)
    {
        if (await CodeIsTakenAsync(request.Code, null, ct))
        {
            return Result.Failure<EmployeeResponse>(new Error(
                "employee.duplicate_code",
                $"Ja existe um funcionario com a matricula {request.Code}.",
                nameof(request.Code)));
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var creation = Employee.Hire(request.FullName, request.PositionName, request.HiredOn, today, request.Code);
        if (creation.IsFailure)
            return Result.Failure<EmployeeResponse>(creation.Error!);

        var employee = creation.Value;
        dbContext.Employees.Add(employee);

        // Evento e dado de negocio na MESMA transacao. E dai que vem a garantia — nao do
        // broker. Ver docs/adr/0005-outbox-e-mensageria.md.
        outbox.Enqueue(new EmployeeHired(
            EventId: Guid.CreateVersion7(),
            TenantId: tenantContext.Current,
            OccurredAt: timeProvider.GetUtcNow(),
            EmployeeId: employee.Id,
            FullName: employee.FullName,
            PositionName: employee.PositionName,
            HiredOn: employee.HiredOn));

        await dbContext.SaveChangesAsync(ct);

        return Result.Success(ToResponse(employee));
    }

    public async Task<Result<EmployeeResponse>> UpdateAsync(
        EmployeeId id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var employee = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
            return Result.Failure<EmployeeResponse>(NotFound(id));

        var rename = employee.Rename(request.FullName);
        if (rename.IsFailure)
            return Result.Failure<EmployeeResponse>(rename.Error!);

        var position = employee.ChangePosition(request.PositionName);
        if (position.IsFailure)
            return Result.Failure<EmployeeResponse>(position.Error!);

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(employee));
    }

    public async Task<Result<EmployeeResponse>> TerminateAsync(
        EmployeeId id, TerminateEmployeeRequest request, CancellationToken ct)
    {
        var employee = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
            return Result.Failure<EmployeeResponse>(NotFound(id));

        var termination = employee.Terminate(request.TerminatedOn);
        if (termination.IsFailure)
            return Result.Failure<EmployeeResponse>(termination.Error!);

        await dbContext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(employee));
    }

    private async Task<bool> CodeIsTakenAsync(string? code, EmployeeId? excluding, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var normalized = code.Trim();

        return await dbContext.Employees.AnyAsync(
            e => e.Code == normalized && (excluding == null || e.Id != excluding.Value), ct);
    }

    private static Error NotFound(EmployeeId id) =>
        new("employee.not_found", $"Funcionario {id} nao encontrado.");

    private static EmployeeResponse ToResponse(Employee e) => new(
        e.Id, e.Code, e.FullName, e.PositionName, e.HiredOn, e.TerminatedOn, e.IsActive, e.UserId is not null);
}
