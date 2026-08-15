#!/usr/bin/env bash
# Backup diario: dump do Postgres + uploads, criptografado, enviado para FORA do VPS.
#
# Backup nunca restaurado nao e backup, e esperanca. O ensaio de restore esta em
# deploy/README.md e precisa ser feito UMA VEZ, cronometrado, antes do primeiro cliente.
# Ver docs/arquitetura/infraestrutura-e-deploy.md#backup
#
# cron:  15 3 * * *  /opt/mamao/backup.sh >> /var/log/mamao-backup.log 2>&1

set -Eeuo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Le SO as chaves que este script usa, em vez de dar `source` no arquivo: as connection
# strings tem ponto e virgula, que no bash separa comandos, e um `source` acaba executando
# pedaco de configuracao como se fosse comando.
ler() { sed -n "s/^$1=//p" "$DIR/.env" | head -1; }

POSTGRES_USER="$(ler POSTGRES_USER)"
BACKUP_PASSPHRASE="$(ler BACKUP_PASSPHRASE)"
BACKUP_REMOTE="$(ler BACKUP_REMOTE)"

: "${POSTGRES_USER:?defina POSTGRES_USER em .env}"
: "${BACKUP_PASSPHRASE:?defina BACKUP_PASSPHRASE em .env}"
: "${BACKUP_REMOTE:?defina BACKUP_REMOTE em .env — sem destino fora do VPS nao ha backup}"

echo "[$STAMP] dump do banco"
docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
    pg_dump -U "$POSTGRES_USER" -d mamao --format=custom \
    > "$WORK/mamao-$STAMP.dump"

# CONFERE o que acabou de gerar. Sem isto, um erro dentro do container (banco fora do ar,
# permissao, disco cheio) produz um arquivo vazio ou truncado que sobe todo dia como se
# fosse backup — e so aparece no dia da restauracao, que e o pior dia possivel.
tamanho=$(stat -c%s "$WORK/mamao-$STAMP.dump")
if (( tamanho < 10240 )); then
    echo "[$STAMP] ERRO: dump com $tamanho bytes. Backup abortado." >&2
    exit 1
fi

if ! pg_restore --list "$WORK/mamao-$STAMP.dump" > /dev/null 2>&1; then
    # pg_restore local pode nao existir; nesse caso o container tem.
    if ! docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
            pg_restore --list /dev/stdin < "$WORK/mamao-$STAMP.dump" > /dev/null 2>&1; then
        echo "[$STAMP] ERRO: o dump nao e um arquivo restauravel. Backup abortado." >&2
        exit 1
    fi
fi

echo "[$STAMP] dump ok ($(numfmt --to=iec "$tamanho" 2>/dev/null || echo "$tamanho bytes"))"

# Os DOCUMENTOS dos funcionarios moram aqui dentro (uploads/documentos), e nao no banco:
# blob em Postgres incha o dump. A consequencia esta neste script — restaurar so o dump
# devolve a lista de documentos com os arquivos faltando. Os dois arquivos deste backup sao
# um par; restaurar um sem o outro deixa o sistema meio certo, que e pior que fora do ar.
echo "[$STAMP] arquivos enviados pelos clientes (inclui os documentos)"
docker run --rm -v mamao_uploads:/uploads:ro -v "$WORK:/out" alpine \
    tar -czf "/out/uploads-$STAMP.tar.gz" -C /uploads .

echo "[$STAMP] criptografando"
for arquivo in "$WORK"/*; do
    gpg --batch --yes --symmetric --cipher-algo AES256 \
        --passphrase "$BACKUP_PASSPHRASE" "$arquivo"
    rm -f "$arquivo"
done

echo "[$STAMP] enviando para fora do VPS"
rclone copy "$WORK" "$BACKUP_REMOTE/$(date -u +%Y/%m)" --no-traverse

# ── retencao ──────────────────────────────────────────────────────────────────
# 7 diarios, 5 semanais (domingo), 6 mensais (dia 1).
#
# A versao anterior dizia isso no comentario e apagava TUDO com mais de 7 dias — o que
# significa que, no dia em que alguem pedisse "como estava em marco", a resposta seria
# "nao existe". Comentario que descreve uma politica que o codigo nao implementa e pior
# que nenhum comentario, porque cria confianca falsa.
manter() {
    local nome="$1" dia
    dia="${nome##*-}"; dia="${dia:0:8}"          # mamao-20260814T031500Z... -> 20260814
    [[ "$dia" =~ ^[0-9]{8}$ ]] || return 0        # nome fora do padrao: nao apaga

    local idade=$(( ( $(date -u +%s) - $(date -u -d "${dia:0:4}-${dia:4:2}-${dia:6:2}" +%s) ) / 86400 ))

    (( idade <= 7 )) && return 0                                  # diario
    [[ "${dia:6:2}" == "01" && idade -le 190 ]] && return 0        # mensal
    [[ "$(date -u -d "${dia:0:4}-${dia:4:2}-${dia:6:2}" +%u)" == "7" && idade -le 35 ]] && return 0  # semanal

    return 1
}

echo "[$STAMP] aplicando retencao"
apagados=0
while read -r caminho; do
    [[ -n "$caminho" ]] || continue
    manter "$(basename "$caminho")" || { rclone deletefile "$BACKUP_REMOTE/$caminho" && apagados=$((apagados + 1)); }
done < <(rclone lsf -R --files-only "$BACKUP_REMOTE" 2>/dev/null || true)

echo "[$STAMP] concluido — $apagados arquivo(s) fora da retencao removido(s)"
