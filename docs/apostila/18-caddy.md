# Capítulo 18 — Caddy: o porteiro da produção

> **Objetivo:** entender o que é um proxy reverso, por que existe um entre a internet e a
> sua aplicação, e ler o `Caddyfile` do Mamão inteiro — 73 linhas que decidem TLS, roteamento,
> cache, segurança e log.

---

## 18.1 Por que não expor a aplicação direto

Sua API .NET escuta na porta 8080 e responde HTTP. Por que não apontar o domínio para ela e
pronto?

Porque falta uma lista comprida de coisas que **não** são responsabilidade da sua
aplicação:

| Precisa | Quem deveria fazer |
|---|---|
| HTTPS com certificado válido e renovado | não é código de negócio |
| Servir os arquivos estáticos do Angular | não é a API |
| Servir a landing em outro domínio | idem |
| Comprimir a resposta | infraestrutura |
| Cabeçalhos de segurança | infraestrutura |
| Log de acesso com rotação | infraestrutura |
| Redirecionar HTTP para HTTPS | infraestrutura |

Colocar tudo isso na API significa: código de infraestrutura misturado com regra de
negócio, e reinício da aplicação para trocar um cabeçalho.

**Proxy reverso** é um servidor que fica na frente, recebe tudo e decide para onde vai.

```
internet ──> [ Caddy ] ──┬──> arquivos estáticos do Angular
                          ├──> arquivos estáticos da landing
                          └──> api:8080  (a aplicação .NET)
```

> **Chimpanzé pergunta:** *"Por que 'reverso'?"*
>
> Um proxy **normal** fica na frente do **cliente**: você configura o navegador para sair
> pela empresa. Um proxy **reverso** fica na frente do **servidor**: o cliente acha que
> está falando com a aplicação, e na verdade fala com o porteiro. Mesma ideia, ponta
> oposta.

## 18.2 Por que Caddy, e não nginx

nginx é o padrão da indústria há 20 anos. Caddy é mais novo. A escolha do Mamão tem uma
razão dominante e algumas secundárias.

**A razão dominante: HTTPS automático.**

No nginx, para ter HTTPS você instala o `certbot`, gera o certificado, configura os
caminhos, agenda a renovação, e testa se a renovação funciona (quase ninguém testa — e o
certificado vence num sábado). São umas 30 linhas de configuração e um processo à parte.

No Caddy:

```
app.mamao.tech {
    reverse_proxy api:8080
}
```

Pronto. Ele obtém o certificado do Let's Encrypt na primeira requisição, renova sozinho,
redireciona HTTP para HTTPS e faz *grapheful reload*. **Zero linha sobre TLS na
configuração do Mamão** — e o capítulo inteiro não vai ter nenhuma, o que é o ponto.

As secundárias: a configuração cabe numa tela e é legível às 2h da manhã; os padrões já são
seguros; e a distribuição é um binário único, sem módulos.

**Quando nginx ainda ganha:** carga muito alta (ele é mais rápido em benchmark bruto),
configurações exóticas, ou quando o time já o domina. Para um VPS servindo uma dezena de
empresas, a diferença de desempenho é irrelevante e a de operação é enorme.

## 18.3 A estrutura de um Caddyfile

```
dominio.com {
    diretiva argumento
    outra_diretiva {
        sub_diretiva valor
    }
}
```

Cada bloco de topo é um **site**. O Mamão tem dois:

```
# Dois sites, dois papeis:
#   mamao.tech       -> landing estatica (indexavel, sem JavaScript)
#   app.mamao.tech   -> aplicacao Angular + API
#
# Separados de proposito: carregar o bundle do app para mostrar uma pagina de venda
# seria pagar o custo de uma aplicacao para entregar um texto.
```

Um domínio para vender, outro para trabalhar. Necessidades opostas: a landing precisa
aparecer no Google e carregar instantaneamente; o app precisa de sessão e **não** pode ser
indexado.

## 18.4 O site da landing, linha a linha

```
{$LANDING_HOST:mamao.tech} {
	encode zstd gzip

	root * /srv/landing

	# /privacidade serve privacidade.html. Sem isto o link do rodape da 404 — e um 404
	# na politica de privacidade de um B2B diz mais que a falta do link.
	try_files {path} {path}.html {path}/index.html

	file_server
	…
}
```

**`{$LANDING_HOST:mamao.tech}`** — variável de ambiente com valor padrão. A mesma sintaxe
do Compose (Capítulo 15). Assim o mesmo arquivo serve produção e um ambiente de teste com
outro domínio.

**`encode zstd gzip`** — comprime a resposta. Ordem = preferência: se o navegador aceitar
zstd (mais eficiente), usa; senão, gzip. Reduz HTML e JavaScript em 60–80%.

**`root * /srv/landing`** — onde estão os arquivos. O `*` é o matcher: "para todos os
caminhos".

**`try_files {path} {path}.html {path}/index.html`** — tenta em ordem. Pedindo
`/privacidade`: procura o arquivo `privacidade`, depois `privacidade.html` (acha), depois
`privacidade/index.html`.

Sem isso, o link do rodapé daria 404. E o comentário aponta o custo real: **um 404 na
política de privacidade de um B2B diz mais que a falta do link** — sugere que o documento é
decorativo.

### Cache: duas políticas opostas no mesmo site

```
	# Fontes e favicon nao mudam: cache longo com immutable.
	@estaticos path /fonts/* /favicon.svg
	header @estaticos Cache-Control "public, max-age=31536000, immutable"

	# O HTML muda a cada deploy: revalida sempre.
	header /index.html Cache-Control "public, max-age=0, must-revalidate"
```

`@estaticos` é um **matcher nomeado** — um apelido para "estes caminhos".

- Fonte e favicon: cache de um ano (`31536000` segundos), com `immutable`, que diz ao
  navegador para **nem perguntar** se mudou.
- HTML: `max-age=0, must-revalidate` — sempre pergunta antes de usar.

A regra vale para qualquer site: **conteúdo com nome fixo que muda → sem cache; conteúdo
que nunca muda → cache eterno.** Errar do lado errado significa usuário vendo versão antiga
depois do deploy, sem forma de descobrir.

## 18.5 O site da aplicação

```
{$PUBLIC_HOST:app.mamao.tech} {
	encode zstd gzip

	# handle, NAO handle_path: a API publica as rotas em /api/v1/..., entao o prefixo
	# tem que chegar nela. handle_path remove o trecho casado antes de encaminhar, e a
	# API responderia 404 em tudo — sem exceção no log, porque 404 nao e erro.
	handle /api/* {
		reverse_proxy api:8080
	}

	handle /healthz* {
		reverse_proxy api:8080
	}

	handle {
		root * /srv/web
		try_files {path} /index.html   # SPA fallback
		file_server
	}
	…
}
```

`handle` blocos são avaliados **em ordem**, e só o primeiro que casar executa. Logo:
`/api/*` vai para a API; `/healthz*` também; **todo o resto** são os arquivos do Angular.

### A armadilha do `handle_path`

Vale entender inteiro, porque é o tipo de erro que consome uma tarde:

| Diretiva | `/api/v1/employees` chega na API como |
|---|---|
| `handle /api/*` | `/api/v1/employees` ✅ |
| `handle_path /api/*` | `/v1/employees` ❌ |

`handle_path` **remove** o prefixo casado. A API do Mamão publica as rotas em `/api/v1/…`,
então ela responderia **404 em tudo**.

E o comentário aponta a parte cruel: *"sem exceção no log, porque 404 nao e erro"*. A
aplicação inteira quebrada, e o log de erro limpo. Você olharia para o .NET, para o banco,
para o token — e a causa estaria numa palavra do Caddyfile.

`handle_path` é útil quando o backend **não** conhece o prefixo — por exemplo, um serviço
que publica `/users` e você quer expor em `/api/users`. Saber qual dos dois usar depende de
onde o prefixo é conhecido.

### O SPA fallback

```
try_files {path} /index.html
```

Uma linha que resolve o problema do Capítulo 11: recarregar em `/pessoas/abc-123` daria
404, porque não existe esse arquivo no servidor. Com o fallback, o Caddy devolve o
`index.html`, o Angular carrega, lê a URL e mostra a tela certa.

⚠️ Note a **ordem**: se o `handle` genérico viesse antes do `/api/*`, o fallback engoliria
as chamadas de API — `/api/v1/employees` devolveria o `index.html`, e o frontend receberia
HTML onde esperava JSON. O erro clássico daí é `Unexpected token '<' in JSON at position 0`
— o `<` é do `<!doctype html>`.

Guarde esse erro: quando ele aparecer, quase sempre a API devolveu uma página de erro ou o
fallback pegou uma rota que não devia.

## 18.6 Cabeçalhos de segurança

```
	header {
		Strict-Transport-Security "max-age=31536000; includeSubDomains"
		X-Content-Type-Options nosniff
		X-Frame-Options DENY
		Referrer-Policy strict-origin-when-cross-origin
		-Server
	}
```

Um por um:

**`Strict-Transport-Security`** (HSTS) — diz ao navegador: *"por um ano, só me acesse por
HTTPS, mesmo que digitem `http://`"*. Protege contra o ataque de rebaixar a conexão para
HTTP na primeira requisição.

⚠️ HSTS é **difícil de desfazer**: o navegador lembra pelo período declarado. Se você
publicar com um ano e depois precisar servir HTTP naquele domínio, os visitantes que já
passaram por lá não conseguem. Comece com um valor pequeno se estiver testando.

**`X-Content-Type-Options: nosniff`** — proíbe o navegador de "adivinhar" o tipo do arquivo.
Sem isso, um arquivo enviado por um usuário e servido como texto poderia ser interpretado
como JavaScript e executado.

**`X-Frame-Options: DENY`** — impede que a aplicação seja colocada dentro de um `<iframe>`
em outro site. É a defesa contra *clickjacking*: um site sobrepõe uma camada invisível e
faz você clicar em "Excluir" achando que clica em outra coisa.

**`Referrer-Policy: strict-origin-when-cross-origin`** — ao sair para outro site, manda só o
domínio, não a URL completa. Sem isso, um link de dentro de
`app.mamao.tech/pessoas/abc-123?token=…` vazaria o caminho e a query para o site de destino.

**`-Server`** — o menos importante e o mais fácil: **remove** o cabeçalho que anuncia qual
servidor você usa. Não impede ataque nenhum, mas não há motivo para entregar de graça a
informação de qual software procurar exploit.

E logo abaixo:

```
	# Area logada nao entra em buscador.
	header X-Robots-Tag "noindex, nofollow"
```

A landing quer ser indexada; o app **não**. Além de não fazer sentido, uma tela de login
indexada é ruído nos resultados e revela superfície.

## 18.7 Log com rotação

```
	log {
		output file /data/access.log {
			roll_size 20mb
			roll_keep 5
		}
	}
```

Log de acesso sem rotação **enche o disco** — e quando o disco enche, o Postgres para de
gravar e o sistema cai por um motivo que não tem nada a ver com a aplicação.

`roll_size 20mb` + `roll_keep 5` = no máximo ~100 MB, para sempre. O Caddy corta e descarta
o mais antigo sozinho.

⚠️ **Nota de privacidade:** log de acesso guarda IP, que é dado pessoal sob a LGPD. A
política de privacidade do Mamão precisa dizer que ele existe e por quanto tempo é
guardado. É o tipo de detalhe que só aparece quando alguém pergunta — e aí é tarde.

## 18.8 O que o Caddy faz e você não vê

Nada disso está no arquivo, e tudo isso acontece:

- **Obtenção do certificado** no Let's Encrypt, no primeiro acesso ao domínio.
- **Renovação automática**, com folga antes do vencimento.
- **Redirecionamento HTTP → HTTPS**, automático para todo site com domínio.
- **HTTP/2 e HTTP/3**, ligados por padrão.
- **Grapheful reload**: `caddy reload` troca a configuração sem derrubar conexão.
- **OCSP stapling**, para o navegador não precisar consultar a validade do certificado.

⚠️ **Cuidado real ao testar:** o Let's Encrypt tem **limite de emissão** (5 certificados
por domínio por semana). Testar deploy repetidamente com o domínio de produção pode te
deixar sem certificado por dias. Existe o ambiente de *staging* deles justamente para isso.

E é por isso que o volume `caddy_data` importa (Capítulo 15): é onde os certificados moram.
Um `docker compose down -v` apaga, e o Caddy pede tudo de novo — consumindo a cota.

## 18.9 Operação

```bash
# validar ANTES de aplicar — evita derrubar o site com erro de sintaxe
docker compose exec caddy caddy validate --config /etc/caddy/Caddyfile

# aplicar sem downtime
docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile

# formatar (o Caddy tem estilo canônico)
caddy fmt --overwrite Caddyfile

# ver o log de acesso
docker compose exec caddy tail -f /data/access.log

# ver o que ele resolveu (a configuração em JSON, expandida)
docker compose exec caddy caddy adapt --config /etc/caddy/Caddyfile
```

O `validate` deveria ser reflexo antes de qualquer `reload`. Erro de sintaxe num Caddyfile
derruba o site inteiro — os dois domínios de uma vez.

> **Nota metodológica.** Ao escrever o Caddyfile do Mamão, eu validei com o **binário real
> do Caddy**, não com uma imitação em Python que eu tinha escrito para testar mais rápido.
> A imitação teria aceitado coisas que o Caddy recusa, e o erro só apareceria no deploy. É
> o mesmo princípio do [Capítulo 13](13-bugs-reais.md): o que não roda de verdade não está
> testado.

## 18.10 O desenho completo

```
                    Internet
                       │
              ┌────────┴────────┐
              │  :80      :443  │
              ▼                 ▼
        ┌───────────────────────────────────┐
        │             CADDY                 │
        │  TLS automático · gzip/zstd       │
        │  cabeçalhos de segurança · log    │
        └──┬──────────────────────────┬─────┘
           │                          │
   mamao.tech                  app.mamao.tech
           │                          │
           ▼               ┌──────────┼──────────┐
    /srv/landing           │          │          │
    (HTML puro)         /api/*   /healthz*    todo o resto
                           │          │          │
                           ▼          ▼          ▼
                       api:8080   api:8080   /srv/web
                                             (Angular,
                                              com fallback)
```

Repare no que **não** aparece nesse desenho: nenhuma seta da internet para `api:8080`. A
API não publica porta (Capítulo 15) — ela só é alcançável através do Caddy. O porteiro não
é opcional; é o único caminho.

---

## Para fixar

1. **O que é um proxy reverso e por que "reverso"?**
   <details><summary>Resposta</summary>
   É um servidor que fica na frente da aplicação, recebe as requisições e decide para onde
   encaminhar. "Reverso" porque um proxy comum fica na frente do cliente; este fica na
   frente do servidor.
   </details>

2. **Qual a diferença entre `handle` e `handle_path`?**
   <details><summary>Resposta</summary>
   `handle_path` remove o prefixo casado antes de encaminhar; `handle` mantém. Como a API
   do Mamão publica rotas em `/api/v1/…`, usar `handle_path` faria tudo responder 404 —
   sem nada no log de erro, porque 404 não é exceção.
   </details>

3. **Por que o `handle` genérico tem que vir por último?**
   <details><summary>Resposta</summary>
   Porque ele casa com tudo e faz o fallback para `index.html`. Vindo antes, engoliria as
   chamadas de API, que passariam a receber HTML — daí o erro
   `Unexpected token '<' in JSON`.
   </details>

4. **Por que HTML não pode ter cache longo e fonte pode?**
   <details><summary>Resposta</summary>
   O HTML aponta para os arquivos com hash no nome e muda a cada deploy. Em cache,
   continua apontando para o bundle antigo. Fontes e favicon têm nome estável e conteúdo
   que não muda, então cache eterno é seguro.
   </details>

5. **Qual o risco de testar deploy repetidamente com o domínio de produção?**
   <details><summary>Resposta</summary>
   O limite de emissão do Let's Encrypt (5 por domínio por semana). Estourando, você fica
   sem certificado por dias. Some a isso o `docker compose down -v`, que apaga o volume
   onde os certificados estão guardados.
   </details>

## Laboratório

1. Instale o Caddy e sirva uma pasta local:
   ```
   localhost:8080 {
       root * ./public
       file_server
   }
   ```
2. Adicione `try_files {path} /index.html` e prove o SPA fallback: navegue para
   `/qualquer/coisa` e veja o `index.html` sendo entregue.
3. **Reproduza a armadilha do `handle_path`:** suba a `Loja.Api` do Capítulo 2, coloque um
   `handle_path /api/*` na frente e veja tudo virar 404. Confira que o log de acesso mostra
   `404` e que **nada** aparece no log de erro da aplicação. Troque para `handle` e veja
   funcionar.
4. **Inverta a ordem dos blocos** — genérico antes do `/api/*` — e observe o
   `Unexpected token '<' in JSON` no console do navegador. Esse erro vai te aparecer na
   vida real; agora você sabe o que é.
5. Rode `caddy adapt` e leia o JSON expandido. Cada linha do Caddyfile vira várias ali — é
   assim que o Caddy realmente enxerga a sua configuração.
6. Adicione os cabeçalhos de segurança e verifique com:
   ```bash
   curl -sI http://localhost:8080 | grep -iE 'x-frame|x-content|referrer'
   ```

---

**Anterior:** [Capítulo 17](17-o-contrato-por-dentro.md) ·
**Apoio:** [Glossário](99-glossario.md)
