#!/usr/bin/env bash
#
# Nightly encrypted PostgreSQL backup → Backblaze B2 (architecture
# 01-system-architecture.md §10.8; TKT-int-006).
#
# Pipeline:
#   1. `pg_dump -Fc` streamed out of the running `postgres` compose service
#      (custom format = compressed + restorable with pg_restore);
#   2. encrypted at rest with GnuPG symmetric AES-256 (the whole point of the
#      "encrypted" requirement — B2 only ever sees ciphertext);
#   3. uploaded to a Backblaze B2 bucket through rclone;
#   4. 30-day retention enforced on both the remote and the local staging dir.
#
# Intended to run from cron on the VPS. Configuration comes from the environment;
# a quoted env file is sourced by the documented crontab line — see
# deploy/backup/backup.env.example and deploy/README.md ("Backups & restore drill").
#
# Exit code 0 = one archive uploaded; non-zero aborts the run (cron will mail it).
set -euo pipefail
umask 077

: "${BACKUP_PASSPHRASE:?set BACKUP_PASSPHRASE (symmetric backup key)}"
: "${RCLONE_REMOTE:?set RCLONE_REMOTE (e.g. b2:degerli-backups)}"

BACKUP_DIR="${BACKUP_DIR:-/opt/degerli/backups}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
COMPOSE_FILE="${COMPOSE_FILE:-/opt/degerli/compose.prod.yml}"
COMPOSE_PROJECT_DIR="$(dirname "$COMPOSE_FILE")"
RCLONE_CONFIG="${RCLONE_CONFIG:-$HOME/.config/rclone/rclone.conf}"

command -v gpg >/dev/null 2>&1 || { echo "pg-backup: gpg not installed" >&2; exit 1; }
command -v rclone >/dev/null 2>&1 || { echo "pg-backup: rclone not installed" >&2; exit 1; }

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR"
dump="$BACKUP_DIR/degerli-${timestamp}.dump"
archive="${dump}.gpg"

cleanup() { rm -f "$dump"; }
trap cleanup EXIT

echo "pg-backup: dumping database (custom format) at ${timestamp}"
# The container's own POSTGRES_USER / POSTGRES_DB env supply the credentials, so no
# secret has to pass through the host shell. A pg_dump failure aborts under `set -e`.
(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T postgres \
    sh -c 'pg_dump -Fc -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
) > "$dump"

if [ ! -s "$dump" ]; then
  echo "pg-backup: dump is empty — aborting" >&2
  exit 1
fi

echo "pg-backup: encrypting (AES-256)"
# Passphrase via fd 3 (here-string) so it never appears in the process list.
gpg --batch --yes --quiet \
  --symmetric --cipher-algo AES256 --compress-algo none \
  --passphrase-fd 3 3<<<"$BACKUP_PASSPHRASE" \
  --output "$archive" "$dump"
rm -f "$dump"

echo "pg-backup: uploading to ${RCLONE_REMOTE}"
# No progress flag: rclone prints no progress unless -P/--progress is given, and it has no
# `--no-progress` flag at all — a current rclone rejects that as an unknown flag, exits 2
# and never uploads (found by the TKT-foundation-076 restore drill: exit 2 on rclone
# v1.75.2, archive left on the host only). A cron log is not a terminal anyway.
rclone --config "$RCLONE_CONFIG" copy "$archive" "$RCLONE_REMOTE"

echo "pg-backup: enforcing ${BACKUP_RETENTION_DAYS}-day retention"
rclone --config "$RCLONE_CONFIG" delete "$RCLONE_REMOTE" \
  --min-age "${BACKUP_RETENTION_DAYS}d" --include 'degerli-*.dump.gpg'
find "$BACKUP_DIR" -type f -name 'degerli-*.dump.gpg' \
  -mtime +"$BACKUP_RETENTION_DAYS" -delete

echo "pg-backup: done — $(basename "$archive")"
