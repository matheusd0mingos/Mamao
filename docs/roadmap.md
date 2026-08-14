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

- [x] Solução .NET 10 com a estrutura de módulos ([visão geral](arquitetura/visao-geral.md))
- [x] `Mamao.AppHost` (Aspire) subindo Postgres + API + Worker
      (Angular fora: o hosting de Node ainda não acompanha esta versão do Aspire —
      `npm start` com proxy resolve, ver [ADR-0011](adr/0011-aspire-e-deploy.md))
- [x] `Identity`: cadastro de empresa, primeiro usuário, login, JWT com `tenant_id`
- [x] `People`: `Employee` com 5 campos, CRUD, filtro de tenant, **RLS verificada**
- [x] Angular: shell, sidebar, login, lista e formulário de funcionário
- [x] OpenAPI → cliente TypeScript gerado (CI quebra se o contrato sair de dia)
- [x] Outbox + `OutboxPublisher` publicando um evento de verdade (`EmployeeHired`)
- [ ] CI: build ✅, testes ✅, **imagem ainda não** — não adianta publicar imagem antes de
      existir servidor que a consuma; entra junto com o deploy
- [ ] **Deploy no VPS**: Caddy + compose + `mamao.tech` + TLS (`deploy/deploy.sh` pronto, falta executar)
- [x] `/healthz`, `/healthz/ready`, OTel exportando
- [ ] **Backup diário + restore testado**
- [x] Teste de integração de vazamento cross-tenant (roda no CI, com Postgres de verdade)

**Pronto quando:** você cria uma empresa em produção, faz login, cadastra um
funcionário, e o trace aparece no coletor.

> **Situação:** o circuito inteiro está fechado e verificado **localmente** — empresa,
> login, funcionário, RLS provada contra banco real, evento na outbox consumido pelo
> Worker. Falta a palavra **produção**: os três itens abertos (imagem, deploy, backup)
> dependem de um servidor existir, e é a única coisa que trava o marco.

Este marco parece caro para o que entrega. Ele existe para que nenhum marco seguinte
precise mexer em infraestrutura — e para que o primeiro deploy aconteça na semana 2,
não no mês 4, quando vira um projeto próprio.

> **Em paralelo, antes de escrever a primeira tela de negócio:** as três conversas
> com os pilotos e a coleta das planilhas de escala
> ([plano dos pilotos](produto/icp-e-pilotos.md#plano-dos-pilotos)). As planilhas são
> o requisito real do Marco 4.

---

## Marco 1 — Pessoas de verdade (1 a 2 semanas)

- [x] **Setores em árvore** (caminho materializado: "tudo abaixo de Operações" em uma
      consulta) e **cargos** como entidade. Equipes e gestor: parcial — `ManagerId` existe
      no modelo, falta a tela
- [x] `EmploymentContract`: **regime de vínculo** (CLT, estatutário, militar, outro —
      [ADR-0017](adr/0017-regime-de-vinculo.md)), tipo de jornada (incluindo **rodízio**),
      carga semanal, acordo de compensação, e os primeiros **alertas em modo alerta**
      — condicionados ao regime, nunca bloqueando
- [ ] `Department` → **`OrgUnit` com `Kind`** (organização, setor, equipe), para o grupo
      com subordinadas caber na árvore que já existe ([ADR-0018](adr/0018-organizacoes-e-unidades.md))
- [x] **Importação CSV** com mapeamento de coluna, pré-visualização e erro por linha
      (XLSX fica para depois: exige biblioteca nova e o Excel exporta CSV em dois cliques —
      a tela ensina como)
- [ ] Perfil do funcionário
- [x] Papéis e permissões ([ADR-0007](adr/0007-autorizacao.md)) — antecipado no Marco 0:
      `Owner`/`Hr`/`Manager`/`Employee`, policies geradas de `Permissions.All`.
      Falta a **tela** de gerenciar quem tem qual papel
- [ ] Auditoria gravando
- [x] **`User` pertence à empresa** ([ADR-0020](adr/0020-usuario-pertence-a-empresa.md)):
      `Membership` removida, tela de escolher empresa removida
- [x] **Convite de acesso**: RH importa a planilha e convida as **pessoas-chave**
      (coordenador, supervisor, auxiliar de RH). Token com prazo, uso único, e-mail faltante
      preenchido no próprio convite. Tela de acessos + página pública de aceite

**Pronto quando:** o cadastro real de um piloto entra em menos de 15 minutos, e as
pessoas-chave dele conseguem entrar sem você no meio.

> **Onde P1 continua valendo:** convidar 3 pessoas-chave é ativação; convidar os 40 é
> dependência. O produto tem que seguir 100% útil com o RH sozinho logado, mesmo que
> ninguém aceite convite nenhum.

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

> **Virou pré-requisito duro do Marco 4**, não paralelo: a proposta de rodízio que
> escala alguém de férias perde a confiança na primeira tentativa.

- [ ] `AbsenceType` configurável; registro de ausência e presença
- [ ] Feriados nacionais + municipais
- [ ] **`IAvailabilityQuery`** ([modelo de domínio](produto/modelo-de-dominio.md#disponibilidade)),
      já com `OffShift` previsto
- [ ] Dashboard: bloco "sua equipe hoje"

**Pronto quando:** "quem está disponível hoje?" tem resposta única em todas as telas.

---

## Marco 4 — Escalas por rodízio (2 semanas)

O marco que define o segmento. **Redesenhado** — ver [ADR-0019](adr/0019-escala-por-rodizio.md).

- [ ] `Duty` + `DutyRequirement`: o serviço, quantas pessoas, mínimos por cargo/posto
- [ ] `RotationLedger`: quantas vezes cada um serviu e quando foi a última —
      **é o que separa rodízio de sorteio**
- [ ] Proposta ordenada por disponibilidade → há quanto tempo não serve → quantas vezes
      → desempate **estável** (nunca aleatório)
- [ ] **Justificativa por nome, guardada e não recalculada**: "última vez 12/03, 2 vezes
      no trimestre". A escala de março tem que continuar explicável em junho
- [ ] Ajuste manual com motivo; quem sai fica devendo uma
- [ ] `Position.Precedence` para posto e graduação (comandante do serviço, ordem de leitura)
- [ ] `ScheduleCycle` (12×36, 5×2, 6×1) como **modo alternativo**, para o cliente CLT de turno
- [ ] Validação de jornada condicionada ao regime ([ADR-0017](adr/0017-regime-de-vinculo.md))
- [ ] Escala publicada em PDF (vai ser impressa e afixada — não lute contra isso)
- [ ] `ScheduleChanged` alimentando disponibilidade e notificações

**Pronto quando:** o responsável monta a escala do próximo serviço no Mamão em vez da
planilha, e consegue responder "por que o Silva e não eu?" sem abrir outra tela.

---

## Marco 5 — Férias (2 a 3 semanas)

- [ ] `VacationEntitlement` com política **por regime** — CLT, 8.112 e militar têm
      padrões diferentes ([ADR-0017](adr/0017-regime-de-vinculo.md))
- [ ] Solicitação com fracionamento e abono; validação das regras
- [ ] **Divisões válidas calculadas do saldo real**, não menu fixo — quem tem 18 dias
      de direito não consegue fracionar, e o saldo muda com faltas e abono
      ([ADR-0014](adr/0014-regras-clt-de-ferias.md#fracionamento))
- [ ] `VacationPolicy` por tenant (nº de períodos e mínimos), **padrão = letra da CLT**;
      empresa que pratica 3×10 ajusta o mínimo e segue com a validação ligada
- [ ] **`VacationRequest.RequestedBy` desde o primeiro commit** — o gestor lança pelo
      funcionário na V1, o próprio funcionário propõe na V1.5, **pelo mesmo fluxo**
      ([P1](produto/mvp-e-posicionamento.md#p1))
- [ ] **Conflito e cobertura por turno**, não por dia
- [ ] Aprovação/recusa com histórico e evento
- [ ] Timeline reutilizando o `TimeGrid` do Marco 4
- [ ] Alerta de período concessivo vencendo
- [ ] Job de geração de períodos aquisitivos
- [ ] **Aprovar gera duas pendências com prazo, não um aviso**: comunicar ao
      funcionário (30 dias, art. 135) e pagar (2 dias antes do início, art. 145).
      O RH é dono da segunda ([ADR-0014](adr/0014-regras-clt-de-ferias.md#depois-da-aprovação))

**Pronto quando:** o gestor vê que aprovar as férias do Carlos deixa o turno noturno
com uma pessoa — **antes** de aprovar; e o RH vê a data-limite de pagamento sem
ninguém ter mandado mensagem para ninguém.

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
- [ ] **Inventário de dado pessoal** conferido contra o schema, e exclusão de conta
      testada ([inventário](arquitetura/multi-tenancy-e-seguranca.md#inventario))
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
| ~~Convites e autoatendimento do funcionário → V1.5~~ · **convite entra na V1**, autoatendimento fica | ~4 dias, não 1 semana | O convite é o caminho de ativação definido pelo autor: RH importa a planilha e convida as pessoas-chave. O que fica para a V1.5 é convidar o efetivo inteiro e as telas de autoatendimento — que é o que [P1](produto/mvp-e-posicionamento.md#p1) protege |

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
