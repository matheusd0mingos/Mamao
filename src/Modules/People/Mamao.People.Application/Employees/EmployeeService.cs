using Mamao.People.Contracts;
using Mamao.People.Contracts.Events;
using Mamao.People.Domain.Employees;
using Mamao.SharedKernel.Auditing;
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
    IAuditLog audit,
    TimeProvider timeProvider)
{
    public async Task<PagedResult<EmployeeListItem>> ListAsync(
        string? search, bool includeInactive, int page, int pageSize,
        DepartmentId? departmentId, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // O filtro por tenant e global e vem do TenantDbContext. Nao ha Where(TenantId)
        // aqui de proposito: repetir o filtro por endpoint e o caminho para esquecer dele
        // em algum. Ver docs/adr/0003-multi-tenancy.md.
        var query = dbContext.Employees.AsNoTracking();

        if (!includeInactive)
            query = query.Where(e => e.TerminatedOn == null);

        // Filtro por setor traz a SUBARVORE, nao so o setor exato: quem pede "Operacoes"
        // quer os turnos abaixo tambem. E o que o caminho materializado paga.
        if (departmentId is { } setorId)
        {
            var caminho = await dbContext.Departments
                .Where(d => d.Id == setorId)
                .Select(d => d.Path)
                .FirstOrDefaultAsync(ct);

            if (caminho is null)
                return new PagedResult<EmployeeListItem>([], 0, page, pageSize);

            var abaixo = dbContext.Departments.Where(d => d.Path.StartsWith(caminho)).Select(d => d.Id);
            query = query.Where(e => e.DepartmentId != null && abaixo.Contains(e.DepartmentId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Like sobre valor em minusculas em vez do ILIKE do Npgsql: mantem Application
            // sem dependencia do provider. No Postgres, um indice de expressao em
            // lower(full_name) atende quando a busca comecar a doer.
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            query = query.Where(e =>
                EF.Functions.Like(e.FullName.ToLower(), term) ||
                (e.Code != null && EF.Functions.Like(e.Code.ToLower(), term)) ||
                dbContext.Positions.Any(p => p.Id == e.PositionId && EF.Functions.Like(p.Name.ToLower(), term)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeListItem(
                e.Id,
                e.Code,
                e.FullName,
                dbContext.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault()!,
                dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                e.Email,
                e.HiredOn,
                e.TerminatedOn == null))
            .ToListAsync(ct);

        return new PagedResult<EmployeeListItem>(items, total, page, pageSize);
    }

    public Task<EmployeeResponse?> GetAsync(EmployeeId id, CancellationToken ct)
        => ProjectAsync(dbContext.Employees.AsNoTracking().Where(e => e.Id == id), ct);

    /// <summary>
    /// Projecao unica de EmployeeResponse. Existe uma vez so porque tres caminhos
    /// (buscar, criar, atualizar) devolvem a mesma forma — e duplicar a juncao seria
    /// garantir que um dos tres um dia esquecesse um campo novo.
    /// </summary>
    private Task<EmployeeResponse?> ProjectAsync(IQueryable<Employee> query, CancellationToken ct)
        => query
            .Select(e => new EmployeeResponse(
                e.Id,
                e.Code,
                e.FullName,
                e.PositionId,
                dbContext.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault()!,
                e.DepartmentId,
                dbContext.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                e.ManagerId,
                dbContext.Employees.Where(m => m.Id == e.ManagerId).Select(m => m.FullName).FirstOrDefault(),
                e.Email,
                e.HiredOn,
                e.TerminatedOn,
                e.TerminatedOn == null,
                e.UserId != null))
            .FirstOrDefaultAsync(ct)!;

    public async Task<Result<EmployeeResponse>> CreateAsync(CreateEmployeeRequest request, CancellationToken ct)
    {
        if (await CodeIsTakenAsync(request.Code, null, ct))
        {
            return Result.Failure<EmployeeResponse>(new Error(
                "employee.duplicate_code",
                $"Ja existe um funcionario com a matricula {request.Code}.",
                nameof(request.Code)));
        }

        var referencias = await ValidarReferenciasAsync(request.PositionId, request.DepartmentId, request.ManagerId, ct);
        if (referencias.IsFailure)
            return Result.Failure<EmployeeResponse>(referencias.Error!);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (await EmailIsTakenAsync(request.Email, null, ct))
        {
            return Result.Failure<EmployeeResponse>(new Error(
                "employee.duplicate_email",
                $"Já existe um funcionário com o e-mail {request.Email!.Trim()}. " +
                "Se as duas pessoas usam o mesmo endereço, deixe este campo em branco.",
                nameof(request.Email)));
        }

        var creation = Employee.Hire(
            request.FullName, request.PositionId, request.HiredOn, today, request.Code,
            request.DepartmentId, request.Email);
        if (creation.IsFailure)
            return Result.Failure<EmployeeResponse>(creation.Error!);

        var employee = creation.Value;

        if (request.ManagerId is not null)
        {
            var gestor = employee.AssignManager(request.ManagerId);
            if (gestor.IsFailure)
                return Result.Failure<EmployeeResponse>(gestor.Error!);
        }

        dbContext.Employees.Add(employee);

        // Auditoria na mesma transacao do fato, como o evento.
        audit.Record(
            AuditActions.EmployeeCreated, nameof(Employee), employee.Id.ToString(),
            Rotulo(employee), new { employee.Code, employee.HiredOn });

        // Evento e dado de negocio na MESMA transacao. E dai que vem a garantia — nao do
        // broker. Ver docs/adr/0005-outbox-e-mensageria.md.
        outbox.Enqueue(new EmployeeHired(
            EventId: Guid.CreateVersion7(),
            TenantId: tenantContext.Current,
            OccurredAt: timeProvider.GetUtcNow(),
            EmployeeId: employee.Id,
            FullName: employee.FullName,
            PositionName: await NomeDoCargoAsync(employee.PositionId, ct),
            HiredOn: employee.HiredOn));

        await dbContext.SaveChangesAsync(ct);

        return Result.Success((await ProjectAsync(
            dbContext.Employees.AsNoTracking().Where(e => e.Id == employee.Id), ct))!);
    }

    public async Task<Result<EmployeeResponse>> UpdateAsync(
        EmployeeId id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var employee = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
            return Result.Failure<EmployeeResponse>(NotFound(id));

        var referencias = await ValidarReferenciasAsync(request.PositionId, request.DepartmentId, request.ManagerId, ct);
        if (referencias.IsFailure)
            return Result.Failure<EmployeeResponse>(referencias.Error!);

        // Ciclo de chefia: A chefia B e B chefia A trava qualquer travessia de hierarquia
        // depois. O agregado so enxerga a si mesmo, entao a cadeia e verificada aqui.
        if (request.ManagerId is { } novoGestor
            && await ChefiaEmCicloAsync(id, novoGestor, ct))
        {
            return Result.Failure<EmployeeResponse>(new Error(
                "employee.manager_cycle",
                "Esse gestor já responde a esta pessoa (direta ou indiretamente).",
                nameof(request.ManagerId)));
        }

        if (await EmailIsTakenAsync(request.Email, id, ct))
        {
            return Result.Failure<EmployeeResponse>(new Error(
                "employee.duplicate_email",
                $"Já existe outro funcionário com o e-mail {request.Email!.Trim()}.",
                nameof(request.Email)));
        }

        var rename = employee.Rename(request.FullName);
        if (rename.IsFailure)
            return Result.Failure<EmployeeResponse>(rename.Error!);

        var email = employee.ChangeEmail(request.Email);
        if (email.IsFailure)
            return Result.Failure<EmployeeResponse>(email.Error!);

        var position = employee.ChangePosition(request.PositionId);
        if (position.IsFailure)
            return Result.Failure<EmployeeResponse>(position.Error!);

        var gestor = employee.AssignManager(request.ManagerId);
        if (gestor.IsFailure)
            return Result.Failure<EmployeeResponse>(gestor.Error!);

        employee.MoveToDepartment(request.DepartmentId);

        audit.Record(
            AuditActions.EmployeeUpdated, nameof(Employee), id.ToString(), Rotulo(employee),
            new { request.FullName, request.PositionId, request.DepartmentId, request.ManagerId });

        await dbContext.SaveChangesAsync(ct);
        return Result.Success((await ProjectAsync(
            dbContext.Employees.AsNoTracking().Where(e => e.Id == id), ct))!);
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

        // Desligamento e das acoes com consequencia: sai das listas, da escala e da
        // cobertura. Quem fez e quando precisa ficar registrado.
        audit.Record(
            AuditActions.EmployeeTerminated, nameof(Employee), id.ToString(), Rotulo(employee),
            new { request.TerminatedOn });

        await dbContext.SaveChangesAsync(ct);
        return Result.Success((await ProjectAsync(
            dbContext.Employees.AsNoTracking().Where(e => e.Id == id), ct))!);
    }

    /// <summary>
    /// Cargo, setor e gestor precisam existir NESTE tenant. O filtro global ja garante o
    /// tenant; o que se checa aqui e a existencia — sem isso, um id inventado viraria
    /// violacao de FK, que chega ao usuario como erro 500 sem explicacao.
    /// </summary>
    private async Task<Result> ValidarReferenciasAsync(
        PositionId positionId, DepartmentId? departmentId, EmployeeId? managerId, CancellationToken ct)
    {
        if (!await dbContext.Positions.AnyAsync(p => p.Id == positionId, ct))
            return Result.Failure("employee.position_not_found", "Cargo não encontrado.", nameof(positionId));

        if (departmentId is { } setor && !await dbContext.Departments.AnyAsync(d => d.Id == setor, ct))
            return Result.Failure("employee.department_not_found", "Setor não encontrado.", nameof(departmentId));

        if (managerId is { } gestor && !await dbContext.Employees.AnyAsync(e => e.Id == gestor, ct))
            return Result.Failure("employee.manager_not_found", "Gestor não encontrado.", nameof(managerId));

        return Result.Success();
    }

    /// <summary>Sobe a cadeia de chefia a partir do gestor proposto até a raiz ou até topar com a propria pessoa.</summary>
    private async Task<bool> ChefiaEmCicloAsync(EmployeeId pessoa, EmployeeId gestor, CancellationToken ct)
    {
        var atual = gestor;

        for (var salto = 0; salto < 64; salto++)
        {
            if (atual == pessoa)
                return true;

            var acima = await dbContext.Employees
                .Where(e => e.Id == atual)
                .Select(e => e.ManagerId)
                .FirstOrDefaultAsync(ct);

            if (acima is null)
                return false;

            atual = acima.Value;
        }

        // Cadeia mais longa que o limite so acontece se ja houver ciclo gravado.
        return true;
    }

    private async Task<string> NomeDoCargoAsync(PositionId id, CancellationToken ct)
        => await dbContext.Positions.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

    private async Task<bool> CodeIsTakenAsync(string? code, EmployeeId? excluding, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var normalized = code.Trim();

        return await dbContext.Employees.AnyAsync(
            e => e.Code == normalized && (excluding == null || e.Id != excluding.Value), ct);
    }

    /// <summary>
    /// Compara em minusculas porque e assim que o dominio guarda: sem isto, "Joao@x.com"
    /// passaria pela checagem e so quebraria no indice unico, com erro de banco na cara
    /// do usuario.
    /// </summary>
    private async Task<bool> EmailIsTakenAsync(string? email, EmployeeId? excluding, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalized = email.Trim().ToLowerInvariant();

        return await dbContext.Employees.AnyAsync(
            e => e.Email == normalized && (excluding == null || e.Id != excluding.Value), ct);
    }

    /// <summary>Rotulo legivel sem join, para a auditoria sobreviver a exclusao do funcionario.</summary>
    private static string Rotulo(Employee e) =>
        e.Code is null ? e.FullName : $"{e.FullName} ({e.Code})";

    private static Error NotFound(EmployeeId id) =>
        new("employee.not_found", $"Funcionario {id} nao encontrado.");
}
