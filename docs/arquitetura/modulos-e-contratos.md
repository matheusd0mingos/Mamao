# Módulos e contratos

## Fronteiras

| Módulo | Responde por | Não responde por |
|---|---|---|
| `People` | Quem é a pessoa, onde está na estrutura, jornada contratada | Se está trabalhando hoje |
| `TimeOff` | Férias, saldo CLT, ausências, licenças, feriados | Escala, tarefa |
| `Work` | Tarefas, responsável, prazo, carga | Se a pessoa está disponível |
| `Documents` | Documentos, exigências, validade, aprovação | Notificar o vencimento |
| `Scheduling` | Turnos, ciclos, cobertura, substituição | Motivo da ausência |
| `Notifications` | Entrega de aviso e preferência de canal | Decidir o que é pendência |
| `Identity` | Usuário, tenant, membership, papel, permissão | Dados de RH |

O teste de fronteira: **se dois módulos precisam da mesma tabela, a fronteira está
errada.** Se um módulo precisa de um *dado* do outro, isso é contrato — normal e
esperado.

Caso de fronteira que já se sabe delicado: `Employee` (People) versus o "usuário"
(Identity). Decisão: `Employee.UserId` é nullable e a única ligação. `Identity`
nunca sabe o que é cargo, setor ou gestor. `People` nunca sabe o que é senha,
sessão ou token.

---

## Contrato público de um módulo

Cada módulo expõe **um único projeto** `*.Contracts`, contendo:

1. Interfaces de consulta (o que outros módulos podem perguntar)
2. DTOs dessas consultas
3. Integration events que o módulo publica
4. Ids fortemente tipados que o módulo é dono

Nada mais. Nada de entidade, `DbContext` ou serviço de aplicação.

```csharp
// Mamao.People.Contracts
namespace Mamao.People.Contracts;

public interface IEmployeeDirectory
{
    Task<EmployeeSummary?> GetAsync(EmployeeId id, CancellationToken ct);

    // sempre em lote — a versão singular gera N+1 no primeiro dashboard
    Task<IReadOnlyDictionary<EmployeeId, EmployeeSummary>> GetManyAsync(
        IReadOnlyCollection<EmployeeId> ids, CancellationToken ct);

    Task<IReadOnlyList<EmployeeId>> GetTeamOfAsync(EmployeeId manager, CancellationToken ct);
    Task<IReadOnlyList<EmployeeId>> GetInDepartmentAsync(DepartmentId id, bool includeChildren, CancellationToken ct);
}

public sealed record EmployeeSummary(
    EmployeeId   Id,
    string       FullName,
    string?      PhotoUrl,
    string       PositionName,
    DepartmentId DepartmentId,
    EmployeeId?  ManagerId,
    WeeklySchedule ContractedSchedule,   // base da capacidade e da disponibilidade
    bool         IsActive);
```

A implementação vive em `Mamao.People.Infrastructure` e é registrada no DI do host.
Quem consome enxerga só a interface. No dia da extração, troca-se a implementação
por um cliente HTTP gerado — **e nenhum consumidor muda**. Esse é o retorno concreto
da disciplina de contrato.

---

## Duas formas de comunicação, e só duas

### Leitura → chamada de método (síncrona, in-process)

```csharp
// Work.Application
public sealed class TaskListHandler(IEmployeeDirectory people, WorkDbContext db)
{
    public async Task<IReadOnlyList<TaskListItem>> HandleAsync(TaskListQuery q, CancellationToken ct)
    {
        var tasks  = await db.Tasks.Where(/* … */).ToListAsync(ct);
        var owners = await people.GetManyAsync(tasks.Select(t => t.AssigneeId).Distinct().ToList(), ct);
        return tasks.Select(t => new TaskListItem(t, owners[t.AssigneeId])).ToList();
    }
}
```

Consistente, trivial de depurar, custo desprezível. **Não use evento para consultar
dado.** Evento é para reagir a fato consumado.

### Reação → integration event (assíncrona, via outbox)

```csharp
// TimeOff.Application — dentro da transação de aprovação
_outbox.Enqueue(new VacationApproved(
    TenantId: tenant, EmployeeId: request.EmployeeId,
    From: request.From, To: request.To, RequestId: request.Id));
```

Consumidores em `Work`, `Scheduling`, `Notifications` e `Documents` reagem. Nenhum
deles conhece o schema do `TimeOff`. Ver
[eventos e outbox](eventos-e-outbox.md).

### O que **não** fazer

| Antipadrão | Por quê |
|---|---|
| `JOIN` entre schemas de módulos | Acopla os dois esquemas para sempre; extração vira reescrita. O schema separado torna isso visível no código |
| Read model replicado entre módulos | Consistência eventual + código de sincronização, para resolver um problema que uma chamada de método já resolve |
| Evento para pedir dado (`GetEmployeeRequested`) | Request/reply sobre broker dentro de um processo é complexidade pura |
| Referenciar `Domain` de outro módulo | Quebra a fronteira. Bloqueado por teste de arquitetura |
| `DbContext` compartilhado | Ver [ADR-0002](../adr/0002-schema-por-modulo.md) |

---

## Registro do módulo

Cada módulo expõe uma única extensão. O `Program.cs` fica legível:

```csharp
// Mamao.Api/Program.cs
builder.AddServiceDefaults();          // Aspire: OTel, health, resilience

builder.Services
    .AddMamaoIdentity(builder.Configuration)
    .AddTenancy()                      // ITenantContext, interceptor, RLS
    .AddPeopleModule(builder.Configuration)
    .AddTimeOffModule(builder.Configuration)
    .AddWorkModule(builder.Configuration)
    .AddDocumentsModule(builder.Configuration)
    .AddSchedulingModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddOutbox();

var app = builder.Build();
app.MapDefaultEndpoints();             // /healthz, /healthz/ready
app.MapPeopleEndpoints();
app.MapTimeOffEndpoints();
// …
```

Cada `Add<Módulo>Module` registra o próprio `DbContext`, handlers, validators,
implementações de contrato e consumidores de evento. Endpoints com Minimal APIs
agrupados por `MapGroup`, um arquivo por caso de uso — combina com vertical slice e
evita controllers de 800 linhas.

---

## Migrations

Uma cadeia de migration **por módulo**, cada uma no seu schema:

```
src/Modules/People/Mamao.People.Infrastructure/Migrations/
src/Modules/TimeOff/Mamao.TimeOff.Infrastructure/Migrations/
```

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    b.HasDefaultSchema("people");
    b.ApplyConfigurationsFromAssembly(typeof(PeopleDbContext).Assembly);
}
```

Migrations rodam no **startup do Worker** (não da API, para evitar corrida entre
réplicas), com lock advisory do Postgres. Com nó único é seguro; quando houver mais
de uma réplica, o lock é o que impede duas migrations simultâneas.

---

## Sinais de que um módulo deve ser extraído

Extrair porque a arquitetura "pede" é o erro. Extrair quando **um destes** for
verdade:

1. Carga do módulo exige escala independente, medida (ex.: geração de escala
   consumindo CPU e degradando a API).
2. Um time separado precisa de cadência de deploy própria.
3. Requisito de isolamento (dado regulado, compliance de cliente grande).
4. Ciclo de vida tecnológico divergente (ex.: componente de otimização em outra
   linguagem).

"Ficou grande" e "seria mais elegante" não estão na lista.

Quando acontecer, a sequência é: publicar os integration events num broker real →
substituir a implementação do contrato por cliente HTTP → mover o schema para outro
banco → mover o projeto. Nesta ordem, cada passo é reversível.
