# Mamão

**Gestão sem complicação.**

Sistema operacional da equipe para pequenas empresas (5–50 funcionários): pessoas,
atividades, férias, ausências, documentos, escalas e a visão que o gestor precisa
para não ter que perguntar o que está acontecendo.

> O gestor não deveria precisar perguntar o que está acontecendo.
> Ele deveria abrir o Mamão e ver.

---

## Estado atual

Repositório em fase de **decisão de produto e arquitetura**. Ainda não há código.

Toda a discussão está em [`docs/`](docs/). Comece por:

1. [Sumário de decisões](docs/00-sumario-de-decisoes.md) — a tabela que responde "o que já está decidido e por quê"
2. [Posicionamento e MVP](docs/produto/mvp-e-posicionamento.md) — onde discordo do briefing e por quê
3. [Visão geral de arquitetura](docs/arquitetura/visao-geral.md)
4. [Roadmap](docs/roadmap.md) — a sequência até o primeiro cliente pagante
5. [Riscos e pontos de atenção](docs/riscos-e-pontos-de-atencao.md) — LGPD, CLT, marca, backup

## Stack decidida

| Camada | Escolha |
|---|---|
| Backend | .NET 10 / ASP.NET Core / C# |
| Persistência | PostgreSQL + EF Core (um banco, um schema por módulo) |
| Arquitetura | Modular monolith, um deployable, bounded contexts separados |
| Mensageria | Outbox transacional + dispatch in-process. Broker só na primeira extração |
| Frontend | Angular (standalone, Signals, Reactive Forms, CDK) — design system próprio |
| Identidade | ASP.NET Core Identity + JWT próprio, `User` global + `Membership` por tenant |
| Dev | Aspire (orquestração local, OTel, Postgres) |
| Produção inicial | Cloudflare → Caddy → Docker Compose em VPS |
| Observabilidade | OpenTelemetry desde o primeiro commit |

## Princípio que governa as decisões

> **Use framework para plumbing. Use nosso código para domínio.**

O código próprio deve concentrar-se no que ninguém entrega pronto: regras de férias
CLT, detecção de conflitos, disponibilidade da equipe, capacidade, escalas,
validade de documentos e o painel do gestor. Todo o resto — autenticação, HTTP,
routing, DI, ORM, OpenAPI, logging, health checks, drag & drop — é do framework.
