# Frontend — Angular

Angular é a escolha certa aqui: formulário pesado, tabela densa, permissão por tela,
aplicação de vida longa e um único desenvolvedor querendo consistência sem decidir
stack a cada feature. E atende o objetivo pessoal de ampliar experiência além de
React.

---

## Decisões

Ver [ADR-0008](../adr/0008-frontend-angular.md).

| Tema | Decisão |
|---|---|
| Componentes | Standalone, sempre. Sem `NgModule` |
| Estado local | Signals |
| Estado de servidor | `httpResource` / service com signal + `HttpClient`. Sem NgRx |
| Assíncrono | RxJS onde é fluxo (busca com debounce, websocket, polling). Signal no resto |
| Formulários | Reactive Forms tipados |
| Controle de fluxo | `@if` / `@for` / `@switch` |
| Detecção de mudança | `OnPush` em todo componente; zoneless quando o projeto estiver estável |
| Roteamento | Router com lazy loading por feature e `canMatch` para permissão |
| Componentes visuais | **Próprios**, sobre Angular CDK |
| HTTP | `provideHttpClient(withInterceptors([auth, tenant, error, correlation]))` |
| DTOs | Gerados do OpenAPI. Ver [ADR-0009](../adr/0009-cliente-gerado-do-openapi.md) |

---

## CDK sim, Material não

Esta é a decisão que mais impacta o resultado visual.

**Angular Material** é uma implementação de Material Design. Tematizar até não
parecer Material significa sobrescrever tokens internos, brigar com densidade,
elevação, ripple e sobrescrita de `::ng-deep` — e cada versão maior reabre a briga.
Você pediu explicitamente que o Mamão não pareça "Angular Material genérico".

**Angular CDK** é o oposto: comportamento sem aparência. É exatamente o que a regra
"não reinventar plumbing" recomenda usar.

Do CDK, use sem hesitar:

| CDK | Onde |
|---|---|
| `DragDrop` | Kanban (V1.5), reordenar checklist, mover pessoa entre turnos |
| `Overlay` | Popover da timeline, menus, diálogos, toast de undo |
| `A11y` | Foco, `LiveAnnouncer`, navegação por teclado da fila de aprovações |
| `Scrolling` | Virtual scroll na timeline e nas listas grandes |
| `Table` | Estrutura de tabela sem estilo imposto |
| `Portal`, `Layout`, `Clipboard` | Diversos |

Componentes próprios necessários no MVP (~15, e é um investimento de 1–2 semanas
que se paga em todas as telas seguintes):

```
Button · Input · Select · DatePicker · DateRangePicker · Checkbox · Radio
Avatar · Badge/Status · Card · Table · Pagination · EmptyState
Dialog · Toast (com undo) · Popover · Tabs · Tooltip · Skeleton
```

Exceção pragmática: se o `DatePicker` próprio se mostrar caro (é sempre mais caro
do que parece), use o do Material **só** para ele, isolado, e siga com o resto
próprio. Não é incoerência — é escolher onde gastar.

---

## Estrutura

```
web/mamao-web/src/app/
├── core/
│   ├── auth/          serviço de sessão, guards, troca de tenant
│   ├── http/          interceptors, client gerado do OpenAPI
│   ├── tenant/        tenant ativo
│   └── config/
├── shared/
│   ├── ui/            design system (os componentes acima)
│   ├── directives/    *hasPermission, *hasScope
│   ├── pipes/         data pt-BR, duração, nome curto, CPF
│   └── utils/
├── layout/            shell, sidebar, header, breadcrumb
└── features/
    ├── dashboard/
    ├── people/
    ├── tasks/
    ├── vacations/
    ├── absences/
    ├── documents/
    ├── approvals/
    ├── schedules/     V1.5
    ├── onboarding/    V1.5
    └── settings/
```

Cada feature: `routes.ts` (lazy), `pages/`, `components/`, `<feature>.store.ts`,
`<feature>.api.ts`.

---

## Estado sem NgRx

```typescript
@Injectable()
export class VacationsStore {
  private readonly api = inject(VacationsApi);

  readonly filters = signal<VacationFilters>({ status: 'pending' });
  readonly pending = signal(false);
  readonly items   = signal<VacationRequest[]>([]);

  readonly conflicts = computed(() =>
    this.items().filter(v => v.conflicts.length > 0));

  async load(): Promise<void> {
    this.pending.set(true);
    try   { this.items.set(await this.api.list(this.filters())); }
    finally { this.pending.set(false); }
  }

  async approve(id: string): Promise<void> {
    const snapshot = this.items();
    this.items.update(xs => xs.filter(x => x.id !== id));   // otimista
    try   { await this.api.approve(id); }
    catch { this.items.set(snapshot); throw; }              // e o toast oferece retry
  }
}
```

Isso cobre a complexidade real do Mamão. NgRx acrescenta actions, reducers,
effects e selectors para resolver problemas — time grande, estado global
compartilhado, time-travel debugging — que você não tem. Se um dia tiver, migrar
uma feature de cada vez é viável. O caminho contrário não é.

Atualização otimista + undo (o princípio de UX) casa naturalmente com signals:
remova da lista, chame a API, restaure em caso de erro.

---

## Permissões no frontend

```html
<button *hasPermission="'timeoff.approve'" (click)="approve()">Aprovar</button>
```

```typescript
export const canMatchPermission = (permission: string): CanMatchFn =>
  () => inject(SessionService).has(permission);
```

Regra: o frontend esconde para **não frustrar**; o backend impede para **proteger**.
Toda verificação de tela tem obrigatoriamente a contrapartida no endpoint. Uma
diretiva de permissão sem policy correspondente no servidor é uma falha de
segurança disfarçada de feature.

---

## Contrato com o backend

Cliente gerado do OpenAPI no CI e commitado:

```
dotnet run --project src/Mamao.Api -- --generate-openapi > web/openapi.json
npx openapi-typescript-codegen --input web/openapi.json --output src/app/core/http/generated
```

Se o gerado mudar e ninguém tiver atualizado o frontend, o build TypeScript quebra —
que é exatamente o momento certo de descobrir. Escrever DTO à mão adia esse
feedback para produção.

Erros: um interceptor traduz `ProblemDetails` para uma estrutura única
(`{ title, detail, fieldErrors }`) e o formulário consome `fieldErrors` diretamente.
Validação de servidor aparecendo no campo certo, sem código por tela.

---

## Desempenho e mobile

- Lazy loading por feature; o bundle inicial carrega shell + dashboard.
- Virtual scroll na timeline, lista de pessoas e auditoria.
- `NgOptimizedImage` nas fotos de perfil.
- **PWA na V1**: "Meu dia" precisa abrir bem no celular do funcionário. PWA resolve
  sem um segundo produto para manter.
- Mobile-first de verdade só em: Meu dia, minhas pendências, solicitar férias, enviar
  documento. Telas de gestão (timeline, escala, importação) são desktop — e tentar
  fazê-las responsivas de forma completa é esforço desperdiçado.
- Locale `pt-BR` registrado uma vez; nada de formatar data à mão.
- Strings em chave de i18n desde o começo, publicando só pt-BR
  ([ADR-0012](../adr/0012-idioma.md)).
