# ADR-0012 — Código em inglês, produto em pt-BR, i18n preparada

**Status:** aceita · **Data:** 2026-08

## Contexto

Produto brasileiro, com domínio cheio de termos regulatórios locais (período
aquisitivo, abono pecuniário, NR-35, ASO, CLT). Desenvolvedor brasileiro.
Possibilidade futura de outros mercados de língua portuguesa ou espanhola.

## Decisão

| Camada | Idioma |
|---|---|
| Código, classes, tabelas, colunas, endpoints, eventos | **Inglês** |
| Interface do produto | **pt-BR** |
| Documentação de arquitetura e ADRs | pt-BR |
| Mensagens de commit, código de exemplo | inglês |
| Termos sem tradução honesta | mantidos em português no código |

## Sobre os termos intraduzíveis

"Período aquisitivo" não é *accrual period* — é um conceito jurídico específico da
CLT, e traduzir cria ambiguidade com quem entende a regra.

Regra prática: **traduza o que é genérico, preserve o que é jurídico.**

```csharp
public sealed class VacationEntitlement          // conceito genérico → inglês
{
    public DateOnly PeriodoAquisitivoStart { get; }   // conceito jurídico → preservado
    public DateOnly PeriodoAquisitivoEnd   { get; }
    public DateOnly PeriodoConcessivoEnd   { get; }
    public int      DaysEntitled { get; }
    public int      DaysTaken    { get; }
    public int      AbonoPecuniarioDays { get; }      // não existe em inglês
}
```

Híbrido é intencional e melhor que qualquer extremo. Traduzir tudo esconde a regra
de negócio; escrever tudo em português quebra a convenção do ecossistema .NET e das
bibliotecas.

## i18n desde o commit 1

Strings de UI atrás de chave, publicando **apenas pt-BR**:

```html
<button>{{ 'vacations.request.submit' | t }}</button>
```

Motivo: o custo hoje é uma chave em vez de um literal. O custo depois é varrer todos
os templates do produto. Assimetria clara, e a decisão não exige adivinhar se haverá
segundo idioma.

Não fazer agora: arquivo de tradução para outros idiomas, seletor de idioma,
pluralização complexa. Só a chave.

## Formatação

- Locale `pt-BR` registrado uma vez no Angular; nada de formatar data à mão.
- Datas **sempre** `dd/MM/yyyy` na UI e **sempre** ISO-8601 UTC na API.
- Todo timestamp em `timestamptz`. Nunca `timestamp` sem timezone.
- Fuso: armazene UTC, apresente no fuso da empresa (`Tenant.TimeZone`). Brasil tem
  mais de um fuso, e "hoje" no dashboard precisa ser o hoje do cliente.
- `DateOnly` para férias, ausência e escala. Férias são dias, não instantes —
  usar `DateTime` aqui gera bug de fuso na virada do dia, e é um erro caro de
  corrigir depois.
