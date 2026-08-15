# Capítulo 17 — O contrato por dentro: anatomia do OpenAPI

> **Objetivo:** abrir o `openapi.json` do Mamão e o `.d.ts` gerado dele, entender o que
> cada construção do C# vira dos dois lados, e conhecer os cinco pontos onde essa tradução
> é traiçoeira.

O [Capítulo 9](09-o-contrato-openapi.md) mostrou a **esteira**: quem gera, quem consome,
como o CI verifica. Este mostra **o que passa por dentro dela** — e por que, em quatro
lugares diferentes, o Mamão precisou intervir para o contrato não mentir.

Leia o 9 antes deste.

---

## 17.1 O documento, em números

O `openapi.json` do Mamão, hoje:

```
openapi:     3.0.4
paths:       49 caminhos
schemas:     94 tipos
```

Quatro seções de topo:

```json
{
  "openapi": "3.0.4",
  "info":    { "title": "Mamao.Api", "version": "1.0.0" },
  "paths":   { "/api/v1/employees": { … }, … },
  "components": { "schemas": { "EmployeeListItem": { … }, … } },
  "tags":    [ { "name": "Employees" }, … ]
}
```

- **`paths`** — os endereços e o que cada método HTTP faz em cada um.
- **`components.schemas`** — os formatos de dado, definidos **uma vez** e referenciados por
  toda parte. É o que evita repetir a descrição de `EmployeeListItem` em oito lugares.
- **`tags`** — agrupamento para documentação.

## 17.2 De um endpoint C# a uma entrada em `paths`

Este endpoint:

```csharp
group.MapGet("/", async Task<IResult> (
    [AsParameters] ListEmployeesQuery query,
    EmployeeService service,
    CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(query, ct)))
    .WithName("listEmployees")
    .Produces<PagedResult<EmployeeListItem>>()
    .WithTags("Employees")
    .RequireAuthorization(Permissions.PeopleRead);
```

vira exatamente isto:

```json
"/api/v1/employees": {
  "get": {
    "tags": ["Employees"],
    "operationId": "listEmployees",
    "parameters": [
      { "name": "search",          "in": "query", "schema": { "type": "string" } },
      { "name": "includeInactive", "in": "query", "schema": { "type": "boolean" } },
      { "name": "page",            "in": "query", "schema": { "format": "int32", "type": "integer" } },
      { "name": "pageSize",        "in": "query", "schema": { "format": "int32", "type": "integer" } },
      { "name": "departmentId",    "in": "query", "schema": { "type": "string", "format": "uuid" } }
    ],
    "responses": {
      "200": {
        "description": "OK",
        "content": {
          "application/json": {
            "schema": { "$ref": "#/components/schemas/PagedResultOfEmployeeListItem" }
          }
        }
      }
    }
  }
}
```

A correspondência, item por item:

| No C# | No documento |
|---|---|
| `MapGet("/")` no grupo `/api/v1/employees` | a chave `"/api/v1/employees"` e o método `"get"` |
| `.WithName("listEmployees")` | `operationId` — vira o nome da operação no TypeScript |
| `[AsParameters] ListEmployeesQuery` | cada propriedade vira um `parameters` com `"in": "query"` |
| `.Produces<PagedResult<…>>()` | `responses.200.content."application/json".schema` |
| `.WithTags("Employees")` | `tags` |
| `EmployeeService service` | **nada** — o gerador sabe que é injeção, não entrada |
| `CancellationToken ct` | **nada** — idem |
| `.RequireAuthorization(…)` | **nada.** Guarde este, é o item 17.7 |

> **Chimpanzé pergunta:** *"Como o gerador sabe que `EmployeeService` não é um parâmetro de
> entrada, mas `ListEmployeesQuery` é?"*
>
> Pelas regras de vinculação das Minimal APIs: um tipo registrado no container de injeção é
> tratado como serviço; `CancellationToken`, `HttpContext` e afins são especiais; o que
> sobra é entrada. Para GET vira query string; para POST/PUT, um tipo complexo vira corpo.
> Quando a inferência erra, existem `[FromServices]`, `[FromBody]`, `[FromQuery]` para você
> dizer explicitamente.

## 17.3 De um `record` C# a um schema

```csharp
public sealed record EmployeeListItem(
    EmployeeId Id,
    string? Code,
    string FullName,
    string PositionName,
    string? DepartmentName,
    string? Email,
    DateOnly HiredOn,
    bool IsActive);
```

```json
"EmployeeListItem": {
  "required": ["id", "code", "fullName", "positionName", "departmentName", "email", "hiredOn", "isActive"],
  "type": "object",
  "properties": {
    "id":             { "$ref": "#/components/schemas/EmployeeId" },
    "code":           { "type": "string", "nullable": true },
    "fullName":       { "type": "string" },
    "positionName":   { "type": "string" },
    "departmentName": { "type": "string", "nullable": true },
    "email":          { "type": "string", "nullable": true },
    "hiredOn":        { "type": "string", "format": "date" },
    "isActive":       { "type": "boolean" }
  }
}
```

E o TypeScript gerado:

```typescript
EmployeeListItem: {
    id: components["schemas"]["EmployeeId"];
    code: string | null;
    fullName: string;
    positionName: string;
    departmentName: string | null;
    email: string | null;
    /** Format: date */
    hiredOn: string;
    isActive: boolean;
};
```

Olhe com atenção para a lista `required`: **ela contém todos os campos**, inclusive os
nulos. Isso não é bug — é o item 17.5, e é a parte mais mal compreendida de todo o
mecanismo.

## 17.4 A forma do TypeScript gerado

O `openapi-typescript` produz três interfaces de topo:

```typescript
export interface paths {
    "/api/v1/employees": {
        parameters: { query?: never; header?: never; path?: never; cookie?: never; };
        get: operations["listEmployees"];
        put?: never;
        post: operations["createEmployee"];
        delete?: never;
        // …
    };
}

export interface components {
    schemas: {
        EmployeeListItem: { … };
        EmployeeId: string;
        // …
    };
}

export interface operations { … }
```

Repare no `put?: never`. Isso é o gerador dizendo em TypeScript: *"este caminho não aceita
PUT"*. Se você usar um cliente que se baseia em `paths`, tentar um PUT ali não compila.

O Mamão usa só `components["schemas"]`, através dos apelidos:

```typescript
type Schemas = components['schemas'];
export type EmployeeListItem = Schemas['EmployeeListItem'];
```

`paths` e `operations` ficam sem uso — são a base para clientes totalmente gerados, que o
Mamão decidiu não usar (Capítulo 9, seção 9.8).

---

## Os cinco pontos traiçoeiros

Aqui está o conteúdo que justifica o capítulo. Cada um destes causou, ou causaria, um
contrato que **mente sem quebrar o build**.

## 17.5 Nulabilidade: `| null` não é `?`

Esta é a que mais confunde.

| No C# | No schema | No TypeScript | Significa |
|---|---|---|---|
| `string FullName` | em `required`, sem `nullable` | `fullName: string` | sempre vem, nunca nulo |
| `string? Code` | **em `required`**, com `nullable: true` | `code: string \| null` | **sempre vem**, podendo ser nulo |
| (campo omitido do JSON) | fora de `required` | `code?: string` | pode não vir |

A diferença entre as duas últimas linhas é real e importa:

```typescript
// code: string | null      → a chave EXISTE sempre, o valor pode ser null
{ "code": null }

// code?: string            → a chave pode não existir
{ }
```

O ASP.NET Core coloca **tudo** em `required` porque, com *nullable reference types*
ligados, ele sabe que a propriedade **sempre será serializada** — o que pode variar é o
valor. É a descrição correta, e é mais rígida do que a maioria das pessoas espera.

**Consequência prática:** no Mamão você quase nunca vê `?` nos tipos gerados, e vê muito
`| null`. Por isso o código está cheio de:

```typescript
{{ pessoa.departmentName ?? '—' }}
this.items.set(result.items ?? []);
```

⚠️ **A armadilha:** se você marcar um campo como `string` (não anulável) no C# mas o valor
vier de um `LEFT JOIN` que pode não achar nada, o contrato **promete** algo que o servidor
não cumpre. O TypeScript confia, não testa em execução, e você recebe `undefined` num campo
tipado como `string`. **O contrato é tão honesto quanto a anotação de nulabilidade do seu
C#.**

## 17.6 Identificadores tipados: o `unknown` silencioso

Este é o melhor caso do capítulo, porque o defeito é invisível.

O Mamão não usa `Guid` cru para identificar coisas. Usa tipos próprios:

```csharp
public readonly record struct EmployeeId(Guid Value);
public readonly record struct PositionId(Guid Value);
```

O ganho é grande: passar um `PositionId` onde se espera um `EmployeeId` **não compila**.
Com `Guid` em tudo, os dois são intercambiáveis e um dia se trocam.

**O problema.** Esses tipos têm `JsonConverter` próprio para serializar como string. O
gerador de OpenAPI olha para um `readonly record struct` com converter customizado, não
consegue inferir nada, e emite um **schema vazio**. O `openapi-typescript` traduz schema
vazio para `unknown`.

O contrato passa a dizer:

```typescript
id: unknown;
```

E aí o TypeScript perde a checagem **exatamente no campo que mais circula** — todo
`routerLink`, todo parâmetro de rota, toda chamada por id. Sem nenhum erro de build para
avisar.

**A correção** tem duas partes. Primeiro, uma interface-marca vazia:

```csharp
/// <summary>
/// Marca um identificador tipado (EmployeeId, DepartmentId, PositionId…).
///
/// Existe por um motivo so, e vale registrar: sem ele, o gerador de OpenAPI olha para um
/// readonly record struct com JsonConverter proprio, nao consegue inferir nada e emite um
/// schema VAZIO — que o openapi-typescript traduz para unknown.
/// </summary>
public interface IStronglyTypedId;
```

Depois, um transformer que descreve todos de uma vez:

```csharp
internal sealed class StronglyTypedIdSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        // Desembrulhar Nullable<> nao e detalhe: um id OPCIONAL (DepartmentId?) chega aqui
        // como Nullable<DepartmentId>, que nao implementa a marca. Sem isto, o id que
        // aparece primeiro num campo opcional continuava saindo como `unknown` — e so
        // esse. Erro que passa despercebido justamente por ser parcial.
        var tipo = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (!typeof(IStronglyTypedId).IsAssignableFrom(tipo))
            return Task.CompletedTask;

        schema.Type = JsonSchemaType.String;
        schema.Format = "uuid";
        schema.Properties?.Clear();

        return Task.CompletedTask;
    }
}
```

Resultado no documento e no TypeScript:

```json
"EmployeeId": { "type": "string", "format": "uuid" }
```

```typescript
/** Format: uuid */
EmployeeId: string;
```

**Três lições aqui, e a terceira é a mais importante:**

1. Um tipo com serialização customizada quase sempre precisa ser descrito à mão para o
   OpenAPI. O gerador não consegue adivinhar o que o seu converter faz.

2. O comentário sobre `Nullable.GetUnderlyingType` conta um segundo defeito **dentro** da
   correção: `DepartmentId?` chega como `Nullable<DepartmentId>`, que não implementa a
   marca. Sem desembrulhar, os ids opcionais continuavam saindo como `unknown` — **e só
   eles**. Um erro parcial é mais difícil de notar que um erro total, porque a maior parte
   funciona.

3. Repare **onde** a correção mora, e por quê:

   > *Fica no transformer, e nao no normalize-openapi.mjs, porque isto e uma verdade sobre
   > a API e nao um conserto de gerador: quem consumir o documento direto merece a mesma
   > informacao que o nosso frontend.*

   Compare com o outro conserto, que fica no script Node: aquele é um **defeito do
   gerador** (`format: int32` sem `type`). Este é **informação verdadeira** sobre a API.
   Verdade vai no documento; remendo de ferramenta vai no pós-processamento. A regra é
   simples: se um consumidor em Python quisesse gerar cliente do seu `openapi.json`, ele
   merece a mesma verdade — mas não precisa herdar os seus remendos.

## 17.7 O que o contrato *não* diz

Aqui é onde a apostila fica honesta. Rode isto:

```bash
python3 -c "
import json
d = json.load(open('web/openapi.json'))
print(list(d['components'].get('securitySchemes', {}).keys()))
"
```

Saída no Mamão hoje:

```
[]
```

**A autenticação não está descrita no contrato.** Todo endpoint tem
`.RequireAuthorization(...)`, mas isso não aparece no documento. Um cliente gerado
automaticamente a partir dele não saberia que precisa mandar `Authorization: Bearer …`.

Não é um problema para o Mamão, porque o bearer é responsabilidade do interceptor
(Capítulo 10) e não do contrato. Mas é uma **limitação real**, e vale saber: se um dia
alguém de fora for gerar um cliente, vai receber 401 sem entender por quê. A correção seria
declarar um `securityScheme` e aplicá-lo — trabalho pequeno, ainda não feito.

Essa é só a primeira de uma lista. **O contrato descreve forma, não significado.** Nenhuma
destas coisas cabe nele:

| O contrato não expressa | Exemplo no Mamão |
|---|---|
| Autenticação (hoje) | `securitySchemes` vazio |
| Quais erros um endpoint devolve | só o 200 está declarado na maioria |
| Invariante de negócio | "férias não podem passar de 30 dias no ano" |
| Validação cruzada | "`expiresOn` tem que ser depois de `issuedOn`" |
| Garantia de ordenação | "a lista vem ordenada por nome" |
| Semântica de campo | `code` é matrícula, única **por empresa** |
| Efeito colateral | criar funcionário dispara e-mail e evento |

Por isso o contrato gerado **não substitui** teste de integração. Ele garante que os dois
lados falam do mesmo **formato** — e é só isso. Que o dado esteja certo, que a permissão
seja checada, que a empresa A não veja dados da empresa B: nada disso está no
`openapi.json`, e tudo isso está nos testes.

## 17.8 Enums e genéricos

**Enum.** O Mamão serializa como texto, então o schema sai assim:

```json
"DocumentStatus": { "enum": ["SemValidade", "Valido", "Vencendo", "Vencido"] }
```

e vira uma união de literais:

```typescript
/** @enum {unknown} */
DocumentStatus: "SemValidade" | "Valido" | "Vencendo" | "Vencido";
```

Note que o schema **não tem `type`** — só `enum`. Daí o comentário `@enum {unknown}` no
arquivo gerado. É cosmético: a união funciona, o compilador recusa `"vencido"` minúsculo ou
com espaço.

**Genéricos.** `PagedResult<EmployeeListItem>` não existe em JSON, então o gerador
**achata** o nome:

```json
"PagedResultOfEmployeeListItem": {
  "required": ["items", "total", "page", "pageSize"],
  "properties": {
    "items": { "type": "array", "items": { "$ref": "#/components/schemas/EmployeeListItem" } },
    "total": { "format": "int32", "type": "integer" }
  }
}
```

Consequência prática: **cada instanciação vira um tipo separado**.
`PagedResult<Employee>` e `PagedResult<Document>` produzem dois schemas completos. Com dez
tipos paginados, você tem dez schemas quase idênticos. Não é problema — é só bom saber por
que o arquivo cresce.

⚠️ E há um risco de colisão: dois tipos com o mesmo nome em namespaces diferentes viram o
mesmo nome no documento, e um sobrescreve o outro. Se você tem `People.Contracts.Status` e
`Work.Contracts.Status`, renomeie um.

## 17.9 Evoluindo o contrato: o que quebra bem e o que quebra mal

Nem toda mudança tem o mesmo custo. A tabela abaixo é o que importa saber antes de mexer
numa API que já tem frontend:

| Mudança no C# | O que acontece no build do TS | Risco |
|---|---|---|
| Adicionar campo | nada quebra | ✅ seguro |
| Remover campo usado no template | **erro de compilação** | ✅ ótimo — você descobre na hora |
| Renomear campo | **erro de compilação** | ✅ ótimo |
| Trocar `string` por `int` | **erro de compilação** | ✅ ótimo |
| Tornar campo anulável (`string` → `string?`) | erro onde o valor é usado sem tratar | ✅ bom |
| Adicionar valor a um enum | erro em `switch` exaustivo; nada em outros lugares | ⚠️ parcial |
| Trocar o **significado** de um campo | **nada** | 🔴 silencioso |
| Mudar a ordem de retorno | **nada** | 🔴 silencioso |
| Mudar de qual erro o endpoint responde | **nada** | 🔴 silencioso |

As três últimas linhas são o limite do mecanismo. O contrato pega mudança de **forma**;
mudança de **contrato semântico** passa por baixo dele.

O caso 6 do [Capítulo 13](13-bugs-reais.md) foi exatamente isso: `positionName` (o nome do
cargo) virou `positionId` (o id de um cargo existente). O tipo mudou — `string` continuou
`string`, mas **o significado** virou outro. O frontend foi corrigido junto, mas os testes
de integração não, e ninguém percebeu por semanas.

**Quando você muda semântica, mude o nome do campo.** Um `positionName` que passa a exigir
id devia se chamar `positionId` — e foi o que se fez. Nome novo quebra o build; significado
novo com nome velho não quebra nada.

## 17.10 As alternativas, e quando cada uma ganha

O Mamão usa **OpenAPI + tipos gerados + `HttpClient` à mão**. Não é a única escolha
razoável.

| Abordagem | Como funciona | Quando ganha | Custo |
|---|---|---|---|
| **DTO à mão** | você escreve os dois lados | protótipo de um fim de semana | diverge em silêncio |
| **openapi-typescript** (Mamão) | gera só os tipos | você quer os interceptors do Angular | ~10 linhas por endpoint |
| **NSwag / Kiota** | gera tipos **e** cliente | consumidor externo, script, outro backend | cliente foge dos interceptors |
| **gRPC** | contrato em `.proto`, binário | serviço-a-serviço, alto volume | precisa de proxy para navegador |
| **GraphQL** | o cliente escolhe os campos | muitos consumidores com necessidades diferentes | servidor bem mais complexo |
| **tRPC** | tipos compartilhados direto | TypeScript nos **dois** lados | não serve para .NET |

A escolha depende de duas perguntas: **quem consome?** e **quantos consumidores?**

Um consumidor só, que você controla, com um framework que tem interceptors valiosos →
tipos gerados e cliente à mão. Vários consumidores externos → gere o cliente inteiro. Muitos
clientes com necessidades diferentes de dado → aí GraphQL começa a se pagar.

## 17.11 Depurando a esteira

Sintomas que você vai encontrar, e a causa de cada um:

| Sintoma | Causa provável |
|---|---|
| Um tipo vira `unknown` no TS | schema vazio — tipo com serialização customizada (17.6) |
| Um número vira `unknown` | `format: int32` sem `type` — o normalizador conserta (Cap. 9) |
| `openapi.json` sempre com diff no CI | algo não determinístico no documento (Cap. 13, caso 7) |
| Campo existe no C# mas não no TS | esqueceu de regenerar, ou o campo não é público, ou não está em `.Produces<T>()` |
| Endpoint não aparece no documento | não foi mapeado, ou o host não iniciou de verdade ao gerar |
| Tipo com nome estranho tipo `…2` | colisão de nomes entre namespaces (17.8) |
| Cliente externo leva 401 | `securitySchemes` não declarado (17.7) |

E o comando que resolve metade das dúvidas — ler o documento em vez de adivinhar:

```bash
# quantos caminhos e schemas existem
python3 -c "
import json; d = json.load(open('web/openapi.json'))
print(len(d['paths']), 'paths;', len(d['components']['schemas']), 'schemas')
"

# como um tipo específico ficou
python3 -c "
import json; d = json.load(open('web/openapi.json'))
print(json.dumps(d['components']['schemas']['EmployeeListItem'], indent=2, ensure_ascii=False))
"

# quais schemas saíram vazios (candidatos a virar unknown)
python3 -c "
import json; d = json.load(open('web/openapi.json'))
for n, s in d['components']['schemas'].items():
    if not s.get('type') and not s.get('properties') and not s.get('enum') and '\$ref' not in s:
        print('VAZIO:', n)
"
```

Aquele último merece virar hábito: rode depois de adicionar um tipo novo. Ele acha o
problema de 17.6 **antes** de o `unknown` se espalhar pelo frontend.

---

## Para fixar

1. **Por que `code: string | null` e não `code?: string`, se o C# diz `string? Code`?**
   <details><summary>Resposta</summary>
   Porque o ASP.NET Core sabe que a propriedade **sempre é serializada** — a chave existe
   no JSON. O que pode variar é o valor. `?` no TypeScript significaria que a chave pode
   não existir, o que seria uma descrição errada.
   </details>

2. **Por que um `readonly record struct` com `JsonConverter` vira `unknown`?**
   <details><summary>Resposta</summary>
   Porque o gerador não consegue inferir o formato serializado — ele veria os membros do
   struct, não a string que o converter produz. Emite schema vazio, e schema vazio vira
   `unknown`.
   </details>

3. **Por que a correção dos ids fica no transformer C# e a dos números no script Node?**
   <details><summary>Resposta</summary>
   O id é uma **verdade sobre a API** — qualquer consumidor merece saber que é
   string/uuid. O `format: int32` sem `type` é **defeito do gerador**, e o conserto dele
   não deve poluir o documento público.
   </details>

4. **Qual mudança de API o contrato gerado NÃO pega?**
   <details><summary>Resposta</summary>
   Mudança de significado com o mesmo tipo — o campo continua `string`, mas passa a
   esperar outra coisa. Também não pega mudança de ordenação nem de quais erros o endpoint
   devolve. Por isso: ao mudar semântica, mude o nome.
   </details>

5. **O `openapi.json` do Mamão não descreve a autenticação. Isso é um bug?**
   <details><summary>Resposta</summary>
   É uma limitação conhecida, não um bug para o uso atual: o bearer é responsabilidade do
   interceptor, não do contrato. Passa a ser problema no dia em que alguém de fora gerar um
   cliente a partir do documento — ele levaria 401 sem entender.
   </details>

## Laboratório

1. Abra o `web/openapi.json` num editor e ache o schema `CreateEmployeeRequest`. Compare
   campo a campo com o `record` C#. Ache o campo que tem `oneOf` e explique por quê.
   *(Dica: é um id **opcional** — veja 17.6.)*
2. Rode o script "quais schemas saíram vazios". No Mamão hoje ele não deve achar nada.
   Agora crie no C# um `readonly record struct` novo com converter, **sem** implementar
   `IStronglyTypedId`, exponha num DTO, regenere — e veja o script achá-lo.
3. Regenere o TypeScript e confirme que o campo virou `unknown`. Depois adicione a marca,
   regenere, e veja virar `string`.
4. **Prove o limite do contrato:** troque o significado de um campo sem trocar o tipo (por
   exemplo, `code` passa a ser matrícula **global** em vez de por empresa). Regenere tudo.
   Nada quebra. Escreva o teste de integração que teria pego.
5. Declare um `securityScheme` bearer no documento e aplique-o aos endpoints
   autenticados. Confirme no `openapi.json` gerado.

---

**Anterior:** [Capítulo 16](16-a-solucao-dotnet.md) ·
**Próximo:** [Capítulo 18 — Caddy](18-caddy.md)
