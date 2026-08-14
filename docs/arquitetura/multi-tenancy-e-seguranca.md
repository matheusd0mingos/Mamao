# Multi-tenancy, autorização e auditoria

O Mamão guarda CPF, RG, endereço, atestado, ASO e licença médica de funcionários de
outras empresas. Um vazamento entre tenants não é bug: é incidente com notificação
à ANPD e fim do produto. As decisões abaixo assumem esse peso.

---

## 1. Modelo de isolamento

**Shared database, shared schema, discriminador `TenantId`.** Ver
[ADR-0003](../adr/0003-multi-tenancy.md) para as alternativas descartadas.

Regras não negociáveis:

1. Toda tabela tenant-owned tem `tenant_id uuid not null`.
2. **Todo índice de tabela tenant-owned começa por `tenant_id`.** Esquecer isso é o
   erro de performance mais comum desse modelo: o Postgres varre os dados de todos
   os tenants para filtrar depois.
3. `TenantId` **nunca** vem do request (rota, query, body ou header). Vem sempre do
   claim do token. Aceitar do cliente é oferecer o vazamento em bandeja.
4. Nada de `IgnoreQueryFilters()` fora de código administrativo explicitamente
   marcado e testado.

---

## 2. Três camadas de defesa

### Camada 1 — `ITenantContext`

```csharp
public interface ITenantContext
{
    TenantId Current { get; }   // lança se não resolvido
    bool     IsResolved { get; }
}
```

Escopo por request, preenchido por middleware a partir do claim `tenant_id`.
Em jobs de background, é preenchido explicitamente por tenant, um de cada vez —
job que roda "para todos" e monta consulta global é o outro vetor clássico de
vazamento.

### Camada 2 — EF Core

Filtro global aplicado por convenção a toda entidade que implemente `ITenantOwned`:

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    foreach (var et in b.Model.GetEntityTypes()
                 .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
    {
        b.Entity(et.ClrType).HasQueryFilter(
            BuildTenantFilter(et.ClrType));                       // e => e.TenantId == _tenant.Current
        b.Entity(et.ClrType).HasIndex(nameof(ITenantOwned.TenantId));
    }
}
```

E um `SaveChangesInterceptor` que **carimba** `TenantId` em toda entidade `Added` e
recusa qualquer `Modified` cujo `TenantId` divirja do contexto. Esquecer de setar o
tenant ao criar registro é o outro erro comum; o interceptor elimina a classe
inteira.

### Camada 3 — PostgreSQL Row-Level Security

Query filter é aplicação. Ele não protege `FromSqlRaw`, `ExecuteSql`, script de
manutenção, ferramenta de BI, nem o dia em que alguém usar `IgnoreQueryFilters` para
"resolver rápido". RLS protege no banco:

```sql
ALTER TABLE people.employees ENABLE ROW LEVEL SECURITY;
ALTER TABLE people.employees FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON people.employees
  USING      (tenant_id = current_setting('app.tenant_id', true)::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
```

A aplicação conecta com um role **sem** `BYPASSRLS` e define a variável por
transação:

```csharp
// DbConnectionInterceptor / a cada abertura de transação
await using var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT set_config('app.tenant_id', $1, true)";  // true = LOCAL
```

Cuidados reais:

- `set_config(..., true)` é **local à transação**. Com pool de conexões isso é
  obrigatório — a alternativa vaza a configuração para o próximo request que pegar
  a mesma conexão.
- O role de migration precisa ser diferente e ter permissão para alterar as tabelas.
- Migrations do EF precisam emitir o `ENABLE ROW LEVEL SECURITY` — gere por convenção
  a partir de `ITenantOwned`, não à mão tabela por tabela.

**Quando implementar:** V1 sobe com camadas 1 e 2. RLS entra **antes do primeiro
cliente pagante**, e é bloqueador de lançamento. Custo estimado: 1–2 dias. O
retorno é transformar "confiamos no código" em "o banco não permite".

### Camada 4 — os testes que sustentam as outras

```csharp
[Fact] // ArchitectureTests
public void Toda_entidade_tenant_owned_tem_query_filter() { … }

[Fact] // IntegrationTests
public async Task Usuario_do_tenant_A_nao_enxerga_dado_do_tenant_B()
{
    // cria dado nos dois, autentica como A, varre todos os endpoints de listagem,
    // afirma que nenhum id do tenant B aparece
}
```

O segundo teste deve ser **genérico e varrer os endpoints de listagem
automaticamente**, para que um endpoint novo esteja coberto no dia em que nasce.
É o teste mais valioso da suíte.

---

## 3. Identidade e pertencimento

```
User          (global)      e-mail, senha, MFA, nome
Tenant                      empresa, plano, status
Membership    (N:N)         UserId × TenantId × Role, convite, status
Employee      (People)      UserId? nullable
```

Por que `User` global com `Membership`: um contador ou consultor atende várias
empresas; um sócio tem duas empresas. Se o usuário for criado dentro do tenant,
essa pessoa precisa de duas senhas e o retrofit depois é migração de dados dolorosa.
O custo hoje é uma tabela a mais. Ver [ADR-0006](../adr/0006-identidade.md).

O token carrega `sub`, `tenant_id` (tenant **ativo**), `role`, `scope` e
`employee_id` quando existir. Trocar de empresa = novo token, não flag na sessão.

Refresh token com rotação e detecção de reuso. Access token curto (~15 min) para
que revogação de acesso (demissão!) tenha efeito rápido — cenário real e frequente
neste produto.

---

## 4. Autorização: permissão × escopo

RBAC puro não responde a pergunta central de um sistema de RH: *"posso ver o
atestado médico de quem?"*. São duas dimensões independentes.

### Permissão — o que a pessoa pode fazer

Claims granulares, agrupadas em papéis:

```
people.read      people.write      people.delete
timeoff.request  timeoff.approve
documents.read   documents.upload  documents.approve
work.read        work.assign
schedule.read    schedule.write
audit.read       settings.write    billing.manage
```

| Papel | Permissões |
|---|---|
| Owner | tudo |
| RH | pessoas, documentos, férias (aprovar), auditoria |
| Gestor | leitura e aprovação **no escopo da equipe** |
| Funcionário | o próprio registro, solicitar, enviar documento |

Papéis são um agrupamento de permissões, não um enum verificado no código.
`[Authorize(Policy = "documents.approve")]` sobrevive à criação de papel
customizado; `if (role == "RH")` não.

### Escopo — sobre quem

```csharp
public enum DataScope { Self, Team, Department, Company }
```

Resolvido uma vez por request e aplicado em toda consulta de lista:

```csharp
public interface IAccessScope
{
    Task<EmployeeFilter> ForAsync(string permission, CancellationToken ct);
}
```

`EmployeeFilter` vira predicado de consulta (`Self` → id próprio, `Team` → equipe
direta, `Department` → subárvore do setor, `Company` → todos). Centralizar isso
evita que cada endpoint reinvente o filtro — e que um deles reinvente errado.

Para acesso a **um** recurso, use autorização baseada em recurso do ASP.NET Core
(`IAuthorizationHandler<Requirement, Document>`), que consegue perguntar "este
documento é de alguém da minha equipe?".

### <a name="inventario"></a>Inventário de dado pessoal

A LGPD exige registro das operações de tratamento (art. 37), e a rotina de exclusão do
checklist abaixo é impossível de escrever sem saber **o que** existe. Este inventário é
mantido **junto com o código**, não montado no Marco 8 por arqueologia:

> **Regra:** todo campo novo que identifique uma pessoa entra nesta tabela **no mesmo
> commit** que o cria. É a única forma de ela não ficar desatualizada — e uma tabela
> desatualizada é pior que nenhuma, porque dá falsa segurança.

| Campo | Onde | Natureza | Na exclusão da conta |
|---|---|---|---|
| Nome completo | `people.employees` | Pessoal | Apagado |
| Matrícula | `people.employees` | Pessoal (identificador interno) | Apagado |
| **E-mail** | `people.employees` | Pessoal · **é chave de contato e de convite** | Apagado |
| Cargo, setor, gestor | `people.employees` | Pessoal (dado funcional) | Apagado |
| Admissão, desligamento | `people.employees` | Pessoal (dado funcional) | Apagado |
| E-mail e nome do usuário | `identity.users` | Pessoal | Ver nota sobre `User` global |
| *(previstos)* CPF, RG, foto | `people.employees` | Pessoal | Apagado |
| *(previstos)* ASO, atestado, licença | `documents` | **Sensível — saúde** | Apagado com o prefixo do tenant ([ADR-0010](../adr/0010-armazenamento-de-arquivos.md)) |

**Nota sobre `User` global:** a mesma pessoa pode ter `Membership` em várias empresas
([ADR-0006](../adr/0006-identidade.md)). Excluir um tenant apaga os vínculos daquele
tenant, **não** o `User` — que ainda pertence às outras. Apagar o `User` só é correto
quando o último vínculo cai. Errar isso derruba o acesso de alguém a uma empresa que
não pediu exclusão nenhuma.

**Nota sobre `Employee.Email`:** ele é opcional e permanece opcional, mas quando existe
é o endereço por onde saem avisos e, na V1.5, o convite de login. Numa exclusão parcial
("remova meus dados mas mantenha o histórico"), este é o primeiro campo a ir — é o único
que permite **contatar** a pessoa.

### Dados sensíveis

Documento de saúde (ASO, atestado, licença médica) merece tratamento além do papel:

- Visível apenas para RH e para o próprio funcionário. **Gestor direto não vê o
  conteúdo** — vê que existe e que está válido. Isso é boa prática de privacidade e
  argumento de venda para o comprador de RH.
- Todo acesso ao arquivo é registrado na auditoria, sempre.

---

## <a name="auditoria"></a>5. Auditoria

Append-only, no schema `audit`, uma tabela:

```
audit.entries
  id, tenant_id, occurred_at, actor_user_id, actor_name, actor_ip,
  action            ex.: "timeoff.vacation.approved"
  subject_type      "VacationRequest"
  subject_id
  subject_label     "Férias de Ana Lima 10/09–20/09"   ← legível sem join
  metadata jsonb    antes/depois quando relevante
  correlation_id
```

Decisões:

- **`subject_label` desnormalizado.** A auditoria precisa continuar legível depois
  que o funcionário for excluído. Sem isso, o histórico vira uma lista de GUIDs.
- Sem `UPDATE`/`DELETE`: `REVOKE` desses privilégios para o role da aplicação.
- Registra: aprovação/recusa (férias, documento, ausência), alteração de escala,
  **acesso a documento**, mudança de papel/permissão, exclusão de qualquer coisa,
  login e falha de login, exportação de dados.
- **Não** registra leitura comum de tela — volume enorme, valor baixo.
- Retenção mínima de 5 anos (alinha com prazos trabalhistas). Particione por ano
  quando o volume pedir.
- Escrita na **mesma transação** do fato. Auditoria via evento assíncrono é
  auditoria que pode faltar exatamente no caso que importa.

A tela de auditoria é V1.5, mas **o registro começa na V1** — histórico não se
gera retroativamente.

---

## 6. Checklist de segurança antes do primeiro cliente

- [ ] RLS habilitada em todas as tabelas tenant-owned
- [ ] Teste automatizado de vazamento cross-tenant varrendo endpoints de listagem
- [ ] Documentos servidos só por URL assinada e expirada; bucket/volume sem acesso público
- [ ] Rate limiting em login, refresh e upload (`AddRateLimiter`)
- [ ] Senhas com hash padrão do ASP.NET Identity; MFA opcional para Owner e RH
- [ ] Secrets fora do repositório; rotação documentada
- [ ] Backup criptografado, **restore testado de verdade**
- [ ] CSP, HSTS e cabeçalhos de segurança no Caddy
- [ ] Dependabot/`dotnet list package --vulnerable` e `npm audit` no CI
- [ ] [Inventário de dado pessoal](#inventario) conferido contra o schema real — cada
      coluna do banco que identifica alguém está na tabela, e vice-versa
- [ ] Política de retenção e rotina de exclusão de dados (LGPD), **derivada do inventário**
- [ ] Exclusão de conta testada de verdade, incluindo o caso do `User` com vínculo em
      outra empresa
- [ ] Registro do papel de operador/controlador e minuta de DPA para o cliente
  (ver [riscos](../riscos-e-pontos-de-atencao.md))
