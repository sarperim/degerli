#!/usr/bin/env bash
#
# One-command on-box status report (SLICE-013; deploy/README.md §Ops commands).
#
# The daily operational picture on the VPS used to be a handful of memorized commands —
# `docker compose -f compose.prod.yml ps`, `docker stats`, `docker compose logs`, and a
# hand-written `ls -l backups/` to guess whether the nightly backup is fresh. This script
# assembles all of it in one shot so the answer to "is the box healthy?" is one command
# and one scrollback, not five commands and an interpretation.
#
# It is a VIEWER, not an operator: nothing here mutates state — no up/down/restart, no
# writes outside stdout. The one write-shaped command in the runbook (`docker compose
# config`) is deliberately never used, because plain `config` interpolates and dumps every
# secret in `.env` (deploy/README.md §Deploying); only `config -q` would be safe and this
# script does not need it.
#
# Sections, each degrading honestly on its own (a missing piece is reported in place, never
# hidden, never fatal to the rest of the report):
#   1. compose state     — `docker compose ps` for the stack
#   2. health            — `GET /health` through Caddy on localhost, bounded curl
#   3. resource usage    — `docker stats --no-stream` (this project's containers),
#                          `docker system df`, and the postgres-data volume size
#   4. backup freshness  — newest backups/degerli-*.dump.gpg age vs the 24 h RPO
#   5. logs (opt-in)     — `--logs [N]` (last N lines per service, default 50) and
#                          `--errors` (last 24 h grepped for error|fail|exception)
#
# Exit code 0 = a completed report — a status report is not a gate, so a degraded box still
# exits 0 and says what is wrong in the output. Non-zero only for a usage error (2).
#
#   ./status.sh                  # the report
#   ./status.sh --logs           # + last 50 lines per service
#   ./status.sh --logs 200 --errors
#   COMPOSE_FILE=compose.ci.yml ./status.sh    # usable against the local CI stack
#
# Tunables (environment): COMPOSE_FILE (default compose.prod.yml, resolved against the repo
# root), HEALTH_URL (override the probe target), BACKUP_DIR (default <repo>/backups).
set -euo pipefail

# Repository root resolved relative to this script, the same way deploy/backup/pg-backup.sh
# and deploy/backup/restore.sh locate their compose file — so the script runs from any cwd
# and from a checkout that is not /opt/degerli.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

COMPOSE_FILE="${COMPOSE_FILE:-compose.prod.yml}"
case "$COMPOSE_FILE" in
  /*) ;;
  *) COMPOSE_FILE="${REPO_ROOT}/${COMPOSE_FILE}" ;;
esac
COMPOSE_PROJECT_DIR="$(dirname "$COMPOSE_FILE")"

BACKUP_DIR="${BACKUP_DIR:-${REPO_ROOT}/backups}"
RPO_MAX_HOURS=24

show_logs=0
logs_lines=50
show_errors=0

usage() {
  cat <<'EOF'
Usage: status.sh [--logs [N]] [--errors] [--help]

Read-only operational status report for the Değerli VPS stack.

  --logs [N]   also print the last N log lines per service (default 50 when N is omitted)
  --errors     also grep the last 24h of logs for error|fail|exception (case-insensitive)
  -h, --help   show this text and exit 0

Environment:
  COMPOSE_FILE  compose file to inspect (default: compose.prod.yml, relative to the repo root)
  HEALTH_URL    override the /health probe target
                (default: https://localhost/health when the stack has a caddy service,
                 else the api service's published host port)
  BACKUP_DIR    where to look for degerli-*.dump.gpg (default: <repo>/backups)

Exit status: 0 for a completed report (including a degraded one); 2 for a usage error.
EOF
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --logs)
      show_logs=1
      # `--logs 200` takes the value; a bare `--logs` means the default. Anything that is
      # not a plain number is left for the positional check below to reject loudly rather
      # than being silently swallowed as a line count.
      if [ "$#" -ge 2 ] && [[ "$2" =~ ^[0-9]+$ ]]; then
        logs_lines="$2"
        shift
      fi
      ;;
    --errors)
      show_errors=1
      ;;
    *)
      echo "status: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
  shift
done

section() {
  echo
  echo "== $1 =="
}

docker_ok=0
if ! command -v docker >/dev/null 2>&1; then
  echo "status: docker not installed — reporting everything else, container sections skipped"
else
  # `docker info` is the cheapest honest liveness probe for the daemon; every container
  # section below is gated on it so one "daemon unreachable" line replaces five failures.
  if docker info >/dev/null 2>&1; then
    docker_ok=1
  else
    echo "status: docker daemon not reachable — container sections skipped"
  fi
fi

compose() {
  ( cd "$COMPOSE_PROJECT_DIR" && docker compose -f "$COMPOSE_FILE" "$@" )
}

echo "status: $(date -u +%Y-%m-%dT%H:%M:%SZ)  repo=${REPO_ROOT}"
echo "status: compose file: ${COMPOSE_FILE}"

# --- 1. compose state --------------------------------------------------------------------
section "compose ps"
if [ "$docker_ok" -ne 1 ]; then
  echo "  skipped (docker unavailable)"
elif ! compose ps; then
  echo "  'docker compose ps' failed — the compose file may be unreadable or invalid for"
  echo "  this checkout; check it with: docker compose -f ${COMPOSE_FILE} config -q"
fi

# Container IDs of this compose project only — the scope for `docker stats` below. Empty is
# a legitimate answer (stack down), and every consumer treats it as "nothing running".
container_ids=""
if [ "$docker_ok" -eq 1 ]; then
  container_ids="$(compose ps -q 2>/dev/null || true)"
fi

# --- 2. health ---------------------------------------------------------------------------
section "health"
if ! command -v curl >/dev/null 2>&1; then
  echo "  skipped (curl not installed)"
else
  health_url="${HEALTH_URL:-}"
  if [ -z "$health_url" ]; then
    # Production routes /health through Caddy (the single public entrypoint). A stack
    # without a caddy service (compose.ci.yml) publishes the api's port directly instead —
    # the same probe, against the same /health, just not through the reverse proxy. The
    # choice is made from the compose file being inspected, so `COMPOSE_FILE=… ./status.sh`
    # reports on the stack it was pointed at rather than on production's shape.
    if grep -qE '^[[:space:]]+caddy:[[:space:]]*$' "$COMPOSE_FILE" 2>/dev/null; then
      health_url="https://localhost/health"
    elif [ "$docker_ok" -eq 1 ]; then
      api_addr="$(compose port api 8080 2>/dev/null | head -n1 || true)"
      if [ -n "$api_addr" ]; then
        health_url="http://${api_addr/0.0.0.0/127.0.0.1}/health"
      fi
    fi
  fi

  if [ -z "$health_url" ]; then
    echo "  no health endpoint could be derived from ${COMPOSE_FILE} (no caddy service and"
    echo "  no published api port). Set HEALTH_URL explicitly to probe one."
  else
    echo "  GET ${health_url}"
    # Bounded exactly like the deploy's smoke-check (30 × 5 s, each curl capped at 5 s):
    # a status report must never hang on a wedged proxy. -s + -o /dev/null keeps the
    # response body out of the output; -w carries the two numbers that matter.
    probe="$(curl -s -o /dev/null --max-time 5 --connect-timeout 3 \
      -w '%{http_code} %{time_total}' "$health_url" 2>/dev/null)" && probe_rc=0 || probe_rc=$?
    if [ "$probe_rc" -ne 0 ]; then
      case "$probe_rc" in
        28) echo "  DOWN — timed out after 5s (curl exit 28)" ;;
        7)  echo "  DOWN — connection refused (curl exit 7); Caddy is not listening" ;;
        6)  echo "  DOWN — host/name resolution failed (curl exit 6)" ;;
        60) echo "  DOWN — TLS certificate not verifiable (curl exit 60)" ;;
        *)  echo "  DOWN — curl failed with exit ${probe_rc}" ;;
      esac
      echo "  outside-in check: the UptimeRobot monitor (deploy/README.md §Monitoring) says"
      echo "  whether the public URL fails the same way."
    else
      http_code="${probe%% *}"
      latency="${probe##* }"
      if [ "$http_code" = "200" ]; then
        echo "  OK — HTTP ${http_code} in ${latency}s"
      else
        echo "  UNHEALTHY — HTTP ${http_code} in ${latency}s (expected 200 from /health)"
        echo "  /health is the liveness *and* database probe; a non-200 usually means the"
        echo "  api cannot reach postgres. See: docker compose -f ${COMPOSE_FILE} ps"
      fi
    fi
  fi
fi

# --- 3. resource usage -------------------------------------------------------------------
section "resources"
if [ "$docker_ok" -ne 1 ]; then
  echo "  skipped (docker unavailable)"
else
  if [ -z "$container_ids" ]; then
    echo "  no running containers for this compose project — nothing to sample"
  else
    # shellcheck disable=SC2086  # deliberate word splitting of the ID list
    if ! docker stats --no-stream \
        --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.NetIO}}\t{{.BlockIO}}' \
        ${container_ids}; then
      echo "  'docker stats' failed"
    fi
  fi

  echo
  echo "  docker system df:"
  if docker system df 2>/dev/null | sed 's/^/    /'; then :; else echo "    unavailable"; fi

  # The postgres-data volume size, read from `docker system df -v` rather than by mounting
  # the volume into a throwaway container: the -v table is already computed by the daemon,
  # needs no image pull (the box may have no spare image) and writes nothing. The volume is
  # named `<compose project>_postgres-data`; the project name is taken from the compose
  # file's own `name:` line, the same line the deploy's bootstrap detection trusts.
  project_name="$(sed -n 's/^name:[[:space:]]*\([^[:space:]]*\).*$/\1/p' "$COMPOSE_FILE" 2>/dev/null | head -n1)"
  if [ -z "$project_name" ]; then
    project_name="$(basename "$COMPOSE_PROJECT_DIR" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9_-')"
  fi
  vol="${project_name}_postgres-data"
  vol_size="$(docker system df -v 2>/dev/null \
    | awk -v want="$vol" '$1 == want { print $3; exit }' || true)"
  if [ -n "$vol_size" ]; then
    echo
    echo "  volume ${vol}: ${vol_size}"
  else
    echo
    echo "  volume ${vol}: not found (the project has no database volume on this host)"
  fi
fi

# --- 4. backup freshness vs RPO ----------------------------------------------------------
section "backup"
# Newest archive chosen by NAME, not by mtime. The names are `degerli-%Y%m%dT%H%M%SZ.dump.gpg`
# (pg-backup.sh writes them UTC), so a reverse name sort is chronological — and unlike
# `ls -t` it is not fooled by a copy (scp/curl/cp onto a rebuilt host) that resets mtime to
# the copy time. The age itself is then computed from the timestamp inside the name, the
# same convention deploy/backup/restore.sh uses, for the same reason.
newest=""
if [ ! -d "$BACKUP_DIR" ]; then
  echo "  no backup directory at ${BACKUP_DIR} — RPO status unknown"
  echo "  the nightly job (deploy/backup/pg-backup.sh) writes here; until it runs once, the"
  echo "  deploy will refuse at the backup-marker phase (deploy/README.md §Backups)"
else
  archives="$(ls -1 "$BACKUP_DIR"/degerli-*.dump.gpg 2>/dev/null | sort -r || true)"
  newest="$(printf '%s\n' "$archives" | sed -n '1p')"
fi

if [ -n "$newest" ]; then
  file_size="$(du -h "$newest" 2>/dev/null | cut -f1 || true)"
  created=""
  stamp="$(basename "$newest" | sed -n 's/^degerli-\([0-9]\{8\}\)T\([0-9]\{6\}\)Z\..*$/\1T\2Z/p')"
  if [ -n "$stamp" ]; then
    iso="$(echo "$stamp" | sed 's/^\([0-9]\{4\}\)\([0-9]\{2\}\)\([0-9]\{2\}\)T\([0-9]\{2\}\)\([0-9]\{2\}\)\([0-9]\{2\}\)Z$/\1-\2-\3T\4:\5:\6Z/')"
    created="$(date -u -d "$iso" +%s 2>/dev/null || true)"
  fi
  echo "  newest: $(basename "$newest") — ${file_size:-?}"
  if [ -z "$created" ]; then
    echo "  UNKNOWN — the archive name is not degerli-%Y%m%dT%H%M%SZ.dump.gpg, so its creation"
    echo "  time cannot be read from the name. mtime is deliberately NOT used here: a copied"
    echo "  archive would then read as fresh and hide exactly what this check exists to say."
  else
    age_h="$(awk -v now="$(date +%s)" -v created="$created" \
      'BEGIN { printf "%.1f", (now - created) / 3600 }')"
    echo "  age: ${age_h}h (RPO target ≤ ${RPO_MAX_HOURS}h)"
    if awk -v age="$age_h" -v max="$RPO_MAX_HOURS" 'BEGIN { exit !(age > max) }'; then
      echo "  WARNING: older than the 24h RPO — the next deploy will refuse at the"
      echo "  backup-marker phase (deploy/README.md §The pre-deploy restore-point marker)."
      echo "  Remedy: set -a; . /etc/degerli/backup.env; set +a; ${REPO_ROOT}/deploy/backup/pg-backup.sh"
      echo "  Until a fresh archive exists, a forward-only migration has no honest undo."
    else
      echo "  OK — inside the 24h RPO"
    fi
  fi
elif [ -d "$BACKUP_DIR" ]; then
  echo "  no backup archive found in ${BACKUP_DIR} — RPO status unknown"
  echo "  the deploy will refuse at the backup-marker phase until one exists; take one with:"
  echo "    set -a; . /etc/degerli/backup.env; set +a; ${REPO_ROOT}/deploy/backup/pg-backup.sh"
fi

# --- 5. logs (opt-in) --------------------------------------------------------------------
if [ "$show_logs" -eq 1 ]; then
  section "logs (last ${logs_lines} lines per service)"
  if [ "$docker_ok" -ne 1 ]; then
    echo "  skipped (docker unavailable)"
  else
    # `config --services` prints service NAMES only — it is not plain `config`, which
    # interpolates and dumps every secret (deploy/README.md §Deploying). Service names are
    # also read from `ps --services` when the config cannot be resolved at all.
    services="$(compose config --services 2>/dev/null || true)"
    if [ -z "$services" ]; then
      # Without the VPS `.env`, interpolation of a required variable fails and `config`
      # prints nothing — fall back to the services actually running in the project.
      services="$(compose ps --services 2>/dev/null || true)"
    fi
    if [ -z "$services" ]; then
      echo "  no services resolved from ${COMPOSE_FILE}"
    else
      while IFS= read -r service; do
        [ -n "$service" ] || continue
        echo
        echo "  --- ${service} ---"
        # Per-service so one service with no logs does not blank the whole tail, and an
        # empty tail is reported as such — a bare heading with nothing under it reads like
        # a swallowed error rather than "this container is quiet".
        service_logs="$(compose logs --no-color --tail "$logs_lines" "$service" 2>&1 || true)"
        if [ -z "$service_logs" ]; then
          echo "  (no log output in the tail window)"
        else
          printf '%s\n' "$service_logs" | sed 's/^/  /'
        fi
      done <<< "$services"
    fi
  fi
fi

if [ "$show_errors" -eq 1 ]; then
  section "errors (last 24h, <=2000 lines scanned)"
  if [ "$docker_ok" -ne 1 ]; then
    echo "  skipped (docker unavailable)"
  else
    # Bounded twice: `--since 24h --tail 2000` at the source (a log flood must not turn
    # this into a pipeline that reads for a minute) and `tail -n 200` on the matches, so a
    # noisy service cannot bury everything else. Case-insensitive, deduplicated-by-eye only
    # — this is a pointer into the logs, not a log parser.
    matches="$(compose logs --no-color --since 24h --tail 2000 2>&1 \
      | grep -iE 'error|fail|exception' | tail -n 200 || true)"
    if [ -z "$matches" ]; then
      echo "  OK — no error|fail|exception lines in the last 24h of logs"
    else
      printf '%s\n' "$matches" | sed 's/^/  /'
      echo
      echo "  (last 200 matches of at most 2000 lines per the log driver's buffer; for more:"
      echo "   docker compose -f ${COMPOSE_FILE} logs --since 24h <service>)"
    fi
  fi
fi

echo
echo "status: report complete (exit 0 — a status report is not a gate)"
exit 0
