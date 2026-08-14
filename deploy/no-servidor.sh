#!/usr/bin/env bash
#
# Deploy do jeito simples: roda NO PROPRIO SERVIDOR, dentro do repositorio clonado.
#
#   git pull && sudo ./deploy/no-servidor.sh
#
# Nao precisa de registry, de token, de CI nem de acesso SSH configurado: constroi as
# imagens aqui mesmo e sobe. A unica coisa que o servidor precisa ter e o Docker — nem
# .NET nem Node, porque a construcao acontece dentro de containers.
#
# A troca consciente: construir aqui usa CPU e memoria da mesma maquina que serve o
# produto, por alguns minutos a cada deploy. Sem cliente no ar isso nao custa nada.
# Quando custar, o caminho com registry ja esta pronto em deploy.sh.
#
# Rollback: `git checkout <sha> && sudo ./deploy/no-servidor.sh`. Volta o codigo, NAO
# o banco — migration ja aplicada continua aplicada.

set -Eeuo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DESTINO="${MAMAO_DIR:-/opt/mamao}"

if [[ -t 1 ]]; then
    VERDE=$'\033[0;32m'; AMARELO=$'\033[0;33m'; VERMELHO=$'\033[0;31m'; NEUTRO=$'\033[0m'
else
    VERDE=''; AMARELO=''; VERMELHO=''; NEUTRO=''
fi

passo()  { printf '\n%s▸ %s%s\n' "$VERDE" "$1" "$NEUTRO"; }
info()   { printf '  %s\n' "$1"; }
aviso()  { printf '%s  ! %s%s\n' "$AMARELO" "$1" "$NEUTRO"; }
morrer() { printf '%s  ✗ %s%s\n' "$VERMELHO" "$1" "$NEUTRO" >&2; exit 1; }

trap 'morrer "Falhou na linha $LINENO."' ERR

[[ $EUID -eq 0 ]] || morrer "Rode com sudo: sudo ./deploy/no-servidor.sh"

# ── Docker ────────────────────────────────────────────────────────────────────
passo "Docker"
if command -v docker >/dev/null && docker compose version >/dev/null 2>&1; then
    info "ja instalado"
else
    info "instalando…"
    apt-get update -q
    apt-get install -y -q ca-certificates curl
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL "https://download.docker.com/linux/$(. /etc/os-release && echo "$ID")/gpg" \
        -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/$(. /etc/os-release && echo "$ID") \
$(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
        > /etc/apt/sources.list.d/docker.list
    apt-get update -q
    apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-compose-plugin
    info "instalado"
fi

mkdir -p "$DESTINO/web-dist" "$DESTINO/landing"

# ── segredos ──────────────────────────────────────────────────────────────────
# Gerados uma vez e NUNCA sobrescritos: trocar a senha do banco aqui derrubaria o
# acesso aos dados que ja existem.
passo "Segredos ($DESTINO/.env)"
if [[ -f "$DESTINO/.env" ]]; then
    info "ja existem — mantidos"
else
    dominio="${DOMINIO:-}"
    if [[ -z "$dominio" ]]; then
        [[ -t 0 ]] || morrer "Sem terminal. Rode com DOMINIO=app.seudominio.com.br"
        read -r -p "  Dominio da aplicacao [app.mamao.tech]: " dominio
        dominio="${dominio:-app.mamao.tech}"
    fi

    # As senhas entram nas connection strings JA RESOLVIDAS: o compose nao expande
    # variavel de dentro do proprio .env, entao um "$POSTGRES_PASSWORD" ali viraria
    # senha literal e o banco recusaria a conexao.
    # -hex na senha do banco: ela entra numa connection string, e hex nao tem
    # caractere que precise de escape.
    senha_pg="$(openssl rand -hex 24)"
    senha_app="$(openssl rand -hex 24)"

    umask 077
    cat > "$DESTINO/.env" <<ENV
# Gerado por no-servidor.sh em $(date -u +%Y-%m-%dT%H:%M:%SZ). NUNCA versionar.
# Perder este arquivo significa perder o acesso ao banco.

POSTGRES_USER=mamao_owner
POSTGRES_PASSWORD=$senha_pg
APP_ROLE_PASSWORD=$senha_app

# Duas conexoes: o Worker migra como dono, a API conecta com o role sem BYPASSRLS.
# Apontar a API para o owner desliga a Row-Level Security em silencio.
DB_CONNECTION_OWNER=Host=postgres;Database=mamao;Username=mamao_owner;Password=$senha_pg
DB_CONNECTION_APP=Host=postgres;Database=mamao;Username=mamao_app;Password=$senha_app

JWT_SIGNING_KEY=$(openssl rand -base64 48)

PUBLIC_ORIGIN=https://$dominio
PUBLIC_HOST=$dominio
LANDING_HOST=${dominio#app.}

# ── e-mail ────────────────────────────────────────────────────────────────────
# Sem SMTP nao ha convite nem recuperacao de senha. A aplicacao sobe assim mesmo e
# registra um CRITICAL no log. Preencha antes de abrir para o primeiro cliente.
SMTP_HOST=
SMTP_PORT=587
SMTP_USE_STARTTLS=true
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM_ADDRESS=nao-responda@$dominio
SMTP_FROM_NAME=Mamao
SMTP_REPLY_TO=

OTEL_ENDPOINT=

# Backup. Configure o rclone antes de agendar o cron — ver deploy/README.md.
BACKUP_PASSPHRASE=$(openssl rand -base64 32)
BACKUP_REMOTE=
ENV

    umask 022
    info "gerados"
    aviso "Guarde o BACKUP_PASSPHRASE de $DESTINO/.env fora deste servidor."
fi

# As duas connection strings referenciam as variaveis acima; o shell do compose nao
# expande isso sozinho, entao resolvemos aqui na leitura.
set -a; . "$DESTINO/.env"; set +a

# ── configuracao ──────────────────────────────────────────────────────────────
# Reenviada a cada deploy para nao dessincronizar com o repositorio.
passo "Configuracao"
cp "$REPO_DIR/deploy/docker-compose.yml" "$REPO_DIR/deploy/Caddyfile" \
   "$REPO_DIR/deploy/init-db.sql" "$REPO_DIR/deploy/backup.sh" "$DESTINO/"
chmod +x "$DESTINO/backup.sh"
info "compose, Caddyfile, init-db, backup"

# ── construcao ────────────────────────────────────────────────────────────────
cd "$REPO_DIR"
TAG="$(git rev-parse --short=12 HEAD)"

[[ -z "$(git status --porcelain)" ]] || aviso "Ha alteracoes nao commitadas; a versao sera marcada como $TAG."

passo "Construindo a API e o Worker ($TAG)"
docker build -f src/Mamao.Api/Dockerfile    -t "mamao-api:$TAG"    .
docker build -f src/Mamao.Worker/Dockerfile -t "mamao-worker:$TAG" .

passo "Construindo o site"
# Node num container: o servidor nao precisa ter Node instalado. O resultado sai no
# diretorio montado, entao fica disponivel aqui fora.
docker run --rm -v "$REPO_DIR/web/mamao-web:/app" -w /app node:24-alpine \
    sh -c 'npm ci && npm run build'

rm -rf "${DESTINO:?}/web-dist/"* "${DESTINO:?}/landing/"*
cp -r "$REPO_DIR/web/mamao-web/dist/mamao-web/browser/." "$DESTINO/web-dist/"
cp -r "$REPO_DIR/web/landing/." "$DESTINO/landing/"
info "site pronto"

# ── subida ────────────────────────────────────────────────────────────────────
# TAG e as imagens vao para dentro do .env, e nao como variavel de um comando so.
# O compose precisa delas em TODA invocacao — inclusive nos `docker compose ps`,
# `logs` e `restart` que voce der na mao depois, quando o script ja terminou.
definir_no_env() {
    local chave="$1" valor="$2"
    sed -i "/^$chave=/d" "$DESTINO/.env"
    printf '%s=%s\n' "$chave" "$valor" >> "$DESTINO/.env"
}

definir_no_env TAG "$TAG"
definir_no_env IMAGE_API mamao-api
definir_no_env IMAGE_WORKER mamao-worker

passo "Subindo"
cd "$DESTINO"
docker compose --env-file .env up -d --remove-orphans

# /healthz/ready so responde 200 quando o banco esta acessivel E as migrations foram
# aplicadas pelo Worker. E o unico sinal confiavel de que subiu de verdade.
passo "Esperando ficar pronto"
for i in $(seq 1 60); do
    if docker compose exec -T api wget -qO- http://localhost:8080/healthz/ready >/dev/null 2>&1; then
        info "pronto (${i}x3s)"
        echo "$TAG" > "$DESTINO/.tag-atual"
        printf '\n%s  https://%s esta no ar (%s)%s\n\n' "$VERDE" "$PUBLIC_HOST" "$TAG" "$NEUTRO"
        exit 0
    fi
    sleep 3
done

docker compose ps || true
docker compose logs --tail 60 api worker || true
morrer "Nao ficou pronto em 3 minutos. Estado e log acima."
