# ADR-0017 — Regime de vínculo configurável (CLT, estatutário, militar)

**Status:** aceita · **Data:** 2026-08 · **Substitui parcialmente:** [ADR-0014](0014-regras-clt-de-ferias.md)

> **Aviso.** Como na ADR-0014: insumo de engenharia, não parecer jurídico. Cada regime
> tem legislação própria e, nos estaduais e municipais, ela varia por ente federativo.

## Contexto

Até aqui o produto assumiu **CLT** em todo lugar: as regras de férias
([ADR-0014](0014-regras-clt-de-ferias.md)) e de jornada
([ADR-0015](0015-regras-de-jornada-e-escala.md)) foram escritas contra a CLT, e o
`EmploymentContract` só previa 12×36, 5×2, 6×1 e ADM.

O autor esclareceu que o público real inclui **servidor público e militar**, e que o
regime precisa ser configurável. Isso não é um detalhe de cadastro: muda qual lei se
aplica a férias, licenças, jornada e escala.

| Regime | Norma base | Férias | Observação para o produto |
|---|---|---|---|
| CLT | CLT, art. 129–145 | 30 dias, reduzidos por faltas; fracionamento em até 3 com um de 14+ | Já modelado na [ADR-0014](0014-regras-clt-de-ferias.md) |
| Estatutário federal | Lei 8.112/90, art. 77–80 | 30 dias, **parceláveis em até 3**, sem o mínimo de 14 dias | Adicional de 1/3; regra de acúmulo distinta |
| Estatutário estadual/municipal | Estatuto do ente | Varia | **Não dá para hard-codear.** Só parametrizar |
| Militar | Lei 6.880/80 (Forças Armadas) ou estatuto da corporação | 30 dias, regime próprio; férias podem ser interrompidas por necessidade do serviço | Conceito de "escala de serviço" é o centro, não jornada semanal |

O erro a evitar é óbvio quando escrito: **aplicar regra de CLT a militar e dizer ao
cliente que ele está em conformidade.** Isso seria pior do que não validar nada.

## Decisão

### 1. O regime pertence ao CONTRATO, não ao tenant

Uma prefeitura tem estatutários, celetistas e comissionados na mesma folha. Uma
corporação tem militares e servidores civis. Amarrar o regime ao tenant obrigaria a
criar contas separadas para a mesma organização — que é exatamente o problema que a
árvore de unidades resolve.

```
EmploymentContract
  Regime          Clt | EstatutarioFederal | EstatutarioLocal | Militar | Outro
  ...jornada, carga semanal, vigência
```

O tenant define o regime **padrão**, para o cadastro não perguntar 40 vezes a mesma
coisa. O contrato pode divergir.

### 2. As regras vêm de uma política resolvida pelo regime

Nenhuma regra de férias ou jornada fica escrita em `if (regime == X)` espalhado pelo
domínio. Existe uma política por regime, com os mesmos parâmetros da
[`VacationPolicy`](0014-regras-clt-de-ferias.md#fracionamento) já decidida:

```
LeavePolicy (por tenant + regime)
  MaxPeriods                 CLT 3   · 8.112 3   · militar 1 (padrão)
  MinimumLongPeriodDays      CLT 14  · 8.112 0   · militar 0
  MinimumOtherPeriodDays     CLT 5   · 8.112 0   · militar 0
  ReducesByAbsence           CLT sim · 8.112 não · militar não
  MinimumDaysBeforeHoliday   CLT 2   · 8.112 0   · militar 0
```

Os padrões saem carregados por regime; o cliente ajusta o que a norma dele disser.
É a mesma decisão do 3×10, generalizada: **o sistema traz o padrão e o cliente
corrige, com a mudança auditada.**

### 3. O que o sistema NÃO promete

Para estatutário estadual/municipal e para militar, o Mamão **não afirma
conformidade**. Ele calcula em cima da política que o cliente configurou, e a tela diz
isso com todas as letras. A promessa continua sendo "o Mamão avisa antes de você
perder o prazo", nunca "o Mamão garante conformidade".

Regime `Outro` existe de propósito: PJ, estágio, voluntário, terceirizado. Ele
desliga o cálculo de férias em vez de fingir um número.

## Consequências

- A [ADR-0014](0014-regras-clt-de-ferias.md) deixa de ser "as regras" e passa a ser
  **a política padrão do regime CLT**. Nada do que está lá se perde.
- O Marco 5 fica mais caro: são três conjuntos de padrões e uma tela de política, não
  um. Em compensação, a mesma estrutura serve ao quarto regime que aparecer.
- `EmploymentContract` (Marco 1) ganha `Regime` desde o primeiro commit. Adicionar
  depois obrigaria a adivinhar o regime de todo contrato já gravado.
- Relatório e dashboard passam a poder misturar regimes na mesma unidade. Toda
  contagem de "dias de férias" precisa dizer sob qual regra foi calculada.

## Quando revisitar

Quando o primeiro cliente estatutário estadual entrar. É lá que se descobre quantos
parâmetros faltam — e a aposta desta ADR é que faltarão poucos, porque os eixos
(quantos períodos, tamanho mínimo, reduz por falta) são os mesmos em todo estatuto.
