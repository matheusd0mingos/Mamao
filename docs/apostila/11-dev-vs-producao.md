# Capítulo 11 — Desenvolvimento vs. produção: proxy, CORS e Caddy

> **Objetivo:** entender por que o Mamão não precisa de CORS, o que é o *SPA fallback*, e o
> que muda quando o código sai da sua máquina.

---

## 11.1 A regra que causa tudo isso

O navegador impõe a **Same-Origin Policy**: uma página só pode fazer requisições para a
mesma **origem** de onde veio.

Origem = **protocolo + domínio + porta**. Basta um diferir:

| De | Para | Mesma origem? |
|---|---|---|
| `http://localhost:4200` | `http://localhost:4200/api` | ✅ |
| `http://localhost:4200` | `http://localhost:5100` | ❌ porta diferente |
| `https://app.mamao.tech` | `https://api.mamao.tech` | ❌ subdomínio diferente |
| `https://site.com` | `http://site.com` | ❌ protocolo diferente |

> **Chimpanzé pergunta:** *"Por que essa regra existe? Parece só atrapalhar."*
>
> Sem ela, um site malicioso aberto numa aba poderia fazer requisições ao seu banco em
> outra aba — **com os seus cookies**, porque o navegador os envia automaticamente para o
> domínio do banco. A regra existe para que a aba maliciosa não consiga ler a resposta.

## 11.2 CORS: relaxando a regra

**CORS** (*Cross-Origin Resource Sharing*) é o servidor dizendo *"pode, eu autorizo esta
origem"*, via cabeçalhos:

```
Access-Control-Allow-Origin: http://localhost:4200
Access-Control-Allow-Methods: GET, POST, PUT, DELETE
Access-Control-Allow-Headers: Authorization, Content-Type
```

Para métodos além de GET/POST simples, o navegador manda antes uma requisição
**`OPTIONS`** — o *preflight* — perguntando se pode. Você vê isso na aba Network e se
assusta na primeira vez.

No .NET seria:

```csharp
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
app.UseCors();
```

**O Mamão não tem isso.** E é decisão consciente.

## 11.3 Por que o Mamão não precisa de CORS

Porque em **nenhum** dos dois ambientes existe requisição entre origens.

### Em desenvolvimento: o proxy do `ng serve`

```json
{
  "/api": {
    "target": "http://localhost:5100",
    "secure": false,
    "changeOrigin": true
  }
}
```

O servidor de desenvolvimento do Angular recebe tudo. O que começa com `/api` ele
encaminha para a porta 5100 e devolve a resposta como se fosse dele.

Para o navegador, **tudo veio de `localhost:4200`**. Uma origem só. Sem preflight, sem
cabeçalho de CORS, sem configuração de segurança em produção que só serve para o dev.

### Em produção: o Caddy

```
{$PUBLIC_HOST:app.mamao.tech} {
	# handle, NAO handle_path: a API publica as rotas em /api/v1/..., entao o prefixo
	# tem que chegar nela. handle_path remove o trecho casado antes de encaminhar, e a
	# API responderia 404 em tudo — sem exceção no log, porque 404 nao e erro.
	handle /api/* {
		reverse_proxy api:8080
	}

	handle {
		root * /srv/web
		try_files {path} /index.html   # SPA fallback
		file_server
	}
}
```

O mesmo desenho: `app.mamao.tech/api/*` vai para a API; todo o resto são os arquivos do
Angular. **Uma origem só**, de novo.

E é por isso que o código do frontend usa caminho relativo:

```typescript
private readonly base = '/api/v1/employees';
```

Sem `http://`, sem domínio, sem variável de ambiente. O mesmo código funciona na sua
máquina e no servidor. Se houvesse domínio absoluto, seria preciso um arquivo de
configuração por ambiente — mais uma coisa para errar no deploy.

⚠️ **A armadilha do `handle_path`** está no comentário e é sutil: `handle_path` **remove**
o prefixo antes de encaminhar. `/api/v1/employees` chegaria na API como `/v1/employees`, e
ela responderia 404 em tudo. Pior: 404 não é exceção, então **não aparece nada no log de
erro**. Você teria uma aplicação totalmente quebrada e um log limpo.

## 11.4 O SPA fallback

Este ponto derruba todo mundo no primeiro deploy.

Sua SPA tem a rota `/pessoas/abc-123`. Funciona perfeitamente quando você navega clicando.
Mas se o usuário **recarregar a página** (F5) estando ali, ou colar o link no WhatsApp e
alguém abrir…

**404.**

Por quê: ao recarregar, o navegador pede `/pessoas/abc-123` ao servidor. E no servidor
existe só `index.html`, `main.js`, `styles.css`. Não existe pasta `pessoas`. O servidor,
corretamente, diz que não achou.

A solução é o **fallback**:

```
try_files {path} /index.html
```

*"Procure o arquivo pedido. Se não existir, devolva `index.html`."*

Aí o Angular carrega, lê a URL atual, acha a rota e mostra a tela certa. O roteamento é
resolvido **no navegador**; o servidor só precisa entregar a aplicação, seja qual for o
caminho pedido.

> **Chimpanzé pergunta:** *"E se a URL for lixo, tipo `/xyz`?"*
>
> O servidor devolve `index.html`, o Angular carrega, não acha rota que case e usa
> `{ path: '**', redirectTo: '' }`. O usuário vê a tela inicial. Note que o **código HTTP é
> 200**, não 404 — para um sistema atrás de login isso não tem consequência; para um site
> público indexado, teria.

## 11.5 A landing separada

O Caddy do Mamão serve **dois** sites:

```
# Dois sites, dois papeis:
#   mamao.tech       -> landing estatica (indexavel, sem JavaScript)
#   app.mamao.tech   -> aplicacao Angular + API
#
# Separados de proposito: carregar o bundle do app para mostrar uma pagina de venda
# seria pagar o custo de uma aplicacao para entregar um texto.
```

A landing é HTML puro. Carrega instantaneamente, aparece no Google, funciona sem
JavaScript. Nem todo problema precisa de framework — e usar o mesmo martelo para tudo custa
justamente onde mais dói, que é o primeiro contato.

## 11.6 Cache: o detalhe que quebra deploy

```
# Fontes e favicon nao mudam: cache longo com immutable.
@estaticos path /fonts/* /favicon.svg
header @estaticos Cache-Control "public, max-age=31536000, immutable"

# O HTML muda a cada deploy: revalida sempre.
header /index.html Cache-Control "public, max-age=0, must-revalidate"
```

Regra geral: **arquivo com hash no nome pode ter cache eterno; HTML não pode ter cache
nenhum.**

O build do Angular gera `main-A7B3C9.js`. Mudou o código, muda o hash, muda o nome do
arquivo — então cachear para sempre é seguro: um arquivo com aquele nome nunca muda de
conteúdo.

O `index.html` é quem aponta para `main-A7B3C9.js`. Se ele ficar em cache, o navegador
continua pedindo o **arquivo antigo** depois do deploy. O usuário vê a versão velha e você
não entende por quê. Pior ainda: se o arquivo antigo já não existe no servidor, a aplicação
simplesmente não carrega.

## 11.7 O build de produção

```bash
npm run build
```

Sai em `dist/mamao-web/browser/`:

```
index.html
main-A7B3C9.js
polyfills-D4E5F6.js
styles-G7H8I9.css
chunk-B0_azGTH.js       ← dashboard-page
chunk-UUdSdnQO.js       ← employee-import-page
favicon.svg
apple-touch-icon.png
```

O que aconteceu nesse comando:

- **Compilação AOT** — templates viram código JavaScript. Erro de template vira erro de
  build, não de execução.
- **Minificação** — nomes curtos, espaços removidos.
- **Tree-shaking** — código não usado é descartado.
- **Hash nos nomes** — para o cache descrito acima.
- **Divisão por rota** — os `chunk-*`, um por tela com lazy loading.

Esses arquivos são estáticos. Vão para dentro da imagem Docker e o Caddy os serve.

## 11.8 O quadro completo

```
   DESENVOLVIMENTO                        PRODUÇÃO
   ───────────────                        ────────

   navegador                              navegador
       │                                      │
       │ localhost:4200                       │ https://app.mamao.tech
       ▼                                      ▼
   ┌─────────────┐                       ┌─────────────┐
   │  ng serve   │                       │    Caddy    │  TLS automático
   │  (proxy)    │                       │  (proxy)    │
   └──┬───────┬──┘                       └──┬───────┬──┘
      │       │                             │       │
      │       └── /api/* ──┐                │       └── /api/* ──┐
      │                    ▼                │                    ▼
   arquivos           localhost:5100     dist/ estático      api:8080
   em memória          (dotnet run)      (do build)        (container)
                            │                                   │
                            ▼                                   ▼
                      Postgres local                     Postgres container
```

O que **não** muda entre os dois: o código do frontend. Nenhuma variável de ambiente,
nenhum arquivo de configuração por ambiente, nenhum `if (producao)`.

---

## Para fixar

1. **Por que recarregar em `/pessoas/123` dá 404 sem configuração?**
   <details><summary>Resposta</summary>
   Porque o navegador pede esse caminho ao servidor, e no servidor não existe esse arquivo
   nem essa pasta — as rotas são resolvidas pelo Angular, no navegador. É preciso o
   fallback que devolve `index.html` para qualquer caminho não encontrado.
   </details>

2. **Por que `handle` e não `handle_path` no Caddy?**
   <details><summary>Resposta</summary>
   `handle_path` remove o prefixo casado antes de encaminhar, então `/api/v1/employees`
   chegaria como `/v1/employees` e a API responderia 404 em tudo — sem nada no log de erro,
   porque 404 não é exceção.
   </details>

3. **Por que `index.html` não pode ter cache longo?**
   <details><summary>Resposta</summary>
   Porque ele aponta para os arquivos com hash. Em cache, continua apontando para o bundle
   antigo depois do deploy — e se aquele arquivo já foi removido do servidor, a aplicação
   nem carrega.
   </details>

4. **Qual a vantagem prática de usar caminho relativo (`/api/v1/...`) no frontend?**
   <details><summary>Resposta</summary>
   O mesmo código funciona em desenvolvimento e produção sem configuração por ambiente,
   porque quem resolve o destino é o proxy (ng serve ou Caddy).
   </details>

## Laboratório

1. Rode `npm run build` e olhe a pasta `dist`. Identifique os arquivos com hash e os
   `chunk-*` das rotas.
2. Sirva a pasta com um servidor estático simples:
   ```bash
   cd dist/seu-app/browser && python3 -m http.server 8080
   ```
   Navegue até uma rota interna e **recarregue**. Você vê o 404. Este é o momento de
   entender o fallback de verdade.
3. Repita com um servidor que faça fallback (`npx serve -s`). Agora funciona.
4. **Provoque um erro de CORS:** remova o `proxyConfig` e troque a URL do frontend para
   `http://localhost:5100/api/v1/produtos`. Leia a mensagem no console — é uma das mais
   temidas por iniciantes, e agora você sabe exatamente o que ela quer dizer.

---

**Anterior:** [Capítulo 10](10-http-e-interceptors.md) ·
**Próximo:** [Capítulo 12 — Uma funcionalidade ponta a ponta](12-ponta-a-ponta.md)
