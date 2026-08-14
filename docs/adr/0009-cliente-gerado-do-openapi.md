# ADR-0009 — Cliente HTTP e DTOs gerados do OpenAPI

**Status:** aceita · **Data:** 2026-08

## Contexto

Um desenvolvedor mantendo backend e frontend. Toda mudança de contrato acontece nos
dois lados. Interface escrita à mão em TypeScript diverge do C# silenciosamente — e
a divergência aparece em produção, não no build.

## Decisão

O backend expõe OpenAPI (`Microsoft.AspNetCore.OpenApi`). O frontend **gera os
tipos** a partir dele e escreve serviços finos sobre o `HttpClient` do Angular.
Nenhum DTO de API escrito à mão.

### Tipos gerados, não cliente gerado

Refinamento descoberto ao implementar o Marco 0: um cliente HTTP gerado (fetch)
**passa por fora dos interceptors do Angular** — e é neles que moram o bearer
token, a rotação do refresh e a tradução de `ProblemDetails`. Adotá-lo
significaria reimplementar tudo isso dentro do gerado, ou perdê-lo.

Solução: `openapi-typescript` gera apenas o `.d.ts` do schema; cada feature tem um
serviço de ~30 linhas sobre `HttpClient` usando esses tipos. Ganha-se a segurança
de tipos do contrato sem abrir mão do pipeline HTTP do framework.

```bash
# O caminho e absoluto porque `dotnet run --project` usa o diretorio do PROJETO como cwd.
dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"

cd web/mamao-web && npm run generate:api
```

O script `generate:api` roda `web/normalize-openapi.mjs` antes do gerador. Motivo: o
gerador de OpenAPI do .NET 10 emite `format: int32` **sem** `type: integer` em algumas
propriedades, e o `openapi-typescript` então infere `unknown`, quebrando o build do
frontend em cima de um contrato que está correto na intenção. A normalização foi
tentada primeiro como `IOpenApiDocumentTransformer` no host, mas nesta versão os
schemas só entram em `components` **depois** dos transformers de documento — por isso
ela vive num passo explícito do pipeline, e não escondida no cliente gerado.

Tanto `web/openapi.json` quanto `api-schema.d.ts` são **commitados**, e o CI verifica
os dois lados do circuito: o job de backend regenera o `openapi.json` a partir da API e
falha se divergir do commitado; o de frontend regenera o `.d.ts` e falha se divergir.
Assim, mudar a API sem atualizar o frontend quebra no PR, não em produção.

## Motivo

Remover um campo no C# quebra o build do TypeScript imediatamente. É a diferença
entre descobrir no `npm run build` e descobrir no cliente.

Também é a aplicação direta de "não reinventar o que o framework resolve": o
contrato já existe formalmente; escrevê-lo de novo à mão é duplicação com
divergência garantida.

## Consequências

- O backend precisa produzir OpenAPI **de qualidade**: `ProblemDetails` documentado,
  enums nomeados, nullability correta, `operationId` estável (define o nome do
  método gerado — mudá-lo quebra chamadas).
- Camada fina própria por cima do gerado, onde ficam auth, tratamento de erro e
  cancelamento. Componentes não chamam o gerado diretamente.
- Regenerar faz parte do fluxo de mudança de API, com passo no CI.
- No dia da extração de um módulo, o cliente HTTP entre serviços sai do mesmo
  gerador. A prática já estará madura.

## Alternativa considerada

NSwag gerando cliente C# **e** TypeScript do mesmo documento. Vale no dia em que
houver um segundo consumidor .NET — por exemplo, o cliente HTTP para um módulo
extraído. Aí o mesmo documento passa a alimentar os dois geradores.
