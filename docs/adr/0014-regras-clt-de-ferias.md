# ADR-0014 — Regras de férias da CLT no domínio, desde a V1

> **Escopo estreitado pela [ADR-0017](0017-regime-de-vinculo.md).** Esta ADR descreve a
> política padrão do regime **CLT**. Estatutário e militar têm normas próprias e usam a
> mesma estrutura de parâmetros com outros padrões. Nada aqui se perde; o que muda é que
> deixou de valer para todo mundo.

**Status:** aceita · **Data:** 2026-08

> **Aviso.** O resumo abaixo é insumo de engenharia, não parecer jurídico. Valide o
> conjunto com contabilidade e jurídico antes de comunicar conformidade ao mercado.
> Convenção coletiva pode ser mais benéfica que a CLT — por isso as regras são
> **configuráveis por tenant**, nunca hard-coded.

## Contexto

O briefing trata férias como um workflow de aprovação: solicita, gestor aprova,
calendário atualiza. Isso é um formulário — e um formulário não vence a planilha
que a empresa já usa.

O que vence a planilha é o **cálculo**: quantos dias a pessoa tem direito, até
quando precisam ser concedidos, se o fracionamento pedido é válido, se a data de
início é permitida, e quanto custa errar.

## Decisão

As regras de férias da CLT são modeladas no domínio de `TimeOff` desde a V1, com
parâmetros por tenant. `TimeOff` é o módulo que recebe tratamento completo de Clean
Architecture — é aqui que está o valor.

## Regras a implementar

| Regra | Referência | Efeito no sistema |
|---|---|---|
| Período aquisitivo: 12 meses a partir da admissão | art. 130 | `VacationEntitlement` gerado automaticamente no aniversário |
| 30 dias corridos, reduzidos por faltas injustificadas: 6–14 → 24 dias; 15–23 → 18; 24–32 → 12; acima de 32 → sem direito | art. 130 | **Depende do módulo de ausências.** É a integração mais concreta do produto |
| Período concessivo: 12 meses seguintes ao aquisitivo | art. 134 | Não conceder gera pagamento em dobro (art. 137). Vira pendência crítica no dashboard |
| Fracionamento em até 3 períodos; um com ao menos 14 dias corridos, os demais com ao menos 5 dias cada | art. 134, §1º | Validação na solicitação — ver [combinações](#fracionamento) |
| Início vedado nos 2 dias que antecedem feriado ou dia de repouso semanal remunerado | art. 134, §3º | Validação; exige calendário de feriados por município |
| Abono pecuniário: converter até 1/3 (10 dias) em dinheiro | art. 143 | Campo na solicitação; abate do saldo |
| Comunicação ao empregado com ao menos 30 dias de antecedência | art. 135 | Alerta ao aprovar data próxima |
| Pagamento até 2 dias antes do início | art. 145 | Alerta ao financeiro/contabilidade |

Notas de implementação:

- A vedação de início do art. 134, §3º precisa do calendário de feriados, incluindo
  **feriados municipais** — que variam por cidade. Modele `Holiday` com escopo
  (nacional/estadual/municipal/empresa) e associe a empresa ao município.
- O antigo art. 134, §2º (férias em período único para menores de 18 e maiores de
  50) foi revogado pela reforma trabalhista de 2017. Não implemente essa restrição.
- Contratos que não são CLT (PJ, estágio, intermitente) precisam de tratamento
  distinto ou de desativação das regras. Modele `EmploymentContract.Type` desde já.

## <a name="fracionamento"></a>Fracionamento: por que não existe um menu fixo

A tentação óbvia é oferecer três botões — 1×30, 2×15, 3×10 — e acabou. Dois problemas.

**O primeiro:** 3×10 não é válido. O art. 134, §1º exige um período com **pelo menos 14
dias corridos**; em 3×10 nenhum chega lá. Em três períodos o válido é 14+11+5, 14+10+6,
16+9+5, 20+5+5 e assim por diante.

**O segundo, mais grave:** *30 dias não é um dado, é um caso*. Faltas injustificadas
reduzem o direito (art. 130) e a régua inteira muda junto:

| Dias de direito | 1 período | 2 períodos | 3 períodos |
|---|---|---|---|
| 30 | 30 | 14+16 … 25+5 | 14+11+5 … 20+5+5 |
| 24 (6–14 faltas) | 24 | 14+10 … 19+5 | **só** 14+5+5 |
| 18 (15–23 faltas) | 18 | **impossível** (14+5 já são 19) | impossível |
| 12 (24–32 faltas) | 12 | impossível | impossível |

Quem tem 18 dias **não consegue fracionar de jeito nenhum** — e nenhum gestor sabe disso
de cabeça. Um menu fixo mentiria para essa pessoa. O abono pecuniário (art. 143) mexe na
tabela de novo: vender 10 dias de 30 deixa 20 para gozar, e aí três períodos deixam de
caber.

**Decisão:** a tela não tem menu fixo. Ela **calcula** as combinações válidas a partir do
saldo real e das regras do tenant, oferece as duas ou três mais comuns como atalho de um
clique, e mostra o resto. Quando uma divisão é recusada, o motivo vem junto com o número
que falta ("faltam 4 dias no período mais longo"), nunca "divisão inválida".

### Os limites são parâmetro do tenant, não constante no código

Empresas fracionam diferente do que está na letra do artigo — por convenção coletiva, por
vínculo que não é CLT, ou por prática antiga que nunca foi questionada. Isso é o caso
normal, não a exceção, então merece configuração e não escape hatch:

```
VacationPolicy (por tenant)
  MaxPeriods                 padrão 3      art. 134, §1º
  MinimumLongPeriodDays      padrão 14     art. 134, §1º
  MinimumOtherPeriodDays     padrão 5      art. 134, §1º
  MinimumDaysBeforeHoliday   padrão 2      art. 134, §3º
```

Uma empresa que pratica 3×10 muda `MinimumLongPeriodDays` para 10 e pronto — **a
validação continua ligada**, só com outra régua. Isso é melhor que um "ignorar a regra"
por dois motivos: a política fica declarada em um lugar auditável em vez de virar
justificativa repetida linha a linha, e o resto da validação continua protegendo (com
mínimo 10, quem tem 18 dias de direito ainda não consegue três períodos, porque 10+5+5
são 20).

**O padrão continua sendo a letra da CLT.** Quem muda é o cliente que sabe por que está
mudando; quem não mexe recebe a regra que protege. Ao alterar, a tela mostra o artigo que
está sendo afastado — sem sermão, uma linha — e a mudança fica no log de auditoria com
autor e data. E o override linha a linha, com justificativa, continua existindo para o
caso único que a política não cobre.

> **Pendente de validação jurídica:** se CCT/ACT pode afastar o mínimo de 14 dias do
> art. 134, §1º. A reforma de 2017 ampliou o negociado sobre o legislado (art. 611-A),
> mas fracionamento de férias não está entre os itens listados. Enquanto isso não estiver
> resolvido, o parâmetro existe e funciona, mas **não vira argumento de marketing** — o
> Mamão não anuncia flexibilizar limite de férias.

## <a name="depois-da-aprovação"></a>Depois da aprovação: quem fica sabendo, e com que prazo

Aprovar não encerra o caso — abre dois relógios, e os dois têm consequência em dinheiro:

| Obrigação | Prazo | Dono | Se estourar |
|---|---|---|---|
| Comunicar o empregado por escrito | 30 dias antes do início (art. 135) | gestor | Férias contestáveis; infração administrativa |
| Pagar remuneração + 1/3 | até 2 dias antes do início (art. 145) | RH / financeiro | Jurisprudência majoritária manda pagar **em dobro** |

**Decisão: isso vira pendência com dono e prazo, não notificação.** A diferença é
prática — uma notificação é lida uma vez e some; uma pendência fica na fila até alguém
resolver, e envelhece visivelmente. Como o custo de esquecer é pagamento em dobro, o
sistema não pode se contentar em ter avisado.

Consequência de projeto: o papel `Hr` já existe em `Permissions` e é o dono padrão da
pendência de pagamento. Quando o tenant não tem ninguém com esse papel, o dono é o
Owner — nunca ninguém.

**O que o Mamão não faz aqui:** não calcula o valor, não emite recibo, não paga. Ele
sabe a data-limite e cobra. Essa fronteira é a mesma da seção abaixo.

## O que o sistema faz e o que não faz

**Faz:** calcula saldo, avisa vencimento do período concessivo, valida
fracionamento, detecta conflito de equipe, registra o histórico.

**Não faz:** calcula folha, emite recibo de férias, integra eSocial, substitui o
contador.

Essa fronteira precisa estar clara na comunicação do produto. "O Mamão avisa antes
de você perder o prazo" é uma promessa cumprível. "O Mamão garante conformidade
trabalhista" não é.

## Consequências

- `TimeOff` é o módulo com o domínio mais rico. Justifica camadas completas e a
  maior densidade de testes unitários do sistema.
- Cada regra vira um caso de teste nomeado em português, legível por não
  programador — a suíte de testes de férias é documentação viva da regra e o
  artefato que você mostra ao contador para validar.
- Um job diário gera períodos aquisitivos, recalcula saldos e emite
  `VacationPeriodExpiring` para quem está a menos de 90 dias do fim do período
  concessivo.
- Regras parametrizadas em `TenantVacationPolicy` (dias base, antecedência mínima,
  fracionamento permitido, exigir aprovação em dois níveis).

## Por que na V1 e não na V2

Sem isto, o módulo de férias é um calendário compartilhado, e calendário
compartilhado é grátis. Com isto, é o único lugar onde a empresa descobre que vai
pagar férias em dobro daqui a 60 dias.

É o argumento de venda do módulo — e um dos poucos fossos defensáveis do produto
inteiro.
