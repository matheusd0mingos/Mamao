# Estratégia de testes

Regra: **teste comportamento e regra de negócio.** Não teste getter, setter,
mapeamento de DTO nem o EF Core.

Com um desenvolvedor, cada teste precisa se pagar. Um teste que quebra a cada
refatoração sem apontar bug real é passivo, não ativo.

---

## Pirâmide, com pesos

| Camada | Ferramenta | Onde concentrar |
|---|---|---|
| **Unitários de domínio** | xUnit + FluentAssertions | Férias/CLT, conflito, capacidade, disponibilidade, escala. **A maior parte do esforço** |
| **Arquitetura** | xUnit + NetArchTest | Fronteira de módulo, filtro de tenant, convenções |
| **Integração / API** | `WebApplicationFactory` + Testcontainers | Fluxo ponta a ponta por caso de uso, autorização, multi-tenancy |
| **Frontend** | Vitest/Karma + Testing Library | Componentes do design system, validação de formulário, stores |
| **E2E** | Playwright | Apenas os 5 caminhos que não podem quebrar |

---

## 1. Domínio — onde está o valor

`TimeOff` merece a maior densidade de testes do sistema. Nomeie em português: a
suíte vira documentação da regra, legível por um contador — e é literalmente o
artefato que você usa para validar as regras com ele.

```csharp
public class FeriasFracionamentoTests
{
    [Fact]
    public void Nao_permite_fracionar_em_mais_de_tres_periodos() { … }

    [Fact]
    public void Exige_que_um_dos_periodos_tenha_ao_menos_quatorze_dias() { … }

    [Fact]
    public void Exige_que_os_demais_periodos_tenham_ao_menos_cinco_dias() { … }

    [Fact]
    public void Nao_permite_inicio_nos_dois_dias_que_antecedem_feriado() { … }

    [Theory]
    [InlineData(0,  30)] [InlineData(6,  24)]
    [InlineData(15, 18)] [InlineData(24, 12)] [InlineData(33, 0)]
    public void Reduz_dias_de_direito_conforme_faltas_injustificadas(int faltas, int dias) { … }
}
```

Domínio sem dependência de infraestrutura: sem `DbContext`, sem `HttpClient`,
`IClock` injetado. Se um teste de regra precisa de banco, o modelo está com a regra
no lugar errado.

---

## 2. Arquitetura — as regras que se impõem sozinhas

Os testes mais baratos e mais valiosos do projeto. Cada um substitui uma convenção
que ninguém lembra às 23h de sexta.

```csharp
[Fact]
public void Modulos_nao_referenciam_internals_de_outros_modulos()
{
    // People.* não pode referenciar TimeOff.Domain/Application/Infrastructure — só Contracts
}

[Fact]
public void Toda_entidade_tenant_owned_possui_query_filter() { … }

[Fact]
public void Toda_entidade_tenant_owned_possui_indice_comecando_por_tenant_id() { … }

[Fact]
public void Handlers_de_integration_event_sao_registrados_no_DI() { … }

[Fact]
public void Nenhum_endpoint_de_listagem_ignora_o_access_scope() { … }
```

---

## 3. Integração — Testcontainers

Postgres real, sem mock de banco e sem SQLite in-memory (que diverge do Postgres
exatamente onde importa: RLS, `jsonb`, tipos de data, colação).

```csharp
public class MamaoApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db =
        new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // migrations de todos os módulos + role sem BYPASSRLS
    }
}
```

Cobrir por caso de uso, não por endpoint:

- Solicitar férias → gestor aprova → saldo debitado → evento no outbox → tarefa
  marcada em risco
- Upload de documento → aprovação → validade → job de vencimento → notificação
- Admissão → exigências de documento geradas → período aquisitivo criado

O terceiro tipo é o mais valioso: ele prova que os módulos conversam, que é a tese
inteira do produto.

### O teste mais importante da suíte

```csharp
[Fact]
public async Task Nenhum_endpoint_de_listagem_vaza_dado_entre_tenants()
{
    // cria dados nos tenants A e B
    // autentica como A
    // descobre por reflexão todos os endpoints GET de listagem
    // chama cada um e afirma que nenhum id do tenant B aparece na resposta
}
```

Genérico e por descoberta, para que **endpoint novo já nasça coberto**. É o único
teste cuja ausência pode encerrar o produto.

---

## 4. Mensageria

- Handler é testado como unidade: dado o evento, o estado muda como esperado.
- Idempotência tem teste próprio: processar duas vezes produz o mesmo estado.
- Um teste de integração cobre o ciclo completo: `Enqueue` → publisher → consumidor
  → efeito.

---

## 5. Frontend

- **Componentes do design system**: teste comportamento e acessibilidade (foco,
  teclado, `aria`). São reusados em todo lugar; um bug aqui aparece em 30 telas.
- **Formulários**: validação e mapeamento de erro do `ProblemDetails`.
- **Stores**: transição de estado, incluindo o caminho de rollback da atualização
  otimista.
- **Não teste** template trivial nem o Angular.

E2E (Playwright), apenas cinco fluxos:

1. Login → dashboard
2. Cadastrar funcionário
3. Solicitar e aprovar férias
4. Enviar e aprovar documento
5. Concluir tarefa em "Meu dia"

Mais que isso vira suíte lenta e instável, e suíte instável acaba desligada — que é
pior do que não ter.

---

## No CI

```
dotnet test                       unitários + arquitetura + integração
npm run test -- --watch=false     frontend
npx playwright test               E2E, só em main
```

Sem meta numérica de cobertura. Cobertura alta em código de mapeamento não protege
nada; um teste bem escrito da regra do art. 134 §1º protege muito. A pergunta certa
não é "quanto está coberto?", é "se eu quebrar esta regra, algum teste falha?".
