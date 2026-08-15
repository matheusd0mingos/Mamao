# Capítulo 10 — HTTP, interceptors, autenticação e erros

> **Objetivo:** entender a corrente de interceptors, como o JWT vai e volta, como o token
> se renova sozinho, e como um erro do .NET vira mensagem em português na tela.

---

## 10.1 `HttpClient`

```typescript
providers: [
  provideHttpClient(withFetch(), withInterceptors([authInterceptor, problemInterceptor])),
]
```

`withFetch()` faz o Angular usar a API `fetch` do navegador em vez do velho
`XMLHttpRequest`. É o padrão moderno.

Uso básico:

```typescript
this.http.get<PagedEmployees>('/api/v1/employees')
```

Isso devolve um **Observable** (RxJS), não uma Promise. Observable suporta múltiplos
valores ao longo do tempo; uma requisição HTTP emite um valor e termina. O Mamão converte:

```typescript
return firstValueFrom(this.http.get<PagedEmployees>(this.base, { params }));
```

`firstValueFrom` pega o primeiro valor e vira Promise — o que permite `async/await` no
resto do código.

Parâmetros de query se montam com `HttpParams`, que é **imutável** (cada `.set()` devolve
um objeto novo — repare no `params =`):

```typescript
let params = new HttpParams()
  .set('page', page)
  .set('pageSize', pageSize)
  .set('includeInactive', includeInactive);

if (departmentId) {
  params = params.set('departmentId', departmentId);
}

if (search.trim()) {
  params = params.set('search', search.trim());
}
```

Vantagem sobre montar a string na mão: escapa caracteres especiais. Uma busca por
`João & Cia` não quebra a URL.

## 10.2 O que é um interceptor

Um interceptor fica **no meio do caminho** de toda requisição:

```
código da tela
      │
      ▼
┌───────────────────┐
│ authInterceptor   │  ← põe o token; no 401, tenta renovar
└─────────┬─────────┘
          ▼
┌───────────────────┐
│ problemInterceptor│  ← traduz o erro para uma forma única
└─────────┬─────────┘
          ▼
      a rede
```

Ele pode alterar a requisição na ida e a resposta na volta. É onde vive tudo que seria
repetitivo escrever em cada chamada.

## 10.3 O interceptor de autenticação

```typescript
const PUBLIC_PATHS = ['/api/v1/auth/login', '/api/v1/auth/register-company', '/api/v1/auth/refresh'];

/**
 * Anexa o bearer e, num 401, tenta rotacionar o refresh token uma vez antes de derrubar a
 * sessao. Access token e curto de proposito (revogacao de acesso precisa valer rapido),
 * entao renovar em silencio e o que torna isso usavel.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);

  if (PUBLIC_PATHS.some((path) => request.url.startsWith(path))) {
    return next(request);
  }

  const withToken = (token: string | null) =>
    token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(withToken(session.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !session.refreshToken) {
        return throwError(() => error);
      }

      return from(session.refresh()).pipe(
        switchMap((renovou) =>
          renovou ? next(withToken(session.accessToken)) : throwError(() => error),
        ),
      );
    }),
  );
};
```

Vamos por partes.

**1. Rotas públicas saem fora.** Login não tem token ainda; mandar o cabeçalho seria
estranho e, no caso do `/refresh`, causaria um laço — o refresh receberia 401, tentaria
refresh, receberia 401…

**2. `request.clone()`.** Requisições no Angular são **imutáveis**. Você não altera; você
cria uma cópia com o cabeçalho a mais.

**3. O 401 e o refresh.** Quando o servidor responde 401 (token expirado), o interceptor
tenta renovar e **refaz a requisição original**. Do ponto de vista da tela, nada aconteceu:
a chamada demorou um pouco mais e devolveu o dado.

### Por que dois tokens

| Token | Validade | Papel |
|---|---|---|
| **Access token** | minutos | Vai em toda requisição. Curto de propósito |
| **Refresh token** | dias | Só serve para obter um access novo |

O access é curto porque ele é **auto-suficiente**: o servidor valida a assinatura e confia,
sem consultar o banco. Isso é rápido, mas significa que revogar acesso não tem efeito
imediato — o token continua válido até expirar. Quanto mais curto, menor essa janela.

Sem o refresh automático, a pessoa seria deslogada a cada poucos minutos. Com ele, a
experiência é de sessão contínua e a janela de revogação continua pequena.

⚠️ **Limitação conhecida deste código:** se cinco requisições saírem juntas e todas
receberem 401, as cinco chamam `refresh()`. Um `refresh` em andamento não é compartilhado.
Na prática o Mamão não sofre com isso porque as telas fazem poucas chamadas simultâneas; em
um app com dez chamadas paralelas, você compartilharia a promessa do refresh. É honesto
saber onde a solução simples para de servir.

## 10.4 O interceptor de erro

```typescript
/**
 * Traduz ProblemDetails para uma forma unica. O formulario consome `fieldErrors`
 * diretamente — validacao de servidor aparecendo no campo certo, sem codigo por tela.
 */
export const problemInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      // Em `responseType: 'blob'` (download de modelo, documento, PDF de escala) o corpo do
      // ERRO tambem chega como Blob — o ProblemDetails vem dentro dele. Sem desembrulhar,
      // toda falha de download viraria "Erro inesperado" e esconderia o motivo real.
      if (error.error instanceof Blob) {
        return from(error.error.text()).pipe(
          switchMap((texto) => throwError(() => traduzir(error, parseOuVazio(texto)))),
        );
      }

      return throwError(() => traduzir(error, (error.error ?? {}) as Partial<ApiProblem>));
    }),
  );

function traduzir(error: HttpErrorResponse, body: Partial<ApiProblem>): ApiProblem {
  return {
    status: error.status,
    title: body.title ?? (error.status === 0 ? 'Sem conexao' : 'Erro inesperado'),
    detail:
      body.detail ??
      (error.status === 0
        ? 'Nao foi possivel falar com o servidor. Verifique sua conexao.'
        : 'Tente novamente. Se persistir, informe o codigo de rastreio.'),
    code: body.code,
    traceId: body.traceId,
    fieldErrors: body.fieldErrors,
  };
}
```

Três coisas boas aqui.

**`status === 0` é falta de rede.** Não é um código HTTP de verdade — é o que o navegador
reporta quando a requisição nem chegou ao servidor: Wi-Fi caiu, servidor fora, DNS falhou.
Merece uma mensagem própria, porque "tente novamente" para quem está sem internet é inútil.

**O caso do Blob** é uma armadilha excelente. Quando você pede `responseType: 'blob'` para
baixar um arquivo, o Angular trata **também o corpo do erro** como Blob. O ProblemDetails
com a explicação está lá dentro, em texto, mas embrulhado. Sem desembrulhar, todo download
que falha vira "Erro inesperado" — escondendo justamente o motivo.

**Toda tela recebe a mesma forma.** Depois desse interceptor, nenhum componente precisa
saber se o erro veio como JSON, como Blob ou como falha de rede. O `catch` recebe sempre um
`ApiProblem`.

## 10.5 O outro lado: o .NET produzindo o erro

```csharp
public sealed class UnhandledExceptionHandler(
    IHostEnvironment environment,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // Requisicao malformada nao e falha do servidor. O ASP.NET Core lanca
        // BadHttpRequestException quando falta um parametro obrigatorio ou o corpo nao
        // converte, e ela JA carrega o status certo — 400.
        if (exception is BadHttpRequestException requisicaoRuim)
        {
            logger.LogInformation(/* … */);
            // devolve 400 com code = "bad_request"
        }

        logger.LogError(exception, "Falha nao tratada em {Method} {Path}. TraceId {TraceId}.", …);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno",
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "Algo deu errado do nosso lado. Se persistir, informe o codigo abaixo.",
            Extensions = { ["traceId"] = traceId, ["code"] = "internal_error" },
        };
        // …
    }
}
```

Dois princípios embutidos:

**O `traceId` liga a tela ao log.** O usuário reporta o código; você busca no log e acha a
requisição exata, com a exceção completa. Sem isso, investigar erro de cliente vira
arqueologia.

**A mensagem muda por ambiente.** Em desenvolvimento você vê a exceção; em produção, não —
mensagem de exceção vaza nome de tabela, caminho de arquivo e às vezes dado de usuário.

⚠️ **Esse `if` do `BadHttpRequestException` é uma correção recente**, e o bug vale a pena.
O handler transformava **qualquer** exceção em 500 — inclusive as que já carregam o status
certo. Parâmetro de query obrigatório faltando virava "erro interno": mentia para o cliente
dizendo que a culpa era nossa, e enchia o log de `Error` com erro de digitação, escondendo
as falhas de verdade. Está contado por inteiro no [Capítulo 13](13-bugs-reais.md).

## 10.6 A sessão

```typescript
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly session = signal<StoredSession | null>(restore());

  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly tenantName = computed(() => this.session()?.tenantName ?? '');
  readonly permissions = computed(() => this.session()?.permissions ?? []);

  has(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  async login(email: string, password: string): Promise<void> {
    const auth = await firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/login', { email, password }),
    );
    this.store(auth);
  }

  private store(auth: AuthResponse): void {
    // … monta o objeto
    this.session.set(stored);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
  }
}
```

O `localStorage` guarda a sessão entre recarregamentos — sem ele, F5 deslogaria.

E a função `restore()` merece atenção:

```typescript
function restore(): StoredSession | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as StoredSession;
  } catch {
    localStorage.removeItem(STORAGE_KEY);
    return null;
  }
}
```

O `catch` que **apaga** o valor corrompido não é paranoia: se o formato mudar entre
versões, ou o valor for truncado, sem isso a aplicação quebraria no boot — antes de
qualquer tela — e a pessoa não teria como se recuperar sem limpar o navegador na mão.

### Sobre guardar token no `localStorage`

É uma escolha com trade-off conhecido: `localStorage` é acessível por JavaScript, então uma
falha de XSS permite roubar o token. A alternativa mais segura é cookie `HttpOnly`, que o
JavaScript não lê — mas exige lidar com CSRF e complica o refresh.

Para o estágio atual do Mamão, `localStorage` com access token curto é aceitável. Vale
saber que é uma escolha, e não a única opção.

## 10.7 Permissões no token

O login devolve, junto com o token, o que a pessoa pode fazer:

```json
{
  "accessToken": "eyJ…",
  "refreshToken": "…",
  "tenantId": "a1b2…",
  "tenantName": "Segurança Beta",
  "role": "Manager",
  "permissions": ["people.read", "people.write", "availability.read", "schedule.write"]
}
```

Isso alimenta os três mecanismos de tela que você já viu:

```typescript
// 1. a guarda de rota
canMatch: [permissionGuard('people.read')]

// 2. a diretiva no template
<a *mamaoHasPermission="'people.write'" routerLink="/pessoas/nova">Cadastrar</a>

// 3. condição no código
if (this.session.has('org.write')) { … }
```

E, pela enésima vez, porque é o que mais se esquece: **os três escondem. Quem protege é a
policy no endpoint.** Toda permissão verificada no Angular tem correspondente no .NET.

---

## Para fixar

1. **Por que `/api/v1/auth/refresh` está na lista de rotas públicas?**
   <details><summary>Resposta</summary>
   Para evitar laço: se o refresh levasse o access token expirado, receberia 401, o que
   dispararia outro refresh, e assim por diante.
   </details>

2. **O que significa `status === 0`?**
   <details><summary>Resposta</summary>
   Que a requisição não chegou ao servidor — sem rede, servidor fora, DNS falhando. Não é
   código HTTP; é o navegador reportando ausência de resposta.
   </details>

3. **Por que o erro precisa ser desembrulhado quando `responseType: 'blob'`?**
   <details><summary>Resposta</summary>
   Porque o Angular trata o corpo do erro com o mesmo `responseType` da requisição. O
   ProblemDetails chega dentro de um Blob, e sem ler o texto dele toda falha de download
   vira "Erro inesperado".
   </details>

4. **Qual a limitação do refresh implementado aqui?**
   <details><summary>Resposta</summary>
   Requisições paralelas que recebem 401 ao mesmo tempo disparam refreshes concorrentes,
   porque a promessa em andamento não é compartilhada.
   </details>

## Laboratório

1. Escreva um interceptor que registra no console método, URL e duração de cada requisição.
   Registre-o **antes** dos outros e observe a ordem dos logs.
2. Force um 401: no DevTools → Application → Local Storage, corrompa o `accessToken`.
   Recarregue e acompanhe na aba Network a sequência 401 → refresh → repetição.
3. Force `status === 0`: derrube a API e clique em algo. Veja a mensagem "Sem conexão".
4. **Reproduza a armadilha do Blob:** faça um endpoint de download que devolve 400 e chame
   com `responseType: 'blob'`. Sem o desembrulho, imprima o erro — você vai ver um Blob
   opaco em vez da mensagem.

---

**Anterior:** [Capítulo 9](09-o-contrato-openapi.md) ·
**Próximo:** [Capítulo 11 — Desenvolvimento vs. produção](11-dev-vs-producao.md)
