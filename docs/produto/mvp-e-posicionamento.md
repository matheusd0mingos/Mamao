# Posicionamento e MVP

Este documento discorda do briefing em três pontos. Os motivos estão explícitos
para que você possa rejeitar com conhecimento de causa.

**Posicionamento:** *Mamão — gestão sem complicação.*
**Segmento inicial:** operação com plantão, turno e rodízio, de 15 a 60
funcionários ([ICP e pilotos](icp-e-pilotos.md)).

O segmento não é detalhe de marketing: ele reordenou a V1 e trouxe Escalas para
dentro dela ([ADR-0015](../adr/0015-regras-de-jornada-e-escala.md)).

---

## O problema que realmente se vende

Uma empresa de 20 pessoas não compra software para "organizar tarefas". Ela já
tem WhatsApp, planilha e Trello, e acha que está resolvido. Ela compra quando
existe uma **dor com consequência**:

| Dor | Consequência | Hoje é resolvido com |
|---|---|---|
| NR-35 do eletricista venceu e ninguém viu | Multa, embargo, funcionário parado, risco em auditoria | Ninguém. Descobre-se na fiscalização |
| Férias vencendo o período concessivo | Pagamento em dobro | Contador avisa tarde, por e-mail |
| Três pessoas de férias na mesma semana | Operação para | Memória do gestor |
| Documento do funcionário sumiu no WhatsApp | Retrabalho, atraso na admissão | Pasta no Drive que ninguém mantém |
| Escala do plantão furada | Cliente sem atendimento **hoje** | Planilha que só uma pessoa entende |
| Interjornada de 11h violada numa dobra | Passivo trabalhista | Ninguém confere |
| "Quem está disponível hoje?" | Trabalho mal distribuído | Gestor perguntando no grupo |

Nenhuma dessas dores é resolvida por um Kanban. Todas são resolvidas pelo Mamão.
**Esse é o produto vendável.**

As duas linhas de escala são as mais agudas no segmento escolhido — e as que a
planilha é estruturalmente incapaz de resolver, porque planilha não valida regra.

O Kanban é o que faz o produto ser *usado todo dia*, não o que faz ser *comprado*.
Essa distinção define o MVP.

---

## <a name="p2"></a>P2 — Kanban sai da V1

**O que o briefing propõe:** Kanban completo na V1 (checklist, recorrência,
dependências, anexos, comentários, evidência de conclusão, estimativa).

**Recomendação:** V1 entrega uma **lista de tarefas mínima**; o Kanban completo
vem na V1.5.

Motivos:

1. **Você perde a comparação de features.** O comprador vai abrir o Trello ao
   lado. Kanban é a única parte do Mamão onde existe um concorrente gratuito,
   maduro e conhecido. Competir ali com 20% das features enfraquece a demo inteira.
2. **É o módulo mais caro da V1.** Dependências, recorrência, anexos e comentários
   representam facilmente 30–40% do esforço de backend e frontend da V1, para a
   parte menos diferenciada do produto.
3. **A integração é o que importa, não o quadro.** O valor de tarefa no Mamão é
   "João entra de férias → estas 5 tarefas ficam órfãs → sugerir quem assume".
   Isso funciona com uma lista simples. Não precisa de swimlane.

O que fica na V1 (tarefa mínima):

```
título · descrição · responsável · prazo · prioridade
estimativa de horas (opcional) · checklist · status (A fazer / Fazendo / Concluída)
```

O que vai para a V1.5: quadro com colunas arrastáveis, recorrência, dependências,
anexos, comentários, evidência de conclusão.

**Compensação obrigatória:** a tela **"Meu dia"** (do funcionário) e **"Minha
equipe"** (do gestor) entram na V1. São elas que criam o hábito diário, não o
quadro. A lista mínima alimenta as duas.

**O segmento reforça esta decisão.** Numa empresa de plantão, o funcionário não
organiza o dia num quadro — ele organiza em torno do turno. "Meu dia" passa a ser
*qual meu turno hoje, o que preciso fazer nele, e o que está pendente comigo*.
Isso reduz `Work` ainda mais na V1 e libera espaço para Escalas.

---

## <a name="p1"></a>P1 — O produto tem que funcionar com um usuário logado

Esta é a decisão de produto mais importante do documento.

**Armadilha:** projetar o Mamão assumindo que os 20 funcionários vão criar conta,
fazer login e enviar seus próprios documentos. Na prática, num trial de 14 dias,
o gestor não consegue mobilizar 20 pessoas. Ele desiste antes de ver valor, e a
conclusão dele é "o sistema não funciona".

**Regra de projeto:**

> Toda funcionalidade da V1 deve entregar valor com **exatamente uma pessoa
> logada** — o gestor ou o RH.

Consequências concretas:

- Funcionário é um **registro**, não um usuário. Existe sem conta, sem e-mail, sem senha.
- O gestor pode: cadastrar, subir documento *pelo* funcionário, lançar férias, marcar
  ausência, criar e concluir tarefa, montar escala. Tudo sozinho.
- Login de funcionário é **convite opcional**, enviado depois, por link (e-mail ou
  WhatsApp), com autoatendimento: ver o próprio dia, subir documento, pedir férias.
- Isso muda o modelo de dados: `Employee` **não** tem `UserId` obrigatório.
  `Employee.UserId` é nullable e opcional para sempre. Ver
  [ADR-0006](../adr/0006-identidade.md).
- Isso também muda o preço: cobrar por **funcionário ativo cadastrado**, não por
  usuário com login.

Convites em massa entram na V1.5, quando o gestor já viu valor e tem motivo para
convidar a equipe.

---

## <a name="p3"></a>P3 — Importação CSV é feature de V1

Cadastrar 30 funcionários à mão num formulário de 15 campos é onde o trial morre.

V1 precisa de: upload de CSV/XLSX → mapeamento de colunas na tela → pré-visualização
com erros por linha → importação. Aceitar arquivo sujo (colunas em qualquer ordem,
CPF com e sem máscara, data em vários formatos, linhas em branco).

Meta de ativação: **do cadastro da empresa ao dashboard com dados reais em menos
de 15 minutos.**

Corolário: o formulário de funcionário na V1 pede **o mínimo** (nome, cargo, setor,
data de admissão). Todo o resto — formação, competências, certificações — é
progressivo, preenchido depois. Formulário longo na primeira tela é abandono.

---

## MVP recomendado

### V1 — o que se vende

| Módulo | Escopo |
|---|---|
| **Pessoas** | Cadastro mínimo, setores/equipes/funções, gestor, jornada contratada, importação CSV, perfil |
| **Documentos** | Tipos configuráveis **pré-populados para o segmento**, upload, validade, aprovação, alerta de vencimento, painel de faltantes |
| **Ausências** | Presencial / home office / falta / justificada / licenças / folga. Alimenta disponibilidade |
| **Escalas** | Turnos, ciclos (12×36, 5×2, 6×1), geração recorrente, grade editável, troca/substituição, cobertura por turno, validação de jornada em alerta |
| **Férias** | Solicitação, saldo CLT, conflito e **cobertura por turno**, aprovação, timeline |
| **Tarefas (mínimo)** | Lista, responsável, prazo, checklist, estimativa opcional, carga de trabalho |
| **Dashboard do gestor** | Pendências acionáveis + equipe hoje + trabalho. Telas "Meu dia" e "Minha equipe" |
| **Aprovações** | Fila única: férias, documentos, ausências, trocas de plantão. Resolver sem sair da lista |
| **Notificações** | In-app + e-mail. Digest diário para o gestor |
| **Permissões** | Papéis + escopo de dados |
| **Auditoria** | Registro imutável das ações sensíveis |

Escalas **antes** de Férias, porque a cobertura por turno é pré-requisito da
detecção de conflito de férias.

### V1.5 — o que retém

Kanban completo · convites em massa e autoatendimento do funcionário ·
onboarding por workflow · calendário unificado · recorrência de tarefas ·
competências ("quem pode cobrir este plantão?")

### V2 — o que expande

Competências e "quem está habilitado?" · treinamentos · relatórios · automações ·
integração com WhatsApp

### V3 — o que diferencia no longo prazo

Geração automática de escala com restrições · sugestão de redistribuição na entrada
de férias · previsão de sobrecarga · integrações · ponto

---

## Ordem de construção dentro da V1

Não construa por módulo. Construa por **fluxo demonstrável**, porque cada etapa
abaixo é uma demo que já vende alguma coisa:

1. **Esqueleto vertical:** empresa + login + 1 funcionário + deploy + CI verde
2. **Pessoas + CSV:** a base real do cliente entra no sistema
3. **Documentos + validade + alerta:** primeira dor com consequência resolvida
4. **Ausências + disponibilidade:** "quem está disponível hoje" passa a existir
5. **Escalas + cobertura + validação de jornada:** o marco que define o segmento
6. **Férias + conflito por turno + timeline:** a tela que impressiona na demo
7. **Tarefas mínimas + "Meu dia":** hábito diário, centrado no turno
8. **Dashboard + aprovações + notificações:** amarra tudo e fecha a narrativa

Ver [roadmap](../roadmap.md).

---

## <a name="preco"></a>Precificação (provisória)

Modelo: **por funcionário ativo por mês (PEPM)**, com mínimo mensal.

Por que PEPM: é o padrão da categoria (o comprador entende sem explicação), escala
junto com o valor entregue, e evita a discussão de "quantos logins eu preciso" —
que, dado P1, seria a métrica errada.

Estrutura sugerida para validar:

- Mínimo mensal equivalente a ~10 funcionários (protege contra a empresa de 6 pessoas
  que dá o mesmo trabalho de suporte que a de 30)
- Um único plano na V1. **Não crie tiers antes de ter 10 clientes** — você ainda não
  sabe qual feature separa o plano barato do caro, e errar isso trava o roadmap
- Trial de 14 dias sem cartão, com importação CSV assistida por você nas primeiras contas

Faixa: valide contra o que o cliente já paga por sistema de ponto ou honorário
contábil — é esse o bolso mental, não o de software de gestão.

Com três pilotos na mão, a calibragem tem fonte melhor que o instinto: a pergunta
6 do [roteiro](icp-e-pilotos.md#antes-de-escrever-mais-código) — *"o que você já
paga hoje para resolver isso?"* — é a que quase nunca se faz e a que mais informa
o preço.

---

## O que explicitamente não fazer

| Não fazer | Motivo |
|---|---|
| Folha de pagamento | Escopo regulatório enorme, concorrentes consolidados |
| Ponto eletrônico com valor jurídico | Portaria 671, certificação de equipamento, responsabilidade legal |
| Banco de horas e apuração de extras/adicional noturno | Porta de entrada para ponto. O Mamão **planeja** a jornada; não **apura** |
| eSocial | Complexidade desproporcional; integre depois, não implemente |
| Chat interno | Você mesmo disse: não é Slack. E não se ganha desse mercado |
| App mobile nativo na V1 | PWA responsivo resolve. Nativo é um segundo produto para manter |
| Marketplace/integrações na V1 | Sem base instalada, ninguém integra |
