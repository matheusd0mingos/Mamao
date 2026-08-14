# UX — as telas que decidem o produto

Cinco telas carregam o produto. Todo o resto é formulário e tabela, e formulário
e tabela se resolvem com um bom design system.

---

## Princípios

1. **Pendência acima de gráfico.** O gestor abre o sistema para resolver, não para
   contemplar. Gráfico é contexto, pendência é ação.
2. **Resolver sem navegar.** Aprovar férias deve acontecer na lista, com um clique,
   sem abrir detalhe.
3. **Undo em vez de confirmação.** Diálogo "tem certeza?" treina o usuário a clicar
   em OK sem ler. Aja imediatamente e ofereça desfazer por 8 segundos. Exceções:
   ações irreversíveis (excluir funcionário, apagar documento).
4. **Vazio nunca é vazio.** Toda tela sem dados mostra o próximo passo concreto
   ("Importe sua equipe por CSV"), não uma ilustração e a palavra "Nenhum registro".
5. **Número sempre com contexto.** "84% da capacidade" sozinho é ruído. Com "7
   tarefas · 32h · 3 sem estimativa" vira informação.
6. **Densidade adulta.** Sistema empresarial usado 8h por dia precisa de mais linhas
   por tela do que uma landing page. Espaço em branco é da marca; a tabela é da
   operação. Não sacrifique a segunda pela primeira.

---

## 1. Dashboard do gestor

Três blocos, nesta ordem vertical. **Pendências primeiro** — é o que diferencia de
um dashboard genérico.

```
Bom dia, João.

┌─ PRECISA DE VOCÊ ─────────────────────────────────────────────┐
│ ⚠  NR-35 de Carlos Mendes vence em 6 dias      [Cobrar]       │
│ 🏖  Férias de Ana Souza (10/09–20/09)          [Aprovar] [Ver] │
│ 🏖  Ana perde 12 dias de férias em 30 dias     [Programar]     │
│ ⏰  3 tarefas atrasadas na equipe               [Ver]          │
│ 📄  RG e comprovante de residência de Beatriz   [Cobrar]       │
└───────────────────────────────────────────────────────────────┘

┌─ SUA EQUIPE HOJE ─────────────────────────────────────────────┐
│  18 pessoas   ·   14 disponíveis   ·   2 férias                │
│                    1 home office   ·   1 ausente               │
│  [linha de avatares com marcador de status]                    │
└───────────────────────────────────────────────────────────────┘

┌─ TRABALHO ────────────────────────────────────────────────────┐
│  32 atividades   18 concluídas   8 em andamento   3 atrasadas  │
│  Atenção: João está com 95% da capacidade esta semana          │
│           Carlos tem 32% — considere redistribuir              │
└───────────────────────────────────────────────────────────────┘
```

Observações:

- Cada pendência tem **um botão que resolve** e um link que aprofunda. Aprovação
  acontece inline, com undo.
- O bloco de trabalho é **texto acionável**, não donut. O donut do brand board pode
  existir, mas abaixo da dobra e como contexto — não como resposta.
- Ordenação por gravidade × prazo. Se a lista passar de ~7 itens, agrupe e mostre
  "mais 12".
- Vazio bom: "Nada pendente hoje. Sua equipe está em dia." — isso vale como produto.

---

## 2. Timeline de férias e ausências

A tela que vende na demo. Precisa ser componente próprio; nenhum scheduler genérico
entrega isso.

```
                         SETEMBRO 2026
                    01 02 03 04 05 06 07 08 09 10 11 12 13 14 15
─────────────────────────────────────────────────────────────────
▾ OPERAÇÕES (8)
  João Silva          ░░ ███████████████████████ ░░
  Carlos Mendes                      ░░ ██████████████████
  Maria Souza         ░░
  Ana Lima
─────────────────────────────────────────────────────────────────
  DISPONÍVEIS         8  8  6  6  6  5  5  5  5  5  6  6  8  8  8
  cobertura mínima: 6 ⚠                    ▲ 3 dias abaixo do mínimo
```

O que importa:

- **A linha de cobertura é o recurso principal**, não as barras. Barras respondem
  "quem está fora"; a linha responde **"a operação aguenta?"**. É a diferença entre
  um calendário e uma ferramenta de gestão.
- Cobertura mínima é configurável por setor/equipe. Dias abaixo do mínimo ficam
  destacados e viram pendência no dashboard.
- Agrupamento por setor, colapsável, com contagem.
- Primeira coluna fixa (sticky). Scroll horizontal por tempo, vertical por pessoa.
- Zoom: dia / semana / mês. Em mês, a barra vira bloco agregado.
- Legenda por padrão visual **além de cor** (hachura, borda) — daltonismo é comum e
  esta tela é toda codificada por cor.
- Clique na barra abre um popover (CDK Overlay) com detalhe e ações.

Implementação: CSS Grid com colunas de largura fixa por dia, linhas virtualizadas
(CDK `VirtualScrollViewport`) acima de ~50 pessoas. Sem biblioteca de gantt.

---

## 3. "Meu dia" (funcionário)

Móvel primeiro. É a tela que o funcionário abre no celular, e a única que ele abre.

```
Bom dia, Carlos.

HOJE · quinta, 14 de agosto        Escala 08:00–17:00 · presencial

  ☑  08:00   Conferir material
  ☐  09:00   Instalar luminárias · Loja 3
  ☐  14:00   Fazer teste de carga
  ☐  16:00   Enviar fotos da instalação

PENDÊNCIAS SUAS
  📄  Seu NR-10 vence em 12 dias        [Enviar novo]
  🏖  Você tem 18 dias de férias        [Solicitar]
```

- Concluir tarefa é um toque na checkbox. Sem tela de detalhe no caminho feliz.
- Sem gráfico, sem métrica, sem percentual da própria produtividade.
- Tarefas sem horário aparecem em "Sem horário definido", abaixo.

---

## 4. "Minha equipe"

A resposta rápida para "como está o time hoje".

```
Carlos Mendes     3/4 concluídas    62% capacidade    🟢 presencial
Marcos Alves      5/5 concluídas    48% capacidade    🟢 home office
João Silva        1/3 concluídas    95% capacidade    🔴 sobrecarga
Ana Lima          —                  —                🏖 férias até 20/09
Beatriz Costa     2/2 concluídas    35% capacidade    🟢 presencial
```

- O vermelho do João é sobre **carga**, não sobre desempenho. O texto de apoio deve
  ser "sobrecarregado", nunca "abaixo da meta". A escolha de palavra aqui é decisão
  de produto, não de copy.
- Ação direta: arrastar/atribuir tarefa de um para outro a partir desta tela.

---

## 5. Fila de aprovações

Uma fila, todos os tipos, resolvível com teclado.

```
[ Todas ]  Férias 2   Documentos 5   Ausências 1

☐  🏖  Ana Lima · Férias 10/09–20/09 · 11 dias
       ⚠ Carlos também estará fora em 12/09–14/09        [Aprovar] [Recusar] [Detalhe]

☐  📄  Beatriz Costa · ASO admissional · enviado há 2h
       [pré-visualização]                                 [Aprovar] [Recusar] [Detalhe]
```

- Seleção múltipla + aprovar em lote.
- **O conflito aparece na própria linha.** Uma aprovação de férias sem o aviso de
  conflito é o mesmo que um e-mail — o valor está em decidir informado.
- Recusar exige motivo (vira notificação e entra na auditoria).
- Navegação por teclado: `J`/`K` move, `A` aprova, `R` recusa. Quem usa isso todo
  dia agradece.

---

## Design system

Ver [ADR-0008](../adr/0008-frontend-angular.md) para a decisão de CDK sem Material.

Base a partir do brand board:

```
--mamao-green-900:  #11362D    superfícies escuras, sidebar, texto forte
--mamao-yellow-500: #F2B233    ação primária, destaque, o til
--mamao-cream-50:   #F7F3EA    fundo da aplicação
--mamao-gray-200:   #E6E6E1    bordas, separadores
--mamao-sage-200:   #CDE6D5    estados positivos suaves
```

Faltam no brand board e precisam ser definidos antes do primeiro componente:

- **Escala semântica de status**: sucesso, atenção, erro, informação, neutro — cada
  um com variante de fundo, borda e texto. O amarelo `#F2B233` é ação **e** atenção
  hoje; isso vai colidir na primeira tela de pendências. Separe-os.
- **Escala de cinzas de texto** (primário, secundário, desabilitado) com contraste
  AA verificado sobre creme *e* sobre verde escuro.
- **Espaçamento** em escala de 4 px e **raios de borda** em 3 níveis.
- **Modo escuro**: decidir agora se existe. Se sim, tokens semânticos desde o
  início; se não, assuma e não deixe meio caminho.

Tipografia: DM Serif Display só em título de página e números grandes. Inter em
todo o resto, incluindo qualquer coisa dentro de tabela. Serifada em tabela densa
prejudica a leitura.

Acessibilidade como requisito, não como polimento: contraste AA, foco visível,
navegação por teclado nas cinco telas acima, `aria-live` nos toasts de undo.
