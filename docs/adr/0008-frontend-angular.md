# ADR-0008 — Angular standalone + Signals + CDK, sem Material visual, sem NgRx

**Status:** aceita · **Data:** 2026-08

## Contexto

Aplicação empresarial: muitos formulários, tabelas densas, permissões por tela,
workflows, calendário e Kanban. Um desenvolvedor. Objetivo paralelo de ampliar
experiência além de React. E uma exigência explícita: o produto **não** pode parecer
"Angular Material genérico".

## Decisão 1 — Angular moderno

Standalone components, Signals para estado, Reactive Forms tipados, `@if`/`@for`,
`OnPush` universal, guards e interceptors funcionais, lazy loading por feature.

Angular é boa escolha aqui: forms e validação são o ponto forte do framework, a
estrutura opinativa reduz decisão a cada feature, e a aplicação tem vida longa —
que é onde a estabilidade de API do Angular compensa.

## Decisão 2 — CDK sim, Material não

**Angular CDK** entra sem hesitação: `DragDrop` (Kanban, escala), `Overlay`
(popover, diálogo, toast), `A11y` (foco, `LiveAnnouncer`), `Scrolling` (virtual
scroll na timeline), `Table`, `Portal`, `Clipboard`. É comportamento testado, sem
aparência imposta — exatamente o que "não reinventar plumbing" recomenda.

**Angular Material** fica fora da camada visual. Tematizá-lo até deixar de parecer
Material significa lutar com tokens internos, densidade, elevação e ripple, e
refazer essa luta a cada versão maior. O resultado costuma ser pior e mais caro do
que ter escrito os componentes.

Custo assumido: ~15 componentes próprios, 1 a 2 semanas de investimento inicial —
que se paga em todas as telas seguintes e é o que dá identidade ao produto.

Exceção pragmática: se o `DatePicker` próprio se mostrar caro (e ele sempre é mais
caro do que parece), use o do Material isolado apenas para ele. Escolher onde gastar
não é incoerência.

## Decisão 3 — sem NgRx

Signals + um store service por feature cobrem a complexidade real do Mamão. NgRx
introduz actions, reducers, effects e selectors para resolver problemas — time
grande, estado global compartilhado entre features distantes, time-travel debugging
— que não existem aqui.

Migrar depois, feature por feature, é viável. O caminho contrário (remover NgRx de
um projeto inteiro) não é.

## Consequências

- O design system é ativo do produto, não acidente. Ver
  [UX](../produto/ux-telas-criticas.md#design-system).
- Componentes precisam de acessibilidade feita à mão — o CDK ajuda muito, mas a
  responsabilidade é sua. Contraste AA, foco visível e navegação por teclado nas
  cinco telas críticas são requisito, não polimento.
- Timeline de férias e Kanban são componentes próprios (CSS Grid + CDK). Nenhuma
  biblioteca de scheduler entrega a linha de cobertura, que é o recurso que vende a
  tela.
- Zoneless assim que o projeto estabilizar: menos surpresa de detecção de mudança e
  melhor desempenho.

## Quando revisitar

Se o estado do frontend crescer a ponto de haver sincronização complexa entre
features distantes, reavalie NgRx (ou NgRx Signal Store) — feature a feature.
