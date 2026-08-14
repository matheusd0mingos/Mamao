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

## Cronograma

- V1 (desenvolvimento): camadas 1, 2 e 4.
- **Antes do primeiro cliente pagante: camada 3 (RLS). Bloqueador de lançamento.**
