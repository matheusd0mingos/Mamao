# Capítulo 15 — Docker para macacos

> **Objetivo:** entender o que é um container, escrever um Dockerfile, ler o
> `docker-compose.yml` do Mamão inteiro e saber por que cada linha dele está lá.

Você pode ler este capítulo logo depois do [Capítulo 11](11-dev-vs-producao.md), se
preferir ver a produção completa antes do estudo de caso.

---

## 15.1 O problema que o Docker resolve

Você terminou o sistema. Ele roda na sua máquina. Agora precisa rodar no servidor.

O servidor precisa de: .NET 10 na versão exata, PostgreSQL 17, as variáveis de ambiente
certas, as pastas com as permissões certas, o `curl` instalado, o fuso configurado…

Você instala tudo à mão. Funciona. Seis meses depois, o servidor morre e você precisa
refazer — e não lembra de metade. Ou entra outra pessoa no projeto e a máquina dela tem
.NET 9, e o comportamento difere em algo sutil.

Essa é a frase mais antiga da profissão:

> *"Na minha máquina funciona."*

Docker responde: **então mandamos a sua máquina junto.**

## 15.2 Container não é máquina virtual

A confusão mais comum. Vale desenhar.

**Máquina virtual** — você simula um computador inteiro, com sistema operacional próprio:

```
┌──────────────────────────────────────────┐
│  Seu computador                          │
│  ┌────────────┐  ┌────────────┐          │
│  │ Linux      │  │ Linux      │  ← dois sistemas inteiros
│  │ completo   │  │ completo   │     ~1 GB cada, boot de minutos
│  │ + sua app  │  │ + sua app  │
│  └────────────┘  └────────────┘          │
│  ┌────────────────────────────┐          │
│  │      Hypervisor            │          │
│  └────────────────────────────┘          │
│           Sistema operacional             │
└──────────────────────────────────────────┘
```

**Container** — processos isolados, compartilhando o núcleo do sistema:

```
┌──────────────────────────────────────────┐
│  Seu computador                          │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ │
│  │ processo │ │ processo │ │ processo │ │  ← isolados, mas sem SO próprio
│  │  + libs  │ │  + libs  │ │  + libs  │ │     ~100 MB, sobem em 1 s
│  └──────────┘ └──────────┘ └──────────┘ │
│  ┌────────────────────────────────────┐ │
│  │        Docker Engine               │ │
│  └────────────────────────────────────┘ │
│      Sistema operacional (um só)          │
└──────────────────────────────────────────┘
```

Um container é **um processo do seu sistema** que enxerga um sistema de arquivos próprio,
uma rede própria e uma lista de processos própria. Isolamento por configuração do núcleo,
não por simulação.

> **Chimpanzé pergunta:** *"Se compartilha o núcleo, dá para rodar container Windows no
> Linux?"*
>
> Não nativamente. Um container Linux precisa de núcleo Linux. No Windows e no macOS, o
> Docker Desktop roda uma máquina virtual Linux minúscula por baixo, e os containers vivem
> dentro dela. Por isso Docker é um pouco mais lento fora do Linux.

## 15.3 Os três substantivos

| Termo | O que é | Analogia |
|---|---|---|
| **Dockerfile** | receita de como montar | a receita do bolo |
| **Imagem** | o resultado da receita, congelado | o bolo pronto, na vitrine |
| **Container** | uma imagem em execução | a fatia que você está comendo |

Uma imagem gera **muitos** containers. A imagem é imutável; o container tem estado e morre.

```bash
docker build -t minha-api .     # Dockerfile → imagem
docker run minha-api            # imagem → container
docker run minha-api            # outro container, mesma imagem
```

## 15.4 Seu primeiro Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app
ENTRYPOINT ["dotnet", "/app/Loja.Api.dll"]
```

Linha a linha:

- **`FROM`** — de onde partir. Nunca se começa do zero: você parte de uma imagem que já
  tem o sistema base e as ferramentas.
- **`WORKDIR`** — o diretório de trabalho dentro do container.
- **`COPY . .`** — copia os arquivos do seu computador para a imagem.
- **`RUN`** — executa um comando **durante a construção**.
- **`ENTRYPOINT`** — o que roda **quando o container inicia**.

⚠️ **`RUN` vs `ENTRYPOINT`** é a confusão nº 1. `RUN` acontece uma vez, ao construir, e o
resultado fica congelado na imagem. `ENTRYPOINT` acontece toda vez que um container sobe.

Construindo e rodando:

```bash
docker build -t loja-api .
docker run -p 8080:8080 loja-api
```

`-p 8080:8080` publica a porta: `porta_no_seu_computador:porta_dentro_do_container`. Sem
isso, o container escuta numa rede que só ele enxerga.

## 15.5 Camadas — e por que a ordem do Dockerfile importa

Cada instrução cria uma **camada**. O Docker guarda cada uma em cache e só refaz a partir
da primeira que mudou.

O Dockerfile ingênuo acima tem um defeito grave:

```dockerfile
COPY . .                              # ← muda a cada linha de código alterada
RUN dotnet restore                    # ← refeito sempre, mesmo sem mudar dependência
```

Trocando uma letra num arquivo `.cs`, o `COPY` invalida o cache, e o `restore` — que baixa
pacotes da internet — roda de novo. Build de três minutos que poderia ser de dez segundos.

O Mamão resolve separando:

```dockerfile
# Restore em camada propria: so refaz quando csproj/props mudam.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/ src/
RUN dotnet restore src/Mamao.Api/Mamao.Api.csproj

RUN dotnet publish src/Mamao.Api/Mamao.Api.csproj -c Release -o /app --no-restore
```

**A regra:** copie primeiro o que **muda pouco** (arquivos de dependência) e depois o que
muda muito (código-fonte).

## 15.6 Build multi-estágio

Para compilar você precisa do SDK do .NET: ~800 MB, com compilador, ferramentas, tudo. Para
**rodar**, precisa só do runtime: ~200 MB.

Levar o SDK para produção é carregar um estaleiro junto com o navio. E não é só tamanho —
é superfície de ataque: um invasor que entre no container encontra um compilador à
disposição.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build       # ← estágio 1: compila
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime  # ← estágio 2: só roda
WORKDIR /app
COPY --from=build /app .                              # ← traz só o resultado
ENTRYPOINT ["dotnet", "Mamao.Api.dll"]
```

`COPY --from=build` pega o resultado do primeiro estágio. Tudo o mais do estágio 1 é
descartado — a imagem final não tem SDK, não tem código-fonte, não tem `node_modules`.

## 15.7 O Dockerfile do Mamão, e dois bugs de produção nele

O arquivo real tem duas coisas que só existem porque quebraram de verdade. Ambas estão
comentadas no código.

### Bug 1 — o healthcheck que nunca passava

```dockerfile
# A imagem da Microsoft nao traz curl nem wget, e o healthcheck do compose roda DENTRO
# do container. Sem isso o healthcheck falha sempre — e um container eternamente
# "unhealthy" e pior que nenhum healthcheck: parece vigilancia e nao e.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
```

O `healthcheck` do Compose executa um comando **dentro** do container. O comando era
`curl -fsS http://localhost:8080/healthz/ready`. Só que a imagem oficial da Microsoft não
traz `curl` — nem `wget`. O comando falhava sempre, e o container ficava eternamente
`unhealthy`.

O comentário aponta o pior de tudo: **um monitoramento sempre vermelho é pior que nenhum**,
porque as pessoas aprendem a ignorá-lo — e aí o dia em que ficar vermelho de verdade não
muda nada.

### Bug 2 — o volume que nascia do root

```dockerfile
# Nunca rodar como root.
#
# Os diretorios de volume precisam existir E pertencer ao usuario AQUI, na imagem:
# quando o Docker monta um volume nomeado vazio, ele copia dono e permissao do que
# encontra na imagem. Sem isso o volume nasce do root, o processo (64198) nao escreve,
# e o Data Protection quebra ao gravar a chave — sem chave nao ha recuperacao de senha
# nem convite.
RUN useradd --uid 64198 --create-home mamao \
    && mkdir -p /var/mamao/keys /var/mamao/uploads \
    && chown -R 64198:64198 /var/mamao
USER 64198
```

Este é sutil e vale entender inteiro:

1. Por segurança, o processo roda como usuário comum (`USER 64198`), não root. Se alguém
   escapar da aplicação, não é administrador do container.
2. Mas o container grava em `/var/mamao/keys`, que é um **volume**.
3. Quando o Docker monta um volume nomeado **vazio**, ele copia dono e permissão **do que
   estiver na imagem naquele caminho**.
4. Se a pasta não existir na imagem, o volume nasce pertencendo ao root.
5. O processo não-root não consegue escrever.
6. O Data Protection não grava a chave.
7. **Recuperação de senha e convite param de funcionar** — e o erro aparece a três camadas
   de distância da causa.

Por isso a pasta é criada **e** tem o dono ajustado dentro da imagem, antes do `USER`.

## 15.8 Docker Compose

Um container é um processo. Um sistema tem vários: API, Worker, banco, servidor web.
Subir cada um na mão, na ordem certa, com as variáveis certas, é inviável.

**Compose** descreve tudo num arquivo:

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: mamao
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?}
    volumes:
      - pgdata:/var/lib/postgresql/data

  api:
    image: ghcr.io/matheusd0mingos/mamao-api:${TAG:?defina TAG}
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  pgdata:
```

```bash
docker compose up -d      # sobe tudo em segundo plano
docker compose ps         # o que está rodando
docker compose logs -f api
docker compose down       # derruba (volumes ficam)
```

## 15.9 Volumes: onde o dado sobrevive

**O sistema de arquivos de um container é descartável.** Recriou o container, perdeu tudo.
Isso é ótimo para a aplicação e catastrófico para o banco.

**Volume** é armazenamento gerenciado pelo Docker que vive fora do container:

```yaml
volumes:
  - pgdata:/var/lib/postgresql/data      # volume nomeado
  - ./Caddyfile:/etc/caddy/Caddyfile:ro  # arquivo do host, somente leitura
```

O Mamão tem cinco:

| Volume | Guarda | Se perder |
|---|---|---|
| `pgdata` | o banco | perdeu tudo |
| `uploads` | os documentos dos funcionários | a lista existe, os arquivos não |
| `keys` | chaves do Data Protection | links de recuperação de senha já enviados param de valer |
| `caddy_data` | certificados TLS | o Caddy pede de novo (e há limite de emissão) |
| `caddy_config` | estado do Caddy | pouco importa |

⚠️ **A armadilha do backup** está documentada no próprio código do Mamão: o dump do
PostgreSQL **não** leva os arquivos de `uploads`. Restaurar só o banco devolve a lista de
documentos com os arquivos faltando — o que é pior que estar fora do ar, porque *parece*
que funcionou.

## 15.10 Rede

Containers do mesmo Compose ficam numa rede própria e **se enxergam pelo nome do serviço**:

```yaml
handle /api/* {
    reverse_proxy api:8080     # "api" é o nome do serviço, não um IP
}
```

```yaml
ConnectionStrings__mamao: Host=postgres;Database=mamao;…
```

`Host=postgres` funciona porque o Docker mantém um DNS interno. Você nunca escreve IP.

E repare no `docker-compose.yml` do Mamão: **só o Caddy publica portas**.

```yaml
caddy:
  ports:
    - "80:80"
    - "443:443"
```

A API, o Worker e o Postgres não têm `ports:`. Eles são alcançáveis **de dentro** da rede,
mas não da internet. Isso não é detalhe: publicar a porta do Postgres num VPS é como
deixar o banco na calçada — existem robôs varrendo a internet por 5432 aberto o tempo todo.

## 15.11 Healthcheck e `depends_on`

```yaml
api:
  depends_on:
    postgres:
      condition: service_healthy
```

⚠️ **`depends_on` sozinho só garante ordem de *início*, não que o serviço esteja pronto.**
O Postgres leva alguns segundos para aceitar conexões depois que o processo sobe. Sem
`condition: service_healthy`, a API sobe, tenta conectar, falha e reinicia — às vezes
várias vezes.

O que define "saudável":

```yaml
postgres:
  healthcheck:
    test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d mamao"]
    interval: 10s
    timeout: 5s
    retries: 5
```

E no Worker há um detalhe:

```yaml
worker:
  healthcheck:
    start_period: 60s        # o Worker aplica as migrations antes de responder
```

`start_period` é a carência: falhas nesse período não contam. O Worker aplica todas as
migrations antes de responder, e sem a carência ele seria declarado morto e reiniciado no
meio de uma migration.

## 15.12 Variáveis e segredos

```yaml
environment:
  ConnectionStrings__mamao: ${DB_CONNECTION_APP:?}
  Jwt__SigningKey: ${JWT_SIGNING_KEY:?}
  Smtp__Port: ${SMTP_PORT:-587}
```

Duas sintaxes:

- `${VAR:?}` — **obrigatória**. Se faltar, o Compose recusa subir. Muito melhor do que
  subir com string vazia e falhar em uso.
- `${VAR:-padrão}` — opcional, com valor padrão.

Os valores vêm de um arquivo `.env` que **nunca** vai para o Git:

```
# .gitignore
.env
deploy/.env
```

> **Chimpanzé pergunta:** *"E se eu commitar a senha sem querer?"*
>
> Considere-a comprometida e **troque**. Apagar num commit seguinte não resolve: o
> histórico do Git guarda tudo, o GitHub indexa, e há robôs varrendo commits públicos por
> credenciais em tempo real. Trocar o segredo é a única correção.

Aliás — o `__` (dois sublinhados) em `ConnectionStrings__mamao` não é enfeite: é como o
.NET representa hierarquia de configuração em variável de ambiente. Equivale a
`{ "ConnectionStrings": { "mamao": "…" } }` no `appsettings.json`.

## 15.13 Limites de memória

```yaml
api:
  # Postgres sem limite consumindo tudo e o OOM killer matando a API e o incidente
  # classico de VPS. Ajuste conforme o plano contratado.
  mem_limit: 768m
```

Sem limite, um container pode consumir toda a RAM da máquina. Aí o núcleo do Linux aciona
o **OOM killer**, que escolhe um processo para matar — e a escolha dele raramente é a que
você faria. O incidente típico: o Postgres cresce, e quem morre é a API.

## 15.14 Os comandos que você vai usar

```bash
# ver o que roda
docker compose ps
docker ps

# logs
docker compose logs -f api            # acompanhar
docker compose logs --tail=100 worker

# entrar no container (depurar)
docker compose exec api sh
docker compose exec postgres psql -U mamao_owner -d mamao

# reconstruir e subir
docker compose up -d --build

# derrubar
docker compose down                   # mantém os volumes
docker compose down -v                # 💀 APAGA OS VOLUMES — apaga o banco

# limpar espaço
docker system df                      # quanto está ocupando
docker system prune -a                # remove imagens não usadas
```

⚠️ `docker compose down -v` apaga os volumes. Num servidor de produção, isso é apagar o
banco. Digite esse comando devagar.

## 15.15 O desenho completo do Mamão em produção

```
                        internet
                            │
                       :80  :443
                            ▼
              ┌──────────────────────────┐
              │         caddy            │  TLS automático (Let's Encrypt)
              │  mamao.tech → /srv/landing
              │  app.…/api/* → api:8080
              │  app.…/*     → /srv/web  │
              └────────┬─────────────────┘
                       │ rede interna do Compose
         ┌─────────────┼──────────────┐
         ▼             ▼              ▼
   ┌──────────┐  ┌──────────┐  ┌────────────┐
   │   api    │  │  worker  │  │  postgres  │
   │  :8080   │  │  :8080   │  │   :5432    │
   │          │  │          │  │            │
   │ role     │  │ role     │  │ volume:    │
   │ mamao_app│  │ mamao_   │  │  pgdata    │
   │ (sem     │  │ owner    │  │            │
   │ BYPASSRLS)  │ (dono)   │  │            │
   └────┬─────┘  └────┬─────┘  └────────────┘
        │             │
        └──── volumes: uploads, keys ────┘
```

Repare no detalhe de segurança que aparece no Compose e que vale mais que qualquer firewall
aqui: **a API e o Worker conectam ao banco com papéis diferentes.**

```yaml
api:
  # A API conecta com o role SEM BYPASSRLS: e isso que faz a Row-Level Security
  # valer. Apontar aqui para o dono das tabelas desliga a camada 3 em silencio.
  ConnectionStrings__mamao: ${DB_CONNECTION_APP:?}

worker:
  # O Worker conecta como DONO: ele aplica migrations, cria o role da aplicacao e
  # concede os acessos.
  ConnectionStrings__mamao: ${DB_CONNECTION_OWNER:?}
```

A API roda com um papel que **não pode** ignorar as políticas de isolamento entre empresas.
Se o código errar — um `IgnoreQueryFilters()` esquecido, um SQL cru mal escrito — o banco
ainda barra. Apontar a API para o dono das tabelas desligaria essa camada **em silêncio**,
sem erro nenhum.

---

## Para fixar

1. **Por que o `COPY` do código vem depois do restore no Dockerfile?**
   <details><summary>Resposta</summary>
   Por causa do cache de camadas: o código muda a cada commit, as dependências quase nunca.
   Copiando primeiro os arquivos de dependência, o `restore` fica numa camada que só é
   refeita quando uma dependência muda.
   </details>

2. **Por que build multi-estágio?**
   <details><summary>Resposta</summary>
   Para a imagem final ter só o runtime. Menor, sobe mais rápido e não carrega compilador
   nem código-fonte para produção.
   </details>

3. **Qual o risco de `depends_on` sem `condition: service_healthy`?**
   <details><summary>Resposta</summary>
   Ele garante só a ordem de início, não que o serviço aceite conexões. A API sobe antes do
   Postgres estar pronto, falha ao conectar e reinicia.
   </details>

4. **Por que a pasta do volume precisa existir na imagem com o dono certo?**
   <details><summary>Resposta</summary>
   Porque o Docker copia dono e permissão do que encontra na imagem quando monta um volume
   nomeado vazio. Se a pasta não existir, o volume nasce do root e o processo não-root não
   consegue escrever.
   </details>

5. **Por que só o Caddy publica portas?**
   <details><summary>Resposta</summary>
   Para que API, Worker e Postgres sejam alcançáveis apenas de dentro da rede do Compose.
   Publicar a porta do Postgres num VPS o expõe a varredura automatizada da internet.
   </details>

## Laboratório

1. Escreva um Dockerfile de estágio único para a `Loja.Api` do Capítulo 2. Meça com
   `docker images` o tamanho da imagem.
2. Converta para multi-estágio. Compare os tamanhos. Deve cair para menos de um terço.
3. **Sinta o cache:** rode `docker build` duas vezes seguidas sem mudar nada — a segunda é
   quase instantânea. Agora mude uma linha de código e reconstrua. Observe quais camadas
   dizem `CACHED` e a partir de qual elas param de dizer.
4. Escreva um `docker-compose.yml` com a API e um Postgres, com healthcheck e
   `condition: service_healthy`.
5. **Reproduza o Bug 2 (o do volume):** crie um Dockerfile com `USER 1000` **sem** criar a
   pasta antes, monte um volume nela e tente escrever. Leia o `Permission denied`. Depois
   adicione o `mkdir` + `chown` antes do `USER` e veja funcionar.

---

**Anterior:** [Capítulo 14](14-exercicios.md) ·
**Próximo:** [Capítulo 16 — A solução .NET por dentro](16-a-solucao-dotnet.md)
