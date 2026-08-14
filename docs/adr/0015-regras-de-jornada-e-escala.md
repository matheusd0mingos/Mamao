# ADR-0015 — Escalas na V1, com validação de regras de jornada

**Status:** aceita · **Data:** 2026-08 · **Altera:** [ADR-0014](0014-regras-clt-de-ferias.md) (mesmo princípio, outro domínio)

> **Aviso.** O resumo de regras abaixo é insumo de engenharia, não parecer jurídico.
> Valide com jurídico/contabilidade antes de comunicar conformidade. Convenção
> coletiva quase sempre é mais restritiva que a CLT em jornada — por isso tudo é
> **parametrizável por tenant**, nunca hard-coded.

## Contexto

O segmento inicial definido é **operação com plantão, turno e rodízio** —
manutenção, segurança, saúde, campo, facilities. Ver
[ICP e pilotos](../produto/icp-e-pilotos.md).

Para essa empresa, a escala **é** o sistema operacional. Ela não é um módulo a mais:
é o artefato consultado todo dia, por todo mundo. Um produto de gestão de pessoas
sem escala, vendido para uma empresa de plantão, não passa da primeira demo.

Isso inverte a decisão anterior, que colocava Escalas na V1.5.

## Decisão

`Scheduling` entra na V1, com escopo deliberadamente contido:

**Entra:**

- `ShiftTemplate` — turno nomeado (A = 07:00–19:00, B = 19:00–07:00, ADM = 08:00–17:00)
- `ScheduleCycle` — padrão recorrente: 12×36, 5×2, 6×1, semanal fixo
- Geração da escala a partir do ciclo, para um período, com edição manual da grade
- Troca e substituição de plantão, com aprovação
- Cobertura mínima por turno, por setor/equipe
- **Validação das regras de jornada em modo alerta**

**Não entra na V1:**

- Geração automática com otimização por restrições (fica na V3, como no briefing)
- Banco de horas
- Apuração de horas extras e adicional noturno para folha
- Rodízio automático de folga dominical

A validação **alerta, não bloqueia**. A escala real tem exceção, e sistema que
impede o gestor de registrar o que já aconteceu é sistema que o gestor abandona.

## Regras a validar

| Regra | Referência | Efeito no sistema |
|---|---|---|
| Intervalo interjornada mínimo de 11h consecutivas | art. 66 | Alerta ao montar turno que viola. **A validação mais útil de todas** |
| Intervalo intrajornada: acima de 6h → mínimo 1h; entre 4h e 6h → 15 min | art. 71 | Definido no `ShiftTemplate` |
| DSR de 24h consecutivas por semana, preferencialmente aos domingos | art. 67 | Alerta de pessoa sem folga na semana |
| 12×36 por acordo individual escrito, convenção ou acordo coletivo | art. 59-A | Flag no contrato: sem acordo registrado, alerta |
| Trabalho noturno urbano: 22h–5h, hora de 52min30s, adicional mínimo de 20% | art. 73 | Cálculo informativo de horas noturnas. **Não vai para folha** |
| Máximo de 2h extras por dia | art. 59 | Alerta ao estender turno |
| Vedado trabalho noturno para menores de 18 anos | art. 404 | Bloqueia atribuição |
| Jornada padrão de 44h semanais | CF art. 7º, XIII | Base do cálculo de capacidade |

A regra das **11h de interjornada** merece destaque: é a mais violada na prática
(dobra de plantão, troca de última hora), a mais fácil de errar numa planilha e a
que gera passivo trabalhista real. Um alerta que diz *"Carlos sai às 19h de terça e
entra às 7h de quarta — 12h, ok"* versus *"sai às 23h e entra às 7h — 8h, abaixo do
mínimo"* já paga o módulo.

## Fronteira — o que o sistema não faz

**Faz:** monta a escala, valida jornada em modo alerta, mostra cobertura, gerencia
troca e substituição, conecta com férias e ausências.

**Não faz:** apura ponto, calcula adicional noturno para pagamento, gera folha,
substitui o registro de ponto legal (Portaria 671).

Mesmo raciocínio de [ADR-0014](0014-regras-clt-de-ferias.md): *"o Mamão avisa antes
de você furar a escala"* é promessa cumprível; *"o Mamão garante conformidade de
jornada"* não é.

## Consequências

### Sobre disponibilidade

`OffShift` deixa de ser detalhe e vira status de primeira classe. Com escala, a
jornada contratada deixa de ser o fallback: **a escala passa a ser a fonte primária
das horas disponíveis do dia**. A ordem de precedência de
[disponibilidade](../produto/modelo-de-dominio.md#disponibilidade) já prevê isso.

### Sobre férias

A linha de cobertura da timeline passa a ser **por turno**, não por dia. Numa
empresa de plantão, "6 pessoas disponíveis" não significa nada se as duas do
noturno estiverem de férias na mesma semana. Sem essa granularidade, a tela mais
importante do produto dá a resposta errada.

Consequência prática: o cálculo de conflito de férias precisa de `Scheduling`. Por
isso Escalas vem **antes** de Férias no roadmap.

### Sobre capacidade

Capacidade sai da jornada contratada e passa a vir das horas de turno atribuídas.
Mais preciso e mais defensável — e reforça [ADR-0013](0013-capacidade-sem-vigilancia.md):
a métrica continua prospectiva, e continua sem qualquer registro de hora trabalhada.

### Sobre tarefas

Para este segmento, a tarefa importa menos do que o turno. "Meu dia" gira em torno
de *"qual meu turno hoje e o que preciso fazer nele"*. Isso reduz ainda mais o
escopo de `Work` na V1 — e reforça [P2](../produto/mvp-e-posicionamento.md#p2).

### Custo

Cerca de 3 semanas a mais na V1. Compensações no
[roadmap](../roadmap.md#o-que-foi-cortado-para-caber): `TimeGrid` reutilizado entre
escala e férias, `Work` reduzido, geração automática adiada para a V3.

## Quando revisitar

Se um piloto exigir banco de horas ou apuração de extras, reavalie a fronteira —
com cuidado, porque é a porta de entrada para ponto eletrônico, que é escopo
explicitamente recusado.
