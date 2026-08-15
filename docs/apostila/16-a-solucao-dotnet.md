# Capítulo 16 — A solução .NET por dentro

> **Objetivo:** entender como o backend do Mamão é organizado — do que é uma "solução" até
> por que um módulo tem quatro projetos, o que é o padrão Result, o outbox e por que
> existem testes que quebram o build por uma convenção.

---

## 16.1 Projeto, solução, assembly

Três palavras que se confundem:

| Termo | O que é | No disco |
|---|---|---|
| **Projeto** | uma unidade compilável | um `.csproj` |
| **Assembly** | o resultado da compilação | um `.dll` |
| **Solução** | um agrupamento de projetos | um `.sln` / `.slnx` |

```bash
dotnet new sln -n Mamao              # cria a solução
dotnet sln add src/Mamao.Api/Mamao.Api.csproj
dotnet build Mamao.slnx              # compila tudo
dotnet test Mamao.slnx               # testa tudo
```

O Mamão tem 14 projetos: 10 no `src/`, 4 no módulo People, mais 3 de teste.

> **Chimpanzé pergunta:** *"Por que não um projeto só? Menos arquivo, menos confusão."*
>
> Porque a fronteira entre projetos é a **única** que o compilador consegue vigiar. Dentro
> de um projeto, qualquer classe pode chamar qualquer outra. Se o domínio e o acesso a
> banco estão no mesmo projeto, nada impede uma regra de negócio abrir uma conexão SQL — e
> um dia alguém faz. Separando, essa chamada simplesmente **não compila**.

## 16.2 A arquitetura: monolito modular

Existem três formas comuns de organizar um backend:

**Monolito tradicional** — tudo junto, camadas horizontais (Controllers / Services /
Repositories). Simples de começar, vira um nó em dois anos: mexer em férias quebra escala.

**Microserviços** — cada contexto é um programa, com banco próprio, comunicando por rede.
Escala time e deploy. Custa caríssimo: rede, latência, transação distribuída,
observabilidade, orquestração.

**Monolito modular** — um programa só, com fronteiras internas **fortes**, verificadas pelo
compilador.

O Mamão escolheu o terceiro ([ADR-0001](../adr/0001-modular-monolith.md)), e a lógica é
direta: microserviços resolvem um problema **organizacional** — times independentes
disputando um repositório. Com uma pessoa, você paga todo o custo e não recebe o benefício.

E as fronteiras internas fortes deixam a porta aberta: quando um módulo precisar virar
serviço próprio, ele já está isolado.

## 16.3 Um módulo tem quatro projetos

```
Modules/People/
├── Mamao.People.Contracts/       ← o que os OUTROS módulos podem ver
├── Mamao.People.Domain/          ← as regras de negócio
├── Mamao.People.Application/     ← os casos de uso
└── Mamao.People.Infrastructure/  ← banco, endpoints, arquivos
```

E as dependências apontam **para dentro**:

```
Infrastructure ──> Application ──> Domain ──> Contracts ──> SharedKernel
```

Repare no que **não** existe: nenhuma seta voltando. `Domain` não conhece `Application`;
`Application` não conhece `Infrastructure`.

### Contracts — o que sai do módulo

```csharp
public readonly record struct EmployeeId(Guid Value);
public sealed record EmployeeHired(Guid TenantId, Guid EmployeeId, string FullName);
```

Só identificadores e eventos. Outro módulo que precise falar com People **só** enxerga
isto. É o equivalente ao arquivo de cabeçalho: a superfície pública.

### Domain — as regras

```csharp
public sealed class Employee
{
    public EmployeeId Id { get; private set; }
    public string FullName { get; private set; }
    public DateOnly? TerminatedOn { get; private set; }
    public bool IsActive => TerminatedOn is null;

    public static Result<Employee> Create(string fullName, DateOnly hiredOn, DateOnly hoje)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Result.Failure<Employee>("employee.name_required", "Informe o nome.", "fullName");

        if (hiredOn > hoje.AddYears(1))
            return Result.Failure<Employee>("employee.hire_too_far", "Admissão muito no futuro.", "hiredOn");

        return Result.Success(new Employee { /* … */ });
    }
}
```

Três características que definem uma camada de domínio:

1. **Nada de infraestrutura.** Sem `DbContext`, sem `HttpClient`, sem `IConfiguration`.
2. **`private set`.** Ninguém altera o objeto por fora; existem métodos que validam antes.
3. **Construtor privado + fábrica.** `Create` pode **recusar**. Construtor não pode devolver
   erro — só lançar exceção, o que é caro e ruim para regra esperada.

E há um teste de arquitetura que garante:

```csharp
[Fact]
public void Dominio_nao_conhece_infraestrutura() { … }
```

### Application — os casos de uso

Orquestra: busca no banco, chama o domínio, grava, publica evento, registra auditoria.

```csharp
public async Task<Result<EmployeeResponse>> CreateAsync(CreateEmployeeRequest request, CancellationToken ct)
{
    var referencias = await ValidarReferenciasAsync(request.PositionId, …, ct);
    if (referencias.IsFailure) return Result.Failure<EmployeeResponse>(referencias.Error!);

    var employee = Employee.Create(request.FullName, request.HiredOn, hoje);
    if (employee.IsFailure) return Result.Failure<EmployeeResponse>(employee.Error!);

    dbContext.Employees.Add(employee.Value);
    outbox.Enqueue(new EmployeeHired(…));       // evento
    auditLog.Record(AuditActions.EmployeeCreated, …);  // auditoria

    await dbContext.SaveChangesAsync(ct);        // ← os três, numa transação só
    return Result.Success(Mapear(employee.Value));
}
```

**A linha que importa é a última.** O funcionário, o evento e a auditoria vão ao banco na
**mesma transação**. Ou os três acontecem, ou nenhum. Isso é o coração do próximo tópico.

### Infrastructure — o mundo externo

`DbContext`, mapeamentos, migrations, endpoints HTTP, gravação de arquivo. É a única camada
que sabe que existe PostgreSQL.

> **Chimpanzé pergunta:** *"Endpoint na Infrastructure? Não deveria ficar na API?"*
>
> É uma escolha do Mamão: o módulo publica os próprios endpoints, e o `Mamao.Api` só
> chama `app.MapPeopleEndpoints()`. Assim, adicionar um módulo não exige mexer no host. A
> alternativa — endpoints no projeto da API — também é defensável.

## 16.4 O padrão Result

Em C#, o jeito comum de sinalizar falha é lançar exceção. O Mamão distingue:

```csharp
/// <summary>
/// Falha de regra de negocio esperada. Nao usar para bug nem para falha de
/// infraestrutura — essas continuam sendo excecao.
/// </summary>
public sealed record Error(string Code, string Message, string? Field = null);
```

| Situação | Como sinalizar |
|---|---|
| Matrícula duplicada | `Result.Failure` — é **esperado** |
| Nome vazio | `Result.Failure` |
| Banco fora do ar | **exceção** |
| Índice fora do array | **exceção** (é bug) |

Por que separar: exceção é cara (captura pilha), é fácil de esquecer de tratar, e — o pior —
confunde "o usuário digitou errado" com "o sistema quebrou". Nos logs, os dois viram a mesma
coisa e o alerta perde sentido.

Os três campos do `Error` alimentam o frontend diretamente:

- `Code` — estável, o frontend decide com base nele (`"employee.duplicate_code"`)
- `Message` — texto em português para o usuário
- `Field` — o campo do formulário, que vira `fieldErrors` (Capítulo 8)

## 16.5 O outbox

**O problema.** Ao admitir alguém, é preciso (a) gravar no banco e (b) avisar outros
módulos. Se você grava e depois publica:

```csharp
await dbContext.SaveChangesAsync();   // ✅ gravou
await messageBroker.Publish(evento);  // 💥 caiu aqui
```

O funcionário existe e ninguém ficou sabendo. O sistema fica **inconsistente para sempre**,
e ninguém percebe.

**A solução.** Grave o evento **na mesma transação**, numa tabela:

```csharp
dbContext.Employees.Add(employee);
outbox.Enqueue(new EmployeeHired(…));
await dbContext.SaveChangesAsync(ct);      // ← atômico: os dois ou nenhum
```

Um processo separado — o Worker — lê a tabela e publica:

```
[API]  grava funcionário + evento  ──> [tabela outbox]
                                              │
[Worker]  lê pendentes, publica, marca ───────┘
```

Se a publicação falhar, o evento continua lá e é tentado de novo. **Entrega garantida ao
menos uma vez** — o que exige que os consumidores sejam **idempotentes**: processar o mesmo
evento duas vezes tem que dar o mesmo resultado.

A auditoria usa a mesma ideia, pelo mesmo motivo:

```csharp
/// <b>Não salva:</b> apenas enfileira no DbContext de quem chamou, e o SaveChanges do fato
/// grava os dois juntos. É a mesma escolha da outbox, pelo mesmo motivo —
/// auditoria em transação separada é auditoria que pode faltar exatamente no caso que importa.
```

## 16.6 Multi-tenancy: três camadas

O Mamão é **multi-inquilino**: várias empresas no mesmo banco. Uma empresa ver dados de
outra é o pior defeito possível — pior que ficar fora do ar.

Por isso há **três** proteções independentes.

### Camada 1 — o contexto

O `tenant_id` vem do token JWT, nunca de parâmetro da requisição:

```csharp
builder.Services.AddScoped<ITenantContext, TenantContext>();
```

⚠️ Se viesse da URL ou do corpo, bastaria trocar o número para ver a empresa vizinha. É
**a** falha clássica de sistema multi-inquilino.

### Camada 2 — filtro global do EF Core

```csharp
modelBuilder.Entity<Employee>().HasQueryFilter(e => e.TenantId == tenantContext.Current);
```

Toda consulta ganha o `WHERE tenant_id = …` automaticamente. Você **não pode esquecer**,
porque não é você quem escreve.

### Camada 3 — Row-Level Security do PostgreSQL

```sql
ALTER TABLE people.employees ENABLE ROW LEVEL SECURITY;
ALTER TABLE people.employees FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON people.employees
  USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
```

O **banco** filtra. Se o código errar — um `IgnoreQueryFilters()` esquecido, um SQL cru, um
script de manutenção — as linhas não voltam.

E a política **falha fechada**: sem tenant na sessão, devolve **zero linhas**, não todas.
Falhar devolvendo nada é uma tela vazia; falhar devolvendo tudo é um vazamento.

É por isso que a API conecta com um papel **sem `BYPASSRLS`** (Capítulo 15).

> **Chimpanzé pergunta:** *"Três camadas não é exagero?"*
>
> É a única defesa que sobrevive a erro humano. As camadas 1 e 2 dependem do código estar
> certo. A 3 vale mesmo com o código errado. E vazamento entre empresas num B2B não é bug —
> é fim de contrato.

## 16.7 Testes de arquitetura

Este é o item mais interessante do backend, e quase ninguém faz.

```csharp
/// <summary>
/// Os testes mais baratos e mais valiosos do projeto: cada um substitui uma convencao que
/// ninguem lembra as 23h de sexta. Regra escrita nao segura ninguem; build quebrado segura.
/// </summary>
```

Eles verificam **estrutura**, não comportamento:

```csharp
[Fact] public void Dominio_nao_conhece_infraestrutura() { }
[Fact] public void Contracts_nao_depende_de_nada_alem_do_shared_kernel() { }
[Fact] public void Application_nao_conhece_a_propria_infraestrutura() { }
[Fact] public void Modulo_so_enxerga_o_Contracts_de_outro_modulo() { }

[Fact] public void Toda_entidade_de_tenant_tem_filtro_global() { }
[Fact] public void Todo_indice_de_entidade_de_tenant_comeca_por_tenant_id() { }
[Fact] public void Outbox_nao_e_filtrada_por_tenant() { }
[Fact] public void Acessar_tenant_nao_resolvido_falha_alto_em_vez_de_devolver_vazio() { }
```

Olhe o segundo bloco. "Todo índice de entidade de tenant começa por `tenant_id`" é uma
regra de desempenho **e** de segurança que ninguém lembraria em toda migration nova. Escrita
num documento, seria esquecida na terceira semana. Como teste, **quebra o build**.

E o último é sutil: quando o tenant não está resolvido, o sistema tem que **falhar alto** —
estourar — em vez de devolver lista vazia. Lista vazia é confundida com "não há dados", e o
defeito passa despercebido.

## 16.8 Minimal APIs

O Mamão não usa Controllers. Usa **Minimal APIs**:

```csharp
group.MapGet("/", async Task<IResult> (
    [AsParameters] ListEmployeesQuery query,
    EmployeeService service,
    CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(query, ct)))
    .WithName("listEmployees")
    .Produces<PagedResult<EmployeeListItem>>()
    .RequireAuthorization(Permissions.PeopleRead);
```

Comparado a Controller: menos cerimônia, injeção direta nos parâmetros, e a descrição do
OpenAPI (`.Produces<…>`) fica ao lado do endpoint — que é o que alimenta o contrato do
Capítulo 9.

`CancellationToken ct` merece nota: se o usuário fecha a aba, o token é cancelado e a
consulta é abortada em vez de continuar ocupando o banco. Aceitar esse parâmetro custa nada
e é frequentemente esquecido.

## 16.9 Autorização por permissão, não por papel

```csharp
.RequireAuthorization(Permissions.PeopleRead)
```

Não é `[Authorize(Roles = "Manager")]`. A diferença importa:

```csharp
public static IReadOnlyList<string> All { get; } = [Owner, Hr, Manager, ItManager, Employee];

public static IReadOnlyList<string> PermissionsOf(string role) => role switch
{
    Owner => Permissions.All,
    ItManager => [Permissions.PeopleRead, Permissions.OrgWrite,
                  Permissions.UsersInvite, Permissions.AuditRead, Permissions.SettingsWrite],
    // …
};
```

Papel é um **apelido** para um conjunto de permissões. Os endpoints exigem permissões.

Vantagem prática: criar o papel "gerente de TI" foi listar permissões — nenhum endpoint
mudou. Se os endpoints exigissem papéis, cada novo papel exigiria revisar dezenas de
`[Authorize]`.

E há um cuidado de fronteira que vale contar: as permissões `people.read` e
`availability.read` são separadas de propósito. "Quem trabalha aqui" e "quem está de
afastamento médico" não têm a mesma sensibilidade. O gerente de TI precisa da primeira para
administrar contas; não precisa da segunda. Numa versão anterior, as restrições médicas
estavam sob `people.read` — e o gerente de TI as enxergava. Foi corrigido movendo para
`availability.read`.

## 16.10 API e Worker: dois processos

| | API | Worker |
|---|---|---|
| Responde HTTP | sim | só healthcheck |
| Aplica migrations | não | **sim** |
| Publica a outbox | não | **sim** |
| Avisa documentos vencendo | não | **sim** |
| Papel no banco | `mamao_app` (sem BYPASSRLS) | `mamao_owner` (dono) |

Por que separar: trabalho de fundo não pode competir com requisição de usuário. Um laço que
varre todas as empresas rodando dentro da API deixaria as telas lentas justamente quando o
sistema está ocupado.

E a ordem de registro no Worker guarda uma armadilha documentada:

```csharp
// A ORDEM IMPORTA: servicos hospedados sobem na ordem de registro, e o DatabaseMigrator
// bloqueia no StartAsync ate as migrations terminarem. Registrar o publisher antes dele
// faz a primeira leitura da outbox acontecer com a tabela ainda inexistente — ele se
// recupera na batida seguinte, mas polui o log justamente no startup.
```

E outra, que quebrou o Worker de verdade:

```csharp
// AddNotifications antes dos modulos que dependem dele. O modulo People registra
// servicos que injetam IEmailSender; sem este registro o Worker nem constroi o container
// em Development, e em Production quebraria so na hora de usar — que e pior, porque
// passa no deploy e falha no cliente.
```

**Build verde não prova que o programa inicia.** Erro de injeção de dependência só aparece
quando o container é construído.

## 16.11 Migrations

```bash
dotnet ef migrations add Documentos \
  -p src/Modules/People/Mamao.People.Infrastructure \
  -s src/Modules/People/Mamao.People.Infrastructure \
  -o Persistence/Migrations
```

Cada módulo tem as suas, no próprio schema. O Worker aplica todas no startup, protegido por
**advisory lock** — se dois containers subirem juntos, só um aplica.

⚠️ **A armadilha mais cara do Mamão nesse assunto:** uma migration que faz `UPDATE` para
preencher dados **não afeta nenhuma linha** sob `FORCE ROW LEVEL SECURITY` sem
`app.tenant_id` definido. Zero linhas, sem erro, DDL verde. O dado simplesmente não foi
preenchido, e ninguém percebe.

A correção, que vale para toda migration com backfill:

```sql
ALTER TABLE people.employees NO FORCE ROW LEVEL SECURITY;
UPDATE people.employees SET normalized_name = lower(full_name);
ALTER TABLE people.employees FORCE ROW LEVEL SECURITY;
```

## 16.12 O quadro geral

```
                    Mamao.Api  (HTTP)          Mamao.Worker  (fundo)
                         │                            │
        ┌────────────────┼────────────────┐           │
        ▼                ▼                ▼           ▼
   Mamao.Identity   Modules/People   Mamao.Audit   Mamao.Messaging
        │                │                │           │
        └────────────────┴────────┬───────┴───────────┘
                                  ▼
                        Mamao.SharedKernel
                (tenancy · Result · permissões · outbox)
                                  │
                                  ▼
                            PostgreSQL
                   (um schema por módulo, RLS em tudo)
```

**A regra de ouro do projeto**, escrita no README:

> **Use framework para plumbing. Use nosso código para domínio.**

Autenticação, HTTP, roteamento, DI, ORM, OpenAPI, logging, health checks — tudo do
framework. O código próprio se concentra no que ninguém entrega pronto: regras de férias
CLT, validação de jornada, disponibilidade, rodízio, validade de documentos.

---

## Para fixar

1. **Por que `Domain` não pode referenciar `Infrastructure`?**
   <details><summary>Resposta</summary>
   Para que as regras de negócio não dependam de banco, HTTP ou arquivo — o que as torna
   testáveis sem infraestrutura e protege contra a regra "vazar" para dentro de uma consulta
   SQL. É verificado por teste de arquitetura.
   </details>

2. **Quando usar `Result` e quando lançar exceção?**
   <details><summary>Resposta</summary>
   `Result` para falha de negócio **esperada** (matrícula duplicada, campo vazio). Exceção
   para bug e falha de infraestrutura. Misturar faz "usuário digitou errado" e "sistema
   quebrou" virarem a mesma coisa no log.
   </details>

3. **Por que a auditoria é gravada na mesma transação do fato?**
   <details><summary>Resposta</summary>
   Porque auditoria em transação separada pode faltar exatamente no caso que importa — se a
   segunda transação falhar, o fato aconteceu sem registro.
   </details>

4. **Se o EF já filtra por tenant, para que a RLS?**
   <details><summary>Resposta</summary>
   Porque o filtro do EF depende do código estar certo, e pode ser contornado por
   `IgnoreQueryFilters()`, SQL cru ou script de manutenção. A RLS vale mesmo com o código
   errado.
   </details>

5. **Por que autorizar por permissão e não por papel?**
   <details><summary>Resposta</summary>
   Porque papel é um apelido para um conjunto de permissões. Criando um papel novo, basta
   listar permissões — nenhum endpoint muda. Com papéis nos endpoints, cada papel novo
   exigiria revisar todos eles.
   </details>

## Laboratório

1. Na `Loja.Api` do Capítulo 2, separe em três projetos: `Loja.Domain`, `Loja.Application`,
   `Loja.Api`. Faça `Domain` **não** referenciar os outros.
2. Implemente `Result<T>` e use numa fábrica `Produto.Create` que recusa preço negativo.
3. **Escreva o teste de arquitetura:** um `[Fact]` que carrega o assembly de `Domain` por
   reflexão e falha se ele referenciar `Microsoft.EntityFrameworkCore`. Depois adicione a
   referência de propósito e veja o teste ficar vermelho.
4. Adicione uma tabela `outbox` e grave um evento na mesma transação de um insert. Depois
   force uma exceção **entre** os dois e confirme que nenhum dos dois foi gravado.
5. **Reproduza a armadilha da migration:** crie uma tabela com `FORCE ROW LEVEL SECURITY`,
   rode um `UPDATE` sem definir `app.tenant_id`, e confira com `SELECT count(*)` que zero
   linhas mudaram — sem nenhum erro.

---

**Anterior:** [Capítulo 15](15-docker-para-macacos.md) ·
**Próximo:** [Capítulo 17 — O contrato por dentro](17-o-contrato-por-dentro.md)
