# Sumário de decisões

Tabela única do que está decidido, com o motivo em uma linha e o link para o
detalhe. Se uma decisão não está aqui, ela não foi tomada.

Legenda de status: **Firme** (mudar custa caro) · **Provisória** (revisitar na data/gatilho indicado) · **Aberta** (precisa de sua decisão)

---

## Produto

| # | Decisão | Status | Motivo |
|---|---|---|---|
| P0 | **Segmento inicial: operação com plantão, turno e rodízio** (manutenção, segurança, saúde, campo, facilities), 15–60 funcionários | Firme | Dor com consequência imediata, planilha realmente não resolve, concorrência fraca nesse porte. [Detalhe](produto/icp-e-pilotos.md) |
| P1 | O produto precisa ser 100% útil com **um único usuário logado** (o gestor). Login do funcionário é opcional e convidado depois | Firme | Se a adoção depender de 20 pessoas criarem conta, o produto morre no trial. [Detalhe](produto/mvp-e-posicionamento.md#p1) |
| P2 | **Kanban sai da V1.** Entra uma lista de tarefas mínima (título, responsável, prazo, status, checklist) | Firme | Kanban compete com Trello/Asana e você perde no comparativo de features. E no segmento escolhido o dia gira em torno do turno, não do quadro. [Detalhe](produto/mvp-e-posicionamento.md#p2) |
| P2b | **Escalas entra na V1**, antes de Férias | Firme | Decorre de P0: para empresa de plantão, a escala é o sistema operacional. Custa ~3 semanas. [ADR-0015](adr/0015-regras-de-jornada-e-escala.md) |
| P3 | **Importação CSV de funcionários** é feature de V1, não de backlog | Firme | Sem ela ninguém chega ao "aha moment". É o gargalo de ativação. |
| P4 | Regras de **férias CLT** (período aquisitivo, saldo, fracionamento, restrição de início) no domínio desde a V1 | Firme | É o "por que não uma planilha". [ADR-0014](adr/0014-regras-clt-de-ferias.md) |
| P4c | Férias: **o modelo suporta o funcionário propor desde o primeiro commit** (`RequestedBy` ≠ funcionário); o que fica para a V1.5 é só o canal de acesso dele | Firme | Adiar o *conceito* obrigaria a reescrever máquina de estados e tela depois. Adiar o *canal* respeita P1 e custa uma coluna hoje. [Detalhe](produto/modelo-de-dominio.md#timeoff) |
| P4e | Limites de fracionamento são **parâmetro do tenant** (`VacationPolicy`), com padrão na letra da CLT | Firme | Empresa que pratica 3×10 ajusta o mínimo e mantém a validação ligada. Melhor que "ignorar a regra": política declarada num lugar auditável, e o resto da validação continua protegendo. [ADR-0014](adr/0014-regras-clt-de-ferias.md#fracionamento) |
| P4d | Aprovar férias abre **duas pendências com prazo** (comunicar em 30 dias, pagar até 2 dias antes), não uma mensagem ao RH | Firme | O custo de esquecer é pagamento em dobro. Mensagem se lê e some; pendência envelhece na fila até alguém resolver. [ADR-0014](adr/0014-regras-clt-de-ferias.md#depois-da-aprovação) |
| P4b | Regras de **jornada** (interjornada de 11h, DSR, 12×36, noturno) validadas na escala, **em modo alerta** | Firme | Mesma lógica de P4. Alerta e não bloqueio, porque a operação real tem exceção. [ADR-0015](adr/0015-regras-de-jornada-e-escala.md) |
| P5 | Capacidade é **prospectiva** (alocação futura), nunca retrospectiva (horas trabalhadas). Sem ranking entre pessoas | Firme | Sustenta o posicionamento anti-vigilância e evita virar ponto eletrônico. [ADR-0013](adr/0013-capacidade-sem-vigilancia.md) |
| P6 | **Disponibilidade** e **Pendências** são os dois conceitos que conectam os módulos. Com escalas, a escala vira a fonte primária das horas do dia | Firme | São a diferença entre "vários CRUDs" e "sistema integrado". [Detalhe](produto/modelo-de-dominio.md#disponibilidade) |
| P7 | Precificação por funcionário ativo/mês (PEPM), com mínimo mensal | Provisória | Padrão da categoria, escala junto com o valor entregue. Calibrar com a pergunta 6 do roteiro dos pilotos. [Detalhe](produto/mvp-e-posicionamento.md#preco) |
| P8 | Não fazer folha de pagamento, ponto eletrônico, banco de horas nem eSocial | Firme | Escopo regulatório enorme, concorrência estabelecida, mata o time-to-market. Vale também para apuração de extras e adicional noturno |
| P9 | Posicionamento é **"Mamão — gestão sem complicação"**. "Senta no Pudim" fora do material comercial e do produto | Firme | Confirmado pelo autor. [Detalhe](riscos-e-pontos-de-atencao.md#marca) |
| P10 | Domínio `mamao.tech` registrado | Firme | Registro de marca no INPI segue pendente — ver Q2 |
| P11 | Três empresas piloto confirmadas; roteiro de validação definido antes do Marco 1 | Firme | Coletar as planilhas de escala é o requisito real do Marco 4. [Plano](produto/icp-e-pilotos.md#plano-dos-pilotos) |

## Arquitetura — estrutura

| # | Decisão | Status | Motivo |
|---|---|---|---|
| A1 | **Modular monolith**, um único deployable de API + um worker | Firme | Microsserviço no MVP é custo puro sem benefício. [ADR-0001](adr/0001-modular-monolith.md) |
| A2 | Um banco PostgreSQL, **um schema por módulo**, um `DbContext` por módulo | Firme | Custo quase zero hoje, torna a extração futura mecânica e impede o atalho do JOIN entre módulos. [ADR-0002](adr/0002-schema-por-modulo.md) |
| A3 | Módulos: `People`, `Work`, `TimeOff`, `Documents`, `Scheduling`, `Notifications` + `Identity` e `SharedKernel` | Firme | [Detalhe](arquitetura/modulos-e-contratos.md) |
| A4 | Leitura entre módulos: **interface de contrato in-process**. Reação entre módulos: **integration event** | Firme | Chamada de método é síncrona, consistente e grátis. Evento é para reagir, não para consultar. [ADR-0004](adr/0004-comunicacao-entre-modulos.md) |
| A5 | **Sem read models replicados** entre módulos enquanto for monolito | Firme | É a armadilha de overengineering mais cara nesse desenho. [ADR-0004](adr/0004-comunicacao-entre-modulos.md) |
| A6 | CQRS apenas onde a leitura diverge da escrita (dashboard, timeline de férias, capacidade). Sem CQRS por padrão | Firme | Dogma custa velocidade. |
| A7 | Vertical slices dentro de cada módulo, Clean Architecture só onde o domínio é rico (`TimeOff`, `Scheduling`, `Work`) | Firme | `Documents` é essencialmente CRUD + workflow; não precisa de 4 camadas. |

## Arquitetura — dados e segurança

| # | Decisão | Status | Motivo |
|---|---|---|---|
| S1 | Multi-tenancy **shared schema** com `TenantId` em toda tabela tenant-owned | Firme | DB-por-tenant não escala operacionalmente em VPS único. [ADR-0003](adr/0003-multi-tenancy.md) |
| S2 | Isolamento por EF global query filters **+ interceptor que carimba `TenantId` no SaveChanges** | Firme | [ADR-0003](adr/0003-multi-tenancy.md) |
| S3 | **PostgreSQL RLS** ligada e verificada contra banco real | Feito | Query filter é uma linha de defesa só; um `FromSql` esquecido vaza dado de RH de outra empresa. [ADR-0003](adr/0003-multi-tenancy.md) |
| S4 | Teste de integração que falha o build se um tipo tenant-owned não tiver filtro, e teste de vazamento cross-tenant | Firme | O guard-rail vale mais que a regra escrita. |
| S5 | Identidade: ASP.NET Core Identity + JWT emitido pela própria API. Sem Duende/Auth0 agora | Firme | Custo e complexidade sem retorno neste estágio. [ADR-0006](adr/0006-identidade.md) |
| S6 | `User` é global por e-mail; `Membership(UserId, TenantId, Role)` permite a mesma pessoa em várias empresas | Firme | Contador/consultor atende várias empresas. Retrofit disso depois é doloroso. [ADR-0006](adr/0006-identidade.md) |
| S7 | Autorização = **permissão** (`timeoff.approve`) **+ escopo de dados** (`Self` / `Team` / `Sector` / `Company`) | Firme | RBAC puro não responde "posso ver o salário/atestado de quem?". [ADR-0007](adr/0007-autorizacao.md) |
| S8 | Auditoria imutável desde a V1 para aprovar/rejeitar/alterar escala/acessar documento | Firme | Dado sensível de saúde exige rastro de acesso. [Detalhe](arquitetura/multi-tenancy-e-seguranca.md#auditoria) |
| S9 | Arquivos fora do Postgres, `IFileStorage` + URL assinada com HMAC e expiração curta | Firme | [ADR-0010](adr/0010-armazenamento-de-arquivos.md) |

## Arquitetura — eventos e integração

| # | Decisão | Status | Motivo |
|---|---|---|---|
| E1 | **Outbox transacional próprio** (~150 linhas) + `BackgroundService` publicador + dispatch in-process | Firme | Enquanto é um processo, broker é container a mais sem ganho. [ADR-0005](adr/0005-outbox-e-mensageria.md) |
| E2 | RabbitMQ entra **no dia da primeira extração de módulo**, não antes | Firme | [ADR-0005](adr/0005-outbox-e-mensageria.md) |
| E3 | Consumidores idempotentes via tabela `ProcessedEvent(EventId, Consumer)`. Nunca depender de ordem | Firme | Outbox garante at-least-once, não exactly-once. |
| E4 | Domain events resolvem dentro do módulo (na mesma transação). Integration events cruzam módulo (via outbox) | Firme | Confundir os dois é o erro clássico. [ADR-0004](adr/0004-comunicacao-entre-modulos.md) |

## Frontend

| # | Decisão | Status | Motivo |
|---|---|---|---|
| F1 | Angular standalone + Signals + Reactive Forms + functional guards/interceptors | Firme | [ADR-0008](adr/0008-frontend-angular.md) |
| F2 | **Angular CDK sim, Angular Material não** na camada visual | Firme | Você quer design system próprio; tematizar Material até não parecer Material é briga perdida. CDK (drag-drop, overlay, a11y, virtual scroll) é neutro. [ADR-0008](adr/0008-frontend-angular.md) |
| F3 | **Sem NgRx no MVP.** Signals + store service por feature | Firme | NgRx é solução para complexidade de estado que você ainda não tem. |
| F4 | Cliente HTTP e DTOs **gerados do OpenAPI**, nunca escritos à mão | Firme | [ADR-0009](adr/0009-cliente-gerado-do-openapi.md) |
| F5 | Um único componente **`TimeGrid`** próprio (CSS Grid + CDK) serve a grade de escala e a timeline de férias | Firme | Mesma estrutura (linhas = pessoas, colunas = dias); construir duas vezes seria erro. Nenhuma biblioteca de scheduler entrega a linha de cobertura. [Detalhe](produto/ux-telas-criticas.md) |
| F6 | Strings de UI atrás de chave de i18n desde o commit 1, mas só pt-BR publicado | Firme | Custo marginal hoje, retrofit caro depois. [ADR-0012](adr/0012-idioma.md) |
| F7 | Código, tabelas e API em inglês; produto em pt-BR | Firme | [ADR-0012](adr/0012-idioma.md) |
| F8 | **Enum trafega como texto no JSON**, nunca como número (`JsonStringEnumConverter` global) | Firme | `status: 2` obriga o frontend a manter tabela de números e faz reordenar o enum virar quebra silenciosa. Texto se explica sozinho no log e no DevTools. |
| F9 | **`InvariantGlobalization` desligado.** O produto é pt-BR e precisa de ICU | Firme | Esteve ligado e custou caro: `string.Normalize` virou no-op e toda planilha com a coluna "Admissão" era recusada sem erro visível. As imagens Debian do .NET já trazem libicu — a economia era zero. |
| F10 | Leitura de arquivo do cliente **não depende de configuração de runtime** (dobra de acento por tabela explícita, formatos de data fechados) | Firme | O parser é a porta de entrada do arquivo sujo; se ele se comportar diferente em dev e em produção, o bug aparece no cliente. |

## Infraestrutura

| # | Decisão | Status | Motivo |
|---|---|---|---|
| I1 | Aspire para desenvolvimento local e `ServiceDefaults` (OTel + health checks) | Firme | Exatamente "framework para plumbing". [ADR-0011](adr/0011-aspire-e-deploy.md) |
| I2 | Produção inicial com **docker-compose escrito à mão**, não gerado pelo Aspire | Firme | O output do Aspire mira Azure/k8s; num VPS você quer um compose estável e legível. [ADR-0011](adr/0011-aspire-e-deploy.md) |
| I3 | Angular servido como **estático pelo Caddy**, sem container Node em produção | Firme | Um processo a menos para operar. |
| I4 | **Sem Kubernetes.** Sem Redis até existir uma dor medida | Firme | |
| I5 | Backup `pg_dump` diário para armazenamento externo + **restore testado** antes do primeiro cliente | Firme | Você vai guardar CPF, RG e ASO de terceiros num VPS de nó único. [Detalhe](arquitetura/infraestrutura-e-deploy.md#backup) |
| I6 | Deploy por GitHub Actions: test → build → scan → imagem → `docker compose up -d` via SSH | Firme | |
| I7 | OpenTelemetry, logs estruturados, correlation id, `/healthz` e `/healthz/ready` desde o primeiro deploy | Firme | |

## Decisões que estão abertas e dependem de você

| # | Pergunta | Por que importa agora |
|---|---|---|
| Q1 | **O que exatamente os três pilotos fazem** (manutenção, segurança, saúde, campo, facilities) e qual o padrão de turno de cada um? | Define os `ScheduleCycle` da V1 e o catálogo de documentos pré-configurado. É o insumo do Marco 4 |
| Q2 | Registro da marca "Mamão" no INPI (classes NCL 9 e 42) | O domínio está garantido; a marca, não. Barato agora, caro depois do primeiro material |
| Q3 | Vale registrar `mamao.com.br` também? | O comprador SMB brasileiro reconhece mais `.com.br`. Ver [riscos](riscos-e-pontos-de-atencao.md#marca) |
| Q4 | O design system tem modo escuro? | Decidir antes do primeiro componente, não depois. [UX](produto/ux-telas-criticas.md#design-system) |

**Resolvidas nesta rodada:** segmento (P0) · slogan e posicionamento (P9) · domínio (P10) · pilotos (P11).
