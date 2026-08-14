# ADR-0004 — Contratos in-process para leitura, eventos para reação

**Status:** aceita · **Data:** 2026-08

## Contexto

Módulos separados por schema ([ADR-0002](0002-schema-por-modulo.md)) não podem se
juntar por `JOIN`. Mas `Work` precisa do nome do responsável, o dashboard precisa
de dados de cinco módulos, e a aprovação de férias precisa afetar tarefas e escala.

## Decisão

Exatamente duas formas de comunicação, e nenhuma outra.

**1. Leitura → interface de contrato, chamada in-process, síncrona.**

Cada módulo publica um projeto `*.Contracts` com interfaces de consulta e DTOs.
A implementação fica em `*.Infrastructure`, registrada no DI.

**2. Reação → integration event, assíncrono, via outbox.**

Fato consumado publicado; consumidores reagem no próprio schema.

## O que fica proibido

| Antipadrão | Motivo |
|---|---|
| `JOIN` entre schemas de módulos | Acopla os esquemas permanentemente |
| **Read model replicado entre módulos** | Ver abaixo |
| Evento para *consultar* dado | Request/reply assíncrono dentro de um processo é complexidade sem retorno |
| Referenciar `Domain`/`Application`/`Infrastructure` de outro módulo | Bloqueado por teste de arquitetura |

## Sobre read models replicados

A recomendação frequente é: "`Work` mantém uma tabela local `EmployeeRef`,
alimentada por `EmployeeCreated`/`EmployeeUpdated`, para não depender de `People`."

Isso é **necessário entre serviços** e **prejudicial dentro de um processo**:

- Introduz consistência eventual num lugar onde há consistência forte de graça.
- Cria código de sincronização, reprocessamento e reparo para todo campo replicado.
- Gera a classe de bug mais irritante do produto: nome desatualizado em uma tela e
  atualizado em outra — visível para o cliente, difícil de explicar.
- Substitui uma chamada de método (microssegundos, mesmo processo) por uma máquina
  de replicação.

**Decisão:** enquanto for monolito, sem replicação. `Work` chama
`IEmployeeDirectory`. No dia da extração, a implementação do contrato vira cliente
HTTP e, se a latência exigir, aí sim entra cache ou projeção local — com o problema
real na mão, não presumido.

Este é o item de overengineering mais comum neste desenho e o que mais custa caro.

## Consequências

- Consultas entre módulos são sempre em **lote** (`GetManyAsync`). A versão
  singular convida ao N+1 e ele aparece já no primeiro dashboard.
- O contrato é a superfície versionada: mudança nele é mudança pública.
- No dia da extração, trocar a implementação do contrato por um cliente gerado do
  OpenAPI não altera nenhum consumidor. É o retorno concreto da disciplina.

## Quando revisitar

Na primeira extração de módulo, para os contratos daquele módulo apenas.
