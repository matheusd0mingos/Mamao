# ADR-0016 — Critério para biblioteca de terceiro (licença e saúde)

**Status:** aceita · **Data:** 2026-08 · **Origem:** decisões tomadas ao montar o Marco 0

## Contexto

Três bibliotecas citadas no briefing (ou padrão no ecossistema) mudaram de modelo
de licenciamento ou trouxeram problema real durante a montagem do esqueleto. Vale
registrar o critério, porque isso vai se repetir.

## Critério

Antes de adotar, verifique **na data de hoje**, não pela memória:

1. **Licença da versão que você vai usar**, não a do projeto em geral. Várias
   bibliotecas .NET populares migraram para licença comercial em versões recentes,
   mantendo a anterior sob licença livre.
2. **Se o custo aparece quando o produto crescer.** Licença gratuita "abaixo de X de
   faturamento" é dívida com data marcada.
3. **Se o benefício é real hoje.** Biblioteca que resolve um problema que você ainda
   não tem é dependência sem contrapartida.
4. **Advisories.** `dotnet list package --vulnerable --include-transitive` no CI,
   quebrando o build. Vale para dependência transitiva.

## Decisões tomadas

### FluentAssertions → Shouldly

O briefing pedia FluentAssertions. A partir da v8 ela passou a exigir licença
comercial para uso comercial; a v7 permanece sob licença livre.

**Escolha: Shouldly.** Gratuita, API equivalente em legibilidade
(`resultado.IsSuccess.ShouldBeTrue()`), sem risco de licença.

Alternativa igualmente válida: **AwesomeAssertions**, fork livre da API da v7, se
você quiser a sintaxe `Should().Be()` exata.

### MassTransit → outbox próprio

Já tratado em [ADR-0005](0005-outbox-e-mensageria.md). Some-se o fato de que o
MassTransit também mudou de modelo de licenciamento em versões recentes: mais um
motivo para não introduzir a dependência antes de ela resolver um problema real.
Se um dia for preciso, **Wolverine** (MIT) é a primeira opção a avaliar.

### SSH.NET — pin transitivo

O Testcontainers arrasta `SSH.NET`, e a versão que ele resolvia tinha advisory de
severidade alta. Com `TreatWarningsAsErrors`, o build quebrou — comportamento
desejado.

Correção: pin transitivo em `Directory.Packages.props` (com
`CentralPackageTransitivePinningEnabled`), fixando uma versão corrigida. É a forma
certa de tratar vulnerabilidade em dependência que não é sua.

### openapi-typescript — override de peer

O Angular 22 traz TypeScript 6; o `openapi-typescript` ainda declara peer de
TypeScript 5. Como a ferramenta apenas **emite** um `.d.ts` (não faz type-check do
projeto), o conflito é nominal.

Correção: `overrides` no `package.json` apontando o peer para a versão do projeto.
Preferido a `--legacy-peer-deps`, que afrouxaria a resolução do repositório inteiro
em vez de um pacote só.

## Consequências

- `Directory.Packages.props` é o único lugar com número de versão, e carrega o
  comentário do porquê quando a escolha não é óbvia.
- O CI falha em pacote vulnerável, inclusive transitivo.
- Ao reavaliar qualquer item acima, **confirme a licença atual**: estas notas
  envelhecem.
