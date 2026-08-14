# ADR-0018 — Empresas independentes são tenants; subordinadas são unidades da árvore

**Status:** aceita · **Data:** 2026-08 · **Depende de:** [ADR-0003](0003-multi-tenancy.md)

## Contexto

O autor descreveu duas situações que convivem:

1. **Empresas totalmente independentes** — A e B não têm nada a ver uma com a outra.
2. **Empresas subordinadas** — C tem c1 e c2 abaixo dela, e alguém em C precisa
   enxergar o conjunto.

O caso 1 já funciona: cada empresa é um tenant, `User` é global e `Membership` liga a
pessoa a várias empresas ([ADR-0006](0006-identidade.md)). A pessoa loga uma vez e
alterna no menu.

O caso 2 é a decisão real, e tem duas saídas possíveis.

## As duas saídas

### Saída A — subordinada é um tenant, com RLS hierárquica

C, c1 e c2 seriam tenants; a policy do Postgres deixaria de ser
`tenant_id = current_setting(...)` e passaria a ser `tenant_id IN (subárvore do
tenant atual)`.

**Recomendo contra, e com convicção.** A RLS é a última linha de defesa do produto:
é o que continua protegendo quando alguém escrever um `FromSql` sem filtro ou usar
`IgnoreQueryFilters()` para destravar um bug. Ela é confiável hoje **porque é
burra** — uma comparação de igualdade, sem consulta, sem recursão, sem estado. Trocar
isso por uma travessia de árvore significa que todo erro nessa travessia vira
vazamento de dado de RH entre clientes diferentes, e é a classe de bug que ninguém
percebe até o cliente errado ver a folha do outro.

O ganho seria isolamento real entre c1 e c2. O custo é comprometer a única defesa que
funciona quando as outras falham.

### Saída B — subordinada é uma unidade dentro do tenant · **escolhida**

O tenant é a fronteira do **cliente** (contrato, cobrança, isolamento). Dentro dele,
a organização é uma árvore de unidades — que é a estrutura que já existe:

```
Tenant "Grupo C"                    ← fronteira de isolamento, RLS
  └── OrgUnit "C"            kind=Organizacao
        ├── OrgUnit "c1"     kind=Organizacao
        │     └── OrgUnit "Operações"   kind=Setor
        │           ├── "Turno A"       kind=Setor
        │           └── "Turno B"       kind=Setor
        └── OrgUnit "c2"     kind=Organizacao
```

Empresa A e empresa B, independentes, continuam sendo dois tenants.

## Decisão

1. **Tenant = cliente.** Nunca é atravessado. A policy do Postgres continua sendo
   igualdade simples, e continua sendo a defesa que não depende de ninguém acertar.
2. `Department` vira **`OrgUnit`**, com `Kind` (`Organizacao` | `Setor` | `Equipe`).
   A árvore, o caminho materializado e o filtro por subárvore **já entregues** servem
   sem mudança estrutural — só ganham o campo que diz o que cada nó é.
3. Quem enxerga o quê dentro do tenant é **autorização**, não isolamento: o
   `DataScope` da [ADR-0007](0007-autorizacao.md) passa a poder ser amarrado a um nó da
   árvore ("este usuário vê a subárvore de c1"). Filtro por subárvore com caminho
   materializado é uma cláusula de prefixo — o mesmo mecanismo que já roda.

## Consequências

- **Nada do Marco 1 é jogado fora.** `Department` já é árvore com caminho
  materializado, profundidade, ciclo barrado e reescrita de descendentes no move. A
  mudança é renomear e acrescentar `Kind`.
- Consolidado sai de graça: "todo mundo abaixo de C" é o filtro que já funciona.
- **O preço, dito com clareza:** c1 e c2 não são isoladas entre si no banco. Um
  usuário com escopo amplo no tenant vê as duas. Se um dia c1 precisar de isolamento
  real — porque foi vendida, ou porque o contrato exige — é migração de dados para um
  tenant novo. É trabalho, não é impossível, e é a troca certa: pagar por um caso raro
  quando ele acontecer, em vez de enfraquecer a segurança de todos hoje.
- Cobrança e limite de funcionários seguem o tenant. Um grupo é um contrato.

## Quando revisitar

Se aparecer exigência contratual ou regulatória de isolamento **físico** entre
subordinadas do mesmo grupo. Aí a resposta é tenant separado com relatório
consolidado montado fora do banco — nunca RLS hierárquica.
