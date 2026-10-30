#!/usr/bin/env bash
#
# Backup restore drill (architecture 01-system-architecture.md §10.8; TKT-int-006,
# drilled by TKT-foundation-076 — see docs/restore-drill-record.md).
#
# Exercises the documented recovery path end-to-end into a SCRATCH database, so it is
# safe to run on the live VPS: it never touches the production `degerli` database.
#
#   ./restore.sh /opt/degerli/backups/degerli-20261015T030000Z.dump.gpg
#
# Steps: decrypt → create the target DB → pg_restore into it → print what landed
# (size, tables, rows, EF migrations) and the elapsed time. Point the API at the scratch
# DB (or inspect it with psql) to confirm the data is intact, then drop it.
#
# The drill's two numbers, and where they come from:
#   * RTO — the `restore: verified … elapsed …` line below, measured from decrypt to
#     verified restore. Target ≤ 4 h (a wide margin: the 2026-10-30 drill restored a
#     production-shaped 45k-row database in ~2 s).
#   * RPO — the archive's age, printed before the restore starts. Target ≤ 24 h, which is
#     the nightly interval: an older archive means the backup job has been failing.
#
# Every restore failure is loud and leaves nothing pretending to be a restore: the
# scratch database is named on the way out so it can be dropped (it is a scratch DB, not
# production, but a half-restored database that looks restored is exactly the shape the
# honesty rule forbids).
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
scratch_created=0
on_exit() {
  local rc=$?
  rm -rf "$work"
  if [ "$rc" -ne 0 ] && [ "$scratch_created" -eq 1 ]; then
    echo "restore: FAILED — scratch database '${RESTORE_DB}' was created and may be" >&2
    echo "restore: partially restored. It is NOT a valid restore point; drop it before" >&2
    echo "restore: re-running:" >&2
    echo "restore:   docker compose -f $COMPOSE_FILE exec postgres sh -c 'dropdb --if-exists -U \"\$POSTGRES_USER\" \"${RESTORE_DB}\"'" >&2
  fi
  exit "$rc"
}
trap on_exit EXIT
dump="$work/restore.dump"

# The RPO number of this drill: how old the archive is right now. Reported, never gated —
# the age gate lives in the deploy's restore-point marker (deploy/README.md §Deploying).
# A too-old archive is still restorable (which is why this warns instead of refusing),
# but it means the nightly job has been failing: the drill exists to notice that.
#
# The age is taken from the creation time in the archive's own name
# (`degerli-%Y%m%dT%H%M%SZ`, as pg-backup.sh writes it), not from the file's mtime: on the
# box where the backup job wrote it mtime == creation, but a real DR copy of the archive
# (scp / curl / cp onto a rebuilt host) resets mtime to the copy time, which would report a
# week-old archive as "0.0h old" and hide the one thing this warning exists to say.
RPO_MAX_HOURS=24
archive_created=""
archive_stamp="$(basename "$archive" | sed -n 's/^degerli-\([0-9]\{8\}\)T\([0-9]\{6\}\)Z\..*$/\1T\2Z/p')"
if [ -n "$archive_stamp" ]; then
  archive_iso="$(echo "$archive_stamp" | sed 's/^\([0-9]\{4\}\)\([0-9]\{2\}\)\([0-9]\{2\}\)T\([0-9]\{2\}\)\([0-9]\{2\}\)\([0-9]\{2\}\)Z$/\1-\2-\3T\4:\5:\6Z/')"
  archive_created="$(date -u -d "$archive_iso" +%s 2>/dev/null || true)"
fi
if [ -n "$archive_created" ]; then
  archive_age_h="$(awk -v now="$(date +%s)" -v created="$archive_created" \
    'BEGIN { printf "%.1f", (now - created) / 3600 }')"
else
  echo "restore: WARNING — the archive name is not degerli-%Y%m%dT%H%M%SZ.dump.gpg, so its" >&2
  echo "restore: creation time is unknown and the age below is the file's mtime — which a" >&2
  echo "restore: copy (scp/curl/cp) resets to the copy time, understating the real age." >&2
  archive_age_h="$(awk -v now="$(date +%s)" -v mtime="$(stat -c %Y "$archive")" \
    'BEGIN { printf "%.1f", (now - mtime) / 3600 }')"
fi
echo "restore: archive $(basename "$archive") — $(du -h "$archive" | cut -f1), ${archive_age_h}h old (RPO target ≤ ${RPO_MAX_HOURS}h)"
if awk -v age="$archive_age_h" -v max="$RPO_MAX_HOURS" 'BEGIN { exit !(age > max) }'; then
  echo "restore: WARNING — this archive is older than the ${RPO_MAX_HOURS}h RPO, so a restore from" >&2
  echo "restore: it loses more than the RPO allows. The nightly backup job has been failing;" >&2
  echo "restore: fix that before relying on this drill's result as evidence of coverage." >&2
fi

# The target database is dropped and recreated, so aiming it at the live database is a
# data-loss operation, not a drill. The script learns the production database's name from
# the running container (the compose `.env` is not sourced here) and refuses that name
# unless the operator says so in as many words: a real DR recovery is restore → verify →
# promote, and the promote step is deliberate. Without this, a mistyped second argument
# (`restore.sh archive degerli`) silently overwrites production.
prod_db=""
if ! prod_db="$(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T postgres printenv POSTGRES_DB | tr -d '\r'
)" && [ "${ALLOW_LIVE_RESTORE:-}" != "1" ]; then
  echo "restore: FAILED — could not read POSTGRES_DB from the running 'postgres' service" >&2
  echo "restore: (the stack is down, or that service does not set POSTGRES_DB)." >&2
  echo "restore: Check with: docker compose -f $COMPOSE_FILE ps" >&2
  echo "restore: Refusing rather than skipping the live-database guard (it is the check that" >&2
  echo "restore: stops a mistyped target from dropping production). A deliberate live" >&2
  echo "restore: recovery sets ALLOW_LIVE_RESTORE=1." >&2
  exit 1
fi
if [ -z "$prod_db" ] && [ "${ALLOW_LIVE_RESTORE:-}" != "1" ]; then
  echo "restore: FAILED — 'postgres' reports an empty POSTGRES_DB, so this stack's live" >&2
  echo "restore: database name is unknown and the guard below cannot tell it from a scratch" >&2
  echo "restore: name. (compose.prod.yml pins a default, so this means an edited or different" >&2
  echo "restore: compose file.) Set POSTGRES_DB on the service, or set ALLOW_LIVE_RESTORE=1" >&2
  echo "restore: for a deliberate live recovery." >&2
  exit 2
fi
if [ "$RESTORE_DB" = "$prod_db" ] && [ "${ALLOW_LIVE_RESTORE:-}" != "1" ]; then
  echo "restore: refusing to restore into '${RESTORE_DB}' — that is this stack's live database" >&2
  echo "restore: (POSTGRES_DB), and the restore drops and recreates it. The drill restores" >&2
  echo "restore: into a scratch database instead:" >&2
  echo "restore:   $0 $archive" >&2
  echo "restore: A deliberate live recovery (last resort, lossy to the RPO) sets" >&2
  echo "restore: ALLOW_LIVE_RESTORE=1 — stop the api first, and expect everything written" >&2
  echo "restore: after the archive to be gone." >&2
  exit 2
fi

started_at="$(date +%s)"
echo "restore: decrypting"
gpg --batch --yes --quiet \
  --passphrase-fd 3 3<<<"$BACKUP_PASSPHRASE" \
  --output "$dump" --decrypt "$archive"

echo "restore: restoring $(du -h "$dump" | cut -f1) into scratch database '${RESTORE_DB}'"
(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T -e RESTORE_DB="$RESTORE_DB" postgres \
    sh -c '
      set -e
      dropdb --if-exists -U "$POSTGRES_USER" "$RESTORE_DB"
      createdb -U "$POSTGRES_USER" "$RESTORE_DB"
    '
)
# Only now does the scratch database exist, so only now can a failure leave one behind.
scratch_created=1
(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T -e RESTORE_DB="$RESTORE_DB" postgres \
    sh -c '
      # --exit-on-error stops at the first failing object instead of logging every
      # subsequent one too (measured on a duplicate-object restore: 1 error line instead
      # of 182, same exit 1). The exit code alone already fails the run; this just fails
      # it where it broke rather than after writing over a database known to be bad.
      pg_restore -U "$POSTGRES_USER" -d "$RESTORE_DB" --no-owner --exit-on-error
    ' < "$dump"
)

# What actually landed. Row counts are exact (query_to_xml runs a real count(*) per
# public table), not pg_stat estimates, which are stale right after a restore.
# ON_ERROR_STOP matters: without it psql reports success while a per-table count failed,
# the `rows=` line simply goes missing, and the script printed "verified … done" over an
# incomplete check — the same false-success shape the rest of this script removes.
echo "restore: verifying"
verify="$(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T -e RESTORE_DB="$RESTORE_DB" postgres \
    sh -c 'psql -U "$POSTGRES_USER" -d "$RESTORE_DB" -Atq -v ON_ERROR_STOP=1' <<'SQL'
select 'db_size=' || pg_size_pretty(pg_database_size(current_database()));
select 'tables=' || count(*) from pg_tables where schemaname = 'public';
select 'rows=' || coalesce(sum((xpath('/row/c/text()', query_to_xml(format('select count(*) as c from public.%I', tablename), false, true, '')))[1]::text::int), 0)
  from pg_tables where schemaname = 'public';
SQL
)"
# Belt and braces on top of ON_ERROR_STOP: the "verified" line may only be printed if all
# three numbers came back. A missing line means an incomplete verification, which fails.
for key in 'db_size=' 'tables=' 'rows='; do
  case "$verify" in
    *"$key"*) ;;
    *)
      echo "restore: FAILED — verification returned no '$key' line (got: $(echo "$verify" | tr '\n' ' '))" >&2
      echo "restore: the restore is UNVERIFIED, not verified." >&2
      exit 1
      ;;
  esac
done
# A missing <-> absent migration history is reported as 'n/a' honestly; any other failure
# of this query (connection lost, permissions) is loud rather than folded into 'n/a'.
migrations="$(
  cd "$COMPOSE_PROJECT_DIR"
  docker compose -f "$COMPOSE_FILE" exec -T -e RESTORE_DB="$RESTORE_DB" postgres \
    sh -c 'psql -U "$POSTGRES_USER" -d "$RESTORE_DB" -Atq -v ON_ERROR_STOP=1' <<'SQL'
select case when to_regclass('public."__EFMigrationsHistory"') is null then 'n/a'
            else (select count(*)::text from public."__EFMigrationsHistory") end;
SQL
)" || { echo "restore: FAILED — could not read __EFMigrationsHistory from '${RESTORE_DB}'" >&2; exit 1; }
elapsed=$(( $(date +%s) - started_at ))
echo "restore: verified $(echo "$verify" | tr '\n' ' ')ef_migrations=${migrations} — elapsed ${elapsed}s (RTO target ≤ 4h)"
echo "restore: done. Inspect with:"
echo "  docker compose -f $COMPOSE_FILE exec postgres sh -c 'psql -U \"\$POSTGRES_USER\" -d \"${RESTORE_DB}\" -c \"\\dt\"'"
echo "  docker compose -f $COMPOSE_FILE exec postgres sh -c 'dropdb --if-exists -U \"\$POSTGRES_USER\" \"${RESTORE_DB}\"'   # when finished"
