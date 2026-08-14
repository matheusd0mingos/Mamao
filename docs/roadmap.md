# Roadmap até o primeiro cliente pagante

**Segmento definido:** operação com plantão, turno e rodízio
([ICP e pilotos](produto/icp-e-pilotos.md)). Isso trouxe Escalas para dentro da V1
([ADR-0015](adr/0015-regras-de-jornada-e-escala.md)) e reordenou os marcos.

Premissa: um desenvolvedor. As faixas de esforço são relativas — o que importa é a
**ordem** e o critério de pronto de cada marco.

Princípio: **cada marco termina em algo demonstrável e implantado.** Nada de dois
meses de infraestrutura antes da primeira tela.

---

## Marco 0 — Esqueleto vertical (1 a 2 semanas)

O objetivo não é código bonito. É fechar o circuito inteiro com o mínimo dentro.

- [ ] Solução .NET 10 com a estrutura de módulos ([visão geral](arquitetura/visao-geral.md))
- [ ] `Mamao.AppHost` (Aspire) subindo Postgres + API + Worker + Angular
- [ ] `Identity`: cadastro de empresa, primeiro usuário, login, JWT com `tenant_id`
- [ ] `People`: `Employee` com 5 campos, CRUD, filtro de tenant, RLS
- [ ] Angular: shell, sidebar, login, lista e formulário de funcionário
- [ ] OpenAPI → cliente TypeScript gerado
- [ ] Outbox + `OutboxPublisher` publicando um evento de verdade (`EmployeeHired`)
- [ ] CI: build, testes, imagem
- [ ] **Deploy no VPS**: Caddy + compose + `mamao.tech` + TLS (`deploy/deploy.sh` pronto, falta executar)
- [ ] `/healthz`, `/healthz/ready`, OTel exportando
- [ ] **Backup diário + restore testado**
- [ ] Teste de integração de vazamento cross-tenant

**Pronto quando:** você cria uma empresa em produção, faz login, cadastra um
funcionário, e o trace aparece no coletor.

Este marco parece caro para o que entrega. Ele existe para que nenhum marco seguinte
precise mexer em infraestrutura — e para que o primeiro deploy aconteça na semana 2,
não no mês 4, quando vira um projeto próprio.

> **Em paralelo, antes de escrever a primeira tela de negócio:** as três conversas
> com os pilotos e a coleta das planilhas de escala
> ([plano dos pilotos](produto/icp-e-pilotos.md#plano-dos-pilotos)). As planilhas são
> o requisito real do Marco 4.

---

## Marco 1 — Pessoas de verdade (1 a 2 semanas)

- [ ] Setores em árvore, equipes, cargos, gestor
- [ ] `EmploymentContract`: tipo de jornada (12×36, 5×2, 6×1, ADM), carga semanal,
      registro de acordo de compensação
- [ ] **Importação CSV/XLSX** com mapeamento de coluna, pré-visualização e erro por linha
- [ ] Perfil do funcionário
- [ ] Papéis e permissões ([ADR-0007](adr/0007-autorizacao.md))
- [ ] Auditoria gravando

**Pronto quando:** o cadastro real de um piloto entra em menos de 15 minutos.

---

## Marco 2 — Documentos (2 semanas)

Primeiro marco que resolve dor com consequência, e o mais barato de todos por
unidade de valor.

- [ ] Tipos de documento configuráveis, **pré-populados com o catálogo do segmento**
      ([ICP](produto/icp-e-pilotos.md#catálogo-de-documentos-pré-configurado))
- [ ] `DocumentRequirement`: o que é exigido de quem (por cargo, setor ou todos)
- [ ] Upload com `IFileStorage` local + URL assinada ([ADR-0010](adr/0010-armazenamento-de-arquivos.md))
- [ ] Validade, aprovação, recusa com motivo
- [ ] Job diário de vencimento → `DocumentExpiring`
- [ ] Painel: vencidos / vencendo em 30 dias / válidos / **faltantes**
- [ ] Notificação por e-mail

**Pronto quando:** o piloto sobe os documentos reais e o sistema avisa um vencimento
de NR antes de alguém perceber. **Primeiro momento de venda.**

---

## Marco 3 — Ausências e disponibilidade (1 a 2 semanas)

- [ ] `AbsenceType` configurável; registro de ausência e presença
- [ ] Feriados nacionais + municipais
- [ ] **`IAvailabilityQuery`** ([modelo de domínio](produto/modelo-de-dominio.md#disponibilidade)),
      já com `OffShift` previsto
- [ ] Dashboard: bloco "sua equipe hoje"

**Pronto quando:** "quem está disponível hoje?" tem resposta única em todas as telas.

---

## Marco 4 — Escalas (3 semanas)

O marco que define o segmento. Entrada mais cara da V1 e a que decide a venda.

- [ ] `ShiftTemplate` (A/B/ADM, com horário e intervalo)
- [ ] `ScheduleCycle`: 12×36, 5×2, 6×1, semanal fixo
- [ ] Geração da escala do período a partir do ciclo
- [ ] **Grade editável** (`TimeGrid`: linhas = pessoas, colunas = dias)
- [ ] Troca e substituição de plantão, com aprovação
- [ ] Cobertura mínima por turno, por setor/equipe
- [ ] **Validação em modo alerta**: interjornada de 11h, DSR semanal, extras,
      acordo 12×36 registrado ([ADR-0015](adr/0015-regras-de-jornada-e-escala.md))
- [ ] Escala do mês exportável em PDF (o coordenador vai imprimir e colar na parede —
      não lute contra isso)
- [ ] `ScheduleChanged` alimentando disponibilidade e notificações

**Pronto quando:** um piloto monta a escala do mês seguinte no Mamão em vez da
planilha, e o sistema aponta uma violação de interjornada que a planilha não via.

---

## Marco 5 — Férias (2 a 3 semanas)

- [ ] `VacationEntitlement` com regras CLT ([ADR-0014](adr/0014-regras-clt-de-ferias.md))
- [ ] Solicitação com fracionamento e abono; validação das regras
- [ ] **Conflito e cobertura por turno**, não por dia
- [ ] Aprovação/recusa com histórico e evento
- [ ] Timeline reutilizando o `TimeGrid` do Marco 4
- [ ] Alerta de período concessivo vencendo
- [ ] Job de geração de períodos aquisitivos

**Pronto quando:** o gestor vê que aprovar as férias do Carlos deixa o turno noturno
com uma pessoa — **antes** de aprovar.

---

## Marco 6 — Tarefas mínimas e "Meu dia" (1 a 2 semanas)

Escopo reduzido de propósito: neste segmento o dia gira em torno do turno.

- [ ] `TaskItem`: título, responsável, prazo, prioridade, estimativa, checklist, status
- [ ] Lista por pessoa, por equipe e por status
- [ ] Carga e capacidade a partir das horas de turno ([ADR-0013](adr/0013-capacidade-sem-vigilancia.md))
- [ ] **"Meu dia" (PWA)**: seu turno hoje + suas tarefas + suas pendências
- [ ] Tela "Minha equipe"
- [ ] `VacationApproved` marcando tarefas em risco

**Pronto quando:** o funcionário de campo abre no celular e vê o turno e o que fazer.

---

## Marco 7 — Amarração (1 a 2 semanas)

- [ ] Dashboard completo, com pendências no topo
- [ ] Fila única de aprovações (férias, documentos, ausências, **trocas de plantão**),
      com ação inline e navegação por teclado
- [ ] Central de notificações in-app + digest diário
- [ ] Preferências de notificação
- [ ] Vazios úteis, carregamento e erro tratados em todas as telas
- [ ] Onboarding da conta: primeiros passos guiados

**Pronto quando:** o coordenador abre de manhã, resolve tudo e fecha.

---

## Marco 8 — Pronto para vender (1 a 2 semanas)

- [ ] Checklist de segurança completo ([segurança](arquitetura/multi-tenancy-e-seguranca.md#6-checklist-de-segurança-antes-do-primeiro-cliente))
- [ ] Cobrança (assinatura, trial, limite de funcionários)
- [ ] Política de privacidade, termos de uso, minuta de DPA
- [ ] Landing page em `mamao.tech` com preço e cadastro self-service
- [ ] Rotina de suporte
- [ ] Runbook: restaurar backup, subir versão, rollback, investigar erro de cliente

---

## <a name="o-que-foi-cortado-para-caber"></a>O que foi cortado para caber

Escalas na V1 custa cerca de 3 semanas. Compensações, sem tocar em qualidade:

| Corte | Economia | Justificativa |
|---|---|---|
| **`TimeGrid` construído uma vez** e reutilizado em escala e timeline de férias | ~1 semana | Mesma estrutura: linhas = pessoas, colunas = dias, célula variável. Construir duas vezes seria erro |
| Geração automática de escala com otimização → V3 | ~2 semanas | Ciclo recorrente + edição manual cobre 90% do uso e é o que a planilha já faz, só que validado |
| `Work` reduzido (sem quadro, recorrência, dependência, anexo, comentário) | ~1 semana | Reforço de [P2](produto/mvp-e-posicionamento.md#p2); no segmento, o turno importa mais que o quadro |
| Banco de horas e apuração de extras fora de escopo | — | Porta de entrada para ponto eletrônico, recusado explicitamente |
| Convites e autoatendimento do funcionário → V1.5 | ~1 semana | Consequência de [P1](produto/mvp-e-posicionamento.md#p1) |

Saldo: a V1 fica ~1 a 2 semanas mais longa que a versão sem escalas, e passa a
servir o segmento escolhido de verdade. É a troca certa.

---

## Depois — V1.5 e V2

1. Kanban completo (colunas, drag & drop, recorrência, dependências, anexos, comentários)
2. Convites e autoatendimento do funcionário
3. Onboarding por workflow
4. Calendário unificado
5. Competências e "quem está habilitado para cobrir este plantão?" — encaixe natural
   com escalas, e o caminho para a V3
6. Relatórios e exportações
7. WhatsApp (notificação de escala publicada e troca de plantão é o caso de uso óbvio
   neste segmento)

**V3:** geração automática de escala com restrições, sugestão de redistribuição,
previsão de sobrecarga, integrações, ponto.

---

## Regras de execução

1. **Nada de infraestrutura fora do Marco 0.** Se um marco exigir infra nova, ela é
   parte do marco.
2. **Deploy a cada marco.** O piloto usa o que existe.
3. **Piloto em produção a partir do Marco 2**, um de cada vez nas primeiras semanas.
4. **Corte escopo, não qualidade.** Ao atrasar, remova campos e telas; nunca teste,
   filtro de tenant, tratamento de erro ou backup.
5. **Um marco não termina com dívida de segurança.**
6. **Pedido de piloto não vira código automaticamente.** Registre, agrupe, e só
   implemente o que aparecer em mais de um — caso contrário você está fazendo
   consultoria, não produto.
