#!/usr/bin/env bash
#
# Deploy do Mamao no VPS — e tambem a primeira configuracao do ambiente.
#
# Roda da SUA maquina (ou do CI) e conduz o servidor por SSH. As imagens sao construidas
# aqui e publicadas no registry; o servidor so faz pull. Construir no VPS competiria com o
# produto pelos recursos do mesmo no unico.
#
#   ./deploy/deploy.sh                 # deploy da HEAD atual (faz o setup se faltar algo)
#   ./deploy/deploy.sh --setup         # so prepara o ambiente, sem publicar nada
#   ./deploy/deploy.sh --status        # o que esta no ar agora
#   ./deploy/deploy.sh --rollback      # volta para a versao anterior
#   ./deploy/deploy.sh --skip-tests    # pula a suite (use com parcimonia)
#   ./deploy/deploy.sh --sim           # responde sim a tudo (CI); nunca sobrescreve segredo
#
# Na primeira execucao ele cria o que faltar: deploy/.env aqui, o diretorio no servidor,
# os segredos de producao (gerados NO servidor) e os arquivos de configuracao.
# Tudo idempotente: rodar de novo nao sobrescreve nada que ja exista.
#
# Ver deploy/README.md e docs/arquitetura/infraestrutura-e-deploy.md

set -Eeuo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$DEPLOY_DIR/.." && pwd)"

# ── saida ─────────────────────────────────────────────────────────────────────
if [[ -t 1 ]]; then
    VERDE=$'\033[0;32m'; AMARELO=$'\033[0;33m'; VERMELHO=$'\033[0;31m'
    AZUL=$'\033[0;36m'; NEUTRO=$'\033[0m'
else
    VERDE=''; AMARELO=''; VERMELHO=''; AZUL=''; NEUTRO=''
fi

passo()  { printf '\n%s▸ %s%s\n' "$VERDE" "$1" "$NEUTRO"; }
info()   { printf '  %s\n' "$1"; }
criou()  { printf '%s  + %s%s\n' "$AZUL" "$1" "$NEUTRO"; }
existe() { printf '  · %s\n' "$1"; }
aviso()  { printf '%s  ! %s%s\n' "$AMARELO" "$1" "$NEUTRO"; }
erro()   { printf '%s  ✗ %s%s\n' "$VERMELHO" "$1" "$NEUTRO" >&2; }
morrer() { erro "$1"; exit 1; }

trap 'erro "Interrompido na linha $LINENO."' ERR

# ── argumentos ────────────────────────────────────────────────────────────────
SKIP_TESTS=false
ASSUMIR_SIM=false
MODO=deploy

while [[ $# -gt 0 ]]; do
    case "$1" in
        --setup|--primeira-vez) MODO=setup ;;
        --status)               MODO=status ;;
        --rollback)             MODO=rollback ;;
        --skip-tests)           SKIP_TESTS=true ;;
        --sim|-y|--yes)         ASSUMIR_SIM=true ;;
        -h|--help)              sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)                      morrer "Argumento desconhecido: $1" ;;
    esac
    shift
done

confirmar() {
    local pergunta="$1" padrao="${2:-N}"

    [[ "$ASSUMIR_SIM" == true ]] && return 0

    if [[ ! -t 0 ]]; then
        # Sem terminal (CI) e sem --sim: nao adivinha. Melhor parar que agir sozinho.
        erro "Precisa de confirmacao para: $pergunta"
        erro "Rode com --sim para responder sim automaticamente."
        return 1
    fi

    local dica='[s/N]'
    [[ "$padrao" == "S" ]] && dica='[S/n]'

    local resposta
    read -r -p "  $pergunta $dica " resposta
    resposta="${resposta:-$padrao}"
    [[ "$resposta" =~ ^[sSyY]$ ]]
}

perguntar() {
    local rotulo="$1" padrao="${2:-}" resposta
    if [[ -n "$padrao" ]]; then
        read -r -p "  $rotulo [$padrao]: " resposta
        printf '%s' "${resposta:-$padrao}"
    else
        while [[ -z "${resposta:-}" ]]; do
            read -r -p "  $rotulo: " resposta
        done
        printf '%s' "$resposta"
    fi
}

# ══════════════════════════════════════════════════════════════════════════════
# Configuracao local (deploy/.env)
# ══════════════════════════════════════════════════════════════════════════════
criar_env_local() {
    passo "Configurando o deploy (deploy/.env)"

    if [[ ! -t 0 ]]; then
        morrer "deploy/.env nao existe e nao ha terminal para perguntar. Copie de deploy/.env.example."
    fi

    info "Este arquivo fica na SUA maquina e diz para onde o deploy aponta."
    info "Os segredos de producao sao gerados depois, direto no servidor."
    printf '\n'

    local ssh_host ssh_user ssh_port ssh_key remote_dir registry public_origin

    ssh_host="$(perguntar 'Host do servidor (IP ou dominio)')"
    ssh_user="$(perguntar 'Usuario SSH' 'mamao')"
    ssh_port="$(perguntar 'Porta SSH' '22')"
    ssh_key="$(perguntar 'Chave SSH privada (Enter usa o ssh-agent)' 'agent')"
    remote_dir="$(perguntar 'Diretorio no servidor' '/opt/mamao')"
    registry="$(perguntar 'Registry das imagens' 'ghcr.io/matheusd0mingos')"
    public_origin="$(perguntar 'Origem publica' 'https://app.mamao.tech')"

    [[ "$ssh_key" == "agent" ]] && ssh_key=""

    cat > "$DEPLOY_DIR/.env" <<EOF
# Gerado por deploy.sh. Config do DEPLOY — nao contem segredo de producao.
# Os segredos vivem em $remote_dir/.env, no servidor.

SSH_HOST=$ssh_host
SSH_USER=$ssh_user
SSH_PORT=$ssh_port
SSH_KEY=$ssh_key

REMOTE_DIR=$remote_dir
REGISTRY=$registry
PUBLIC_ORIGIN=$public_origin
EOF

    chmod 600 "$DEPLOY_DIR/.env"
    criou "deploy/.env"
}

[[ -f "$DEPLOY_DIR/.env" ]] || criar_env_local

# shellcheck source=/dev/null
set -a; source "$DEPLOY_DIR/.env"; set +a

: "${SSH_HOST:?defina SSH_HOST em deploy/.env}"
: "${SSH_USER:?defina SSH_USER em deploy/.env}"
: "${REMOTE_DIR:?defina REMOTE_DIR em deploy/.env}"
: "${REGISTRY:?defina REGISTRY em deploy/.env}"
: "${PUBLIC_ORIGIN:?defina PUBLIC_ORIGIN em deploy/.env}"

SSH_PORT="${SSH_PORT:-22}"
SSH_OPTS=(-p "$SSH_PORT" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15)
[[ -n "${SSH_KEY:-}" ]] && SSH_OPTS+=(-i "${SSH_KEY/#\~/$HOME}")

# Host publico sem esquema — o Caddyfile usa isso para emitir o certificado.
PUBLIC_HOST="${PUBLIC_ORIGIN#*://}"
PUBLIC_HOST="${PUBLIC_HOST%%/*}"

remoto()     { ssh "${SSH_OPTS[@]}" "$SSH_USER@$SSH_HOST" "$@"; }
remoto_faz() { remoto "cd '$REMOTE_DIR' && $*"; }

ARQUIVO_VERSAO="$REMOTE_DIR/.tag-atual"
ARQUIVO_ANTERIOR="$REMOTE_DIR/.tag-anterior"

# ══════════════════════════════════════════════════════════════════════════════
# Preparacao do ambiente — idempotente
# ══════════════════════════════════════════════════════════════════════════════
verificar_ferramentas_locais() {
    local faltando=()
    for cmd in "$@"; do
        command -v "$cmd" >/dev/null || faltando+=("$cmd")
    done

    if ((${#faltando[@]})); then
        erro "Comandos ausentes nesta maquina: ${faltando[*]}"
        info "Veja os pre-requisitos no README da raiz."
        return 1
    fi
}

verificar_conexao() {
    passo "Conferindo o acesso ao servidor"

    if ! remoto "echo ok" >/dev/null 2>&1; then
        erro "Nao consegui conectar em $SSH_USER@$SSH_HOST:$SSH_PORT."
        info "Confira a chave, a porta e se o usuario existe. Para criar o usuario:"
        info "  ssh root@$SSH_HOST"
        info "  adduser --disabled-password --gecos '' $SSH_USER"
        info "  mkdir -p /home/$SSH_USER/.ssh && cp ~/.ssh/authorized_keys /home/$SSH_USER/.ssh/"
        info "  chown -R $SSH_USER:$SSH_USER /home/$SSH_USER/.ssh && chmod 700 /home/$SSH_USER/.ssh"
        return 1
    fi

    existe "Conexao com $SSH_USER@$SSH_HOST:$SSH_PORT"
}

garantir_docker_remoto() {
    passo "Conferindo o Docker no servidor"

    if remoto "command -v docker >/dev/null && docker compose version >/dev/null 2>&1"; then
        existe "Docker e plugin compose"

        if ! remoto "docker info >/dev/null 2>&1"; then
            erro "O Docker esta instalado mas $SSH_USER nao consegue usa-lo."
            info "No servidor: sudo usermod -aG docker $SSH_USER  (e refaca o login)"
            return 1
        fi
        return 0
    fi

    aviso "Docker nao encontrado no servidor."

    if ! confirmar "Instalar o Docker agora? (usa sudo no servidor)"; then
        info "Sem problema. Instale manualmente e rode de novo:"
        info "  https://docs.docker.com/engine/install/debian/"
        return 1
    fi

    passo "Instalando o Docker (pode levar alguns minutos)"

    remoto "bash -s" <<'REMOTO'
set -Eeuo pipefail

sudo apt-get update -q
sudo apt-get install -y -q ca-certificates curl

sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL "https://download.docker.com/linux/$(. /etc/os-release && echo "$ID")/gpg" \
    -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/$(. /etc/os-release && echo "$ID") \
$(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
    sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update -q
sudo apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-compose-plugin

sudo usermod -aG docker "$USER"
REMOTO

    criou "Docker instalado"

    # O grupo novo so vale na proxima sessao SSH; a conexao atual ainda nao enxerga.
    if ! remoto "docker info >/dev/null 2>&1"; then
        aviso "O grupo docker so vale na proxima sessao. Rode o comando de novo."
        return 1
    fi
}

garantir_diretorio_remoto() {
    passo "Preparando $REMOTE_DIR no servidor"

    if remoto "test -d '$REMOTE_DIR'"; then
        existe "$REMOTE_DIR"
    else
        # /opt costuma exigir root; tenta sem sudo primeiro.
        if remoto "mkdir -p '$REMOTE_DIR' 2>/dev/null"; then
            criou "$REMOTE_DIR"
        elif confirmar "Criar $REMOTE_DIR com sudo?" S; then
            remoto "sudo mkdir -p '$REMOTE_DIR' && sudo chown '$SSH_USER':'$SSH_USER' '$REMOTE_DIR'"
            criou "$REMOTE_DIR (com sudo)"
        else
            return 1
        fi
    fi

    remoto "mkdir -p '$REMOTE_DIR/web-dist' '$REMOTE_DIR/landing'"
    existe "$REMOTE_DIR/web-dist e $REMOTE_DIR/landing"
}

garantir_segredos_remotos() {
    passo "Segredos de producao ($REMOTE_DIR/.env)"

    if remoto "test -f '$REMOTE_DIR/.env'"; then
        existe "$REMOTE_DIR/.env (mantido — nunca sobrescrevemos segredo)"
        return 0
    fi

    info "Gerando as senhas NO servidor. Elas nunca passam por esta maquina."

    # openssl -hex para a senha do banco: entra numa connection string, e hex evita
    # qualquer caractere que precise de escape. base64 no resto, que sao valores livres.
    remoto "bash -s" <<REMOTO
set -Eeuo pipefail

command -v openssl >/dev/null || { sudo apt-get update -q && sudo apt-get install -y -q openssl; }

PG_PASS="\$(openssl rand -hex 24)"
APP_PASS="\$(openssl rand -hex 24)"
JWT_KEY="\$(openssl rand -base64 48)"
BACKUP_PASS="\$(openssl rand -base64 32)"

cat > '$REMOTE_DIR/.env' <<ENV
# Gerado por deploy.sh em \$(date -u +%Y-%m-%dT%H:%M:%SZ). NUNCA versionar.
# Perder este arquivo significa perder o acesso ao banco e invalidar as sessoes.

POSTGRES_USER=mamao_owner
POSTGRES_PASSWORD=\$PG_PASS
APP_ROLE_PASSWORD=\$APP_PASS

# Duas conexoes: o Worker migra como dono, a API conecta com o role sem BYPASSRLS.
# Apontar a API para o owner desliga a Row-Level Security em silencio.
DB_CONNECTION_OWNER=Host=postgres;Database=mamao;Username=mamao_owner;Password=\$PG_PASS
DB_CONNECTION_APP=Host=postgres;Database=mamao;Username=mamao_app;Password=\$APP_PASS

JWT_SIGNING_KEY=\$JWT_KEY

PUBLIC_ORIGIN=$PUBLIC_ORIGIN
PUBLIC_HOST=$PUBLIC_HOST

# Dominio da landing (apex). O app fica no subdominio acima.
LANDING_HOST=${PUBLIC_HOST#app.}

# ── e-mail (SMTP) ─────────────────────────────────────────────────────────────
# Preencha antes de abrir para clientes: sem SMTP nao ha recuperacao de senha, e
# quem esquecer a senha fica sem acesso. A aplicacao sobe assim mesmo e registra
# um log CRITICAL no startup.
SMTP_HOST=
SMTP_PORT=587
SMTP_USE_STARTTLS=true
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM_ADDRESS=nao-responda@$PUBLIC_HOST
SMTP_FROM_NAME=Mamao
SMTP_REPLY_TO=

# Coletor OTLP. Vazio desliga a exportacao.
OTEL_ENDPOINT=

# Backup (backup.sh). Configure o rclone antes de agendar o cron.
BACKUP_PASSPHRASE=\$BACKUP_PASS
BACKUP_REMOTE=
ENV

chmod 600 '$REMOTE_DIR/.env'
REMOTO

    criou "$REMOTE_DIR/.env com senhas geradas (modo 600)"
    aviso "Guarde uma copia do BACKUP_PASSPHRASE fora do servidor —"
    aviso "sem ela, o backup criptografado nao pode ser restaurado."
    aviso "Preencha SMTP_HOST em $REMOTE_DIR/.env: sem e-mail nao ha recuperacao de senha."
}

enviar_configuracao() {
    passo "Enviando arquivos de configuracao"

    rsync -az -e "ssh ${SSH_OPTS[*]}" \
        "$DEPLOY_DIR/docker-compose.yml" \
        "$DEPLOY_DIR/Caddyfile" \
        "$DEPLOY_DIR/init-db.sql" \
        "$DEPLOY_DIR/backup.sh" \
        "$SSH_USER@$SSH_HOST:$REMOTE_DIR/"

    remoto "chmod +x '$REMOTE_DIR/backup.sh'"
    existe "compose, Caddyfile, init-db.sql, backup.sh"
}

ambiente_pronto() {
    remoto "test -d '$REMOTE_DIR' && test -f '$REMOTE_DIR/.env' && command -v docker >/dev/null" 2>/dev/null
}

preparar_ambiente() {
    verificar_conexao
    garantir_docker_remoto
    garantir_diretorio_remoto
    garantir_segredos_remotos
    enviar_configuracao
}

# ══════════════════════════════════════════════════════════════════════════════
# Subida e verificacao
# ══════════════════════════════════════════════════════════════════════════════

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

    aguardar_saude || return 1

    passo "Conferindo pela borda"
    if curl -fsS --max-time 20 "$PUBLIC_ORIGIN/healthz" >/dev/null 2>&1; then
        info "$PUBLIC_ORIGIN/healthz respondeu."
    else
        aviso "A borda nao respondeu. O container esta saudavel — verifique DNS, Caddy e Cloudflare."
        aviso "No primeiro deploy, deixe o proxy do Cloudflare DESLIGADO ate o certificado sair."
    fi
}

# ══════════════════════════════════════════════════════════════════════════════
# Modos
# ══════════════════════════════════════════════════════════════════════════════
if [[ "$MODO" == status ]]; then
    passo "Estado do ambiente"
    info "Servidor: $SSH_USER@$SSH_HOST:$SSH_PORT"

    if ! ambiente_pronto; then
        aviso "Ambiente ainda nao preparado. Rode: ./deploy/deploy.sh --setup"
        exit 0
    fi

    info "Versao no ar:    $(remoto "cat '$ARQUIVO_VERSAO' 2>/dev/null || echo '(nenhuma)'")"
    info "Versao anterior: $(remoto "cat '$ARQUIVO_ANTERIOR' 2>/dev/null || echo '(nenhuma)'")"
    remoto_faz "docker compose ps" || true
    exit 0
fi

if [[ "$MODO" == setup ]]; then
    verificar_ferramentas_locais ssh rsync
    preparar_ambiente

    passo "Ambiente pronto"
    info "Proximo passo: ./deploy/deploy.sh"
    info "Antes do primeiro deploy: docker login ${REGISTRY%%/*}"
    exit 0
fi

if [[ "$MODO" == rollback ]]; then
    ambiente_pronto || morrer "Ambiente nao preparado. Rode: ./deploy/deploy.sh --setup"

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

# ══════════════════════════════════════════════════════════════════════════════
# Deploy
# ══════════════════════════════════════════════════════════════════════════════
passo "Verificacoes iniciais"

verificar_ferramentas_locais docker rsync ssh curl git dotnet npm

cd "$REPO_DIR"

if [[ -n "$(git status --porcelain)" ]]; then
    aviso "Ha alteracoes nao commitadas. A imagem sera marcada com o SHA do ultimo commit,"
    aviso "entao o que subir pode NAO ser o que voce esta vendo aqui."
    confirmar "Continuar mesmo assim?" || exit 1
fi

TAG="$(git rev-parse --short=12 HEAD)"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"

info "Commit: $TAG ($BRANCH)"
info "Destino: $SSH_USER@$SSH_HOST -> $REMOTE_DIR"
info "Origem publica: $PUBLIC_ORIGIN"

# Primeira execucao: prepara tudo em vez de morrer reclamando.
if ambiente_pronto; then
    existe "Ambiente ja preparado"
    enviar_configuracao
else
    aviso "Ambiente ainda nao preparado neste servidor."
    confirmar "Preparar agora? (cria diretorio, gera segredos e envia a configuracao)" S \
        || morrer "Rode ./deploy/deploy.sh --setup quando estiver pronto."
    preparar_ambiente
fi

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
if ! docker push "$REGISTRY/mamao-api:$TAG"; then
    erro "Push recusado. Falta autenticar: docker login ${REGISTRY%%/*}"
    exit 1
fi
docker push "$REGISTRY/mamao-worker:$TAG"

passo "Construindo o frontend"
(
    cd web/mamao-web
    npm ci --silent
    npm run build
)

passo "Enviando os arquivos estaticos"
# O Angular vai como estatico servido pelo Caddy; sem container Node em producao.
rsync -az --delete -e "ssh ${SSH_OPTS[*]}" \
    "web/mamao-web/dist/mamao-web/browser/" \
    "$SSH_USER@$SSH_HOST:$REMOTE_DIR/web-dist/"

# A landing nao tem build: e HTML estatico, vai como esta.
rsync -az --delete -e "ssh ${SSH_OPTS[*]}" \
    "web/landing/" \
    "$SSH_USER@$SSH_HOST:$REMOTE_DIR/landing/"

# ── subida ────────────────────────────────────────────────────────────────────
ANTERIOR="$(remoto "cat '$ARQUIVO_VERSAO' 2>/dev/null || true")"

if ! subir_e_verificar "$TAG"; then
    if [[ -n "$ANTERIOR" ]]; then
        erro "Deploy falhou. Voltando para $ANTERIOR."
        subir_e_verificar "$ANTERIOR" || erro "O rollback automatico tambem falhou. Intervencao manual necessaria."
    else
        erro "Deploy falhou e nao ha versao anterior para voltar."
        info "Logs: ssh $SSH_USER@$SSH_HOST 'cd $REMOTE_DIR && docker compose logs --tail 100'"
    fi
    exit 1
fi

# So registra a versao depois de ela provar que sobe.
[[ -n "$ANTERIOR" ]] && remoto "echo '$ANTERIOR' > '$ARQUIVO_ANTERIOR'"
remoto "echo '$TAG' > '$ARQUIVO_VERSAO'"

passo "Limpando imagens antigas no servidor"
remoto "docker image prune -af --filter 'until=168h'" >/dev/null 2>&1 || aviso "Limpeza falhou (nao bloqueia)."

passo "Deploy concluido: $TAG"
info "Aplicacao: $PUBLIC_ORIGIN"

if [[ -z "$ANTERIOR" ]]; then
    printf '\n'
    aviso "Primeiro deploy. Ainda faltam duas coisas para o ambiente ficar de pe:"
    info "  1. Backup: configure o rclone no servidor, preencha BACKUP_REMOTE em"
    info "     $REMOTE_DIR/.env e agende o cron (deploy/README.md)."
    info "  2. Restore: exercite o procedimento de restore ANTES do primeiro cliente."
    info "     Backup nunca testado nao e backup."
else
    info "Para voltar: ./deploy/deploy.sh --rollback  (volta para $ANTERIOR)"
fi

exit 0
