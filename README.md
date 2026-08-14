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

## Estado atual

Repositório em fase de **decisão de produto e arquitetura**. Ainda não há código.

Toda a discussão está em [`docs/`](docs/). Comece por:

1. [Sumário de decisões](docs/00-sumario-de-decisoes.md) — a tabela que responde "o que já está decidido e por quê"
2. [Cliente ideal e pilotos](docs/produto/icp-e-pilotos.md) — para quem é e como validar
3. [Posicionamento e MVP](docs/produto/mvp-e-posicionamento.md) — onde discordo do briefing e por quê
4. [Visão geral de arquitetura](docs/arquitetura/visao-geral.md)
5. [Roadmap](docs/roadmap.md) — a sequência até o primeiro cliente pagante
6. [Riscos e pontos de atenção](docs/riscos-e-pontos-de-atencao.md) — LGPD, CLT, jornada, marca, backup

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
