# Modelo de domínio

O briefing lista módulos. O que transforma módulos em produto são **dois conceitos
transversais** que não aparecem no briefing e precisam ser projetados
explicitamente: **Disponibilidade** e **Pendências**.

Sem eles você constrói seis CRUDs bonitos que não conversam. Com eles, a promessa
"o gestor abre e vê" vira arquitetura.

---

## <a name="disponibilidade"></a>1. Disponibilidade — o conceito que conecta tudo

### Definição

Para uma pessoa e uma data, **disponibilidade** é uma resposta única e canônica:

```
Disponibilidade(pessoa, data) → {
  status:     Working | Vacation | Absent | Leave | DayOff | OffShift | Holiday
  origem:     TimeOff:{id} | Attendance:{id} | Schedule:{id} | Holiday:{code}
  modalidade: OnSite | Remote | null
  horas:      decimal   // horas úteis disponíveis naquele dia
}
```

Uma única função. Todo o resto do produto consome ela:

| Consumidor | Uso |
|---|---|
| Dashboard "equipe hoje" | Contagem por status |
| Timeline de férias | Barras + linha de cobertura |
| Detecção de conflito de férias | Quantas pessoas do setor já estão fora naquele intervalo |
| Escala | Onde há buraco / quem pode cobrir |
| Atribuição de tarefa | "O prazo cai num período em que o responsável está de férias" |
| Capacidade | O denominador do cálculo |
| Onboarding | Primeiro dia cai em dia útil? |

**Esta é a peça de domínio mais valiosa do sistema.** Se ela for calculada de três
formas diferentes em três telas, o produto perde credibilidade no primeiro uso real
— o gestor vê números diferentes e para de confiar.

### Precedência

Quando várias fontes se aplicam à mesma data, a ordem é fixa e explícita:

```
1. Feriado (nacional / estadual / municipal / da empresa)
2. Férias aprovadas
3. Licença / afastamento
4. Falta (justificada ou não)
5. Folga / compensação
6. Fora de escala (não é dia de trabalho para esta pessoa)
7. Registro de presença (presencial / home office)
8. Turno atribuído na escala          ← fonte primária das horas do dia
9. Padrão da jornada contratada       ← fallback, quando não há escala
```

Regra: a primeira que casar vence. Documentada aqui e implementada em um único
lugar.

Com Escalas na V1 ([ADR-0015](../adr/0015-regras-de-jornada-e-escala.md)), os níveis
6 e 8 deixam de ser detalhe: numa operação de plantão, **a escala é quem define
quantas horas a pessoa tem naquele dia**, e a jornada contratada vira apenas o
fallback de quem trabalha em horário administrativo.

### Onde mora

Serviço de leitura `AvailabilityService`, que **compõe** os contratos públicos de
`TimeOff`, `Scheduling` e `People`. **Não é um módulo com banco próprio na V1.**

```csharp
public interface IAvailabilityQuery
{
    Task<AvailabilityMap> GetAsync(
        IReadOnlyCollection<EmployeeId> employees,
        DateOnly from, DateOnly to,
        CancellationToken ct);
}
```

`AvailabilityMap` é indexado por `(EmployeeId, DateOnly)`. Sempre em lote — a
timeline pede 40 pessoas × 90 dias de uma vez, e a versão "uma pessoa por vez"
gera N+1 imediatamente.

Quando materializar numa tabela `availability_day`: só quando a timeline com ~200
pessoas × 12 meses ficar lenta de verdade, medida. Antes disso é otimização
prematura com custo de invalidação.

---

## 2. Pendências — o que torna o dashboard acionável

O briefing pede um dashboard "acionável, não apenas gráficos". Isso exige um
conceito de **pendência**: algo que espera uma ação humana, com dono, prazo e link
direto para resolver.

```
Pendência = {
  tipo:      DocumentExpiring | DocumentPending | VacationApproval |
             AbsenceApproval | TaskOverdue | ScheduleGap | OnboardingStep
  assunto:   referência ao objeto (documento, férias, tarefa…)
  dono:      quem resolve (usuário ou papel)
  gravidade: Critical | Warning | Info
  prazo:     data
  link:      rota do frontend que resolve
}
```

### Decisão: fan-out, não projeção

Existem duas formas de montar essa lista:

| Opção | Como | Veredito |
|---|---|---|
| **Fan-out** | Dashboard chama em paralelo `IPendingItems` de cada módulo, no mesmo banco | **Escolhida para V1/V1.5** |
| Projeção materializada | Cada módulo publica evento, um módulo "Inbox" mantém tabela consolidada | Só depois da extração de módulos |

Por quê fan-out: é **sempre consistente**. A tela mais visível do produto não pode
mostrar uma aprovação que já foi feita porque a projeção atrasou 3 segundos. E não
há projeção para reparar quando um evento se perde. Com 6 módulos, tenant pequeno,
mesmo banco e índices corretos, são 6 queries em paralelo — dezenas de milissegundos.

Quando trocar: quando um módulo virar serviço separado (aí o fan-out vira chamada
de rede) ou quando o p95 do dashboard passar de ~300 ms medidos.

**Não confundir com notificações.** Notificação é um evento entregue e lido (sino,
e-mail, push) — tem armazenamento próprio no módulo `Notifications`. Pendência é
estado atual derivado. São coisas diferentes; unificar as duas é um erro que se
paga depois.

---

## 3. Agregados por módulo

Um agregado = uma fronteira de transação e consistência. Referências entre módulos
são sempre por **id**, nunca por navegação EF.

### People

```
Employee (raiz)                      Tenant, código, nome, foto, CPF,
                                     admissão, desligamento, UserId? (nullable),
                                     ManagerId?, DepartmentId, TeamId?, PositionId
  ├─ Education                       formação
  ├─ EmployeeSkill                   competência + nível
  └─ EmploymentContract              jornada semanal, tipo, vigência

Department (raiz, hierárquico)       ParentId → árvore de setores
Team (raiz)                          pertence a Department
Position (raiz)                      cargo/função
Skill (raiz)                         catálogo do tenant
```

Notas:

- `Employee.UserId` é **nullable e permanece nullable** — ver P1 em
  [MVP](mvp-e-posicionamento.md#p1).
- `EmploymentContract` guarda a jornada (ex.: 44h/semana, seg–sex 08:00–17:00). É a
  base do cálculo de capacidade e o fallback da disponibilidade.
- Setor é árvore. Use `parent_id` + uma coluna `path` (`ltree` ou texto
  materializado) para consultar subárvore em uma query — "todo mundo abaixo de
  Operações" é uma consulta constante no produto.

### TimeOff

```
VacationEntitlement (raiz)   período aquisitivo: início, fim, dias de direito,
                             dias gozados, dias vendidos, dias em solicitação,
                             fim do período concessivo
VacationRequest (raiz)       períodos solicitados, status, aprovador,
                             abono pecuniário, adiantamento 13º, histórico
Absence (raiz)               tipo, intervalo, justificativa, anexo, aprovação
AbsenceType (raiz)           catálogo configurável: conta como ausência?
                             precisa aprovação? precisa documento? afeta férias?
Holiday (raiz)               nacional / estadual / municipal / da empresa
```

`AbsenceType` configurável é importante: cada empresa tem tipos próprios
("banco de horas", "doação de sangue", "licença nojo"). Enum fechado gera pedido de
customização na primeira semana.

### Work

```
TaskItem (raiz)     título, descrição, AssigneeId, prazo, prioridade,
                    estimativa?, status, checklist, TenantId
                    (V1.5: recorrência, dependências, anexos, comentários)
TaskBoard (raiz)    V1.5
```

### Documents

```
DocumentType (raiz)      catálogo do tenant: nome, obrigatório?, tem validade?,
                         dias de antecedência do alerta, exige aprovação?
Document (raiz)          OwnerRef (funcionário ou empresa), tipo, emissão,
                         validade, status, StorageKey, hash, validador, motivo
DocumentRequirement      qual tipo é exigido de quem (por cargo, setor ou todos)
```

`DocumentRequirement` é o que permite responder **"o que falta do João?"** — a
pergunta que vende o módulo. Sem ela, você só sabe o que existe, não o que deveria
existir. É a diferença entre um repositório de arquivos e um controle de
conformidade.

### Scheduling (V1)

```
ShiftTemplate (raiz)      turno nomeado: A = 07–19, B = 19–07, ADM = 08–17
                          com intervalo intrajornada e marcação de noturno
ScheduleCycle (raiz)      padrão recorrente: 12×36, 5×2, 6×1, semanal fixo
ScheduleAssignment        pessoa × data × turno, origem (gerado/manual/troca)
ShiftSwap (raiz)          troca ou substituição, com aprovação e motivo
CoverageRequirement       turno × setor/equipe × mínimo de pessoas
```

`CoverageRequirement` é o que transforma a escala de calendário em ferramenta: sem
ele o sistema mostra quem trabalha; com ele, mostra **onde a operação vai furar**.
É também o que a timeline de férias consome.

`ShiftSwap` como agregado próprio (e não um `update` no `ScheduleAssignment`) porque
troca de plantão é workflow com aprovação, histórico e auditoria — e é a operação
mais frequente do dia a dia do coordenador.

### Notifications

```
Notification (raiz)          destinatário, tipo, payload, canais, lido em
NotificationPreference       por usuário e tipo: in-app / e-mail / push
```

---

## 4. Capacidade e carga de trabalho

O briefing acerta ao rejeitar "produtividade". A implementação precisa sustentar
essa promessa. Ver [ADR-0013](../adr/0013-capacidade-sem-vigilancia.md).

### Fórmula

```
Capacidade(pessoa, semana) = Σ horas disponíveis por dia   ← vem de Disponibilidade
                             (horas de turno da escala, ou jornada contratada
                              como fallback; menos férias/ausência/folga/feriado)

Carga(pessoa, semana)      = Σ horas estimadas das tarefas abertas,
                             distribuídas nos dias úteis entre início e prazo

Utilização                 = Carga / Capacidade
```

Com escalas, a capacidade fica mais precisa: quem faz 12×36 tem 3 ou 4 turnos na
semana, não 5 dias de 8h. Derivar da escala em vez da jornada contratada é a
diferença entre um número defensável e um número que o coordenador desmente na hora.

### As três armadilhas

**1. Tarefa sem estimativa.** Na prática, a maioria não terá. Se você somar só as
que têm, a utilização mente para baixo e o gestor perde a confiança no número.

Regra: **nunca mostrar um percentual sozinho.** Sempre três números juntos:

```
João    7 tarefas · 32h estimadas · 3 sem estimativa    → 84% da capacidade
```

O "3 sem estimativa" é o que mantém o número honesto — e ainda estimula o
comportamento certo (estimar).

**2. Concentrar tudo no prazo.** Uma tarefa de 16h com prazo daqui a 10 dias não
carrega o dia do prazo — ela ocupa a janela. Distribua uniformemente pelos dias
úteis disponíveis entre início (ou hoje) e prazo. Simples e suficiente; nada de
otimizador.

**3. Tarefa atrasada.** Vencida ontem, ainda aberta: carrega **hoje**, integralmente.
É o que corresponde à realidade e o que faz o número ser útil.

### O que fica proibido no produto

- Ranking de funcionários por conclusão
- Métrica de horas trabalhadas ou tempo de resposta
- Percentual individual visível para pares
- Qualquer coisa que responda "quem produziu mais"

Utilização é visível para o **gestor**, sobre a **equipe**, olhando para o
**futuro**. O funcionário vê a própria. É uma linha de produto, e mantê-la é o que
torna a frase "não é vigilância" verdadeira em vez de marketing.

---

## 5. Regras de férias (CLT)

Detalhe completo, com artigos e casos de teste, em
[ADR-0014](../adr/0014-regras-clt-de-ferias.md).

Resumo do que o domínio precisa saber calcular:

| Regra | Efeito no sistema |
|---|---|
| Período aquisitivo de 12 meses a partir da admissão | Gera `VacationEntitlement` automaticamente |
| 30 dias corridos; faltas injustificadas reduzem (6–14 → 24; 15–23 → 18; 24–32 → 12; >32 → 0) | Saldo depende do módulo de ausências. **Aqui os módulos se conectam de verdade** |
| Período concessivo: 12 meses após o aquisitivo | Alerta de risco de pagamento em dobro — pendência crítica |
| Fracionamento em até 3 períodos, um ≥ 14 dias, demais ≥ 5 dias | Validação na solicitação |
| Início vedado nos 2 dias que antecedem feriado ou repouso semanal | Validação; precisa do calendário de feriados |
| Abono pecuniário: até 1/3 (10 dias) | Campo na solicitação, abate do saldo |
| Aviso ao empregado com 30 dias de antecedência | Alerta ao aprovar com data próxima |

**Isto é o fosso do produto.** Trello não faz, planilha não faz, e sistema de RH
genérico importado faz errado. É também o motivo de `TimeOff` ser o módulo que
merece Clean Architecture de verdade.

Duas cautelas:

- Regras devem ser **configuráveis por tenant**, não hard-coded: convenção coletiva
  pode ser mais benéfica que a CLT.
- Valide o conjunto com contabilidade/jurídico antes de anunciar conformidade.
  O sistema **calcula e alerta**; ele não substitui o contador — e a comunicação
  precisa deixar isso claro.

---

## 6. O fluxo que prova a integração

O exemplo do briefing ("João entra de férias"), traduzido em eventos concretos:

```
VacationRequest.Approve()
        │
        ├─ domain event  VacationApproved            (mesma transação, dentro de TimeOff)
        │     └─ debita saldo do VacationEntitlement
        │
        └─ integration event  VacationApproved       (via outbox)
              │
              ├─ Work           → tarefas do João no período ficam "em risco"
              │                    → sugere responsável: mesmo time, competência
              │                      compatível, utilização < 70%
              ├─ Scheduling     → marca buracos na escala do período
              ├─ Notifications  → avisa João, o gestor e quem herdar tarefa
              └─ Documents      → documento vencendo durante as férias?
                                   antecipa a cobrança
```

Nenhum desses consumidores lê a tabela de férias. Cada um reage ao evento e
resolve dentro do seu próprio contexto. É isso que mantém a extração futura
possível — e é exatamente a integração que o cliente percebe como "o sistema
pensa por mim".
