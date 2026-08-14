# Arquitetura — visão geral

## Regra que governa tudo

> Use framework para plumbing. Use nosso código para domínio.

Traduzido em teste prático: antes de escrever qualquer classe de infraestrutura,
pergunte *"o ASP.NET Core, o EF Core ou o Angular já resolvem isso?"*. Se sim, use.
O código autoral do Mamão deve concentrar-se em férias, conflito, disponibilidade,
capacidade, escala, validade de documento e painel do gestor.

## Segunda regra

> Arquitetura serve para proteger o domínio, não para impressionar.

Todo padrão nestes documentos passou por um filtro: *qual problema real ele resolve
nos próximos 12 meses?* Os que não passaram estão listados no final, com o motivo.

---

## Topologia

### Hoje — um deployable

```
                     Cloudflare
                          │
                       Caddy  (TLS, estáticos, reverse proxy)
                          │
        ┌─────────────────┼──────────────────┐
        │                 │                  │
   Angular (SPA)      Mamao.Api          Mamao.Worker
   estático                 │                  │
                            └────────┬─────────┘
                                     │
                              PostgreSQL
                          (schemas por módulo)
                                     │
                              Volume de arquivos
```

Dois processos .NET: a **API** (HTTP) e o **Worker** (outbox publisher, jobs de
vencimento de documento, digest diário, geração de períodos aquisitivos). Ambos da
mesma solução, compartilhando os módulos. O Worker existe separado porque job longo
competindo com request HTTP degrada latência — e porque escalar os dois
independentemente é a primeira necessidade real que vai aparecer.

### Amanhã — extração seletiva

Quando um módulo justificar (carga, cadência de deploy, time separado), ele sai:

```
Mamao.Api ──HTTP──> Scheduling.Service
     │                     │
     └──── RabbitMQ ───────┘
```

A extração é mecânica **porque** o schema já é separado, a comunicação já passa por
interface de contrato e os eventos já passam por outbox. Ver
[ADR-0002](../adr/0002-schema-por-modulo.md) e
[ADR-0005](../adr/0005-outbox-e-mensageria.md).

---

## Estrutura da solução

```
Mamao.sln
├── src/
│   ├── Mamao.AppHost/                      Aspire — orquestração local
│   ├── Mamao.ServiceDefaults/              OTel, health checks, resilience
│   │
│   ├── Mamao.Api/                          host HTTP: DI, auth, middleware, OpenAPI
│   ├── Mamao.Worker/                       host de background jobs + outbox
│   │
│   ├── Mamao.SharedKernel/                 TenantId, EmployeeId, DateRange,
│   │                                       Money, Result, IClock, IntegrationEvent,
│   │                                       IUnitOfWork, AuditEntry
│   │
│   ├── Modules/
│   │   ├── People/
│   │   │   ├── Mamao.People.Contracts/     ← única referência pública
│   │   │   ├── Mamao.People.Domain/
│   │   │   ├── Mamao.People.Application/
│   │   │   └── Mamao.People.Infrastructure/
│   │   ├── TimeOff/       (mesmo formato)
│   │   ├── Work/
│   │   ├── Documents/
│   │   ├── Scheduling/
│   │   └── Notifications/
│   │
│   └── Mamao.Identity/                     usuários, tenants, membership, JWT
│
├── tests/
│   ├── <Modulo>.UnitTests/                 regras de domínio
│   ├── Mamao.IntegrationTests/             Testcontainers + WebApplicationFactory
│   └── Mamao.ArchitectureTests/            fronteiras de módulo, filtro de tenant
│
└── web/mamao-web/                          Angular
```

### Regra de referência de projeto

| De | Pode referenciar |
|---|---|
| `Mamao.Api` | todos os `*.Infrastructure` (só para registrar DI) e todos os `*.Contracts` |
| `<M>.Application` | `<M>.Domain`, `SharedKernel`, `*.Contracts` de **outros** módulos |
| `<M>.Infrastructure` | `<M>.Application`, `<M>.Domain`, `SharedKernel` |
| `<M>.Domain` | apenas `SharedKernel` |
| `<M>.Contracts` | apenas `SharedKernel` |

**Proibido:** qualquer projeto referenciar `Domain`, `Application` ou
`Infrastructure` de outro módulo. Isso é verificado em teste automatizado
(`Mamao.ArchitectureTests`) — a regra escrita não segura ninguém às 23h de uma
sexta; o build quebrado segura.

### Quanto de Clean Architecture

Camadas completas só onde há domínio de verdade:

| Módulo | Tratamento |
|---|---|
| `TimeOff` | Camadas completas. Regras CLT, saldo, conflito, fracionamento |
| `Scheduling` | Camadas completas. Rodízio, cobertura, restrição |
| `Work` | Camadas completas (leves). Capacidade, atraso, redistribuição |
| `Documents` | `Application` + `Infrastructure`. É workflow + CRUD; agregado anêmico é honesto aqui |
| `People` | Idem. Cadastro com pouca regra |
| `Notifications` | Idem |

Criar `Domain` vazio em módulo CRUD só para manter simetria é cerimônia. Simetria
não é objetivo.

---

## Pilha transversal

| Preocupação | Solução | Autoral? |
|---|---|---|
| DI | `Microsoft.Extensions.DependencyInjection` | não |
| Configuração | `IOptions<T>` + validação no startup | não |
| Logging | `ILogger` + serializador estruturado | não |
| Erros HTTP | `ProblemDetails` + `IExceptionHandler` | não |
| Validação | FluentValidation em `Application` | não |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` | não |
| Autenticação | ASP.NET Core Identity + JWT bearer | não |
| Autorização | Policies + `IAuthorizationHandler` de recurso | fina camada |
| ORM / migrations | EF Core, migration por módulo | não |
| Jobs | `BackgroundService` + `PeriodicTimer` | não |
| Observabilidade | OpenTelemetry via `ServiceDefaults` | não |
| Health | `AddHealthChecks` → `/healthz`, `/healthz/ready` | não |
| Testes de API | `WebApplicationFactory` + Testcontainers | não |
| **Multi-tenancy** | query filter + interceptor + RLS | **sim** |
| **Outbox** | ~150 linhas | **sim** |
| **Disponibilidade / capacidade / CLT** | domínio | **sim, é o produto** |

A coluna "autoral" tem quatro linhas. É a proporção certa.

---

## Estilo de API

- REST por recurso, `/api/v1/...`. Verbos de negócio como sub-recurso quando o CRUD
  não expressa a intenção:
  `POST /api/v1/vacation-requests/{id}/approve` em vez de `PATCH {status:"approved"}`.
  A intenção explícita é o que permite auditar e emitir evento corretamente.
- Erros sempre em `ProblemDetails`, com `type` estável que o frontend consegue tratar.
- Paginação por cursor nas listas que crescem (tarefas, auditoria, documentos);
  offset é aceitável em cadastros.
- `If-Match`/`ETag` nas telas com edição concorrente (escala, aprovação) — dois
  gestores aprovando ao mesmo tempo é cenário real.
- Idempotency key nos POST que disparam efeito externo (envio de convite, upload).
- Versionamento por URL. Simples, visível em log, suficiente.

---

## Padrões avaliados e recusados por enquanto

| Padrão | Por que não agora | Gatilho para revisitar |
|---|---|---|
| Microsserviços | Custo operacional e de debug sem nenhum benefício com 1 dev e 0 clientes | Time separado ou carga isolada real |
| Event sourcing | Complexidade alta; auditoria resolve o "quem mudou o quê" com 5% do custo | Necessidade de reconstruir estado histórico arbitrário |
| CQRS com bases separadas | Não há gargalo de leitura | p95 de leitura degradado com índices já otimizados |
| MediatR em toda chamada | Indireção sem ganho quando o handler é chamado de um lugar só | Pipeline transversal (validação/log/transação) virar repetitivo |
| Repository sobre `DbSet` | `DbContext` já é UoW + repositório | Precisar trocar de ORM (não vai acontecer) |
| GraphQL | Um cliente só, telas conhecidas | Múltiplos clientes com necessidades divergentes |
| Redis | Sem métrica que justifique | Sessão distribuída ou cache com hit rate medido |
| Kubernetes | Um nó, um deployable | Múltiplos serviços com escala independente |
| Feature flags elaborados | 1 dev, deploy contínuo | Time maior ou release coordenado |

O valor desta tabela é poder responder "por que não usei X?" com um motivo, e ter
o gatilho anotado para não perder o momento certo.
