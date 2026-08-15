# Glossário

Todo termo destacado ao longo da apostila, em ordem alfabética.

---

**ADR** (*Architecture Decision Record*) — documento curto registrando o que foi decidido,
por quê, e o que foi descartado. O Mamão tem 20 em [`docs/adr/`](../adr/).

**AOT** (*Ahead-Of-Time*) — compilação dos templates Angular em tempo de build. Erro de
template vira erro de compilação em vez de erro em execução.

**Binding** — ligação entre o TypeScript e o template. `[prop]="x"` manda valor;
`(evento)="f()"` recebe evento.

**Bundle** — o conjunto de arquivos JavaScript gerados pelo build.

**CI** (*Continuous Integration*) — automação que compila e testa a cada push. O do Mamão
está em `.github/workflows/ci.yml`.

**Chunk** — pedaço separado do bundle, gerado por lazy loading. Um por tela, no Mamão.

**`computed`** — signal derivado de outros. Recalcula só quando uma dependência muda e
guarda o resultado.

**CORS** (*Cross-Origin Resource Sharing*) — mecanismo pelo qual um servidor autoriza
requisições de outra origem. O Mamão não usa: o proxy elimina a necessidade.

**DbContext** — a sessão do Entity Framework com o banco. O Mamão tem um por módulo.

**Decorator** — anotação em classe (`@Component`, `@Injectable`). Equivale aos *attributes*
do C#.

**Diretiva** — comportamento aplicado a um elemento existente, sem template próprio.
Exemplo: `*mamaoHasPermission`.

**DI** (*Dependency Injection*) — o objeto declara o que precisa e o container entrega.
`inject()` no Angular; construtor no .NET.

**DTO** (*Data Transfer Object*) — objeto que existe só para trafegar dados entre camadas.
No Mamão, gerados do OpenAPI, nunca escritos à mão no TypeScript.

**`effect`** — executa código quando um signal lido dentro dele muda. Para efeito colateral,
nunca para calcular valor.

**Fallback (SPA)** — configuração do servidor que devolve `index.html` para qualquer
caminho não encontrado, para o roteamento acontecer no navegador.

**FluentValidation** — biblioteca .NET para regras de validação. Alimenta o `fieldErrors`.

**Guard** — função que decide se uma rota pode ser ativada. `canMatch` age antes de a rota
ser escolhida; `canActivate`, depois.

**Hot reload** — recompilação e troca automática na tela ao salvar o arquivo, sem perder
estado.

**`HttpClient`** — o cliente HTTP do Angular. Passa pelos interceptors — motivo pelo qual o
Mamão não usa cliente gerado.

**`inject()`** — obtém uma dependência. Só funciona em contexto de injeção.

**`input()` / `output()`** — comunicação entre componentes: pai → filho e filho → pai.

**Interceptor** — função que fica no meio do caminho das requisições HTTP, podendo alterar
a ida e a volta.

**Interpolação** — `{{ expressao }}` no template. O Angular escapa o resultado, o que
protege contra XSS.

**IQueryable vs. IEnumerable** — mesma sintaxe LINQ, naturezas diferentes: o primeiro vira
SQL (nem tudo é traduzível), o segundo executa em memória. Origem do bug do Capítulo 13.

**JWT** (*JSON Web Token*) — token assinado que carrega quem é o usuário e o que pode
fazer. Validado sem consulta ao banco, por isso é curto.

**Lazy loading** — carregar o código de uma tela só quando ela é acessada.

**LINQ** — sintaxe de consulta do C#.

**Migration** — script versionado que altera o esquema do banco. O Worker aplica no startup.

**Monorepo** — um repositório com backend, frontend e infraestrutura juntos.

**Observable** — fluxo de valores ao longo do tempo (RxJS). `firstValueFrom` converte em
Promise.

**OpenAPI** — formato padrão para descrever uma API HTTP. Antes chamado Swagger.

**Origem** — protocolo + domínio + porta. Base da *Same-Origin Policy*.

**Outbox** — padrão em que o evento é gravado na mesma transação do fato e publicado depois
por um processo separado, garantindo que não se perca.

**Pipe** — transformação aplicada só na exibição: `{{ data | date: 'dd/MM/yyyy' }}`.

**Preflight** — requisição `OPTIONS` que o navegador manda antes de uma chamada
cross-origin não simples.

**ProblemDetails** — formato padrão (RFC 7807) para erro em API HTTP. O Mamão acrescenta
`code`, `traceId` e `fieldErrors`.

**Promise** — promessa de um valor futuro. `await` espera sem travar o navegador.

**Proxy (dev)** — recurso do `ng serve` que encaminha `/api` para o backend, fazendo tudo
parecer vir da mesma origem.

**Refresh token** — token de vida longa cuja única função é obter um access token novo.

**RLS** (*Row-Level Security*) — recurso do PostgreSQL que filtra linhas por política, no
próprio banco. Terceira camada de isolamento entre empresas no Mamão.

**RxJS** — biblioteca de programação reativa. No Mamão, usada só onde há fluxo com tempo.

**Same-Origin Policy** — regra do navegador que impede uma página de ler respostas de outra
origem.

**Selector** — a etiqueta HTML de um componente. No Mamão, sempre com prefixo `mamao-`.

**`signal`** — caixa reativa que sabe quem depende dela e avisa quando o valor muda.
Compara por **referência**.

**SPA** (*Single Page Application*) — aplicação que carrega uma vez e reescreve a tela no
navegador, buscando só dados no servidor.

**SSR** (*Server-Side Rendering*) — renderizar no servidor para acelerar o primeiro
carregamento. O Mamão não usa: é sistema atrás de login.

**Standalone** — componente que declara os próprios `imports`, sem `NgModule`. Padrão desde
o Angular 17.

**Store** — serviço que guarda o estado de uma feature em signals. Alternativa enxuta ao
NgRx.

**Strict mode** — configuração do TypeScript que exige tratar nulos e proíbe `any`
implícito.

**Tenant** — a empresa cliente num sistema multi-inquilino. Todo dado do Mamão pertence a
um.

**`track`** — expressão obrigatória no `@for` que identifica cada item para o Angular
reaproveitar elementos.

**`traceId`** — identificador que liga a mensagem de erro na tela à linha exata no log do
servidor.

**Tree-shaking** — remoção de código não utilizado durante o build.

**XSS** (*Cross-Site Scripting*) — injeção de script malicioso numa página. A interpolação
do Angular escapa por padrão.

**Zone.js** — biblioteca que substituía funções do navegador para o Angular saber quando
verificar mudanças. Substituída por signals.

**Zoneless** — modo sem Zone.js. Bundle menor, redesenho preciso — e exige que estado de
tela esteja em signals.

---

**Voltar ao** [índice](README.md)
