# Deploy

Cloudflare → Caddy → Docker Compose, num VPS de nó único.
Decisões em [ADR-0011](../docs/adr/0011-aspire-e-deploy.md) e
[infraestrutura](../docs/arquitetura/infraestrutura-e-deploy.md).

| Arquivo | Papel |
|---|---|
| `deploy.sh` | Executa o deploy a partir da sua máquina, por SSH |
| `docker-compose.yml` | Topologia de produção |
| `Caddyfile` | TLS, estáticos do Angular, proxy de `/api` |
| `init-db.sql` | Cria o role `mamao_app` (sem `BYPASSRLS`) na primeira subida |
| `backup.sh` | Dump + uploads, criptografado, enviado para fora do VPS |
| `.env.example` | Config do **deploy** (fica na sua máquina) |
| `.env.producao.example` | **Segredos** de produção (ficam só no servidor) |

Os dois `.env` são diferentes de propósito: a senha do banco e a chave do JWT nunca
saem do servidor nem passam pela sua máquina no fluxo normal.

---

## Provisionamento (uma vez)

### 1. Usuário e Docker no servidor

```bash
ssh root@vps

adduser --disabled-password --gecos '' mamao
mkdir -p /home/mamao/.ssh
cp ~/.ssh/authorized_keys /home/mamao/.ssh/
chown -R mamao:mamao /home/mamao/.ssh
chmod 700 /home/mamao/.ssh && chmod 600 /home/mamao/.ssh/authorized_keys

# Docker (Debian trixie)
apt-get update && apt-get install -y ca-certificates curl
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
  https://download.docker.com/linux/debian trixie stable" > /etc/apt/sources.list.d/docker.list
apt-get update && apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
usermod -aG docker mamao

mkdir -p /opt/mamao && chown mamao:mamao /opt/mamao
```

Feche o SSH por senha (`PasswordAuthentication no`) e o login de root antes de expor a
máquina.

### 2. Segredos no servidor

```bash
scp deploy/.env.producao.example mamao@vps:/opt/mamao/.env
ssh mamao@vps 'chmod 600 /opt/mamao/.env'
ssh mamao@vps 'vi /opt/mamao/.env'     # gere as senhas com: openssl rand -base64 48
```

### 3. Config do deploy na sua máquina

```bash
cp deploy/.env.example deploy/.env     # já está no .gitignore
vi deploy/.env
docker login ghcr.io                   # token com write:packages
```

### 4. DNS

Aponte `app.mamao.tech` para o IP do VPS no Cloudflare. Deixe o proxy (nuvem laranja)
**desligado no primeiro deploy**, para o Caddy conseguir emitir o certificado; ligue
depois.

---

## Deploy

```bash
./deploy/deploy.sh              # testes → imagens → push → envio → subida → readiness
./deploy/deploy.sh --status     # o que está no ar
./deploy/deploy.sh --rollback   # volta para a versão anterior
./deploy/deploy.sh --skip-tests # só quando você já rodou a suíte
```

O que o script faz, na ordem:

1. Confere ferramentas, `.env` local e `.env` no servidor
2. Avisa se há alteração não commitada (a imagem é marcada com o SHA do commit)
3. Roda a suíte
4. Constrói e publica `mamao-api` e `mamao-worker` marcadas com o SHA — **nunca `latest`**
5. Constrói o Angular e envia o `dist` por rsync
6. Envia compose, Caddyfile, init-db e backup
7. `docker compose pull && up -d`
8. **Espera o `/healthz/ready`**, que só fica verde quando as migrations foram aplicadas
9. Confere pela borda pública
10. Registra a versão e guarda a anterior para rollback
11. Limpa imagens com mais de 7 dias

Se qualquer passo depois da subida falhar, o script volta sozinho para a versão anterior.

### O que o rollback não faz

Volta o **código**, não o **banco**. Migration já aplicada continua aplicada. Se a versão
com problema fez mudança destrutiva de schema, o caminho é restaurar o backup — por isso
mudança destrutiva deve ser feita em duas etapas (adicionar o novo, migrar, remover o
antigo num deploy posterior), nunca de uma vez.

### Indisponibilidade

Nó único: o `up -d` recria os containers e há alguns segundos de indisponibilidade.
Aceitável neste estágio — faça fora do horário comercial do cliente. Rolling update só
quando houver cliente que reclame.

---

## Backup

```bash
ssh mamao@vps 'crontab -e'
# 15 3 * * * /opt/mamao/backup.sh >> /var/log/mamao-backup.log 2>&1
```

Requer `rclone` configurado no servidor com um destino **fora do VPS** (B2, S3, R2) e
`gpg` instalado. Retenção: 7 diários, 4 semanais, 6 mensais.

## Restore — **teste antes do primeiro cliente**

Backup nunca testado não é backup, é esperança. Faça este exercício inteiro, cronometrado,
em uma máquina limpa, e anote quanto tempo levou.

```bash
# 1. Baixe e descriptografe
rclone copy b2:mamao-backups/2026/08/mamao-20260814T031500Z.dump.gpg .
gpg --batch --passphrase "$BACKUP_PASSPHRASE" -d mamao-20260814T031500Z.dump.gpg \
    > mamao.dump

# 2. Suba um Postgres limpo
docker run -d --name restore-teste -e POSTGRES_PASSWORD=teste -p 55432:5432 postgres:17-alpine
docker exec restore-teste createdb -U postgres mamao

# 3. Restaure
docker cp mamao.dump restore-teste:/tmp/
docker exec restore-teste pg_restore -U postgres -d mamao --no-owner /tmp/mamao.dump

# 4. Confira que os dados estão lá
docker exec restore-teste psql -U postgres -d mamao \
    -c 'SELECT count(*) FROM people.employees;' \
    -c 'SELECT count(*) FROM identity.tenants;'

# 5. Restaure os uploads e confira que um arquivo abre
gpg --batch --passphrase "$BACKUP_PASSPHRASE" -d uploads-20260814T031500Z.tar.gz.gpg \
    | tar -tzf - | head

docker rm -f restore-teste
```

Para restaurar **em produção**, pare `api` e `worker` antes (`docker compose stop api
worker`), restaure, e só então suba de novo — restaurar com a aplicação escrevendo
produz um estado inconsistente.

---

## Quando migrar para nuvem gerenciada

Gatilho: a operação do VPS consumindo tempo que deveria ser de produto, ou um cliente
exigindo SLA e backup gerenciado. O caminho já está preparado — ver
[infraestrutura](../docs/arquitetura/infraestrutura-e-deploy.md#evolução-para-azure-quando-houver-tração).
