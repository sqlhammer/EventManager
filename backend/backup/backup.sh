#!/bin/sh
# Daily encrypted PostgreSQL backup with 30-day retention (NFR-3.10, Q2=A).
# Runs in the backup sidecar; writes gzip+encrypted dumps to the mounted /backups volume.
set -eu

STAMP=$(date -u +%Y%m%d%H%M%S)
OUT="/backups/em-${STAMP}.sql.gz.enc"

pg_dump -h db -U "${POSTGRES_USER}" "${POSTGRES_DB}" \
  | gzip \
  | openssl enc -aes-256-cbc -salt -pbkdf2 -pass "pass:${BACKUP_ENCRYPTION_KEY}" \
  > "${OUT}"

echo "backup written: ${OUT}"

# Retention sweep: delete archives older than 30 days.
find /backups -name 'em-*.sql.gz.enc' -type f -mtime +30 -delete
