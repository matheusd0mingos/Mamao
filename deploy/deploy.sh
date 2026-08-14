#!/usr/bin/env bash
#
# Deploy do Mamao no VPS.
#
# Roda da SUA maquina (ou do CI) e conduz o servidor por SSH. As imagens sao construidas
# aqui e publicadas no GHCR; o servidor so faz pull. Construir no VPS competiria com o
# produto pelos recursos do mesmo no unico.
#
#   ./deploy/deploy.sh                 # deploy da HEAD atual
#   ./deploy/deploy.sh --skip-tests    # pula a suite (use com parcimonia)
#   ./deploy/deploy.sh --rollback      # volta para a versao anterior registrada no servidor
#   ./deploy/deploy.sh --status        # o que esta no ar agora
#
# Configuracao em deploy/.env (veja .env.example). Nunca comitado.
#
# Ver docs/arquitetura/infraestrutura-e-deploy.md

set -Eeuo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$DEPLOY_DIR/.." && pwd)"

# ── saida ─────────────────────────────────────────────────────────────────────
if [[ -t 1 ]]; then
    VERDE=$'\033[0;32m'; AMARELO=$'\033[0;33m'; VERMELHO=$'\033[0;31m'; NEUTRO=$'\033[0m'
else
    VERDE=''; AMARELO=''; VERMELHO=''; NEUTRO=''
fi

passo()  { printf '\n%s▸ %s%s\n' "$VERDE" "$1" "$NEUTRO"; }
info()   { printf '  %s\n' "$1"; }
aviso()  { printf '%s  ! %s%s\n' "$AMARELO" "$1" "$NEUTRO"; }
erro()   { printf '%s  ✗ %s%s\n' "$VERMELHO" "$1" "$NEUTRO" >&2; }

morrer() { erro "$1"; exit 1; }

trap 'erro "Deploy interrompido na linha $LINENO."' ERR

# ── argumentos ────────────────────────────────────────────────────────────────
SKIP_TESTS=false
MODO=deploy

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-tests) SKIP_TESTS=true ;;
        --rollback)   MODO=rollback ;;
        --status)     MODO=status ;;
        -h|--help)    sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)            morrer "Argumento desconhecido: $1" ;;
    esac
    shift
done

# ── configuracao ──────────────────────────────────────────────────────────────
[[ -f "$DEPLOY_DIR/.env" ]] || morrer "deploy/.env nao encontrado. Copie de deploy/.env.example."

# shellcheck source=/dev/null
set -a; source "$DEPLOY_DIR/.env"; set +a

: "${SSH_HOST:?defina SSH_HOST em deploy/.env}"
: "${SSH_USER:?defina SSH_USER em deploy/.env}"
: "${REMOTE_DIR:?defina REMOTE_DIR em deploy/.env}"
: "${REGISTRY:?defina REGISTRY em deploy/.env}"
: "${PUBLIC_ORIGIN:?defina PUBLIC_ORIGIN em deploy/.env}"

SSH_PORT="${SSH_PORT:-22}"
SSH_OPTS=(-p "$SSH_PORT" -o StrictHostKeyChecking=accept-new)
[[ -n "${SSH_KEY:-}" ]] && SSH_OPTS+=(-i "$SSH_KEY")

remoto()      { ssh "${SSH_OPTS[@]}" "$SSH_USER@$SSH_HOST" "$@"; }
remoto_faz()  { remoto "cd '$REMOTE_DIR' && $*"; }
enviar()      { rsync -az --delete -e "ssh ${SSH_OPTS[*]}" "$@"; }

ARQUIVO_VERSAO="$REMOTE_DIR/.tag-atual"
ARQUIVO_ANTERIOR="$REMOTE_DIR/.tag-anterior"

# ── status ────────────────────────────────────────────────────────────────────
if [[ "$MODO" == status ]]; then
    passo "Estado do ambiente"
    info "Servidor: $SSH_USER@$SSH_HOST:$SSH_PORT"
    info "Versao no ar:  $(remoto "cat '$ARQUIVO_VERSAO' 2>/dev/null || echo '(nenhuma)'")"
    info "Versao anterior: $(remoto "cat '$ARQUIVO_ANTERIOR' 2>/dev/null || echo '(nenhuma)'")"
    remoto_faz "docker compose ps"
    exit 0
fi

# ── espera de readiness ───────────────────────────────────────────────────────
# /healthz/ready so responde 200 quando o banco esta acessivel E as migrations foram
# aplicadas pelo Worker. Ver src/Mamao.Api/PendingMigrationsHealthCheck.cs.
aguardar_saude() {
    local tentativas=40 espera=3

    passo "Aguardando a API ficar pronta"

    for ((i = 1; i <= tentativas; i++)); do
        if remoto_faz "docker compose exec -T api wget -qO- http://localhost:8080/healthz/ready >/dev/null 2>&1"; then
            info "Pronta apos ${i}x${espera}s."
            return 0
        fi
        printf '  aguardando… %d/%d\r' "$i" "$tentativas"
        sleep "$espera"
    done

    printf '\n'
    erro "A API nao ficou pronta em $((tentativas * espera))s."
    remoto_faz "docker compose logs --tail 60 api worker" || true
    return 1
}

subir_e_verificar() {
    local tag="$1"

    passo "Subindo a versao $tag"
    remoto_faz "TAG='$tag' docker compose --env-file .env pull --quiet"
    remoto_faz "TAG='$tag' docker compose --env-file .env up -d --remove-orphans"

    if ! aguardar_saude; then
        return 1
    fi

    passo "Conferindo pela borda"
    if curl -fsS --max-time 20 "$PUBLIC_ORIGIN/healthz" >/dev/null; then
        info "$PUBLIC_ORIGIN/healthz respondeu."
    else
        aviso "A borda nao respondeu. O container esta saudavel — verifique Caddy/Cloudflare."
    fi
}

# ── rollback ──────────────────────────────────────────────────────────────────
if [[ "$MODO" == rollback ]]; then
    ANTERIOR="$(remoto "cat '$ARQUIVO_ANTERIOR' 2>/dev/null || true")"
    [[ -n "$ANTERIOR" ]] || morrer "Nao ha versao anterior registrada no servidor."

    ATUAL="$(remoto "cat '$ARQUIVO_VERSAO' 2>/dev/null || echo '?'")"
    passo "Rollback: $ATUAL -> $ANTERIOR"

    # Rollback nao desfaz migration. Se a versao no ar aplicou mudanca destrutiva de
    # schema, voltar o codigo nao volta o banco — restaure do backup.
    aviso "Rollback volta apenas o codigo. Migrations ja aplicadas permanecem."

    subir_e_verificar "$ANTERIOR" || morrer "Rollback falhou. Intervencao manual necessaria."
    remoto "echo '$ANTERIOR' > '$ARQUIVO_VERSAO'"

    passo "Rollback concluido: $ANTERIOR"
    exit 0
fi

# ── preflight ─────────────────────────────────────────────────────────────────
passo "Verificacoes iniciais"

for cmd in docker rsync ssh curl git dotnet npm; do
    command -v "$cmd" >/dev/null || morrer "Comando ausente: $cmd"
done

cd "$REPO_DIR"

if [[ -n "$(git status --porcelain)" ]]; then
    aviso "Ha alteracoes nao commitadas. A imagem sera marcada com o SHA do ultimo commit,"
    aviso "entao o que subir pode NAO ser o que voce esta vendo aqui."
    read -r -p "  Continuar mesmo assim? [s/N] " resposta
    [[ "$resposta" =~ ^[sS]$ ]] || exit 1
fi

TAG="$(git rev-parse --short=12 HEAD)"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"

info "Commit: $TAG ($BRANCH)"
info "Destino: $SSH_USER@$SSH_HOST -> $REMOTE_DIR"
info "Origem publica: $PUBLIC_ORIGIN"

remoto "test -d '$REMOTE_DIR'" || morrer "$REMOTE_DIR nao existe no servidor. Rode o provisionamento primeiro (deploy/README.md)."
remoto "test -f '$REMOTE_DIR/.env'" || morrer "$REMOTE_DIR/.env nao existe no servidor. Ele guarda os segredos de producao."

# ── testes ────────────────────────────────────────────────────────────────────
if [[ "$SKIP_TESTS" == true ]]; then
    aviso "Testes pulados a pedido."
else
    passo "Rodando os testes"
    dotnet test Mamao.slnx -c Release --nologo
fi

# ── build ─────────────────────────────────────────────────────────────────────
passo "Construindo as imagens ($TAG)"

docker build -f src/Mamao.Api/Dockerfile    -t "$REGISTRY/mamao-api:$TAG"    -t "$REGISTRY/mamao-api:latest"    .
docker build -f src/Mamao.Worker/Dockerfile -t "$REGISTRY/mamao-worker:$TAG" -t "$REGISTRY/mamao-worker:latest" .

passo "Publicando no registry"
docker push "$REGISTRY/mamao-api:$TAG"
docker push "$REGISTRY/mamao-worker:$TAG"

passo "Construindo o frontend"
(
    cd web/mamao-web
    npm ci --silent
    npm run build
)

# ── envio ─────────────────────────────────────────────────────────────────────
passo "Enviando arquivos estaticos e de configuracao"

# O Angular vai como estatico servido pelo Caddy; sem container Node em producao.
enviar "web/mamao-web/dist/mamao-web/browser/" "$SSH_USER@$SSH_HOST:$REMOTE_DIR/web-dist/"

# --delete nao se aplica aqui: o .env de producao vive no servidor e nao pode ser apagado.
rsync -az -e "ssh ${SSH_OPTS[*]}" \
    deploy/docker-compose.yml deploy/Caddyfile deploy/init-db.sql deploy/backup.sh \
    "$SSH_USER@$SSH_HOST:$REMOTE_DIR/"

remoto "chmod +x '$REMOTE_DIR/backup.sh'"

# ── subida ────────────────────────────────────────────────────────────────────
ANTERIOR="$(remoto "cat '$ARQUIVO_VERSAO' 2>/dev/null || true")"

if ! subir_e_verificar "$TAG"; then
    if [[ -n "$ANTERIOR" ]]; then
        erro "Deploy falhou. Voltando para $ANTERIOR."
        subir_e_verificar "$ANTERIOR" || erro "O rollback automatico tambem falhou. Intervencao manual necessaria."
    else
        erro "Deploy falhou e nao ha versao anterior para voltar."
    fi
    exit 1
fi

# So registra a versao depois de ela provar que sobe.
[[ -n "$ANTERIOR" ]] && remoto "echo '$ANTERIOR' > '$ARQUIVO_ANTERIOR'"
remoto "echo '$TAG' > '$ARQUIVO_VERSAO'"

# ── limpeza ───────────────────────────────────────────────────────────────────
passo "Limpando imagens antigas no servidor"
remoto "docker image prune -af --filter 'until=168h'" >/dev/null || aviso "Limpeza falhou (nao bloqueia)."

passo "Deploy concluido: $TAG"
info "Aplicacao: $PUBLIC_ORIGIN"
[[ -n "$ANTERIOR" ]] && info "Para voltar: ./deploy/deploy.sh --rollback  (volta para $ANTERIOR)"
exit 0
