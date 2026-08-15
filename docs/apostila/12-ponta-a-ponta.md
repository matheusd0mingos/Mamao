# Capítulo 12 — Uma funcionalidade ponta a ponta

> **Objetivo:** seguir um dado só, da tabela no PostgreSQL até o pixel na tela, passando
> por todas as camadas — e entender o papel de cada uma.

Vamos rastrear **a listagem de funcionários**. É a funcionalidade mais simples do Mamão que
ainda assim atravessa tudo.

---

## 12.1 O caminho completo

```
 1. PostgreSQL             tabela people.employees, com RLS ligada
        ↓
 2. Employee.cs            a entidade de domínio
        ↓
 3. PeopleDbContext        o mapeamento EF Core
        ↓
 4. EmployeeService        a regra de negócio + paginação
        ↓
 5. EmployeeEndpoints      o endpoint HTTP
        ↓
 6. openapi.json           o contrato gerado
        ↓
 7. api-schema.d.ts        os tipos TypeScript gerados
        ↓
 8. EmployeesApi           a chamada HTTP
        ↓
 9. EmployeesStore         o estado da tela
        ↓
10. EmployeesPage          o template
        ↓
11. o navegador            a tabela
```

Onze camadas para mostrar uma lista. Parece exagero — e não é: cada uma tem um motivo que
vai ficar claro. (E note que três delas, 6 e 7 inclusive, são **geradas**, não escritas.)

## 12.2 Camadas 1 a 3 — o dado

A tabela é criada por migration, e cada uma delas liga a **Row-Level Security**:

```csharp
TenantRls.EnableFor(migrationBuilder, "people", "employees");
```

Isso faz o PostgreSQL só devolver linhas cuja `tenant_id` bate com a da sessão. É a terceira
camada de isolamento entre empresas — abaixo do filtro do EF Core e da checagem na
aplicação. Se o código errar, o banco ainda barra.

A entidade:

```csharp
public sealed class Employee
{
    public EmployeeId Id { get; private set; }
    public string FullName { get; private set; }
    public PositionId? PositionId { get; private set; }
    public DateOnly HiredOn { get; private set; }
    public DateOnly? TerminatedOn { get; private set; }
    public bool IsActive => TerminatedOn is null;
    // …
}
```

Repare em `private set`: ninguém altera um funcionário atribuindo propriedade solta. Existem
métodos (`ChangePosition`, `Terminate`) que validam antes. Isso é o que impede um objeto
existir em estado inválido.

## 12.3 Camada 4 — o serviço

```csharp
var query = dbContext.Employees.AsNoTracking();

if (!string.IsNullOrWhiteSpace(search))
{
    var term = $"%{search.ToLower()}%";
    query = query.Where(e =>
        EF.Functions.Like(e.NormalizedName, term) ||
        dbContext.Positions.Any(p => p.Id == e.PositionId && EF.Functions.Like(p.Name.ToLower(), term)));
}

var total = await query.CountAsync(ct);

var items = await query
    .OrderBy(e => e.FullName)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .Select(e => new EmployeeListItem(/* … */))
    .ToListAsync(ct);
```

Três coisas para notar.

**`AsNoTracking()`** — para leitura, diz ao EF Core que não precisa rastrear mudanças. Menos
memória, mais rápido. Use sempre que não for salvar.

**Paginação de verdade.** `Skip`/`Take` viram `OFFSET`/`LIMIT` no SQL. A alternativa —
trazer tudo e paginar em memória — funciona com 40 funcionários e derruba o servidor com
40.000.

**A ordem das operações importa mais do que parece.** `OrderBy` e `Take` vêm **antes** do
`Select`. Isso não é estilo: é um bug real do Mamão. Depois do `Select`, o LINQ perde a
capacidade de traduzir a ordenação para SQL e a requisição responde 500 — com o build verde.
Está contado no [Capítulo 13, caso 2](13-bugs-reais.md).

## 12.4 Camada 5 — o endpoint

```csharp
group.MapGet("/", async Task<IResult> (
    [AsParameters] ListEmployeesQuery query,
    EmployeeService service,
    CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(query, ct)))
    .WithName("listEmployees")
    .Produces<PagedResult<EmployeeListItem>>()
    .RequireAuthorization(Permissions.PeopleRead);
```

- `.RequireAuthorization(Permissions.PeopleRead)` — **esta** é a proteção real. A guarda no
  Angular esconde o menu; esta linha impede o acesso.
- `.Produces<...>()` — informa o OpenAPI qual o formato da resposta. É daqui que sai o tipo
  TypeScript.
- `CancellationToken ct` — se o usuário fecha a aba no meio, a consulta é cancelada em vez
  de continuar ocupando o banco.

## 12.5 Camadas 6 e 7 — o contrato (geradas)

```bash
dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"
cd web/mamao-web && npm run generate:api
```

Do `.Produces<PagedResult<EmployeeListItem>>()` nasce, no `openapi.json`, um schema
`PagedResultOfEmployeeListItem`, que vira um tipo TypeScript, que ganha apelido:

```typescript
export type PagedEmployees = Schemas['PagedResultOfEmployeeListItem'];
```

Nenhuma dessas duas camadas foi escrita por uma pessoa.

## 12.6 Camada 8 — a chamada

```typescript
@Injectable({ providedIn: 'root' })
export class EmployeesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/employees';

  list(search: string, includeInactive: boolean, page: number, pageSize: number,
       departmentId: string | null = null): Promise<PagedEmployees> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize)
      .set('includeInactive', includeInactive);

    if (departmentId) params = params.set('departmentId', departmentId);
    if (search.trim()) params = params.set('search', search.trim());

    return firstValueFrom(this.http.get<PagedEmployees>(this.base, { params }));
  }
}
```

Camada fina de propósito: nenhuma regra de negócio aqui. Traduz argumentos em query string
e devolve a promessa.

## 12.7 Camada 9 — o estado

```typescript
@Injectable({ providedIn: 'root' })
export class EmployeesStore {
  readonly items = signal<EmployeeListItem[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly search = signal('');
  readonly loading = signal(false);
  readonly error = signal<ApiProblem | null>(null);

  readonly isEmpty = computed(() => !this.loading() && this.items().length === 0);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await this.api.list(/* … */);
      this.items.set(result.items ?? []);
      this.total.set(result.total);
    } catch (problem) {
      this.error.set(problem as ApiProblem);
    } finally {
      this.loading.set(false);
    }
  }

  async setSearch(term: string): Promise<void> {
    this.search.set(term);
    this.page.set(1);        // ← detalhe que importa
    await this.load();
  }
}
```

`this.page.set(1)` ao buscar: sem isso, você está na página 5, digita uma busca que retorna
3 resultados, e a tela mostra a página 5 de 1 — ou seja, vazia. Parece que a busca não achou
nada.

## 12.8 Camada 10 — a tela

```html
@if (store.error(); as problema) {
  <div class="alert alert--danger">{{ problema.detail }}</div>
}

<div class="card">
  @if (store.loading()) {
    <p class="empty-state">Carregando…</p>
  } @else if (store.isEmpty()) {
    <div class="empty-state">
      @if (store.search()) {
        <p>Nenhum funcionário encontrado para "{{ store.search() }}".</p>
      } @else {
        <p><strong>Sua equipe ainda não está aqui.</strong></p>
        <p>Traga a planilha que você já usa — leva menos de um minuto.</p>
        <div *mamaoHasPermission="'people.write'" class="empty-state__acoes">
          <a class="btn btn--primary" routerLink="/pessoas/importar">Importar planilha</a>
          <a class="btn btn--ghost" routerLink="/pessoas/nova">Cadastrar uma pessoa</a>
        </div>
      }
    </div>
  } @else {
    <table class="data">
      <tbody>
        @for (pessoa of store.items(); track pessoa.id) {
          <tr>
            <td><a [routerLink]="['/pessoas', pessoa.id]">{{ pessoa.fullName }}</a></td>
            <td>{{ pessoa.positionName }}</td>
            <td class="muted">{{ pessoa.departmentName ?? '—' }}</td>
            <td>{{ pessoa.hiredOn | date: 'dd/MM/yyyy' }}</td>
          </tr>
        }
      </tbody>
    </table>
  }
</div>
```

E a classe, quase vazia:

```typescript
export class EmployeesPage implements OnInit {
  readonly store = inject(EmployeesStore);

  readonly resumo = computed(() => {
    const total = this.store.total();
    if (total === 0) return 'Nenhuma pessoa cadastrada';
    return total === 1 ? '1 pessoa cadastrada' : `${total} pessoas cadastradas`;
  });

  readonly busca = new FormControl('', { nonNullable: true });

  constructor() {
    this.busca.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((termo) => void this.store.setSearch(termo));
  }

  ngOnInit(): void {
    void this.store.load();
    void this.store.loadDepartments();
  }
}
```

### Dois detalhes de produto escondidos no código

**O estado vazio tem dois textos.** Vazio por busca e vazio por não haver nada são situações
diferentes: no primeiro caso a pessoa precisa saber que a busca não achou; no segundo, ela
precisa saber **o que fazer agora**. O comentário no código diz:

> *Vazio nunca e vazio: mostra o proximo passo concreto.*

E "Importar planilha" vem antes de "Cadastrar uma pessoa" de propósito:

> *Importar vem primeiro de proposito: quem esta avaliando o Mamao ja tem a equipe numa
> planilha e nao vai digitar 40 pessoas para experimentar.*

**O `resumo` evita o "(s)".**

> *"(s)" e preguica: o texto aparece em toda listagem e o usuario le todo dia.*

"1 pessoa cadastrada", não "1 pessoa(s) cadastrada(s)". Custa três linhas e aparece mil
vezes.

## 12.9 Onde mora cada responsabilidade

| Pergunta | Quem responde |
|---|---|
| Quem pode ver esta lista? | Endpoint (`RequireAuthorization`) — e o banco (RLS) |
| De qual empresa são estes dados? | RLS + filtro global do EF |
| Como ordenar e paginar? | Serviço (vira SQL) |
| Qual o formato do JSON? | O `record` C# → OpenAPI → TypeScript |
| A lista está carregando? | Store (signal) |
| Como isso aparece? | Template + CSS |
| Devo mostrar o botão "Cadastrar"? | Diretiva de permissão (conforto) |

O erro clássico de iniciante é misturar: paginar no frontend, decidir permissão no
template, formatar data no serviço. Cada mistura dessas parece economizar tempo hoje e
custa caro no primeiro requisito novo.

## 12.10 Como adicionar um campo — o roteiro completo

Suponha que você queira mostrar o **e-mail** na listagem.

```bash
# 1. C#: adicione ao record EmployeeListItem e ao Select do serviço
# 2. regenere o contrato
dotnet run --project src/Mamao.Api -- --generate-openapi "$PWD/web/openapi.json"
# 3. regenere os tipos
cd web/mamao-web && npm run generate:api
# 4. use no template
#    <td>{{ pessoa.email ?? '—' }}</td>
# 5. compile
npm run build
# 6. commite TUDO junto — C#, openapi.json, api-schema.d.ts e o template
```

Se você pular o passo 2 ou 3, o passo 4 não compila: o TypeScript não conhece `email`. O
erro aparece na sua máquina, não em produção. **É esse o valor da esteira toda.**

---

## Para fixar

1. **Por que `OrderBy` e `Take` vêm antes do `Select`?**
   <details><summary>Resposta</summary>
   Porque depois da projeção o LINQ pode não conseguir traduzir a ordenação para SQL,
   causando erro em tempo de execução — com o build verde.
   </details>

2. **Por que `page.set(1)` ao mudar a busca?**
   <details><summary>Resposta</summary>
   Porque o número de páginas muda com o filtro. Continuar na página 5 depois de uma busca
   com 3 resultados mostra uma lista vazia que parece "não encontrado".
   </details>

3. **Se a guarda do Angular já esconde a tela, por que o endpoint verifica de novo?**
   <details><summary>Resposta</summary>
   Porque o Angular roda na máquina do usuário e pode ser modificado por ele. A guarda evita
   frustração; a policy no endpoint é a que protege.
   </details>

## Laboratório

Trabalhe no Mamão de verdade:

1. Adicione o campo `email` à listagem, seguindo o roteiro de 12.10.
2. **Pule o passo 3 de propósito.** Leia o erro do compilador — ele diz exatamente que
   `email` não existe no tipo.
3. Adicione um filtro "somente admitidos este ano": um checkbox na tela, um signal no store,
   um parâmetro no `EmployeesApi`, um `Where` no serviço.
4. Escreva um teste no `Mamao.People.UnitTests` que prove a regra do filtro sem subir a API.

---

**Anterior:** [Capítulo 11](11-dev-vs-producao.md) ·
**Próximo:** [Capítulo 13 — Bugs reais e o que eles ensinam](13-bugs-reais.md)
