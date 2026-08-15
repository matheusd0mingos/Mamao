# Capítulo 9 — O contrato: OpenAPI como fonte da verdade

> **Objetivo:** entender por que ninguém no Mamão escreve um DTO à mão no TypeScript, e
> montar a mesma esteira no seu projeto.

Este é o capítulo central da integração Angular + .NET. Se você levar uma ideia só desta
apostila, que seja esta.

> **Tem um aprofundamento.** Este capítulo mostra a esteira funcionando. O
> [Capítulo 17](17-o-contrato-por-dentro.md) abre o `openapi.json` e o `.d.ts` gerado, e
> mostra os cinco pontos onde a tradução entre C# e TypeScript é traiçoeira — incluindo um
> defeito que faz o contrato mentir sem quebrar o build.

---

## 9.1 O problema: dois lados que combinam de boca

Você tem uma classe no C#:

```csharp
public sealed record EmployeeResponse(
    Guid Id,
    string FullName,
    string PositionName,
    DateOnly HiredOn,
    bool IsActive);
```

E escreve o equivalente no TypeScript:

```typescript
export interface EmployeeResponse {
  id: string;
  fullName: string;
  positionName: string;
  hiredOn: string;
  isActive: boolean;
}
```

Funciona. Por uma semana.

Aí alguém renomeia `PositionName` para `Position` no C#. O TypeScript **continua
compilando** — ele não faz ideia de que existe um C# do outro lado. A tela mostra
`undefined` no lugar do cargo. Ninguém percebe até um usuário reclamar.

Ou pior — e isso aconteceu de verdade no Mamão:

> A API passou a exigir `positionId` (o id de um cargo existente) em vez de `positionName`
> (o nome). Os testes de integração continuavam mandando `positionName`. Todo cadastro de
> funcionário respondia **400**. Ninguém viu por semanas, porque aqueles testes não estavam
> rodando (Capítulo 13, caso 6).

**A raiz do problema:** existem duas descrições do mesmo contrato, mantidas por mãos
diferentes, e nada garante que continuem iguais.

## 9.2 A solução: uma fonte, duas saídas

Só o C# descreve o contrato. O TypeScript é **gerado**.

```
    ┌──────────────────────┐
    │   C#  (a verdade)    │
    │  record Employee…    │
    └──────────┬───────────┘
               │  1. dotnet run -- --generate-openapi
               ▼
    ┌──────────────────────┐
    │    openapi.json      │   ← descrição da API num formato padrão
    └──────────┬───────────┘
               │  2. openapi-typescript
               ▼
    ┌──────────────────────┐
    │   api-schema.d.ts    │   ← tipos TypeScript, gerados
    └──────────┬───────────┘
               │  3. import
               ▼
    ┌──────────────────────┐
    │  o código das telas  │
    └──────────────────────┘
```

Com isso, renomear um campo no C# **quebra o build do TypeScript**. O erro aparece no
computador de quem fez a mudança, em segundos, e não num chamado de suporte.

> **Chimpanzé pergunta:** *"O que é OpenAPI, afinal?"*
>
> Um formato padronizado (antes chamado Swagger) para descrever uma API HTTP em JSON: quais
> caminhos existem, quais métodos, o que vai no corpo, o que volta, quais códigos de status.
> Como é padrão, existe um ecossistema enorme de ferramentas que leem esse arquivo — geradores
> de cliente para quase toda linguagem, telas de documentação, ferramentas de teste.

## 9.3 Passo 1: o C# produz o documento

O ASP.NET Core sabe gerar OpenAPI a partir dos endpoints:

```csharp
builder.Services.AddOpenApi();
```

Mas para gerar num **arquivo**, o Mamão tem um pequeno utilitário:

```csharp
/// <summary>
/// Escreve o documento OpenAPI em arquivo e encerra. E o passo que alimenta o gerador do
/// cliente TypeScript no CI — DTO escrito a mao diverge do C# em silencio.
///
/// Uso: dotnet run --project src/Mamao.Api -- --generate-openapi web/openapi.json
///
/// O host precisa iniciar de fato: os endpoints so sao materializados quando o pipeline de
/// roteamento e construido. Sobe numa porta efemera do loopback e para em seguida — nao
/// toca no banco, porque nenhum servico hospedado da API depende dele.
/// </summary>
public static class OpenApiDocumentWriter
{
    public static async Task<bool> TryWriteAndExitAsync(WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, "--generate-openapi");
        if (index < 0) return false;

        var path = index + 1 < args.Length ? args[index + 1] : "openapi.json";
        var fullPath = Path.GetFullPath(path);

        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");      // porta 0 = "escolha uma livre"

        await app.StartAsync();
        try
        {
            var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
            var document = await provider.GetOpenApiDocumentAsync();

            await using var stream = File.Create(fullPath);
            await document.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_0);
        }
        finally
        {
            await app.StopAsync();
        }

        return true;
    }
}
```

Detalhe que o comentário explica e vale entender: **a aplicação precisa iniciar de
verdade**. Os endpoints só existem depois que o pipeline de roteamento é construído — não
dá para inspecionar o código estaticamente e saber o que está mapeado. Por isso ele sobe
numa porta efêmera do loopback, tira a "fotografia" e desliga.

Rodando:

```bash
dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"
```

⚠️ **Armadilha:** `dotnet run --project` usa o diretório **do projeto** como diretório de
trabalho, não o seu. Caminho relativo vai parar em `src/Mamao.Api/web/openapi.json`. Por
isso o `$PWD` — e por isso o CI usa `$GITHUB_WORKSPACE`.

## 9.4 Passo 2: normalizar

O documento gerado precisa de dois retoques. O script `web/normalize-openapi.mjs` faz.

### Retoque 1 — tipos numéricos incompletos

```javascript
if (no.type === undefined && (no.format === 'int32' || no.format === 'int64')) {
  no.type = 'integer';
  delete no.pattern;
}
```

O gerador do .NET 10 às vezes emite `format: int32` sem o `type: integer`. O
`openapi-typescript` então infere `unknown`, e o build do frontend quebra em cima de um
contrato que, na intenção, está certo.

O comentário no arquivo conta que a primeira tentativa foi consertar no próprio host, com
um `IOpenApiDocumentTransformer`. Não funcionou porque, nessa versão, os schemas só entram
em `components` **depois** dos transformers de documento. A correção então vive num passo
explícito do pipeline — visível, e não escondida dentro do cliente gerado.

### Retoque 2 — a porta efêmera

Este é o meu favorito, porque é um caso de teste que **jamais** poderia passar duas vezes.

```javascript
// Fora o `servers`.
//
// O host escreve ali a URL em que ele subiu para gerar o documento — com uma porta
// EFEMERA, diferente a cada execucao. Comitado, isso transforma a checagem de contrato do
// CI num teste que nunca passa duas vezes seguidas: o diff acusa "openapi desatualizado"
// quando a unica coisa que mudou foi o numero da porta sorteada.
delete documento.servers;
```

O documento saía assim:

```json
"servers": [ { "url": "http://127.0.0.1:36335" } ]
```

E na execução seguinte, `44737`. O CI comparava o arquivo commitado com o recém-gerado,
via diferença, e reprovava — sempre. O erro dizia "openapi.json desatualizado", que é
exatamente a mensagem errada para o problema certo.

Tirar não perde nada: o documento existe para gerar tipos, e o frontend fala com a API por
caminho relativo. O endereço real é assunto de deploy, não de contrato.

## 9.5 Passo 3: gerar o TypeScript

```json
"scripts": {
  "generate:api": "node ../normalize-openapi.mjs ../openapi.json && openapi-typescript ../openapi.json -o src/app/core/http/api-schema.d.ts"
}
```

```bash
npm run generate:api
```

Sai um arquivo com todos os tipos. Ele **não se edita** — a cada geração é sobrescrito.

## 9.6 Passo 4: apelidos legíveis

O arquivo gerado tem uma estrutura profunda:
`components['schemas']['EmployeeResponse']`. Escrever isso em toda tela seria horrível.
Então há um arquivo de apelidos:

```typescript
// src/app/core/http/api.types.ts
import type { components } from './api-schema';

/**
 * Apelidos para os tipos gerados do OpenAPI. Nenhum DTO e escrito a mao: remover um
 * campo no C# quebra o build do TypeScript, que e o momento certo de descobrir.
 * Regenerar com `npm run generate:api`.
 */
type Schemas = components['schemas'];

export type EmployeeResponse = Schemas['EmployeeResponse'];
export type CreateEmployeeRequest = Schemas['CreateEmployeeRequest'];
export type PagedEmployees = Schemas['PagedResultOfEmployeeListItem'];
// … cerca de 70 linhas assim
```

Este arquivo **é** escrito à mão, mas note o que ele contém: só apelidos. Se
`EmployeeResponse` deixar de existir no C#, esta linha não compila. É uma camada fina que
melhora a leitura sem introduzir uma segunda verdade.

Uso:

```typescript
import type { EmployeeResponse } from '../../core/http/api.types';

get(id: string): Promise<EmployeeResponse> {
  return firstValueFrom(this.http.get<EmployeeResponse>(`${this.base}/${id}`));
}
```

## 9.7 O CI fecha o circuito

Gerar não adianta se alguém esquecer de gerar. O CI verifica os **dois** lados.

```yaml
- name: OpenAPI commitado esta em dia?
  run: |
    dotnet run --project src/Mamao.Api --no-build -c Release --no-launch-profile \
      -- --generate-openapi "$GITHUB_WORKSPACE/web/openapi.json"
    node web/normalize-openapi.mjs web/openapi.json
    if ! git diff --quiet -- web/openapi.json; then
      echo "::error::web/openapi.json desatualizado. Regenere e comite."
      git --no-pager diff -- web/openapi.json
      exit 1
    fi
```

E do lado do frontend:

```yaml
- name: Cliente gerado esta em dia?
  run: |
    npm run generate:api
    if ! git diff --quiet -- src/app/core/http/api-schema.d.ts; then
      echo "::error::api-schema.d.ts desatualizado. Rode 'npm run generate:api' e comite."
      exit 1
    fi
```

A lógica é a mesma nos dois: **regenere e veja se mudou alguma coisa.** Se mudou, o que
está commitado não corresponde ao código — e o CI reprova com uma instrução exata do que
fazer.

## 9.8 Por que não usar um cliente HTTP gerado

Ferramentas como o NSwag geram não só os tipos, mas as **funções** de chamada
(`api.getEmployee(id)`). O Mamão gera só os tipos, de propósito:

```typescript
/**
 * Camada fina sobre o HttpClient com os tipos GERADOS do OpenAPI.
 *
 * Nao usamos um cliente fetch gerado de proposito: ele passaria por fora dos
 * interceptors do Angular, e e neles que moram auth, refresh de token e traducao de erro.
 * Tipos gerados + HttpClient da os dois lados.
 */
@Injectable({ providedIn: 'root' })
export class EmployeesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/employees';
  // …
}
```

Um cliente gerado normalmente usa `fetch` direto. Isso significa que ele **não passa** pelos
interceptors do Angular — e é lá que estão o bearer token, o refresh automático no 401 e a
tradução de erro. Você teria que reimplementar as três coisas dentro do cliente gerado, ou
perdê-las.

Tipos gerados + `HttpClient` escrito à mão dá as duas coisas: segurança de tipo e a corrente
de interceptors. O custo é escrever ~10 linhas por endpoint.

## 9.9 Detalhes de tradução entre C# e TypeScript

| C# | JSON | TypeScript |
|---|---|---|
| `Guid` | `"a1b2…"` | `string` |
| `DateOnly` | `"2026-08-15"` | `string` |
| `DateTimeOffset` | `"2026-08-15T09:00:00Z"` | `string` |
| `decimal` | `24.90` | `number` |
| `string?` | `null` ou ausente | `string \| null` |
| `enum` | **texto**, ver abaixo | união de literais |

**Datas viram string.** Não existe tipo de data em JSON. Se você precisa de um `Date`,
converta explicitamente — e prefira manter como string quando for só para exibir, porque o
`DatePipe` aceita string e converter cria risco de fuso.

**Enum vai como texto**, e isso é decisão explícita no Mamão:

```csharp
// Enum vai como TEXTO no JSON, nunca como numero. Um `status: 2` no contrato obriga o
// frontend a manter uma tabela de numeros e transforma reordenar o enum em quebra
// silenciosa; `status: "DuplicadaNoArquivo"` se explica sozinho no log e no DevTools.
```

Com texto, o TypeScript gerado vira uma união de literais:

```typescript
export type DocumentStatus = 'SemValidade' | 'Valido' | 'Vencendo' | 'Vencido';
```

O compilador então recusa `status === 'Vencido '` (com espaço) ou `'vencido'` minúsculo.
Com número, seria `status === 2` — e ninguém lembra o que é 2.

## 9.10 O fluxo de trabalho no dia a dia

Quando você mexe na API:

```bash
# 1. mudou o C#
# 2. regenera o contrato
dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"

# 3. regenera os tipos
cd web/mamao-web && npm run generate:api

# 4. compila o front — aqui aparecem os lugares que quebraram
npm run build

# 5. conserta, e commita TUDO junto
git add -A && git commit
```

O passo 4 é o presente: o compilador lista exatamente quais telas usavam o campo que você
mudou. Sem essa esteira, essa lista você descobriria em produção.

---

## Para fixar

1. **Por que o `servers` é removido do openapi.json?**
   <details><summary>Resposta</summary>
   Porque contém a porta efêmera onde o host subiu para gerar o documento — diferente a
   cada execução. Commitado, faz a checagem de contrato do CI falhar sempre, com uma
   mensagem que aponta para o problema errado.
   </details>

2. **Por que o Mamão gera só os tipos e não o cliente HTTP inteiro?**
   <details><summary>Resposta</summary>
   Porque um cliente gerado normalmente usa `fetch` direto e não passa pelos interceptors
   do Angular, onde estão o bearer token, o refresh no 401 e a tradução de erro.
   </details>

3. **O CI regenera o contrato e compara com o commitado. Qual falha isso pega?**
   <details><summary>Resposta</summary>
   Alguém mudou a API e não regenerou o contrato — então o frontend está tipado contra uma
   versão que não existe mais.
   </details>

4. **Por que enum como texto e não como número?**
   <details><summary>Resposta</summary>
   Porque número obriga o frontend a manter uma tabela de correspondência, e reordenar o
   enum no C# muda o significado dos números sem quebrar nada visivelmente. Texto se
   explica sozinho no log e no DevTools, e vira união de literais checada pelo compilador.
   </details>

## Laboratório

Volte ao projeto `loja` do Capítulo 2 e monte a esteira inteira:

1. No `Program.cs`, adapte o `OpenApiDocumentWriter` (copie do Mamão) e gere o
   `web/openapi.json`.
2. `npm i -D openapi-typescript` e adicione o script `generate:api`.
3. Gere os tipos e troque a sua `interface Produto` escrita à mão pelo tipo gerado.
4. **O momento da verdade:** adicione `decimal Estoque` ao `record Produto` no C#. Regenere.
   Rode `npm run build`. Se você tinha um `Produto` construído à mão em algum lugar do
   TypeScript, o compilador vai acusar. Agora **remova** um campo no C# e repita — veja o
   erro apontar exatamente a linha do template que usa o campo removido.
5. Escreva um script `verificar-contrato.sh` que regenera e falha se `git diff` mostrar
   diferença. É o CI do Mamão em cinco linhas.

---

**Anterior:** [Capítulo 8](08-formularios.md) ·
**Próximo:** [Capítulo 10 — HTTP, interceptors, autenticação e erros](10-http-e-interceptors.md)
