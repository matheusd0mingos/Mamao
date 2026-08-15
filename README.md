# Mamão

**Gestão sem complicação.**

Sistema operacional da equipe para pequenas empresas: pessoas, escalas, férias,
ausências, documentos, atividades e a visão que o gestor precisa para não ter que
perguntar o que está acontecendo.

> O gestor não deveria precisar perguntar o que está acontecendo.
> Ele deveria abrir o Mamão e ver.

**Segmento inicial:** operação com plantão, turno e rodízio — manutenção,
segurança, saúde, campo e facilities, de 15 a 60 funcionários.

`mamao.tech` · [Apache-2.0](LICENSE)

> **English** — Mamão is a multi-tenant B2B SaaS for running small operational teams
> (5–60 people): people, shift rosters, leave, absences, documents with expiry dates,
> and the manager's daily view. It is a .NET 10 modular monolith with schema-per-module,
> tenant isolation enforced three independent ways (EF Core global query filters,
> PostgreSQL Row-Level Security under `FORCE ROW LEVEL SECURITY`, and architecture tests
> that fail the build when an index does not start with `tenant_id`), a transactional
> outbox, an append-only audit log with `UPDATE`/`DELETE` revoked at the database, and an
> Angular 22 front end typed from the generated OpenAPI schema.
>
> Code and docs are in Portuguese. The best entry point is
> [`docs/adr/`](docs/adr/) — 20 architecture decision records explaining what was decided
> and, more usefully, what was rejected.

---

## Estado atual — em produção, em validação

O sistema está no ar num VPS (Cloudflare → Caddy → Docker Compose), com backup diário
criptografado saindo do servidor e script de restore cronometrado. Está em uso por uma
equipe real; o produto ainda não tem preço público.

### O que existe

| Área | Situação |
|---|---|
| **Pessoas** — cadastro, perfil, contrato por regime de vínculo, desligamento | ✅ |
| **Estrutura** — setores em árvore, cargos, filtro por subárvore | ✅ [ADR-0018](docs/adr/0018-organizacoes-e-unidades.md) |
| **Disponibilidade** — ocupações, férias, ausências, solicitação e aprovação | ✅ regras CLT em [ADR-0014](docs/adr/0014-regras-clt-de-ferias.md) |
| **Escala por rodízio** — "preciso de 20 pessoas amanhã", com o motivo de cada posição | ✅ [ADR-0019](docs/adr/0019-escala-por-rodizio.md) |
| **Calendário** — mês, trimestre, semestre e ano, com filtros e corte de sobreposição | ✅ |
| **Demandas** — quadro por estado, prioridade e responsável | ✅ em avaliação de uso |
| **Documentos com validade** — aviso por e-mail antes de vencer, uma vez por validade | ✅ |
| **Painel "Hoje"** e busca global (sem sensibilidade a acento) | ✅ |
| **Auditoria** append-only, na mesma transação do fato | ✅ |
| **Acessos** — convite com prazo e uso único, 5 papéis, 24 permissões | ✅ [ADR-0007](docs/adr/0007-autorizacao.md) |
| Importação de planilha com prévia linha a linha | ✅ |

### Como isso se sustenta

| Fundamento | Situação |
|---|---|
| Modular monolith, .NET 10 — 10 projetos + 4 do módulo People | ✅ [ADR-0001](docs/adr/0001-modular-monolith.md) |
| Isolamento entre empresas por **três** caminhos independentes | ✅ [ADR-0003](docs/adr/0003-multi-tenancy.md) |
| ├ filtro global do EF Core | ✅ |
| ├ Row-Level Security do PostgreSQL, sob `FORCE ROW LEVEL SECURITY` | ✅ API roda em role **sem** `BYPASSRLS` |
| └ teste de arquitetura que quebra o build se um índice não começar em `tenant_id` | ✅ |
| Outbox transacional + dispatcher idempotente | ✅ [ADR-0005](docs/adr/0005-outbox-e-mensageria.md) |
| Auditoria com `UPDATE`/`DELETE`/`TRUNCATE` revogados no banco | ✅ |
| 12 migrations por módulo, com advisory lock | ✅ |
| OpenAPI → tipos TypeScript, verificado nos dois lados pelo CI | ✅ [ADR-0009](docs/adr/0009-cliente-gerado-do-openapi.md) |
| Testes | ✅ **183** unitários + **21** de arquitetura + 10 de integração |
| CI (GitHub Actions) | ✅ |
| Deploy, backup com retenção GFS, ensaio de restore | ✅ rodando no servidor |

Os 10 testes de integração sobem Postgres via Testcontainers e provam a RLS contra o
banco de verdade. **Sem Docker eles se marcam como `skipped`, não como aprovados** — o
número honesto numa máquina sem Docker é 204, não 214.

### Limites conhecidos

Estão aqui porque um README que só lista acertos não serve para avaliar nada:

- **Um servidor só, sem SLA.** Está escrito nos termos de uso, e continua sendo o risco
  operacional real. RPO de até 24h (intervalo do cron); o RTO é o que o
  [`restaurar.sh`](deploy/restaurar.sh) cronometra.
- O quadro de **demandas** está em observação. Se ninguém além do autor mexer nos cartões
  em duas semanas, a tela sai — kanban sem equipe que o alimente é enfeite.
- Política de privacidade e termos de uso **precisam de revisão jurídica** antes do
  primeiro cliente pagante; os arquivos dizem isso no topo.
- Não há tela de configurações da empresa, e `billing.manage` ainda não tem rota.

Ver [roadmap](docs/roadmap.md) e
[riscos e pontos de atenção](docs/riscos-e-pontos-de-atencao.md).

---

## Rodando localmente (Debian 13 trixie)

### 1. Pré-requisitos

```bash
# .NET 10 SDK — o script oficial instala em ~/.dotnet, sem root e com versao fixada.
# Preferido ao pacote da distro, que costuma ficar atrasado.
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0
echo 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"' >> ~/.bashrc
source ~/.bashrc
dotnet --version

# Node 24 LTS (o Angular 22 exige >= 22.22.3)
curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
sudo apt-get install -y nodejs

# Docker — necessario para o Aspire e para os testes com Testcontainers
sudo apt-get install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
  https://download.docker.com/linux/debian trixie stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo usermod -aG docker "$USER"   # exige relogin
```

### 2. Backend

```bash
dotnet restore Mamao.slnx
dotnet run --project src/Mamao.AppHost
```

O Aspire sobe Postgres (com volume, os dados sobrevivem ao restart), a API e o
Worker, e abre o dashboard com traces e logs. O Worker aplica as migrations no
startup, protegido por advisory lock.

### 3. Frontend

Em outro terminal:

```bash
cd web/mamao-web
npm ci
npm start        # http://localhost:4200, com proxy de /api para a API
```

Ajuste a porta da API em `web/mamao-web/proxy.conf.json` conforme a que o Aspire
atribuir.

### 4. Primeiro uso

Abra `http://localhost:4200`, clique em **Cadastre sua empresa**, e você cai no
sistema já como Owner, com todas as permissões.

---

## Comandos do dia a dia

```bash
# Testes (integracao exige Docker; sem ele eles se marcam como skipped)
dotnet test Mamao.slnx

# Nova migration de um modulo
dotnet ef migrations add <Nome> \
  -p src/Modules/People/Mamao.People.Infrastructure \
  -s src/Modules/People/Mamao.People.Infrastructure \
  -o Persistence/Migrations

# Regenerar o contrato: OpenAPI a partir da API, tipos a partir do OpenAPI.
# O CI verifica os dois lados e quebra se estiverem desatualizados.
dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"
cd web/mamao-web && npm run generate:api

# Deploy — a primeira execucao provisiona o servidor e pergunta o que falta.
# Procedimento completo em deploy/README.md.
./deploy/deploy.sh --setup      # so prepara o ambiente
./deploy/deploy.sh              # prepara (se preciso) e publica
./deploy/deploy.sh --status
./deploy/deploy.sh --rollback

# NO servidor, quando se prefere publicar de la em vez de por SSH
sudo ./deploy/no-servidor.sh

# Restore. O --ensaio restaura num banco descartavel e NAO toca em producao;
# os dois imprimem quanto tempo levaram, que e o RTO de verdade.
./deploy/restaurar.sh --ensaio   /caminho/mamao-....dump.gpg
./deploy/restaurar.sh --producao /caminho/mamao-....dump.gpg /caminho/uploads-....tar.gz.gpg
```

---

## Estrutura

```
src/
  Mamao.AppHost/              Aspire — orquestracao local
  Mamao.ServiceDefaults/      OTel, health checks, resiliencia
  Mamao.SharedKernel/         Tenancy, outbox, Result, permissoes
  Mamao.SharedKernel.Web/     Traducao de Result -> ProblemDetails
  Mamao.Identity/             Usuario, tenant, membership, JWT
  Mamao.Audit/                Trilha append-only, mapeada em cada modulo
  Mamao.Messaging/            Outbox: publisher e dispatcher
  Mamao.Notifications/        E-mail (MailKit) e templates
  Mamao.Api/                  Host HTTP
  Mamao.Worker/               Migrations, outbox e avisos de vencimento
  Modules/People/             Contracts | Domain | Application | Infrastructure
tests/                        Unitarios | Arquitetura | Integracao
web/landing/                  Landing estatica (mamao.tech)
web/mamao-web/                Angular 22 (app.mamao.tech)
deploy/                       Scripts, Compose, Caddy, init do banco, backup, restore
docs/                         Decisoes de produto e arquitetura (20 ADRs)
```

O módulo People é o único extraído até agora — os demais contextos ainda vivem no host.
A regra que decide quando extrair está na [ADR-0001](docs/adr/0001-modular-monolith.md):
módulo nasce quando tem domínio próprio, não quando a pasta fica grande.

## Stack

| Camada | Escolha |
|---|---|
| Backend | .NET 10 / ASP.NET Core / Minimal APIs |
| Persistência | PostgreSQL 17 + EF Core 10 (um schema por módulo, snake_case) |
| Arquitetura | Modular monolith, um deployable, bounded contexts separados |
| Mensageria | Outbox transacional + dispatch in-process. Broker só na primeira extração |
| Frontend | Angular 22 (standalone, signals, zoneless, CDK) — design system próprio |
| Identidade | ASP.NET Core Identity + JWT próprio, `User` global + `Membership` por tenant |
| Dev | Aspire 13 |
| Produção inicial | Cloudflare → Caddy → Docker Compose em VPS |
| Observabilidade | OpenTelemetry desde o primeiro commit |

## Documentação

Comece por [docs/](docs/) — em especial o
[sumário de decisões](docs/00-sumario-de-decisoes.md), que responde "o que já está
decidido e por quê", e as [ADRs](docs/README.md#decisões-de-arquitetura-adrs).

### 📘 Apostila: Angular + .NET do zero

O repositório traz um **[material de estudo completo](docs/apostila/README.md)** — 14
capítulos que ensinam Angular e a integração Angular ↔ .NET usando este código como
laboratório, do "o que é uma SPA" até o contrato gerado por OpenAPI, com exercícios
resolvidos.

Inclui um capítulo de [**bugs reais**](docs/apostila/13-bugs-reais.md): sete defeitos
verdadeiros deste projeto, com sintoma, investigação, causa e lição. Nenhum deles foi
pego por compilador, tipagem estrita ou revisão de código.

## Princípio que governa as decisões

> **Use framework para plumbing. Use nosso código para domínio.**

O código próprio se concentra no que ninguém entrega pronto: regras de férias CLT,
validação de jornada, disponibilidade da equipe, capacidade, escalas, validade de
documentos e o painel do gestor. Todo o resto — autenticação, HTTP, routing, DI,
ORM, OpenAPI, logging, health checks, drag & drop — é do framework.

---

## Licença

[Apache License 2.0](LICENSE). Copyright 2026 Matheus Domingos.

Você pode usar, modificar e distribuir, inclusive comercialmente, desde que preserve o
aviso de copyright e sinalize os arquivos que alterou. A escolha é Apache e não MIT pela
**concessão explícita de patente** (seção 3), que o MIT não tem — quem adota o código não
fica exposto a uma reivindicação futura de quem o publicou.

Duas coisas que a licença **não** faz, e que valem estar escritas: ela não cede a marca
"Mamão" (seção 6) e não é exclusiva — publicar sob Apache-2.0 não impede licenciamento
comercial do mesmo código.
