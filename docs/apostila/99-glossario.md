# Glossário

Todo termo destacado ao longo da apostila, em ordem alfabética.---

**`$ref`** — referência a um schema definido em `components`. É o que evita repetir a
descrição do mesmo tipo em cada endpoint que o usa.

**ADR** (*Architecture Decision Record*) — documento curto registrando o que foi decidido,
por quê, e o que foi descartado. O Mamão tem 20 em [`docs/adr/`](../adr/).

**Advisory lock** — trava cooperativa do PostgreSQL. O Worker usa para que dois containers
subindo juntos não apliquem as mesmas migrations em paralelo.

**AOT** (*Ahead-Of-Time*) — compilação dos templates Angular em tempo de build. Erro de
template vira erro de compilação em vez de erro em execução.

**Binding** — ligação entre o TypeScript e o template. `[prop]="x"` manda valor;
`(evento)="f()"` recebe evento.

**Bundle** — o conjunto de arquivos JavaScript gerados pelo build.

**Cabeçalho de segurança** — resposta HTTP que instrui o navegador a restringir algo:
HSTS, `X-Frame-Options`, `nosniff`, `Referrer-Policy`. Configurados no Caddy, não na aplicação.

**Caddy** — servidor web e proxy reverso com HTTPS automático. Serve os dois domínios do
Mamão e encaminha `/api/*` para a aplicação.

**Camada (Docker)** — cada instrução do Dockerfile gera uma. São cacheadas: o Docker só
refaz a partir da primeira que mudou. Por isso a ordem das instruções importa.

**Central Package Management** — versões de pacote num arquivo só
(`Directory.Packages.props`), e os `.csproj` dizendo apenas o que usam. Evita dois projetos
com versões divergentes do mesmo pacote.

**Chunk** — pedaço separado do bundle, gerado por lazy loading. Um por tela, no Mamão.

**CI** (*Continuous Integration*) — automação que compila e testa a cada push. O do Mamão
está em `.github/workflows/ci.yml`.

**Clickjacking** — ataque em que o site é embutido num iframe invisível para induzir
cliques. Barrado por `X-Frame-Options: DENY`.

**Compose** — arquivo que descreve vários containers, com variáveis, volumes e
dependências, subidos com um comando.

**`computed`** — signal derivado de outros. Recalcula só quando uma dependência muda e
guarda o resultado.

**Container** — processo isolado que enxerga sistema de arquivos, rede e lista de processos
próprios. Não é máquina virtual: compartilha o núcleo do sistema.

**CORS** (*Cross-Origin Resource Sharing*) — mecanismo pelo qual um servidor autoriza
requisições de outra origem. O Mamão não usa: o proxy elimina a necessidade.

**CsvHelper** — biblioteca de parsing de CSV. No Mamão faz só o nível baixo (aspas,
delimitador dentro de campo); o mapeamento de cabeçalho é código próprio, porque é ali que
mora a tolerância a planilha suja.

**DbContext** — a sessão do Entity Framework com o banco. O Mamão tem um por módulo.

**Decorator** — anotação em classe (`@Component`, `@Injectable`). Equivale aos *attributes*
do C#.

**DI** (*Dependency Injection*) — o objeto declara o que precisa e o container entrega.
`inject()` no Angular; construtor no .NET.

**Diretiva** — comportamento aplicado a um elemento existente, sem template próprio.
Exemplo: `*mamaoHasPermission`.

**Dockerfile** — a receita de como construir uma imagem.

**DTO** (*Data Transfer Object*) — objeto que existe só para trafegar dados entre camadas.
No Mamão, gerados do OpenAPI, nunca escritos à mão no TypeScript.

**`effect`** — executa código quando um signal lido dentro dele muda. Para efeito colateral,
nunca para calcular valor.

**Fallback (SPA)** — configuração do servidor que devolve `index.html` para qualquer
caminho não encontrado, para o roteamento acontecer no navegador.

**FluentValidation** — biblioteca .NET para regras de validação. Alimenta o `fieldErrors`.

**FluentValidation** — biblioteca .NET de regras de validação. Produz o `fieldErrors` que o
formulário do Angular consome.

**Guard** — função que decide se uma rota pode ser ativada. `canMatch` age antes de a rota
ser escolhida; `canActivate`, depois.

**Healthcheck** — comando que o Docker executa dentro do container para saber se ele está
saudável. `depends_on` só espera de verdade quando combinado com `condition: service_healthy`.

**Hot reload** — recompilação e troca automática na tela ao salvar o arquivo, sem perder
estado.

**HSTS** (*Strict-Transport-Security*) — cabeçalho que força o navegador a usar só HTTPS
naquele domínio pelo período declarado. Difícil de desfazer: o navegador lembra.

**`HttpClient`** — o cliente HTTP do Angular. Passa pelos interceptors — motivo pelo qual o
Mamão não usa cliente gerado.

**ICU** — biblioteca de internacionalização. Sem ela (`InvariantGlobalization`),
`string.Normalize` vira no-op silencioso e comparação sem acento para de funcionar.

**Idempotente** — operação que, repetida, produz o mesmo resultado. Requisito para
consumidores de outbox, que garante entrega ao menos uma vez.

**Identificador tipado** — `readonly record struct EmployeeId(Guid Value)`. Impede trocar um
id por outro em tempo de compilação; exige um transformer para não virar `unknown` no contrato.

**Imagem** — o resultado congelado de um Dockerfile. Uma imagem gera muitos containers.

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

**Let's Encrypt** — autoridade certificadora gratuita usada pelo Caddy. Tem limite de
emissão (5 por domínio por semana), o que importa ao testar deploy.

**LINQ** — sintaxe de consulta do C#.

**MailKit** — cliente SMTP para .NET. Substitui o `SmtpClient` da BCL, que é obsoleto.

**Matcher** — em Caddy, a expressão que decide a quais requisições uma diretiva se aplica.
Pode ser nomeado (`@estaticos`) e reutilizado.

**Migration** — script versionado que altera o esquema do banco. O Worker aplica no startup.

**Minimal API** — forma de declarar endpoints no ASP.NET Core sem Controllers, com injeção
direta nos parâmetros.

**Monolito modular** — um único programa com fronteiras internas fortes, verificadas pelo
compilador e por testes de arquitetura. A escolha do Mamão.

**Monorepo** — um repositório com backend, frontend e infraestrutura juntos.

**Multi-estágio** — Dockerfile com mais de um `FROM`: um estágio compila, outro só roda. A
imagem final não carrega o SDK.

**NetArchTest** — biblioteca usada nos testes de arquitetura: verifica por reflexão quem
pode referenciar quem.

**Observable** — fluxo de valores ao longo do tempo (RxJS). `firstValueFrom` converte em
Promise.

**OOM killer** — mecanismo do Linux que mata um processo quando a memória acaba. Motivo dos
`mem_limit` no Compose.

**OpenAPI** — formato padrão para descrever uma API HTTP. Antes chamado Swagger.

**OpenTelemetry** — padrão aberto de traces e métricas. No Mamão desde o primeiro commit,
para não prender a observabilidade a um fornecedor.

**`operationId`** — nome da operação no OpenAPI, vindo de `.WithName()` no C#. Vira a chave
em `operations` no TypeScript gerado.

**Origem** — protocolo + domínio + porta. Base da *Same-Origin Policy*.

**Outbox** — padrão em que o evento é gravado na mesma transação do fato e publicado depois
por um processo separado, garantindo que não se perca.

**Pin transitivo** — fixar a versão de uma dependência da sua dependência. A forma correta
de tratar vulnerabilidade em pacote que você não escolheu.

**Pipe** — transformação aplicada só na exibição: `{{ data | date: 'dd/MM/yyyy' }}`.

**Preflight** — requisição `OPTIONS` que o navegador manda antes de uma chamada
cross-origin não simples.

**ProblemDetails** — formato padrão (RFC 7807) para erro em API HTTP. O Mamão acrescenta
`code`, `traceId` e `fieldErrors`.

**Promise** — promessa de um valor futuro. `await` espera sem travar o navegador.

**Proxy (dev)** — recurso do `ng serve` que encaminha `/api` para o backend, fazendo tudo
parecer vir da mesma origem.

**Proxy reverso** — servidor na frente da aplicação que recebe as requisições e decide para
onde encaminhar. "Reverso" porque fica do lado do servidor, não do cliente.

**Refresh token** — token de vida longa cuja única função é obter um access token novo.

**`required` (OpenAPI)** — lista de propriedades que sempre aparecem no JSON. Não tem
relação com ser anulável: um campo pode ser `required` **e** `nullable`.

**Result** — padrão em que a função devolve sucesso ou erro em vez de lançar exceção. No
Mamão, reservado para falha de negócio esperada; bug e infraestrutura continuam sendo exceção.

**RLS** (*Row-Level Security*) — recurso do PostgreSQL que filtra linhas por política, no
próprio banco. Terceira camada de isolamento entre empresas no Mamão.

**RxJS** — biblioteca de programação reativa. No Mamão, usada só onde há fluxo com tempo.

**Same-Origin Policy** — regra do navegador que impede uma página de ler respostas de outra
origem.

**Selector** — a etiqueta HTML de um componente. No Mamão, sempre com prefixo `mamao-`.

**Shouldly** — biblioteca de asserções dos testes. Escolhida no lugar de FluentAssertions,
que passou a exigir licença comercial a partir da v8.

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

**Testcontainers** — sobe containers reais durante os testes. No Mamão, um PostgreSQL de
verdade por execução, em vez de banco em memória.

**Teste de arquitetura** — teste que verifica estrutura em vez de comportamento: quem pode
referenciar quem, se todo índice começa por `tenant_id`. Substitui convenção que ninguém lembra.

**`traceId`** — identificador que liga a mensagem de erro na tela à linha exata no log do
servidor.

**`track`** — expressão obrigatória no `@for` que identifica cada item para o Angular
reaproveitar elementos.

**`TreatWarningsAsErrors`** — configuração que transforma aviso em erro de compilação. É o
que faz o alerta de pacote vulnerável quebrar o build em vez de rolar na tela.

**Tree-shaking** — remoção de código não utilizado durante o build.

**Volume** — armazenamento gerenciado pelo Docker que sobrevive à destruição do container.
Sem ele, recriar o container apaga o banco.

**XSS** (*Cross-Site Scripting*) — injeção de script malicioso numa página. A interpolação
do Angular escapa por padrão.

**xUnit** — o framework de teste usado no backend.

**Zone.js** — biblioteca que substituía funções do navegador para o Angular saber quando
verificar mudanças. Substituída por signals.

**Zoneless** — modo sem Zone.js. Bundle menor, redesenho preciso — e exige que estado de
tela esteja em signals.

---

**Voltar ao** [índice](README.md)
