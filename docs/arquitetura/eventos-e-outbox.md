# Eventos, outbox e mensageria

## A distinção que evita 80% dos erros

| | Domain event | Integration event |
|---|---|---|
| Alcance | Dentro do módulo | Entre módulos |
| Transação | A mesma do agregado | Publicado depois do commit, via outbox |
| Entrega | Síncrona, em memória | Assíncrona, at-least-once |
| Falha do handler | Desfaz a operação | Não desfaz nada; precisa de retry |
| Contrato | Interno, mude à vontade | **Público, versionado** |
| Nome | `VacationApproved` em `TimeOff.Domain` | `VacationApproved` em `TimeOff.Contracts` |

Confundir os dois produz os dois piores sintomas: aprovação que falha porque o
e-mail caiu, e saldo debitado que some porque o handler rodou fora da transação.

**Domain event:** debitar o saldo do `VacationEntitlement` ao aprovar. Tem que ser
atômico com a aprovação.

**Integration event:** notificar, marcar tarefas em risco, recalcular escala. Se
falhar, tenta de novo — e a aprovação continua válida.

---

## Decisão: outbox próprio, sem broker na V1

Ver [ADR-0005](../adr/0005-outbox-e-mensageria.md).

Resumo: enquanto API e Worker são um processo cada, dentro do mesmo compose, um
broker adiciona um container para operar, uma dependência para monitorar e uma
biblioteca para versionar — sem entregar nada que uma tabela `outbox` + um
`BackgroundService` não entreguem. A **garantia** vem do outbox (escrita atômica
com o dado de negócio), não do broker.

O código dos handlers é idêntico nos dois mundos. Trocar o transporte depois muda o
publicador, não o domínio. Esse é o ponto: o custo de adiar é ~zero, e o custo de
antecipar é operacional e permanente.

---

## Esquema

```sql
CREATE TABLE messaging.outbox (
    id             uuid        PRIMARY KEY,
    tenant_id      uuid        NOT NULL,
    occurred_at    timestamptz NOT NULL,
    type           text        NOT NULL,      -- "TimeOff.VacationApproved.v1"
    payload        jsonb       NOT NULL,
    correlation_id text,
    processed_at   timestamptz,
    attempts       int         NOT NULL DEFAULT 0,
    error          text
);
CREATE INDEX ix_outbox_pending ON messaging.outbox (occurred_at)
    WHERE processed_at IS NULL;             -- índice parcial: só o que falta

CREATE TABLE messaging.processed_event (
    event_id  uuid NOT NULL,
    consumer  text NOT NULL,
    handled_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (event_id, consumer)
);
```

O índice parcial é o detalhe que mantém a tabela rápida mesmo com milhões de linhas
já processadas.

---

## Publicação

Na mesma transação do fato de negócio:

```csharp
public async Task<Result> ApproveAsync(VacationRequestId id, CancellationToken ct)
{
    var request = await _db.VacationRequests.FirstAsync(r => r.Id == id, ct);

    var result = request.Approve(_currentUser.EmployeeId, _clock.Now);   // regra no domínio
    if (result.IsFailure) return result;

    _outbox.Enqueue(new VacationApproved(                                 // mesma transação
        _tenant.Current, request.EmployeeId, request.From, request.To, request.Id));

    await _db.SaveChangesAsync(ct);   // dado + evento, atômicos
    return Result.Success();
}
```

`_outbox.Enqueue` só adiciona uma linha ao `DbContext` do módulo. Um único
`SaveChangesAsync`, uma única transação. É isto que o Transactional Outbox é — não
precisa de mais nada.

Detalhe: cada módulo escreve na tabela `messaging.outbox` através do **próprio**
`DbContext` (mapeando a mesma tabela). Enquanto for um banco só, a atomicidade é
garantida. Na extração, cada serviço leva a sua outbox.

---

## Despacho

```csharp
public sealed class OutboxPublisher(IServiceProvider sp, ILogger<OutboxPublisher> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            // FOR UPDATE SKIP LOCKED permite mais de um worker sem entrega duplicada
            var batch = await FetchPendingAsync(size: 100, ct);
            foreach (var msg in batch)
                await DispatchAsync(msg, ct);     // marca processed_at ou incrementa attempts
        }
    }
}
```

- `FOR UPDATE SKIP LOCKED` é o padrão certo para fila em Postgres: permite escalar
  workers sem coordenação externa.
- Backoff exponencial em `attempts`. Após ~8 tentativas, marca como falha
  permanente e gera **alerta** — mensagem morta silenciosa é pior que erro.
- Latência de 2 s é irrelevante para notificação, e-mail e recálculo. Nada que o
  usuário observe imediatamente deve depender do outbox.
- Limpeza: `DELETE` do que está processado há mais de 30 dias, job semanal.

O `IEventDispatcher` resolve os consumidores registrados no DI e chama cada um em
sua própria transação/escopo. Trocar por RabbitMQ é trocar a implementação de
`DispatchAsync`.

---

## Consumo

```csharp
public sealed class MarkTasksAtRiskOnVacationApproved(WorkDbContext db)
    : IIntegrationEventHandler<VacationApproved>
{
    public async Task HandleAsync(VacationApproved e, CancellationToken ct)
    {
        var affected = await db.Tasks
            .Where(t => t.AssigneeId == e.EmployeeId
                     && t.Status != TaskStatus.Done
                     && t.DueDate >= e.From && t.DueDate <= e.To)
            .ToListAsync(ct);

        foreach (var task in affected)
            task.FlagAtRisk(AtRiskReason.AssigneeOnVacation);

        await db.SaveChangesAsync(ct);
    }
}
```

Regras para consumidor:

1. **Idempotente.** O dispatcher checa `processed_event` antes de chamar, mas o
   handler também deve ser seguro em reexecução. At-least-once significa que
   duplicata vai acontecer.
2. **Sem depender de ordem.** `VacationApproved` pode chegar antes de
   `EmployeeUpdated`. Se a ordem importa, o modelo está errado.
3. **Sem chamar outro módulo para escrever.** Consumidor escreve no próprio schema.
   Se precisa que outro módulo mude, publique outro evento.
4. **Tolerante a dado que sumiu.** O funcionário pode ter sido desligado entre a
   publicação e o consumo. Ignorar e sair é resposta válida.

---

## Catálogo de eventos (V1)

| Evento | Publicado por | Consumido por |
|---|---|---|
| `EmployeeHired` | People | Documents (gera exigências), Notifications, TimeOff (cria período aquisitivo) |
| `EmployeeTerminated` | People | Work (tarefas órfãs), TimeOff, Scheduling, Identity (revoga acesso) |
| `EmployeeTransferred` | People | Scheduling, Work |
| `VacationRequested` | TimeOff | Notifications (avisa gestor) |
| `VacationApproved` | TimeOff | Work, Scheduling, Notifications, Documents |
| `VacationRejected` | TimeOff | Notifications |
| `AbsenceRegistered` | TimeOff | Work, Scheduling, Notifications |
| `DocumentUploaded` | Documents | Notifications (avisa validador) |
| `DocumentApproved` / `DocumentRejected` | Documents | Notifications, People |
| `DocumentExpiring` | Documents (job) | Notifications |
| `TaskAssigned` | Work | Notifications |
| `TaskOverdue` | Work (job) | Notifications |
| `ScheduleChanged` | Scheduling | Notifications, Work |

Regra sobre `Notifications`: ele consome quase tudo e é o único módulo com essa
característica. Isso é normal e não indica fronteira errada — ele é, por definição,
o assinante universal.

### Versionamento

Nome do tipo carrega a versão: `TimeOff.VacationApproved.v1`. Mudança compatível
(campo novo opcional) mantém `v1`. Mudança incompatível cria `v2`, e o publicador
emite as duas até que todo consumidor migre. Enquanto é monolito isso parece
excessivo — mas o hábito é o que torna a extração possível sem parar o produto.

---

## Quando entra o RabbitMQ

Gatilho único: **primeiro módulo extraído para processo separado.**

A migração então é:

1. Trocar a implementação de `DispatchAsync` por publicação no broker (o outbox
   continua sendo a fonte da verdade — esse é o ponto do padrão).
2. Cada serviço mantém a própria outbox e o próprio `processed_event`.
3. Consumidores não mudam.
4. Fila morta de verdade, com alerta.

Sobre biblioteca: se preferir não manter o publicador, avalie **Wolverine** (MIT,
outbox com Postgres integrado) ou **MassTransit** (verifique as condições de
licenciamento da versão atual antes de adotar — houve mudança de modelo comercial
nas versões recentes). Ambas resolvem; nenhuma é necessária para começar.
