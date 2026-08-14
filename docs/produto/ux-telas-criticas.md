# UX — as telas que decidem o produto

Seis telas carregam o produto. Todo o resto é formulário e tabela, e formulário e
tabela se resolvem com um bom design system.

Duas delas — a grade de escala e a timeline de férias — são o **mesmo componente**
(`TimeGrid`) com conteúdo de célula diferente. Construir uma vez e usar duas é o
principal corte que faz Escalas caber na V1 ([roadmap](../roadmap.md#o-que-foi-cortado-para-caber)).

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
│ 🚨  Turno B sem cobertura mínima em 07/09      [Ajustar escala]│
│ ⚠  NR-35 de Carlos Mendes vence em 6 dias      [Cobrar]       │
│ 🏖  Férias de Ana Souza (10/09–20/09)          [Aprovar] [Ver] │
│ 🏖  Ana perde 12 dias de férias em 30 dias     [Programar]     │
│ 🔄  Troca de plantão Pedro ⇄ Carlos em 20/08   [Aprovar]       │
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

## 2. Grade de escala

A tela mais usada do produto no segmento escolhido. É ela que substitui a planilha.

```
                    SETEMBRO 2026 · OPERAÇÕES        [12×36] [Publicar]
                 01  02  03  04  05  06  07  08  09  10  11  12
                 seg ter qua qui sex sáb dom seg ter qua qui sex
──────────────────────────────────────────────────────────────────
 João Silva      A   ·   A   ·   A   ·   A   ·   A   ·   A   ·
 Carlos Mendes   B   ·   B   ·   B   ·   B   ·   B   ·   B   ·
 Maria Souza     ·   A   ·   A   ·   A   ·   A   ·   A   ·   A
 Pedro Alves     ·   B   ·   B   ·   B   ·  🏖  ·  🏖  ·  🏖
──────────────────────────────────────────────────────────────────
 TURNO A (mín 2)  2   2   2   2   2   2   2   2   2   2   2   2
 TURNO B (mín 2)  2   2   2   2   2   2   1   2   1   2   1   2
                                          ⚠           ⚠       ⚠
        Pedro em férias 07–12/09 · turno B abaixo do mínimo em 3 dias
```

O que importa:

- **Cobertura por turno, não por dia.** "6 disponíveis" não significa nada se as
  duas pessoas do noturno estiverem fora. Este é o recurso que a planilha não tem.
- Gerar do ciclo e **editar na grade**: clicar na célula troca o turno, arrastar
  move a atribuição (CDK DragDrop).
- **Alerta, nunca bloqueio.** Interjornada abaixo de 11h, semana sem DSR, dobra de
  plantão: marca a célula e explica no popover. A operação real tem exceção, e um
  sistema que impede o coordenador de registrar o que já aconteceu é abandonado na
  primeira semana.
- Distinguir **rascunho** de **publicada**. Publicar é o evento que notifica a
  equipe — e é o momento em que a escala vira compromisso.
- **Exportar em PDF.** O coordenador vai imprimir e colar na parede. Não lute contra
  isso; é sinal de que a escala virou a fonte da verdade.

---

## 3. Timeline de férias e ausências

A tela que vende na demo. Mesmo `TimeGrid`, célula com barra em vez de turno.

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
  TURNO A (mín 2)     3  3  2  2  2  2  2  2  2  2  2  3  3  3  3
  TURNO B (mín 2)     3  3  2  2  2  1  1  1  1  1  2  2  3  3  3
                              ⚠  ⚠  ⚠  ⚠  ⚠  ⚠  ⚠
                              ▲ turno B abaixo do mínimo em 6 dias
```

O que importa:

- **A linha de cobertura é o recurso principal**, não as barras. Barras respondem
  "quem está fora"; a linha responde **"a operação aguenta?"**. É a diferença entre
  um calendário e uma ferramenta de gestão.
- Cobertura mínima é configurável **por turno** e por setor/equipe. Dias abaixo do
  mínimo ficam destacados e viram pendência no dashboard.
- Agrupamento por setor, colapsável, com contagem.
- Primeira coluna fixa (sticky). Scroll horizontal por tempo, vertical por pessoa.
- Zoom: dia / semana / mês. Em mês, a barra vira bloco agregado.
- Legenda por padrão visual **além de cor** (hachura, borda) — daltonismo é comum e
  esta tela é toda codificada por cor.
- Clique na barra abre um popover (CDK Overlay) com detalhe e ações.

Implementação (vale para as duas telas): CSS Grid com colunas de largura fixa por
dia, primeira coluna sticky, linhas virtualizadas (CDK `VirtualScrollViewport`)
acima de ~50 pessoas. Sem biblioteca de gantt e sem scheduler pronto — nenhum
entrega a linha de cobertura, que é justamente o que vende as duas telas.

---

## 4. "Meu dia" (funcionário)

Móvel primeiro. É a tela que o funcionário abre no celular, e a única que ele abre.
No segmento escolhido, ela gira em torno do **turno**, não de uma lista de tarefas.

```
Bom dia, Carlos.

SEU TURNO HOJE · quinta, 14 de agosto
  ▸ Turno B · 19:00 – 07:00 · noturno
    Próximo turno: sábado, 16/08

  ☑  Conferir material
  ☐  Instalar luminárias · Loja 3
  ☐  Enviar fotos da instalação

PENDÊNCIAS SUAS
  📄  Seu NR-10 vence em 12 dias        [Enviar novo]
  🏖  Você tem 18 dias de férias        [Solicitar]
  🔄  Troca com Pedro em 20/08          [Ver]
```

- O turno vem primeiro e em destaque. "Trabalho hoje? Que horas? Quando é o
  próximo?" são as três perguntas reais do funcionário de plantão.
- Concluir tarefa é um toque na checkbox. Sem tela de detalhe no caminho feliz.
- Dia de folga mostra isso com clareza, e o próximo turno logo abaixo.
- Sem gráfico, sem métrica, sem percentual da própria produtividade.

---

## 5. "Minha equipe"

A resposta rápida para "como está o time hoje".

```
Carlos Mendes    Turno B 19–07   3/4 concluídas   62% capacidade   🟢
Marcos Alves     Turno A 07–19   5/5 concluídas   48% capacidade   🟢
João Silva       Turno A 07–19   1/3 concluídas   95% capacidade   🔴 sobrecarga
Ana Lima         —                —                —               🏖 férias até 20/09
Pedro Alves      folga            —                —               ⚪ próximo: 16/08
```

- O vermelho do João é sobre **carga**, não sobre desempenho. O texto de apoio deve
  ser "sobrecarregado", nunca "abaixo da meta". A escolha de palavra aqui é decisão
  de produto, não de copy.
- Ação direta: arrastar/atribuir tarefa de um para outro a partir desta tela.

---

## 6. Fila de aprovações

Uma fila, todos os tipos, resolvível com teclado.

```
[ Todas ]  Férias 2   Documentos 5   Ausências 1   Trocas 3

☐  🏖  Ana Lima · Férias 10/09–20/09 · 11 dias
       ⚠ Turno B fica com 1 pessoa (mín. 2) em 6 dias    [Aprovar] [Recusar] [Detalhe]

☐  🔄  Pedro ⇄ Carlos · plantão de 20/08 · turno B
       ✓ Interjornada ok · cobertura mantida             [Aprovar] [Recusar] [Detalhe]

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
