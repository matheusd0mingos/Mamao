# ADR-0009 — Cliente HTTP e DTOs gerados do OpenAPI

**Status:** aceita · **Data:** 2026-08

## Contexto

Um desenvolvedor mantendo backend e frontend. Toda mudança de contrato acontece nos
dois lados. Interface escrita à mão em TypeScript diverge do C# silenciosamente — e
a divergência aparece em produção, não no build.

## Decisão

O backend expõe OpenAPI (`Microsoft.AspNetCore.OpenApi`). O frontend **gera**
cliente e tipos a partir dele. Nenhum DTO de API escrito à mão no Angular.

```bash
dotnet run --project src/Mamao.Api -- --generate-openapi > web/openapi.json
npx openapi-typescript-codegen --input web/openapi.json \
    --output src/app/core/http/generated
```

O gerado é **commitado**. O CI regenera e falha se houver diferença não commitada —
assim ninguém esquece.

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

NSwag gerando cliente C# **e** TypeScript do mesmo documento. Vale se um dia houver
um segundo consumidor .NET (por exemplo, um serviço extraído). Por enquanto,
`openapi-typescript-codegen` é mais simples e produz saída mais limpa.
