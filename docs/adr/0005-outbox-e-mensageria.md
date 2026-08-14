# ADR-0005 — Outbox próprio e dispatch in-process; broker só na extração

**Status:** aceita · **Data:** 2026-08

## Contexto

O briefing prevê Transactional Outbox (correto) e RabbitMQ como broker inicial.
A aplicação, porém, é um único processo ([ADR-0001](0001-modular-monolith.md)).

## A pergunta certa

O outbox garante **atomicidade entre o dado de negócio e o evento**. Essa garantia
vem da escrita na mesma transação — não do broker. O broker entrega **transporte
entre processos**.

Com um processo, transporte entre processos não é um problema que existe.

## Decisão

**V1:** tabela `messaging.outbox` + `OutboxPublisher : BackgroundService` +
`IEventDispatcher` que resolve consumidores no DI e chama cada um em seu escopo.
Aproximadamente 150 linhas.

**Broker (RabbitMQ):** entra no dia da primeira extração de módulo. Não antes.

Implementação em [eventos e outbox](../arquitetura/eventos-e-outbox.md).

## Motivo

Adotar broker agora custa: um container em produção e em desenvolvimento, uma
dependência para monitorar (fila, DLQ, memória, disco), uma biblioteca para
versionar e um modo de falha novo. Entrega: nada que a tabela não entregue,
enquanto for um processo.

Adiar custa: quase zero. O código de publicação e de consumo é **idêntico** nos
dois cenários; muda apenas o corpo de `DispatchAsync`.

Esta é a aplicação mais direta do princípio "diferencie necessidade real de
overengineering".

## Sobre bibliotecas

MassTransit e Wolverine resolvem isso muito bem — e ambas são opção legítima se
você preferir não manter o publicador.

Considerações:

- **MassTransit**: houve mudança no modelo de licenciamento em versões recentes.
  Verifique as condições atuais antes de adotar; não é uma decisão a tomar por
  memória.
- **Wolverine**: MIT, com outbox sobre Postgres integrado e bom encaixe em modular
  monolith.
- **Próprio**: sem dependência, sem risco de licença, ~150 linhas de código que
  você entende inteiro. Para o catálogo de eventos do Mamão (13 eventos na V1), é
  suficiente.

**Escolha: próprio.** Justamente por não ser plumbing complexa neste tamanho. Se em
algum momento surgir necessidade de saga, scheduling de mensagem ou request/reply
distribuído, migre para Wolverine — aí a biblioteca passa a resolver problema real.

## Consequências

- Sem broker em produção nem em desenvolvimento na V1.
- Entrega at-least-once: consumidores **precisam** ser idempotentes
  (`messaging.processed_event`).
- Latência de alguns segundos entre o fato e a reação. Aceitável para tudo que é
  assíncrono; nada que o usuário observe imediatamente deve depender do outbox.
- Métrica `mamao.outbox.pending` monitorada — fila crescendo significa worker
  parado, e isso precisa gerar alerta.
- Mensagem que esgota tentativas gera alerta, não silêncio.

## Quando revisitar

Primeira extração de módulo; ou necessidade de saga/coreografia complexa; ou
integração com sistema externo que exija fila persistente.
