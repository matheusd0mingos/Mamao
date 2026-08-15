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

## 16.12 As bibliotecas: o que entrou, o que não entrou e por quê

Arquitetura você já viu. Falta dizer **com o que ela é feita** — e, mais interessante, o
que foi recusado.

### Onde ficam as versões

O Mamão usa **Central Package Management**: nenhum `.csproj` traz número de versão. Tudo
mora num arquivo só, o `Directory.Packages.props`:

```xml
<PropertyGroup>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
</PropertyGroup>

<ItemGroup Label="ASP.NET Core / EF Core">
  <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
  <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
</ItemGroup>
```

E o `.csproj` só diz **o que** usa:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" />
```

Com 14 projetos, isso elimina a classe inteira de bug em que dois projetos usam versões
diferentes do mesmo pacote e o comportamento muda conforme quem carrega primeiro.

### O que entrou

**Acesso a dados**

| Pacote | Para quê | Por que este |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | ORM: objetos ⇄ tabelas, migrations, LINQ | Padrão do ecossistema, migrations integradas |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | O provedor PostgreSQL do EF | É **o** provedor; suporta `jsonb`, arrays, `DateOnly` |
| `Npgsql` | O driver puro | Usado direto nos testes de RLS, que precisam de SQL cru |
| `EFCore.NamingConventions` | `FullName` → `full_name` | Convenção do Postgres é snake_case. Sem isso, todo SQL manual precisaria de aspas |

**Web e identidade**

| Pacote | Para quê |
|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Valida o token em cada requisição |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Hash de senha, recuperação, bloqueio por tentativas |
| `Microsoft.AspNetCore.OpenApi` | Gera o documento do Capítulo 9 |

⚠️ Sobre o Identity: é tentador escrever "é só um `SHA256` na senha". **Não é.** É salt por
usuário, algoritmo lento e parametrizado (PBKDF2), migração de algoritmo quando o padrão
mudar, token de recuperação assinado com expiração, e contagem de tentativas. Cada um
desses é uma vulnerabilidade quando feito por conta própria.

**Validação e e-mail**

| Pacote | Para quê | Nota |
|---|---|---|
| `FluentValidation` | Regras de forma da requisição | Produz o `fieldErrors` do Capítulo 8 |
| `MailKit` | Envio SMTP | O `SmtpClient` da BCL é obsoleto e a própria Microsoft recomenda MailKit |

**Importação de planilha** — e este comentário no arquivo diz exatamente onde traçar a
linha entre biblioteca e código próprio:

```xml
<!-- CsvHelper faz o parsing de baixo nivel (aspas, delimitador dentro de campo).
     O mapeamento de cabecalho e a coercao de valores sao nossos: e ali que mora a
     tolerancia a arquivo sujo, que e o diferencial. Licenca MS-PL/Apache-2.0. -->
<PackageVersion Include="CsvHelper" Version="33.1.0" />
```

Ninguém deve reimplementar parsing de CSV — aspas dentro de campo, delimitador dentro de
aspas, quebra de linha dentro de célula. Mas aceitar que a coluna se chame "Nome", "nome
completo" ou "NOME DO FUNCIONÁRIO" é o **produto**, não plumbing.

**Observabilidade** — cinco pacotes de `OpenTelemetry`, desde o primeiro commit. Um padrão
aberto: traces e métricas saem para qualquer coletor sem prender o projeto a um fornecedor.

**Testes**

| Pacote | Para quê |
|---|---|
| `xunit.v3` | O framework de teste |
| `Shouldly` | Asserções legíveis: `resultado.IsSuccess.ShouldBeTrue()` |
| `Testcontainers.PostgreSql` | Sobe um Postgres **de verdade** por execução |
| `NetArchTest.Rules` | Os testes de arquitetura da seção 16.7 |
| `Microsoft.AspNetCore.Mvc.Testing` | Sobe a API em memória para teste ponta a ponta |

### O que **não** entrou — e esta é a parte instrutiva

Três bibliotecas aparecem em quase todo projeto .NET e **não estão** aqui:

**MediatR** — o padrão mediator, `_mediator.Send(new CreateEmployeeCommand(…))`. O Mamão
chama o serviço direto: `service.CreateAsync(request, ct)`. O MediatR resolve o problema de
desacoplar quem chama de quem executa; num monolito modular onde a fronteira já é o
projeto, ele adiciona uma indireção que você paga toda vez que quer saber quem trata um
comando — o "vá para a definição" para de funcionar.

**AutoMapper** — mapeia objeto em objeto por convenção. O Mamão escreve o mapeamento à mão:

```csharp
new EmployeeResponse(e.Id, e.Code, e.FullName, …)
```

Chato, e o compilador vigia. Com AutoMapper, remover um campo compila e falha em execução —
ou pior, mapeia silenciosamente para o valor padrão. (E o AutoMapper também passou a exigir
licença comercial em versões recentes.)

**Serilog** — o `ILogger` do próprio .NET, com OpenTelemetry por trás, cobre o caso.

O critério é sempre o mesmo, e está no README do projeto:

> **Use framework para plumbing. Use nosso código para domínio.**

Uma biblioteca precisa resolver um problema que você **tem hoje**. Adicionada antes disso,
ela é custo puro: mais uma superfície de atualização, de vulnerabilidade e de licença.

### O critério de adoção — [ADR-0016](../adr/0016-bibliotecas-de-terceiros.md)

Quatro perguntas, a fazer **na data de hoje** e não pela memória:

1. **Qual a licença da versão que você vai usar** — não a do projeto em geral.
2. **O custo aparece quando o produto crescer?** Licença gratuita "abaixo de X de
   faturamento" é dívida com data marcada.
3. **O benefício é real hoje?**
4. **Há advisory?** — verificado pelo CI, inclusive em dependência transitiva.

E a ADR registra três casos concretos em que isso mudou uma decisão:

**FluentAssertions → Shouldly.** O briefing pedia FluentAssertions. A partir da v8 ela
passou a exigir licença comercial para uso comercial. Trocada por Shouldly, gratuita e
igualmente legível. O comentário ficou no arquivo:

```xml
<!-- Shouldly no lugar de FluentAssertions: a v8+ da FluentAssertions passou a
     exigir licenca comercial. Ver docs/adr/0016-bibliotecas-de-terceiros.md -->
```

**MassTransit → outbox próprio.** Também mudou de licenciamento. E, mais importante, o
Mamão ainda não tem o problema que ele resolve — mensageria distribuída. A ADR já anota a
alternativa para o dia em que tiver: Wolverine, MIT.

**SSH.NET — pin transitivo.** Este é o mais técnico e o mais útil:

```xml
<!-- Pin transitivo: o Testcontainers arrasta SSH.NET 2025.1.0, que tem advisory de
     severidade alta (GHSA-q939-rpr3-3284). Com TreatWarningsAsErrors, o build quebra —
     que e exatamente o comportamento desejado. -->
<PackageVersion Include="SSH.NET" Version="2026.0.0" />
```

O `SSH.NET` **não é dependência sua** — o Testcontainers o traz junto. Você não pode
esperar o Testcontainers atualizar. Com `CentralPackageTransitivePinningEnabled`, você fixa
a versão corrigida de uma dependência de dependência. É a forma certa de tratar
vulnerabilidade em pacote que não é seu.

E o CI cobra:

```yaml
- name: Pacotes com vulnerabilidade conhecida
  run: |
    saida=$(dotnet list Mamao.slnx package --vulnerable --include-transitive)
    if echo "$saida" | grep -q "has the following vulnerable packages"; then
      echo "::error::Dependencia com vulnerabilidade conhecida."
      exit 1
    fi
```

`--include-transitive` é o que importa: a maioria das vulnerabilidades vem de pacotes que
você nunca escolheu.

### Duas configurações de build que valem por uma biblioteca

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

`TreatWarningsAsErrors` é rigoroso e é o que faz o item anterior funcionar: um aviso de
pacote vulnerável **quebra o build** em vez de rolar na tela.

E há um comentário no topo do `Directory.Build.props` que é uma história inteira:

```xml
<!--
  NAO ligue InvariantGlobalization aqui. Ja esteve ligado e custou caro: sem ICU,
  string.Normalize vira no-op silencioso e "Admissão" deixa de casar com "admissao",
  ou seja, toda planilha brasileira era recusada sem erro visivel.
-->
```

`InvariantGlobalization` é uma otimização recomendada em vários guias de container: reduz a
imagem tirando as tabelas de internacionalização (ICU). Num produto brasileiro, o efeito é
que `string.Normalize` — usado para comparar texto ignorando acento — **vira uma operação
que não faz nada**, sem erro. Toda planilha com cabeçalho acentuado era recusada, e a
mensagem não fazia sentido.

**Otimização copiada de guia genérico, aplicada a um produto com requisito específico.** É
o mesmo padrão dos bugs do Capítulo 13: nada estourou, o build ficou verde, e o
comportamento mudou em silêncio.

### E no frontend?

Vale a comparação, porque é curta. As dependências de produção do Angular são:

```
@angular/*   (common, compiler, core, forms, platform-browser, router)
rxjs
tslib
@fontsource/dm-serif-display, @fontsource/inter
```

**Nenhuma biblioteca de terceiro além das fontes.** Sem NgRx, sem biblioteca de
componentes, sem Lodash, sem date-fns, sem Tailwind. O design system é próprio, o estado é
signals, as datas usam `DatePipe`.

Não é ascetismo: cada dependência de frontend entra no bundle que o usuário baixa. O
resultado é um build inicial de **95 kB comprimidos** — os quatro arquivos que o
`index.html` referencia. Uma biblioteca de componentes típica custa mais que isso sozinha.

Meça o seu antes de acreditar em qualquer número:

```bash
npm run build
python3 - <<'EOF'
import gzip, pathlib, re
d = pathlib.Path('dist/mamao-web/browser')
idx = (d / 'index.html').read_text()
unicos = sorted(set(re.findall(r'(?:src|href)="([^"]+\.(?:js|css))"', idx)))
total = sum(len(gzip.compress((d / n).read_bytes())) for n in unicos)
print(f'{len(unicos)} arquivos iniciais, {total/1024:.0f} kB gzip')
EOF
```

## 16.13 O quadro geral

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

6. **Por que o Mamão não usa MediatR nem AutoMapper?**
   <details><summary>Resposta</summary>
   MediatR desacopla quem chama de quem executa — problema que, num monolito modular, a
   fronteira de projeto já resolve; em troca, você perde o "ir para a definição".
   AutoMapper mapeia por convenção, então remover um campo compila e falha em execução, em
   vez de o compilador acusar. Ambos também mudaram para modelos de licença mais restritos
   em versões recentes.
   </details>

7. **O que é *pin transitivo* e quando ele é necessário?**
   <details><summary>Resposta</summary>
   É fixar a versão de uma dependência **da sua dependência**. Necessário quando um pacote
   que você usa arrasta outro com vulnerabilidade e você não pode esperar a correção
   upstream — foi o caso do `SSH.NET` vindo pelo Testcontainers.
   </details>

8. **Por que `InvariantGlobalization` é perigoso num produto brasileiro?**
   <details><summary>Resposta</summary>
   Sem ICU, `string.Normalize` vira uma operação que não faz nada, **sem erro**. Comparações
   ignorando acento param de funcionar: "Admissão" deixa de casar com "admissao", e toda
   planilha com cabeçalho acentuado é recusada sem mensagem que faça sentido.
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
6. **Audite as suas dependências:**
   ```bash
   dotnet list Mamao.slnx package --vulnerable --include-transitive
   dotnet list Mamao.slnx package --include-transitive | head -40
   ```
   Compare a segunda saída com o `Directory.Packages.props`. A diferença são os pacotes que
   você nunca escolheu — e é de onde vem a maioria das vulnerabilidades.
7. **Meça o custo de uma dependência de frontend.** Instale uma biblioteca de componentes
   qualquer, importe **um** componente dela, rode `npm run build` e compare o tamanho do
   bundle inicial com os 95 kB atuais.

---

**Anterior:** [Capítulo 15](15-docker-para-macacos.md) ·
**Próximo:** [Capítulo 17 — O contrato por dentro](17-o-contrato-por-dentro.md)
