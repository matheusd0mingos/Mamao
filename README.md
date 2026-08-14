# Mamão

**Gestão sem complicação.**

Sistema operacional da equipe para pequenas empresas: pessoas, escalas, férias,
ausências, documentos, atividades e a visão que o gestor precisa para não ter que
perguntar o que está acontecendo.

> O gestor não deveria precisar perguntar o que está acontecendo.
> Ele deveria abrir o Mamão e ver.

**Segmento inicial:** operação com plantão, turno e rodízio — manutenção,
segurança, saúde, campo e facilities, de 15 a 60 funcionários.

`mamao.tech`

---

## Estado atual — Marco 0 (esqueleto vertical)

O circuito completo está fechado: cadastro de empresa → login com JWT → cadastro de
funcionário com isolamento por tenant → evento na outbox → consumo no Worker, com
observabilidade, migrations e testes.

| Área | Situação |
|---|---|
| Solução .NET 10, modular monolith | ✅ 8 projetos + 3 de teste, compilando |
| Aspire (Postgres + API + Worker) | ✅ `dotnet run --project src/Mamao.AppHost` |
| Tenancy: contexto, filtro global, interceptor | ✅ coberto por teste de arquitetura |
| RLS no PostgreSQL | ✅ ligada e provada contra banco real — ver [ADR-0003](docs/adr/0003-multi-tenancy.md) |
| Identity: empresa, usuário **da empresa**, JWT + refresh | ✅ [ADR-0020](docs/adr/0020-usuario-pertence-a-empresa.md) |
| Autorização por permissão | ✅ policies geradas de `Permissions.All` |
| Módulo People (CRUD de funcionário) | ✅ |
| Outbox + publisher + dispatcher idempotente | ✅ `People.EmployeeHired.v1` ponta a ponta |
| Migrations por módulo, com advisory lock | ✅ |
| OpenAPI → tipos TypeScript gerados | ✅ verificado no CI dos dois lados |
| Angular 22: login, cadastro, shell, pessoas | ✅ build de 79 kB gzip inicial |
| Importação de planilha (formato na tela + prévia linha a linha) | ✅ |
| Setores em árvore e cargos, com filtro por subárvore | ✅ Marco 1 em andamento |
| Testes | ✅ 73 unitários + 9 de arquitetura + 10 de integração |
| CI (GitHub Actions) | ✅ |
| Deploy (`deploy.sh` com provisionamento + Compose + Caddy + backup) | ✅ escrito e lintado, ainda não executado contra um servidor |

**Pendente do Marco 0:** primeiro deploy no VPS e a rotina de backup rodando com
restore testado. Ver [roadmap](docs/roadmap.md).

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
  Mamao.Messaging/            Outbox: publisher e dispatcher
  Mamao.Api/                  Host HTTP
  Mamao.Worker/               Migrations + publicacao da outbox
  Modules/People/             Contracts | Domain | Application | Infrastructure
tests/                        Unitarios | Arquitetura | Integracao
web/landing/                  Landing estatica (mamao.tech)
web/mamao-web/                Angular 22 (app.mamao.tech)
deploy/                       deploy.sh, Compose, Caddy, init do banco, backup
docs/                         Decisoes de produto e arquitetura
```

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

## Princípio que governa as decisões

> **Use framework para plumbing. Use nosso código para domínio.**

O código próprio se concentra no que ninguém entrega pronto: regras de férias CLT,
validação de jornada, disponibilidade da equipe, capacidade, escalas, validade de
documentos e o painel do gestor. Todo o resto — autenticação, HTTP, routing, DI,
ORM, OpenAPI, logging, health checks, drag & drop — é do framework.
