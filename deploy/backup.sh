#!/usr/bin/env bash
# Backup diario: dump do Postgres + uploads, criptografado, enviado para FORA do VPS.
#
# Backup nunca testado nao e backup, e esperanca. Agende o restore de verdade
# (deploy/restore.md) antes do primeiro cliente pagante.
# Ver docs/arquitetura/infraestrutura-e-deploy.md#backup
#
# cron:  15 3 * * *  /opt/mamao/deploy/backup.sh >> /var/log/mamao-backup.log 2>&1

set -Eeuo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# shellcheck source=/dev/null
source "$DIR/.env"

: "${POSTGRES_USER:?}" "${BACKUP_PASSPHRASE:?}" "${BACKUP_REMOTE:?}"

echo "[$STAMP] dump do banco"
docker compose -f "$DIR/docker-compose.yml" exec -T postgres \
    pg_dump -U "$POSTGRES_USER" -d mamao --format=custom \
    > "$WORK/mamao-$STAMP.dump"

echo "[$STAMP] arquivos enviados pelos clientes"
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

# Retencao: 7 diarios, 4 semanais, 6 mensais. Ajuste conforme o custo do destino.
rclone delete "$BACKUP_REMOTE" --min-age 7d --include "*T*Z.dump.gpg" || true

echo "[$STAMP] concluido"
