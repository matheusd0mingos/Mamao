#!/usr/bin/env bash
# Restaura um backup — de ensaio ou de verdade — e CRONOMETRA.
#
# Existe porque o Mamão roda num servidor só. Isso não muda com script: o que muda é
# quanto tempo você leva para voltar. Sem isto, restaurar é seguir uma página de README
# às três da manhã, no dia em que você menos tem paciência para ler.
#
#   Ensaio (não toca em nada em produção — restaura num banco descartável):
#     ./deploy/restaurar.sh --ensaio /caminho/mamao-2026….dump.gpg
#
#   De verdade (DESTRÓI o banco atual — pede confirmação digitada):
#     ./deploy/restaurar.sh --producao /caminho/mamao-….dump.gpg /caminho/uploads-….tar.gz.gpg
#
# O par importa: os documentos dos funcionários NÃO estão no dump — estão no tar de
# uploads. Restaurar só o banco devolve a lista de documentos com os arquivos faltando,
# que é pior que estar fora do ar, porque parece que funcionou.

set -Eeuo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INICIO=$(date +%s)

ler() { sed -n "s/^$1=//p" "$DIR/.env" | head -1; }

erro() { echo "ERRO: $*" >&2; exit 1; }
info() { echo "  $*"; }
passo() { echo; echo "── $* ──────────────────────────────────────────"; }

MODO=""
DUMP=""
UPLOADS=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --ensaio|--producao) MODO="${1#--}"; shift ;;
        *.dump.gpg|*.dump)   DUMP="$1"; shift ;;
        *.tar.gz.gpg|*.tar.gz) UPLOADS="$1"; shift ;;
        *) erro "argumento não reconhecido: $1" ;;
    esac
done

[[ -n "$MODO" ]] || erro "diga --ensaio ou --producao. Sem padrão de propósito: um deles apaga o banco."
[[ -n "$DUMP" ]] || erro "informe o arquivo do dump."
[[ -f "$DUMP" ]] || erro "não achei $DUMP"

PASSPHRASE="$(ler BACKUP_PASSPHRASE)"
POSTGRES_USER="$(ler POSTGRES_USER)"
: "${PASSPHRASE:?defina BACKUP_PASSPHRASE em .env}"
: "${POSTGRES_USER:?defina POSTGRES_USER em .env}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ── 1. decifrar ───────────────────────────────────────────────────────────────
passo "decifrando"

if [[ "$DUMP" == *.gpg ]]; then
    gpg --batch --yes --quiet --passphrase "$PASSPHRASE" --decrypt "$DUMP" > "$WORK/mamao.dump" \
        || erro "não consegui decifrar. Senha errada, ou o arquivo veio corrompido."
else
    cp "$DUMP" "$WORK/mamao.dump"
fi

TAMANHO=$(stat -c%s "$WORK/mamao.dump")
(( TAMANHO > 10240 )) || erro "o dump tem $TAMANHO bytes. Isso não é um backup."
info "dump: $(numfmt --to=iec "$TAMANHO" 2>/dev/null || echo "$TAMANHO bytes")"

if [[ -n "$UPLOADS" ]]; then
    [[ -f "$UPLOADS" ]] || erro "não achei $UPLOADS"

    if [[ "$UPLOADS" == *.gpg ]]; then
        gpg --batch --yes --quiet --passphrase "$PASSPHRASE" --decrypt "$UPLOADS" > "$WORK/uploads.tar.gz" \
            || erro "não consegui decifrar o tar de uploads."
    else
        cp "$UPLOADS" "$WORK/uploads.tar.gz"
    fi

    info "uploads: $(numfmt --to=iec "$(stat -c%s "$WORK/uploads.tar.gz")" 2>/dev/null)"
else
    echo
    echo "  ATENÇÃO: você não passou o tar de uploads."
    echo "  Os DOCUMENTOS dos funcionários não estão no dump. Restaurar só o banco devolve"
    echo "  a lista de documentos com os arquivos faltando."
fi

# ── 2. restaurar ──────────────────────────────────────────────────────────────
if [[ "$MODO" == "ensaio" ]]; then
    passo "ensaio: restaurando num banco descartável"

    BANCO="mamao_ensaio_$(date +%s)"

    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        createdb -U "$POSTGRES_USER" "$BANCO"

    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        pg_restore -U "$POSTGRES_USER" -d "$BANCO" --no-owner --no-privileges \
        < "$WORK/mamao.dump" 2> "$WORK/erros.txt" || true

    ERROS=$(grep -c "^pg_restore: error" "$WORK/erros.txt" || true)
    info "erros do pg_restore: $ERROS"
    [[ "$ERROS" == "0" ]] || { head -5 "$WORK/erros.txt" >&2; erro "o restore acusou erro."; }

    passo "conferindo o que voltou"

    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        psql -U "$POSTGRES_USER" -d "$BANCO" -tA -c "
            SELECT '  ' || rpad(relname, 22) || to_char(n_live_tup, '999999') || ' linha(s)'
            FROM pg_stat_user_tables
            WHERE schemaname IN ('people','identity','audit') AND n_live_tup > 0
            ORDER BY relname;"

    # As policies vêm no dump. Um restore que as perdesse devolveria o sistema no ar SEM
    # isolamento entre empresas — funcionando e vazando.
    POLICIES=$(docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        psql -U "$POSTGRES_USER" -d "$BANCO" -tA -c "SELECT count(*) FROM pg_policies;" | tr -d '[:space:]')

    info "policies de isolamento: $POLICIES"
    (( POLICIES > 0 )) || erro "o banco restaurado está SEM políticas de RLS."

    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        dropdb -U "$POSTGRES_USER" "$BANCO"

    info "banco de ensaio removido"
else
    passo "PRODUÇÃO: isto apaga o banco atual"
    echo
    echo "  O banco 'mamao' será DESTRUÍDO e substituído pelo backup."
    echo "  Tudo que entrou depois de $(basename "$DUMP") será perdido."
    echo
    read -r -p "  Digite RESTAURAR para confirmar: " confirmacao
    [[ "$confirmacao" == "RESTAURAR" ]] || erro "cancelado."

    info "parando a aplicação (o banco fica de pé)"
    docker compose -f "$DIR/docker-compose.yml" stop api worker

    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        psql -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE IF EXISTS mamao WITH (FORCE);"
    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        createdb -U "$POSTGRES_USER" mamao

    docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
        pg_restore -U "$POSTGRES_USER" -d mamao --no-owner --no-privileges \
        < "$WORK/mamao.dump" 2> "$WORK/erros.txt" || true

    ERROS=$(grep -c "^pg_restore: error" "$WORK/erros.txt" || true)
    info "erros do pg_restore: $ERROS"
    [[ "$ERROS" == "0" ]] || head -5 "$WORK/erros.txt" >&2

    if [[ -n "$UPLOADS" ]]; then
        info "devolvendo os arquivos ao volume"
        docker run --rm -v mamao_uploads:/uploads -v "$WORK:/in" alpine \
            sh -c "rm -rf /uploads/* && tar -xzf /in/uploads.tar.gz -C /uploads"
    fi

    info "subindo a aplicação (o Worker recria o role e reaplica os grants)"
    docker compose -f "$DIR/docker-compose.yml" up -d api worker
fi

# ── 3. o número que importa ───────────────────────────────────────────────────
FIM=$(date +%s)
DURACAO=$(( FIM - INICIO ))

passo "pronto em ${DURACAO}s"
echo
echo "  Anote este número. Ele é o seu tempo de volta (RTO) para um backup deste tamanho."
echo "  O que você perde num desastre (RPO) é o intervalo do cron: até 24 horas."
echo
