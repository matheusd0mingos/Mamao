# ADR-0003 — Multi-tenancy: shared schema + TenantId + RLS

**Status:** aceita · **Data:** 2026-08

## Contexto

SaaS B2B para empresas de 5 a 50 funcionários, hospedado inicialmente num VPS de nó
único. Os dados incluem CPF, RG, endereço, atestado e ASO — dado pessoal e, em
parte, **dado sensível de saúde** sob a LGPD.

Vazamento entre tenants é o risco existencial do produto.

## Opções

| Modelo | Isolamento | Custo operacional | Veredito |
|---|---|---|---|
| Banco por tenant | Máximo | Migration × N bancos, backup × N, conexões × N. Inviável em VPS único com centenas de tenants pequenos | Não |
| Schema por tenant | Alto | Migration × N schemas, catálogo do Postgres degrada com milhares | Não |
| **Shared schema + `TenantId`** | Depende da aplicação | Uma migration, um backup, pooling normal | **Sim, com RLS** |

Com tenants pequenos e numerosos (o perfil exato do Mamão), o custo por tenant é o
que decide. Banco por tenant só faz sentido com poucos clientes grandes.

## Decisão

Shared schema com `tenant_id` em toda tabela tenant-owned, protegido por **quatro**
camadas:

1. `ITenantContext` resolvido do claim JWT — **nunca** de parâmetro do request.
2. EF Core global query filter aplicado por convenção + `SaveChangesInterceptor`
   que carimba `TenantId` na criação e recusa alteração divergente.
3. **PostgreSQL Row-Level Security** com `current_setting('app.tenant_id')`
   definido por transação (`set_config(..., local := true)`).
4. Testes: teste de arquitetura que exige o filtro em todo tipo `ITenantOwned`, e
   teste de integração que varre os endpoints de listagem procurando vazamento.

Detalhes de implementação em
[multi-tenancy e segurança](../arquitetura/multi-tenancy-e-seguranca.md).

## Por que RLS mesmo tendo query filter

O query filter cobre consultas via `DbSet` do EF. Não cobre `FromSqlRaw`,
`ExecuteSqlRaw`, script de manutenção, relatório futuro, ferramenta de BI, nem o
dia em que alguém usar `IgnoreQueryFilters()` para destravar um bug.

RLS move a garantia da aplicação para o banco: mesmo com a query errada, o Postgres
não devolve linha de outro tenant. Custo estimado de 1–2 dias contra um risco de
consequência terminal.

## Consequências

- **Todo índice de tabela tenant-owned começa por `tenant_id`.** É a regra de
  performance que acompanha esta decisão.
- O role da aplicação **não pode** ter `BYPASSRLS`; migrations usam outro role.
- Jobs de background processam **um tenant por vez**, com o contexto definido.
  Job que consulta globalmente é o vetor de vazamento mais fácil de introduzir.
- Provisionamento de tenant é uma inserção, não uma migration. Onboarding em
  segundos.
- Cliente que exigir banco dedicado no futuro é atendido pelo mesmo código, com
  outra connection string — decisão de roteamento, não de arquitetura.

## Estado — implementado e verificado

As quatro camadas estão no código e a camada 3 foi exercitada contra um PostgreSQL de
verdade, com o mesmo role que a produção usa:

| Situação | Resultado |
|---|---|
| Sessão sem tenant definido (o job que esquece de setar) | **0 linhas** — falha fechada |
| Tenant da Alfa, consultando tudo por SQL cru | só as linhas da Alfa |
| Tenant da Alfa, pedindo explicitamente `WHERE tenant_id = <Beta>` | **0 linhas** |
| Tenant da Alfa, tentando `INSERT` para a Beta | recusado pelo Postgres |
| Dono das tabelas (superusuário) | vê tudo — é por isso que a API **não** usa esse role |

Três detalhes que só apareceram ao ligar de verdade:

**`NULLIF` na policy.** Sem tenant, `current_setting` devolve string vazia e `''::uuid`
**lança erro** em vez de negar. Com `NULLIF` vira `NULL`, a comparação dá falso e a
tabela não devolve linha — que é o comportamento seguro.

**O interceptor escreve sempre**, inclusive sem tenant resolvido. O Npgsql reusa
conexões do pool: sair sem escrever deixaria o tenant da requisição anterior valendo
para a próxima.

**Duas conexões.** O Worker conecta como dono (aplica migrations, cria o role, concede
acesso); a API conecta com `mamao_app`, sem `BYPASSRLS`. Apontar a API para o dono
desliga a camada 3 em silêncio — e há um teste que reprova o role se ele for
superusuário ou tiver `BYPASSRLS`.

O role é criado pelo **Worker a cada startup**, não por script de init do Postgres:
aquele roda uma vez só, na criação do volume, e trocar a senha depois exigiria recriar
o banco.
