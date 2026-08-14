# ADR-0002 — Um banco, um schema por módulo, um DbContext por módulo

**Status:** aceita · **Data:** 2026-08

## Contexto

Modular monolith ([ADR-0001](0001-modular-monolith.md)) com intenção de extrair
módulos no futuro. A pergunta é onde traçar a linha na camada de dados.

## Opções

| Opção | Avaliação |
|---|---|
| Um `DbContext`, um schema `public` | Mais simples hoje. Nada impede `JOIN` entre módulos, e o primeiro atalho sob pressão inviabiliza a extração para sempre |
| **Um banco, um schema e um `DbContext` por módulo** | **Escolhida** |
| Um banco por módulo | Perde transação local, exige coordenação distribuída. Todo o custo da extração sem nenhum benefício |

## Decisão

Um banco PostgreSQL. Schemas `people`, `work`, `timeoff`, `documents`,
`scheduling`, `notifications`, `identity`, `audit`, `messaging`. Um `DbContext`
por módulo, com `HasDefaultSchema` e migrations próprias.

## Motivo

O custo hoje é uma linha por `DbContext` e uma cadeia de migration por módulo.
Em troca:

- **O `JOIN` entre módulos deixa de ser possível por acidente.** Ele exige um
  `FromSqlRaw` explícito e feio, que salta aos olhos em code review. Convenção
  documentada não sobrevive à sexta-feira à noite; ausência de `DbSet` sobrevive.
- Migrations independentes por módulo — sem conflito de arquivo entre features.
- No dia da extração, `pg_dump --schema=scheduling` move o módulo inteiro. Não há
  desemaranhar tabela por tabela.
- Ainda existe **uma transação** enquanto for um banco só: `SaveChanges` de dois
  contextos na mesma conexão participa da mesma transação. Não se perde
  consistência.

## Consequências

- Não há foreign key entre schemas de módulos. Referências entre módulos são por
  id, com integridade garantida pelo domínio e pelos eventos. É o mesmo contrato
  que valeria entre serviços — só que testado desde já.
- Consulta que precisa de dados de dois módulos passa pelo contrato do outro
  ([ADR-0004](0004-comunicacao-entre-modulos.md)), não por SQL.
- Registro de migration é por módulo (tabela `__EFMigrationsHistory` no schema do
  módulo).
- Tabelas transversais (`messaging.outbox`, `audit.entries`) são mapeadas por
  vários contextos deliberadamente — é a exceção conhecida e justificada.

## Quando revisitar

Nunca, na prática. Esta decisão é o alicerce da extração futura; desfazê-la
significa desistir dela.
