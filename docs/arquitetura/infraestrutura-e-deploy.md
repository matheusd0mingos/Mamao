# Infraestrutura, deploy e observabilidade

## Desenvolvimento — Aspire

Aspire resolve exatamente o que você não quer escrever: subir Postgres, injetar
connection string, descobrir serviço, ligar OpenTelemetry, expor dashboard de
traces localmente.

```csharp
// Mamao.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
                      .WithDataVolume()          // dados sobrevivem ao restart
                      .WithPgAdmin();

var db = postgres.AddDatabase("mamao");

var api    = builder.AddProject<Projects.Mamao_Api>("api").WithReference(db);
var worker = builder.AddProject<Projects.Mamao_Worker>("worker").WithReference(db);

builder.AddNpmApp("web", "../../web/mamao-web", "start")
       .WithReference(api)
       .WithHttpEndpoint(env: "PORT");

builder.Build().Run();
```

`F5` sobe tudo. O `ServiceDefaults` que vem junto entrega OTel, health checks e
políticas de resiliência de HTTP client sem código.

**RabbitMQ não entra aqui na V1** — não existe na aplicação
([ADR-0005](../adr/0005-outbox-e-mensageria.md)). Quando entrar, é uma linha.

### Limite importante

O output de deploy do Aspire mira Azure Container Apps e Kubernetes. Num VPS
HostGator, isso não ajuda — **produção usa um `docker-compose.yml` escrito à mão**.
Ver [ADR-0011](../adr/0011-aspire-e-deploy.md). Aspire é ferramenta de
desenvolvimento neste projeto, e assumir isso desde já evita uma frustração
previsível.

---

## Produção inicial — VPS

```
Cloudflare  (DNS, TLS na borda, WAF, cache de estáticos)
     │
   Caddy     (TLS interno, estáticos do Angular, reverse proxy /api)
     │
  ┌──┴───────────────┬──────────────┐
 api               worker        postgres
 (.NET)            (.NET)        (+ volume)
                                 uploads (volume)
```

```yaml
# docker-compose.yml (esqueleto)
services:
  caddy:
    image: caddy:2-alpine
    ports: ["80:80", "443:443"]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - ./web-dist:/srv/web:ro          # build do Angular
      - caddy_data:/data
    depends_on: [api]

  api:
    image: ghcr.io/matheusd0mingos/mamao-api:${TAG}
    environment:
      ConnectionStrings__mamao: ${DB_CONNECTION}
      ASPNETCORE_ENVIRONMENT: Production
    volumes: [uploads:/var/mamao/uploads]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/healthz/ready"]
      interval: 15s
    restart: unless-stopped

  worker:
    image: ghcr.io/matheusd0mingos/mamao-worker:${TAG}
    environment:
      ConnectionStrings__mamao: ${DB_CONNECTION}
    volumes: [uploads:/var/mamao/uploads]
    restart: unless-stopped

  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: mamao
    volumes: [pgdata:/var/lib/postgresql/data]
    restart: unless-stopped

volumes: { pgdata: , uploads: , caddy_data: }
```

```
# Caddyfile
app.mamao.com.br {
    encode zstd gzip
    handle_path /api/* { reverse_proxy api:8080 }
    handle {
        root * /srv/web
        try_files {path} /index.html      # SPA fallback
        file_server
    }
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
        X-Content-Type-Options nosniff
        Referrer-Policy strict-origin-when-cross-origin
    }
}
```

Angular é **estático servido pelo Caddy**. Nada de container Node em produção.

### Dois sites, dois papéis

| Domínio | Conteúdo |
|---|---|
| `mamao.tech` | Landing estática (`web/landing/`), HTML puro, sem JavaScript |
| `app.mamao.tech` | Aplicação Angular + API, com `X-Robots-Tag: noindex` |

A landing **não** mora dentro do SPA. Carregar o bundle do Angular para mostrar uma
página de venda é pagar o custo de uma aplicação para entregar um texto — e um
`index.html` vazio preenchido por JavaScript é pior para indexação e para o tempo até
o primeiro conteúdo. São 120 KB no total, fontes incluídas.

As fontes da marca são servidas do próprio domínio, não do Google Fonts: uma
requisição a terceiro a menos e um dado a menos saindo do navegador de quem visita.

### Realidades do nó único (assuma, não descubra)

| Fato | Consequência prática |
|---|---|
| Deploy causa alguns segundos de indisponibilidade | Aceitável agora. Faça fora do horário comercial. Rolling só quando houver clientes que reclamem |
| Postgres é seu | Você faz backup, upgrade e tuning. Não há botão |
| Sem réplica | Falha de disco = perda de dados sem backup externo |
| Uploads em volume local | Migrar para S3/Blob depois é a razão do `IFileStorage` ([ADR-0010](../adr/0010-armazenamento-de-arquivos.md)) |
| Recursos limitados | Defina `mem_limit` nos serviços. Postgres sem limite consumindo tudo e o OOM killer matando a API é o incidente clássico de VPS |

Ajuste mínimo do Postgres (default de container é conservador demais):
`shared_buffers` ≈ 25% da RAM, `effective_cache_size` ≈ 50–75%, `work_mem`
moderado, `max_connections` baixo com pooling do Npgsql.

---

## <a name="backup"></a>Backup — bloqueador de lançamento

Você vai guardar CPF, RG, ASO e atestado médico de funcionários de terceiros num
VPS de nó único. Perder isso não é bug; é incidente com dever de notificação.

Mínimo aceitável antes do primeiro cliente:

1. `pg_dump` diário, comprimido e **criptografado**, enviado para armazenamento
   **fora do VPS** (Backblaze B2, S3, Cloudflare R2 — custa poucos dólares/mês).
2. Backup dos uploads na mesma rotina.
3. Retenção: 7 diários, 4 semanais, 6 mensais.
4. **Restore testado de verdade**, em ambiente limpo, com procedimento escrito.
   Backup nunca testado não é backup — é esperança.
5. Alerta se o backup não rodar. Falha silenciosa é o modo de falha real.

Faça isso na semana 1, não na semana 20. Um script e um cron.

---

## CI/CD

```yaml
# .github/workflows/ci.yml (esboço)
on: { push: { branches: [main] }, pull_request: }

jobs:
  backend:
    steps:
      - dotnet restore / build --no-restore
      - dotnet test            # Testcontainers sobe Postgres real
      - dotnet list package --vulnerable --include-transitive

  frontend:
    steps:
      - npm ci
      - npm run lint && npm run test -- --watch=false && npm run build
      - npm audit --audit-level=high

  deploy:                       # só em main, após os dois
    steps:
      - build & push das imagens (api, worker) para GHCR
      - build do Angular → artefato
      - ssh: rsync do web-dist, docker compose pull, docker compose up -d
      - smoke test em /healthz/ready; rollback para a TAG anterior se falhar
```

Detalhes que importam:

- Imagem taggeada por SHA do commit, nunca `latest`. Rollback é trocar `TAG` e subir.
- Migrations rodam no **startup do Worker**, com advisory lock — não em passo
  separado do pipeline, que dessincroniza de código.
- Deploy por SSH com chave dedicada e usuário sem root.
- Escaneamento de imagem (Trivy) e secret scanning no PR.

### `deploy/deploy.sh`

O deploy em si é um script, não um passo enterrado no YAML do CI: você precisa
conseguir subir e **voltar** da sua máquina, às 22h, sem depender do GitHub estar de
pé. O workflow do CI pode chamá-lo depois; a ordem certa é essa, não a inversa.

```bash
./deploy/deploy.sh              # testes → imagens → push → envio → subida → readiness
./deploy/deploy.sh --status     # o que está no ar
./deploy/deploy.sh --rollback   # volta para a versão anterior registrada no servidor
```

Ele guarda no servidor a versão atual e a anterior (`.tag-atual`, `.tag-anterior`), e
volta sozinho se a subida não passar no readiness. Procedimento completo, incluindo
provisionamento e restore, em [`deploy/README.md`](../../deploy/README.md).

### Readiness que significa alguma coisa

A API e o Worker sobem juntos, mas quem aplica as migrations é o Worker. Sem cuidado,
a API se declara pronta com o schema atrasado, o Caddy manda tráfego e o usuário recebe
erro de coluna inexistente — durante o deploy, que é exatamente quando ninguém está
olhando o log.

Por isso `/healthz/ready` inclui uma checagem de **migrations pendentes**, e o endpoint
responde 503 em `Degraded` (o padrão do ASP.NET Core é 200, que aqui seria mentira). O
`deploy.sh` só considera a subida concluída quando esse endpoint fica verde.

**Rollback volta código, não banco.** Migration aplicada continua aplicada. Consequência
prática: mudança destrutiva de schema vai em duas etapas — adiciona o novo, migra os
dados, remove o antigo num deploy posterior. Nunca de uma vez.

---

## Observabilidade

`AddServiceDefaults()` já entrega o essencial. O que falta configurar:

- **Traces**: ASP.NET Core, `HttpClient` e EF Core instrumentados. Adicione
  `ActivitySource` próprio nos fluxos de negócio críticos (aprovação de férias,
  cálculo de disponibilidade, publicação de outbox).
- **Métricas** que valem a pena desde o dia 1:
  `mamao.outbox.pending` (fila crescendo = worker parado),
  `mamao.outbox.failed`,
  `mamao.documents.expiring`,
  duração do cálculo de disponibilidade,
  p95 do dashboard.
- **Logs estruturados** com `tenant_id`, `user_id` e `correlation_id` em todo
  escopo. Sem `tenant_id` no log, investigar problema de cliente vira arqueologia.
  Nunca logar CPF, documento ou conteúdo de anexo.
- **Correlation id**: aceite `traceparent` do frontend, propague, devolva no
  `ProblemDetails`. O usuário reporta o id, você acha o trace.

Para onde exportar: no VPS, o mais barato é um endpoint OTLP gerenciado com plano
gratuito (Grafana Cloud, Honeycomb, SigNoz self-hosted). **Não suba stack de
observabilidade completa no mesmo VPS** — o consumo de recursos compete com o
produto, que é a pior troca possível.

Health checks:

```
/healthz         liveness   — o processo responde
/healthz/ready   readiness  — Postgres alcançável, migrations aplicadas, storage acessível
```

O Docker usa `ready`. Nunca coloque dependência externa lenta no `liveness` — o
container reinicia por causa de um serviço de terceiro fora do ar.

---

## Evolução para Azure (quando houver tração)

O caminho já está preparado pelas decisões tomadas:

| Hoje | Depois | O que muda no código |
|---|---|---|
| Postgres em container | Azure Database for PostgreSQL | connection string |
| Volume local | Azure Blob Storage | implementação de `IFileStorage` |
| Dispatch in-process | Azure Service Bus | implementação do publicador do outbox |
| Docker Compose | Azure Container Apps | `Dockerfile` é o mesmo |
| OTLP para o coletor | Azure Monitor | configuração |
| Secrets em `.env` | Key Vault | provider de configuração |

Nenhuma linha de domínio muda. É esse o critério para dizer que a arquitetura
permite a evolução — e a razão de as abstrações existentes (`IFileStorage`,
publicador de outbox) valerem o custo, enquanto outras não valem.

**Gatilho para migrar:** operação do VPS consumindo tempo que deveria ser de
produto, ou um cliente exigindo SLA/backup gerenciado. Não antes.
