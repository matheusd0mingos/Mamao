# Documentação do Mamão

Resposta ao briefing de produto e arquitetura. O objetivo destes documentos é
**sustentar decisões**, não descrever código — cada um explica o motivo, separa
necessidade real de overengineering e anota o gatilho para revisitar.

## Por onde começar

| Se você quer… | Leia |
|---|---|
| A resposta curta de tudo | [Sumário de decisões](00-sumario-de-decisoes.md) |
| Saber para quem é o produto | [Cliente ideal e pilotos](produto/icp-e-pilotos.md) |
| Saber o que construir primeiro | [Posicionamento e MVP](produto/mvp-e-posicionamento.md) e [Roadmap](roadmap.md) |
| Entender o desenho técnico | [Visão geral de arquitetura](arquitetura/visao-geral.md) |
| Saber o que pode dar errado | [Riscos e pontos de atenção](riscos-e-pontos-de-atencao.md) |

## Produto

- [Cliente ideal e pilotos](produto/icp-e-pilotos.md) — segmento, catálogo de
  documentos e roteiro de validação
- [Posicionamento e MVP](produto/mvp-e-posicionamento.md) — onde este documento
  discorda do briefing, e por quê
- [Modelo de domínio](produto/modelo-de-dominio.md) — agregados, disponibilidade,
  pendências, capacidade, regras CLT
- [UX — telas críticas](produto/ux-telas-criticas.md) — as cinco telas que decidem
  o produto e o design system

## Arquitetura

- [Visão geral](arquitetura/visao-geral.md) — topologia, estrutura da solução,
  padrões recusados
- [Módulos e contratos](arquitetura/modulos-e-contratos.md) — fronteiras e as duas
  formas de comunicação
- [Multi-tenancy e segurança](arquitetura/multi-tenancy-e-seguranca.md) —
  isolamento, autorização, auditoria
- [Eventos e outbox](arquitetura/eventos-e-outbox.md) — catálogo, despacho, consumo
- [Frontend Angular](arquitetura/frontend-angular.md)
- [Infraestrutura e deploy](arquitetura/infraestrutura-e-deploy.md) — Aspire, VPS,
  CI/CD, observabilidade
- [Testes](arquitetura/testes.md)

## Decisões de arquitetura (ADRs)

| # | Decisão |
|---|---|
| [0001](adr/0001-modular-monolith.md) | Modular monolith, um deployable |
| [0002](adr/0002-schema-por-modulo.md) | Um banco, um schema e um `DbContext` por módulo |
| [0003](adr/0003-multi-tenancy.md) | Shared schema + `TenantId` + RLS |
| [0004](adr/0004-comunicacao-entre-modulos.md) | Contratos in-process para leitura, eventos para reação |
| [0005](adr/0005-outbox-e-mensageria.md) | Outbox próprio; broker só na extração |
| [0006](adr/0006-identidade.md) | Identity + JWT próprio; `User` global com `Membership` |
| [0007](adr/0007-autorizacao.md) | Permissão + escopo de dados |
| [0008](adr/0008-frontend-angular.md) | Angular standalone + Signals + CDK, sem Material visual, sem NgRx |
| [0009](adr/0009-cliente-gerado-do-openapi.md) | Cliente HTTP gerado do OpenAPI |
| [0010](adr/0010-armazenamento-de-arquivos.md) | `IFileStorage` com URL assinada |
| [0011](adr/0011-aspire-e-deploy.md) | Aspire no dev, Compose à mão em produção |
| [0012](adr/0012-idioma.md) | Código em inglês, produto em pt-BR |
| [0013](adr/0013-capacidade-sem-vigilancia.md) | Capacidade prospectiva, nunca vigilância |
| [0014](adr/0014-regras-clt-de-ferias.md) | Regras CLT de férias no domínio, desde a V1 |
| [0015](adr/0015-regras-de-jornada-e-escala.md) | Escalas na V1, com validação de jornada em modo alerta |
| [0016](adr/0016-bibliotecas-de-terceiros.md) | Critério de licença e saúde para biblioteca de terceiro |
- [ADR-0017 — Regime de vínculo configurável (CLT, estatutário, militar)](adr/0017-regime-de-vinculo.md)
- [ADR-0018 — Empresas independentes são tenants; subordinadas são unidades](adr/0018-organizacoes-e-unidades.md)
- [ADR-0019 — Escala de serviço por rodízio justo](adr/0019-escala-por-rodizio.md)
- [ADR-0020 — Usuário pertence à empresa (substitui S6 da ADR-0006)](adr/0020-usuario-pertence-a-empresa.md)

## Como manter

- Decisão nova ou revista → ADR nova (nunca edite uma aceita; crie a substituta e
  marque a antiga como *substituída por*).
- Toda ADR responde: contexto, decisão, motivo, consequências e **quando
  revisitar**. É o último item que evita carregar decisão vencida por inércia.
- O [sumário](00-sumario-de-decisoes.md) é o índice canônico. Se não está lá, não
  foi decidido.
