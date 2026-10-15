#!/usr/bin/env bash
#
# Backup restore drill (architecture 01-system-architecture.md §10.8; TKT-int-006).
#
# Exercises the documented recovery path end-to-end into a SCRATCH database, so it is
# safe to run on the live VPS: it never touches the production `degerli` database.
#
#   ./restore.sh /opt/degerli/backups/degerli-20261015T030000Z.dump.gpg
#
# Steps: decrypt → create the target DB → pg_restore into it. Point the API at the
# scratch DB (or inspect it with psql) to confirm the data is intact, then drop it.
#
# Target: RTO ≤ 4h / RPO ≤ 24h (architecture §10.8) — see deploy/README.md.
set -euo pipefail
umask 077

: "${BACKUP_PASSPHRASE:?set BACKUP_PASSPHRASE (the key used by pg-backup.sh)}"

archive="${1:?usage: restore.sh <degerli-*.dump.gpg> [target_db]}"
RESTORE_DB="${2:-${RESTORE_DB:-degerli_restore}}"
COMPOSE_FILE="${COMPOSE_FILE:-/opt/degerli/compose.prod.yml}"
COMPOSE_PROJECT_DIR="$(dirname "$COMPOSE_FILE")"

command -v gpg >/dev/null 2>&1 || { echo "restore: gpg not installed" >&2; exit 1; }
[ -f "$archive" ] || { echo "restore: no such archive: $archive" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
dump="$work/restore.dump"

echo "restore: decrypting $(basename "$archive")"
gpg --batch --yes --quiet \
  --passphrase-fd 3 3<<<"$BACKUP_PASSPHRASE" \
  --output "$dump" --decrypt "$archive"

echo "restore: restoring into scratch database '${RESTORE_DB}'"
(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T -e RESTORE_DB="$RESTORE_DB" postgres \
    sh -c '
      set -e
      dropdb --if-exists -U "$POSTGRES_USER" "$RESTORE_DB"
      createdb -U "$POSTGRES_USER" "$RESTORE_DB"
      pg_restore -U "$POSTGRES_USER" -d "$RESTORE_DB" --no-owner
    ' < "$dump"
)

echo "restore: done. Inspect with:"
echo "  docker compose -f $COMPOSE_FILE exec postgres psql -U ${POSTGRES_USER:-degerli} -d $RESTORE_DB -c '\\dt'"
echo "  docker compose -f $COMPOSE_FILE exec postgres dropdb -U ${POSTGRES_USER:-degerli} $RESTORE_DB   # when finished"
