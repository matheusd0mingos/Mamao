# Roadmap até o primeiro cliente pagante

Premissa: um desenvolvedor, tempo parcial ou integral. As faixas de esforço são
relativas — o que importa é a **ordem** e o critério de pronto de cada etapa.

Princípio: **cada marco termina em algo demonstrável e implantado**. Nada de dois
meses de infraestrutura antes da primeira tela.

---

## Marco 0 — Esqueleto vertical (1 a 2 semanas)

O objetivo não é código bonito. É fechar o circuito inteiro, de ponta a ponta, com
o mínimo possível dentro.

- [ ] Solução .NET 10 com a estrutura de módulos ([visão geral](arquitetura/visao-geral.md))
- [ ] `Mamao.AppHost` (Aspire) subindo Postgres + API + Worker + Angular
- [ ] `Identity`: cadastro de empresa, primeiro usuário, login, JWT com `tenant_id`
- [ ] `People`: `Employee` com 5 campos, CRUD, filtro de tenant, RLS
- [ ] Angular: shell, sidebar, login, lista e formulário de funcionário
- [ ] OpenAPI → cliente TypeScript gerado
- [ ] Outbox + `OutboxPublisher` publicando um evento de verdade (`EmployeeHired`)
- [ ] CI: build, testes, imagem
- [ ] **Deploy no VPS**: Caddy + compose + domínio + TLS
- [ ] `/healthz`, `/healthz/ready`, OTel exportando
- [ ] **Backup diário + restore testado**
- [ ] Teste de integração de vazamento cross-tenant

**Pronto quando:** você cria uma empresa em produção, faz login, cadastra um
funcionário, e o trace aparece no coletor.

Este marco parece caro para o que entrega. Ele existe para que nenhum marco
seguinte precise mexer em infraestrutura — e para que o primeiro deploy aconteça na
semana 2 e não no mês 4, quando ele vira um projeto próprio.

---

## Marco 1 — Pessoas de verdade (1 a 2 semanas)

- [ ] Setores em árvore, equipes, cargos, gestor
- [ ] `EmploymentContract` com jornada semanal
- [ ] **Importação CSV/XLSX** com mapeamento de coluna, pré-visualização e erro por linha
- [ ] Perfil do funcionário
- [ ] Papéis e permissões ([ADR-0007](adr/0007-autorizacao.md))
- [ ] Auditoria gravando

**Pronto quando:** o cadastro real de uma empresa piloto entra em menos de 15
minutos.

---

## Marco 2 — Documentos (2 semanas)

O primeiro marco que resolve uma dor com consequência.

- [ ] Tipos de documento configuráveis por tenant
- [ ] `DocumentRequirement`: o que é exigido de quem (por cargo, setor ou todos)
- [ ] Upload com `IFileStorage` local + URL assinada ([ADR-0010](adr/0010-armazenamento-de-arquivos.md))
- [ ] Validade, aprovação, recusa com motivo
- [ ] Job diário de vencimento → `DocumentExpiring`
- [ ] Painel: vencidos / vencendo em 30 dias / válidos / faltantes
- [ ] Notificação por e-mail

**Pronto quando:** o piloto sobe os documentos reais da equipe e o sistema avisa um
vencimento antes de alguém perceber. **Este é o primeiro momento de venda.**

---

## Marco 3 — Ausências e disponibilidade (1 a 2 semanas)

- [ ] `AbsenceType` configurável; registro de ausência e presença
- [ ] Feriados nacionais + municipais
- [ ] **`IAvailabilityQuery`** ([modelo de domínio](produto/modelo-de-dominio.md#disponibilidade))
- [ ] Dashboard: bloco "sua equipe hoje"

**Pronto quando:** "quem está disponível hoje?" tem resposta única em todas as
telas.

---

## Marco 4 — Férias (2 a 3 semanas)

O marco que impressiona na demo.

- [ ] `VacationEntitlement` com regras CLT ([ADR-0014](adr/0014-regras-clt-de-ferias.md))
- [ ] Solicitação com fracionamento e abono; validação das regras
- [ ] Detecção de conflito no setor/equipe
- [ ] Aprovação/recusa com histórico e evento
- [ ] **Timeline com linha de cobertura** ([UX](produto/ux-telas-criticas.md))
- [ ] Alerta de período concessivo vencendo
- [ ] Job de geração de períodos aquisitivos

**Pronto quando:** o gestor vê que três pessoas do mesmo setor pediram a mesma
semana — **antes** de aprovar.

---

## Marco 5 — Tarefas mínimas e carga (2 semanas)

- [ ] `TaskItem`: título, responsável, prazo, prioridade, estimativa, checklist, status
- [ ] Lista por pessoa, por equipe e por status
- [ ] Cálculo de carga e capacidade ([ADR-0013](adr/0013-capacidade-sem-vigilancia.md))
- [ ] Tela "Meu dia" (mobile, PWA)
- [ ] Tela "Minha equipe"
- [ ] `VacationApproved` marcando tarefas em risco

**Pronto quando:** aprovar férias mostra quais tarefas ficam órfãs e quem tem
capacidade para assumir.

---

## Marco 6 — Amarração (1 a 2 semanas)

- [ ] Dashboard completo, com pendências no topo
- [ ] Fila única de aprovações, com ação inline e navegação por teclado
- [ ] Central de notificações in-app + digest diário por e-mail
- [ ] Preferências de notificação
- [ ] Vazios úteis, estados de carregamento, tratamento de erro em todas as telas
- [ ] Onboarding da conta: primeiros passos guiados

**Pronto quando:** um gestor abre o Mamão de manhã, resolve tudo que precisa e
fecha. É a promessa do produto cumprida.

---

## Marco 7 — Pronto para vender (1 a 2 semanas)

- [ ] Checklist de segurança completo ([segurança](arquitetura/multi-tenancy-e-seguranca.md#6-checklist-de-segurança-antes-do-primeiro-cliente))
- [ ] Cobrança (assinatura, trial, limite de funcionários)
- [ ] Política de privacidade, termos de uso, minuta de DPA
- [ ] Landing page com preço e cadastro self-service
- [ ] Rotina de suporte: onde o cliente pede ajuda e como você é avisado
- [ ] Runbook: restaurar backup, subir versão, fazer rollback, investigar erro de cliente

---

## Depois — V1.5 e V2

Ordem sugerida, a ser revista com o feedback dos pilotos:

1. **Escalas** — se o segmento inicial for operação com plantão, sobe para antes do Marco 5
2. Kanban completo (colunas, drag & drop, recorrência, dependências, anexos, comentários)
3. Convites e autoatendimento do funcionário
4. Onboarding por workflow
5. Calendário unificado
6. Competências e "quem está habilitado?"
7. Relatórios e exportações
8. WhatsApp

---

## Regras de execução

1. **Nada de infraestrutura fora do Marco 0.** Se um marco exigir infra nova, ela é
   parte do marco, não um marco próprio.
2. **Deploy a cada marco.** O piloto usa o que existe.
3. **Duas a três empresas piloto desde o Marco 2.** Sem usuário real, a ordem acima é
   chute informado — e chute informado continua sendo chute.
4. **Corte escopo, não qualidade.** Ao atrasar, remova campos e telas; não remova
   teste, filtro de tenant, tratamento de erro ou backup.
5. **Um marco não termina com dívida de segurança.** Todo o resto pode esperar.
