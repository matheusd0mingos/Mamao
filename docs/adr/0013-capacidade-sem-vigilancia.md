# ADR-0013 — Capacidade é prospectiva, nunca vigilância

**Status:** aceita · **Data:** 2026-08

## Contexto

O briefing rejeita o termo "controle de produtividade" para evitar sensação de
vigilância, preferindo "carga de trabalho" e "capacidade da equipe". A intenção é
correta — mas nomenclatura não sustenta a promessa. A implementação sustenta.

Se o sistema registrar horas trabalhadas e ranquear conclusões, ele **é** um
sistema de vigilância, chamando-se como se chamar. E o funcionário percebe na
primeira semana.

## Decisão

A métrica é **prospectiva**: quanto trabalho está alocado para uma pessoa no futuro,
comparado ao tempo que ela terá disponível.

```
Capacidade(pessoa, semana) = Σ horas disponíveis por dia
                             (jornada contratada − férias/ausência/folga/feriado)

Carga(pessoa, semana)      = Σ horas estimadas das tarefas abertas,
                             distribuídas nos dias úteis entre início e prazo

Utilização                 = Carga / Capacidade
```

### Regras de exibição

1. **Nunca um percentual sozinho.** Sempre com os números que o sustentam:
   `7 tarefas · 32h estimadas · 3 sem estimativa → 84%`. O contador de tarefas sem
   estimativa é o que mantém o número honesto.
2. Utilização individual é visível para o **gestor** e para a **própria pessoa**.
   Nunca entre pares.
3. Vocabulário: "sobrecarregado" e "com capacidade disponível". Nunca "abaixo da
   meta", "ocioso" ou "desempenho".
4. O alerta aponta para a **decisão do gestor**, não para a pessoa:
   "Você atribuiu 95% da capacidade do João esta semana."

### O que fica proibido no produto

- Ranking de funcionários por tarefas concluídas
- Registro de horas trabalhadas, hora de início/fim de tarefa, tempo de resposta
- Score, nota ou índice individual
- Qualquer relatório que responda "quem produziu mais"

Isto é uma **restrição de produto**, não uma prioridade de backlog. Vale também
para pedidos de cliente — e há de vir.

## As três armadilhas de cálculo

**Tarefa sem estimativa.** A maioria não terá. Somar só as estimadas faz a
utilização mentir para baixo, e o gestor perde a confiança no número na primeira
semana. Por isso a regra 1: sempre exibir quantas ficaram de fora.

**Concentrar no prazo.** Uma tarefa de 16h com prazo em 10 dias não pesa no dia do
prazo — ela ocupa a janela. Distribua uniformemente pelos dias úteis disponíveis
entre início (ou hoje) e prazo. Suficiente; nada de otimizador.

**Tarefa atrasada.** Vencida e ainda aberta pesa **hoje**, integralmente. É o que
corresponde à realidade e o que torna o número útil.

## Motivo

Vigilância é ativamente ruim para o produto:

- **Adoção.** Funcionário que se sente vigiado sabota o preenchimento. Dados ruins
  destroem o valor para o gestor — o efeito é autodestrutivo.
- **Venda.** "Não é sistema de vigilância" é diferenciação real num mercado onde os
  concorrentes vendem controle. Só funciona se for verdade demonstrável na tela.
- **Escopo.** Registro de horas leva direto a ponto eletrônico, que é escopo
  regulatório que você decidiu não ter.

## Consequências

- Estimativa em horas passa a ser campo importante. Estimule o preenchimento sem
  torná-lo obrigatório (obrigatório gera "1h" em tudo, que é pior que vazio).
- A capacidade depende de `Disponibilidade`, que depende de `TimeOff` e
  `Scheduling`. É o cálculo mais integrado do sistema — e uma boa demonstração de
  que os módulos conversam.
- Sem estimativa nenhuma, mostre apenas contagem de tarefas. Melhor um número menos
  informativo do que um percentual falso.
