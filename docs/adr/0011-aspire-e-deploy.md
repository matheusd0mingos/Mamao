# ADR-0011 — Aspire para desenvolvimento; Docker Compose escrito à mão para produção

**Status:** aceita · **Data:** 2026-08

## Contexto

O briefing prevê Aspire e hospedagem inicial num VPS HostGator, com migração
eventual para Azure. Aspire é excelente em desenvolvimento; seu caminho de deploy,
porém, mira Azure Container Apps e Kubernetes.

## Decisão

**Desenvolvimento:** Aspire, sem ressalvas.

- `AppHost` sobe Postgres, API, Worker e o dev server do Angular com um `F5`.
- Connection strings e service discovery injetados — nada de `appsettings` local
  divergindo entre máquinas.
- `ServiceDefaults` entrega OpenTelemetry, health checks e resiliência de
  `HttpClient` sem código.
- Dashboard local de traces e logs, que é onde se depura o cálculo de
  disponibilidade e o outbox.

**Produção:** `docker-compose.yml` escrito e versionado à mão.

## Motivo

Aspire modela a topologia para gerar manifesto de deploy em plataformas
gerenciadas. Num VPS de nó único com Caddy, esse output não é o que você quer
implantar — e depurar compose gerado é pior do que ler compose escrito.

O compose de produção é ~60 linhas, muda pouco, e precisa ser óbvio às 2h da manhã.
Legibilidade vale mais que geração automática aqui.

Não há duplicação relevante: o `Dockerfile` é o mesmo, e o `AppHost` descreve
desenvolvimento (com pgAdmin, volume de dados, hot reload) — que legitimamente não
é a topologia de produção.

## Consequências

- Duas descrições de topologia. Aceito conscientemente; são públicos e objetivos
  diferentes.
- `ServiceDefaults` é referenciado por API e Worker e vale em ambos os ambientes —
  a parte do Aspire que realmente vai para produção.
- Ao migrar para Azure Container Apps, o `AppHost` volta a ser útil para gerar
  infraestrutura, e aí a duplicação desaparece.
- Aspire acrescenta dependência de tooling no desenvolvimento. Aceitável: o ganho
  em observabilidade local é grande.

## Nota sobre versões (verificada na montagem do Marco 0)

- Aspire está em **13.4.6** e não usa mais workload: o AppHost declara
  `<Sdk Name="Aspire.AppHost.Sdk" Version="13.4.6" />`. Um `IsAspireHost=true` sem esse
  SDK dispara erro de workload descontinuada (`NETSDK1228`).
- `Aspire.Hosting.NodeJs` ainda está na linha 9.x e não acompanhou o 13.x. Por isso o
  **Angular não sobe pelo AppHost**: rode `npm start` em `web/mamao-web`, que usa o
  proxy do dev server para a API. Uma decisão a revisitar quando o pacote alcançar.
- Fixe tudo no `Directory.Packages.props` e confirme antes de subir de versão — estas
  notas envelhecem.

## Quando revisitar

Na migração para Azure, ou se o Aspire passar a gerar compose adequado a VPS de nó
único.
