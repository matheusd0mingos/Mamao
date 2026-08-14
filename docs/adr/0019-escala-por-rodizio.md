# ADR-0019 — Escala de serviço por rodízio justo é o motor primário

**Status:** aceita · **Data:** 2026-08 · **Reordena:** [ADR-0015](0015-regras-de-jornada-e-escala.md)

## Contexto

A [ADR-0015](0015-regras-de-jornada-e-escala.md) desenhou o Marco 4 em cima de **ciclo
fixo de turno**: 12×36, 5×2, 6×1, com validação de interjornada e DSR. É o desenho
certo para uma equipe de vigilância patrimonial em regime CLT.

O autor esclareceu o caso real: *"20 pessoas precisam ir marchar, aí fazer esse
rodízio"*. Isso não é ciclo de turno. É:

> Existe um **serviço** que precisa de N pessoas numa data.
> Existe um **efetivo elegível**.
> A pergunta é **de quem é a vez**.

A regra central deixa de ser conformidade de jornada e passa a ser **justiça na
distribuição**. São motores diferentes, e o segundo é mais simples que o primeiro.

## Decisão

**Escala por rodízio é o motor primário do Marco 4.** Ciclo fixo de turno continua
existindo, mas como o caso do cliente CLT de turno — não como a base do módulo.

### Modelo

```
Duty (raiz)              serviço: nome, unidade, recorrência ou data avulsa,
                         efetivo necessário, duração, se conta como plantão
DutyRequirement          composição exigida: N no total, e mínimos por cargo/posto
                         (ex.: 20 pessoas, sendo ao menos 1 sargento e 2 cabos)
DutyRoster (raiz)        um serviço numa data: rascunho → publicado
DutyAssignment           pessoa designada; situação: designado | cumprido |
                         dispensado | trocado | faltou
RotationLedger           a memória do rodízio: por pessoa e por tipo de serviço —
                         quantas vezes serviu, quando foi a última
```

`RotationLedger` é o coração. Sem ele o rodízio não existe: seria sorteio.

### O algoritmo, e por que ele é assim

Dado um serviço na data D precisando de N pessoas, o sistema **ordena** os elegíveis e
propõe os N primeiros. A ordenação é, nesta ordem:

1. **Disponibilidade** — férias, licença, dispensa, já escalado no dia, folga
   obrigatória depois do último serviço. Quem não pode, não entra.
2. **Há quanto tempo não serve** — mais antigo primeiro.
3. **Quantas vezes serviu no período** — menos vezes primeiro.
4. **Desempate estável** — antiguidade, ou matrícula. **Nunca aleatório.**

O item 4 parece detalhe e é o produto inteiro. Se o desempate for aleatório, a
pergunta *"por que o Silva foi escalado e eu não?"* não tem resposta, e o sargento
volta para a planilha na primeira reclamação. Com ordenação estável, a resposta é uma
frase: **"a última vez do Silva foi 12/03 e ele serviu 2 vezes no trimestre; a sua foi
04/06, com 4 vezes."**

Por isso a decisão que sustenta tudo:

> **A escala é uma PROPOSTA explicável, não uma designação automática.**
> O sistema propõe, mostra o porquê de cada nome, e quem manda ajusta. Toda troca
> manual fica registrada com motivo.

Isso também resolve o caso que nenhum algoritmo cobre: "o Souza não pode ir porque a
mãe dele está internada". O sistema não precisa saber disso. Precisa deixar trocar, e
lembrar que o Souza ficou devendo uma.

### Hierarquia

`Position` ganha **`Precedence`** (inteiro, opcional). Cargo civil não tem ordem;
posto e graduação têm, e ela é rígida. Precedência serve a três coisas concretas:

- exigir que o serviço tenha alguém acima de determinado nível ("comandante da guarda");
- ordenar a escala publicada pela precedência, que é como ela é lida e afixada;
- impedir designação incoerente sem precisar de regra escrita em código.

### O que muda no que já existe

| Antes ([ADR-0015](0015-regras-de-jornada-e-escala.md)) | Agora |
|---|---|
| `ScheduleCycle` (12×36, 5×2, 6×1) como base | Continua, mas como **um** dos modos |
| Grade do mês como tela principal | Tela principal é **o serviço e quem vai** |
| Validação de interjornada como regra central | Vira validação **condicionada ao regime** ([ADR-0017](0017-regime-de-vinculo.md)) |
| — | **`RotationLedger` e a justificativa de cada nome** |

`TimeGrid` continua valendo: a escala publicada do mês é a mesma estrutura (linhas =
pessoas, colunas = dias), e a timeline de férias segue reaproveitando.

## Fronteira

O sistema **não** faz otimização com restrições (nada de solver). Ele ordena, propõe e
explica. Otimização entra na V3, se entrar — e só depois de existir cliente reclamando
da proposta, não antes.

O sistema **não** decide dispensa, punição nem escala de castigo. Registra ausência e
substituição; o resto é do comando.

## Consequências

- **Marco 4 fica mais barato:** ordenação e memória de rodízio custam menos que gerador
  de ciclo com validação de jornada. Estimativa cai de ~3 para ~2 semanas.
- **Marco 3 (ausências) vira pré-requisito duro**, não paralelo: sem disponibilidade
  confiável, a proposta escala quem está de férias e perde a confiança na primeira
  tentativa.
- A explicação de cada designação precisa ser guardada, não recalculada — a escala de
  março tem que continuar explicável em junho, mesmo com o contador já diferente.
- Militar e CLT usam o **mesmo** motor de rodízio. O que muda entre eles é a política
  de descanso e de férias, que já é parametrizada pela [ADR-0017](0017-regime-de-vinculo.md).

## Quando revisitar

Quando um piloto tiver mais de ~200 pessoas num mesmo pool, ou quando as restrições
passarem de "N pessoas com M de tal posto" para combinações que a ordenação não
expressa. Aí, e só aí, se discute solver.
