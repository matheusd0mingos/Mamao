# ADR-0001 — Modular monolith, um deployable

**Status:** aceita · **Data:** 2026-08

## Contexto

O objetivo declarado é caminhar para microsserviços, sem overengineering no MVP.
Realidade atual: um desenvolvedor, zero clientes, um VPS de nó único, prazo curto
para um produto vendável.

## Decisão

Um único deployable de API + um Worker, com módulos internos fortemente separados
(`People`, `Work`, `TimeOff`, `Documents`, `Scheduling`, `Notifications`).

Microsserviço só quando houver gatilho concreto — ver
[módulos e contratos](../arquitetura/modulos-e-contratos.md#sinais-de-que-um-módulo-deve-ser-extraído).

## Motivo

O que microsserviço entrega — escala independente, deploy independente, isolamento
de falha, autonomia de time — pressupõe times independentes e carga desigual
medida. Com um desenvolvedor, cada um desses benefícios se converte em custo:
debug distribuído, consistência eventual, versionamento de contrato entre
processos, orquestração local, latência de rede em toda leitura.

O que realmente se quer preservar é a **fronteira**. Fronteira é decisão de código,
não de topologia. Um monolito com fronteiras rígidas pode virar microsserviços;
microsserviços com fronteiras erradas viram um monolito distribuído — que é o pior
dos dois mundos e não tem volta barata.

## Consequências

- Transações locais e consistência forte no MVP: mais simplicidade, menos bug.
- Uma implantação, um log, um trace. Debug trivial.
- A disciplina de fronteira precisa ser **imposta por teste automatizado**, não por
  boa vontade ([ADR-0002](0002-schema-por-modulo.md),
  [ADR-0004](0004-comunicacao-entre-modulos.md)).
- Escala vertical até um limite alto. Para SMEs, muito além do previsível.

## Quando revisitar

Ao surgir o primeiro gatilho real de extração. A preparação já está feita: schema
separado, comunicação por contrato, eventos por outbox.
