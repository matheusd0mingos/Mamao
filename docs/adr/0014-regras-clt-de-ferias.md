# ADR-0014 — Regras de férias da CLT no domínio, desde a V1

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
| Fracionamento em até 3 períodos; um com ao menos 14 dias corridos, os demais com ao menos 5 dias cada | art. 134, §1º | Validação na solicitação |
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
