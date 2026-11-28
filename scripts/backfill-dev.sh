#!/usr/bin/env bash
#
# Backfill local development data against a running Değerli API (`compose.dev.yml`).
#
# A fresh dev database starts with only seed rows (metric catalog, macro series, and
# the `builder` account). This script authenticates as the builder account and invokes
# `POST /api/v1/admin/ingest/{job}/run` in dependency order to populate multi-year
# prices, macro series, KAP fundamentals, and derived metrics/snapshots.
#
# Usage:
#   scripts/backfill-dev.sh [options]
#
# Options:
#   --years N              Backfill window in years from today (default: 3)
#   --from YYYY-MM-DD      Explicit start date (overrides --years)
#   --fast                 Run Phase 1 only (skip Phase 2 KAP jobs)
#   --statements-passes N  Bounded `statements` passes in Phase 2 (default: 1; 0 skips)
#   --resume-statements    Omit `backfillFrom` on the first `statements` pass
#   --only JOB1,JOB2,...   Run only the specified comma-separated job codes in order
#   --continue-on-error    Continue on job failure and exit non-zero at the end
#   --dry-run              Print resolved plan and payloads without network calls
#   -h, --help             Show this help message and exit
#
# Options (environment):
#   API_BASE               Target API base URL (default: http://localhost:8080)
#   BUILDER_EMAIL          Builder account email (default: builder@degerli.local)
#   BUILDER_PASSWORD       Builder account password (default: Degerli-Builder-2026)
#   CURL_MAX_TIME          Per-request curl timeout in seconds (default: 5700)
#
# Phases:
#   Phase 1 (--fast):
#     universe-sync -> prices (with backfillFrom) -> macro-daily -> macro-cpi ->
#     intraday-quotes -> metrics-recompute -> dcf-baselines -> market-snapshot -> medians
#   Phase 2 (full mode):
#     kap-public-identity -> shares -> dividends -> corporate-actions -> disclosures ->
#     statements (pass 1 with backfillFrom unless --resume-statements, passes 2..N {}) ->
#     metrics-recompute -> dcf-baselines -> market-snapshot -> medians
#
# Examples:
#   scripts/backfill-dev.sh --fast
#   scripts/backfill-dev.sh --years 5 --statements-passes 2
#   scripts/backfill-dev.sh --from 2023-01-01 --resume-statements
#   scripts/backfill-dev.sh --only prices,market-snapshot --dry-run
set -euo pipefail

API_BASE="${API_BASE:-http://localhost:8080}"
API_BASE="${API_BASE%/}"
BUILDER_EMAIL="${BUILDER_EMAIL:-builder@degerli.local}"
BUILDER_PASSWORD="${BUILDER_PASSWORD:-Degerli-Builder-2026}"
CURL_MAX_TIME="${CURL_MAX_TIME:-5700}"

years="3"
from_date=""
fast_mode=0
statements_passes="1"
resume_statements=0
only_jobs=""
continue_on_error=0
dry_run=0

usage() {
  cat <<'EOF'
Usage: scripts/backfill-dev.sh [options]

Backfill multi-year market, macro, and KAP data via POST /api/v1/admin/ingest/{job}/run.

Options:
  --years N              Backfill window in years from today (default: 3)
  --from YYYY-MM-DD      Explicit start date (overrides --years)
  --fast                 Run Phase 1 only (skip Phase 2 KAP jobs)
  --statements-passes N  Number of bounded statements passes in Phase 2 (default: 1; 0 skips)
  --resume-statements    Do not send backfillFrom on the first statements pass
  --only JOB1,JOB2,...   Run only the specified comma-separated job codes in order
  --continue-on-error    Continue remaining jobs if a job fails (exits non-zero at end)
  --dry-run              Print resolved start date, API base, and planned calls without network I/O
  -h, --help             Show this help message and exit

Environment variables:
  API_BASE               Target API base URL (default: http://localhost:8080)
  BUILDER_EMAIL          Builder login email (default: builder@degerli.local)
  BUILDER_PASSWORD       Builder login password (default: Degerli-Builder-2026)
  CURL_MAX_TIME          Max seconds per HTTP request (default: 5700)

Examples:
  scripts/backfill-dev.sh --fast
  scripts/backfill-dev.sh --years 3 --statements-passes 2
  scripts/backfill-dev.sh --from 2023-01-01 --statements-passes 3 --dry-run
  scripts/backfill-dev.sh --only prices,market-snapshot
EOF
}

die() {
  echo "backfill-dev: $*" >&2
  exit 2
}

require_value() {
  local flag="$1"
  local remaining="$2"
  if ((remaining < 2)); then
    die "missing value for ${flag}"
  fi
}

while (($# > 0)); do
  case "$1" in
    --years)
      require_value "$1" "$#"
      years="$2"
      shift 2
      ;;
    --years=*)
      years="${1#*=}"
      shift
      ;;
    --from)
      require_value "$1" "$#"
      from_date="$2"
      shift 2
      ;;
    --from=*)
      from_date="${1#*=}"
      shift
      ;;
    --fast)
      fast_mode=1
      shift
      ;;
    --statements-passes)
      require_value "$1" "$#"
      statements_passes="$2"
      shift 2
      ;;
    --statements-passes=*)
      statements_passes="${1#*=}"
      shift
      ;;
    --resume-statements)
      resume_statements=1
      shift
      ;;
    --only)
      require_value "$1" "$#"
      only_jobs="$2"
      shift 2
      ;;
    --only=*)
      only_jobs="${1#*=}"
      shift
      ;;
    --continue-on-error)
      continue_on_error=1
      shift
      ;;
    --dry-run)
      dry_run=1
      shift
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      die "unknown argument '$1' (see --help)"
      ;;
  esac
done

command -v python3 >/dev/null 2>&1 || die "python3 is required but not found in PATH"
if ((dry_run == 0)); then
  command -v curl >/dev/null 2>&1 || die "curl is required but not found in PATH"
fi

if [[ ! "$CURL_MAX_TIME" =~ ^[1-9][0-9]*$ ]]; then
  die "CURL_MAX_TIME must be a positive integer (got '${CURL_MAX_TIME}')"
fi

if [[ ! "$years" =~ ^[1-9][0-9]*$ ]]; then
  die "--years must be a positive integer (got '${years}')"
fi

if [[ ! "$statements_passes" =~ ^[0-9]+$ ]]; then
  die "--statements-passes must be a non-negative integer (got '${statements_passes}')"
fi

if [[ -n "$from_date" ]]; then
  if ! resolved_from="$(python3 - "$from_date" <<'PY'
import datetime
import re
import sys

raw = sys.argv[1]
if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", raw):
    sys.exit(1)
try:
    parsed = datetime.date.fromisoformat(raw)
except ValueError:
    sys.exit(1)
print(parsed.isoformat())
PY
  )"; then
    die "--from must be a valid YYYY-MM-DD date (got '${from_date}')"
  fi
else
  resolved_from="$(python3 - "$years" <<'PY'
import datetime
import sys

years = int(sys.argv[1])
today = datetime.date.today()
target_year = today.year - years
try:
    result = today.replace(year=target_year)
except ValueError:
    # Handle leap day (Feb 29 -> Feb 28) on non-leap target years.
    result = today.replace(year=target_year, day=28)
print(result.isoformat())
PY
  )"
fi

# Build ordered execution plan as parallel arrays of job codes and JSON payloads.
declare -a plan_jobs=()
declare -a plan_payloads=()

add_step() {
  plan_jobs+=("$1")
  plan_payloads+=("$2")
}

from_payload="{\"backfillFrom\":\"${resolved_from}\"}"

if [[ -n "$only_jobs" ]]; then
  IFS=',' read -r -a requested_jobs <<<"$only_jobs"
  statements_seen=0
  for raw_job in "${requested_jobs[@]}"; do
    job="${raw_job#"${raw_job%%[![:space:]]*}"}"
    job="${job%"${job##*[![:space:]]}"}"
    if [[ -z "$job" ]]; then
      die "--only contains an empty job code ('${only_jobs}')"
    fi
    if [[ "$job" == "prices" ]]; then
      add_step "$job" "$from_payload"
    elif [[ "$job" == "statements" ]]; then
      if ((statements_seen == 0 && resume_statements == 0)); then
        add_step "$job" "$from_payload"
      else
        add_step "$job" "{}"
      fi
      statements_seen=$((statements_seen + 1))
    else
      add_step "$job" "{}"
    fi
  done
  if ((${#plan_jobs[@]} == 0)); then
    die "--only must specify at least one job code"
  fi
else
  # Phase 1: universe, prices, macro, intraday quotes, and derived snapshots/medians.
  add_step "universe-sync" "{}"
  add_step "prices" "$from_payload"
  add_step "macro-daily" "{}"
  add_step "macro-cpi" "{}"
  add_step "intraday-quotes" "{}"
  add_step "metrics-recompute" "{}"
  add_step "dcf-baselines" "{}"
  add_step "market-snapshot" "{}"
  add_step "medians" "{}"

  # Phase 2: KAP public identity, actions, disclosures, bounded statements, and recompute.
  if ((fast_mode == 0)); then
    add_step "kap-public-identity" "{}"
    add_step "shares" "{}"
    add_step "dividends" "{}"
    add_step "corporate-actions" "{}"
    add_step "disclosures" "{}"
    for ((pass = 1; pass <= statements_passes; pass++)); do
      if ((pass == 1 && resume_statements == 0)); then
        add_step "statements" "$from_payload"
      else
        add_step "statements" "{}"
      fi
    done
    add_step "metrics-recompute" "{}"
    add_step "dcf-baselines" "{}"
    add_step "market-snapshot" "{}"
    add_step "medians" "{}"
  fi
fi

if ((dry_run == 1)); then
  echo "Dry run — no network calls will be made."
  echo "Target API base : ${API_BASE}"
  echo "Backfill from   : ${resolved_from}"
  echo "Planned steps   : ${#plan_jobs[@]}"
  echo ""
  for idx in "${!plan_jobs[@]}"; do
    step_num=$((idx + 1))
    printf "%2d. POST %s/api/v1/admin/ingest/%s/run  %s\n" \
      "$step_num" "$API_BASE" "${plan_jobs[$idx]}" "${plan_payloads[$idx]}"
  done
  exit 0
fi

workdir="$(mktemp -d "${TMPDIR:-/tmp}/degerli-backfill.XXXXXX")"
cookie_jar="$(mktemp "${workdir}/cookies.XXXXXX")"
resp_file="${workdir}/response.json"
summary_printed=0

cleanup() {
  rm -f "$cookie_jar" "$resp_file"
  rm -rf "$workdir"
}
trap cleanup EXIT

timestamp_utc() {
  date -u "+%Y-%m-%dT%H:%M:%SZ"
}

fetch_csrf_token() {
  local http_code
  http_code="$(curl -sS -o "$resp_file" -w "%{http_code}" \
    --max-time 30 \
    -b "$cookie_jar" -c "$cookie_jar" \
    "${API_BASE}/api/v1/auth/csrf-token")" || {
    echo "backfill-dev: failed to reach ${API_BASE}/api/v1/auth/csrf-token" >&2
    return 1
  }

  if [[ ! "$http_code" =~ ^2[0-9]{2}$ ]]; then
    echo "backfill-dev: GET /api/v1/auth/csrf-token returned HTTP ${http_code}" >&2
    cat "$resp_file" >&2 || true
    return 1
  fi

  python3 - "$resp_file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as fh:
    data = json.load(fh)
token = data.get("token")
if not token:
    sys.exit(1)
print(token)
PY
}

# 1. Pre-flight API health check.
echo "[$(timestamp_utc)] Checking API health at ${API_BASE}/health..."
health_code="$(curl -sS -o "$resp_file" -w "%{http_code}" --max-time 15 "${API_BASE}/health" || echo "000")"
if [[ "$health_code" != "200" ]]; then
  echo "backfill-dev: API is not reachable or unhealthy at ${API_BASE}/health (HTTP ${health_code})." >&2
  echo "  Ensure the dev stack is running (e.g. docker compose -f compose.dev.yml up -d)." >&2
  exit 1
fi
echo "[$(timestamp_utc)] API healthy (HTTP 200). Backfill start date: ${resolved_from}"

# 2. Authenticate with CSRF protection.
echo "[$(timestamp_utc)] Fetching initial CSRF token..."
csrf_token="$(fetch_csrf_token)" || {
  echo "backfill-dev: could not parse CSRF token from ${API_BASE}/api/v1/auth/csrf-token" >&2
  exit 1
}

echo "[$(timestamp_utc)] Signing in as ${BUILDER_EMAIL}..."
login_code="$(
  BUILDER_EMAIL="$BUILDER_EMAIL" BUILDER_PASSWORD="$BUILDER_PASSWORD" python3 - <<'PY' |
import json
import os
import sys

sys.stdout.write(json.dumps({
    "email": os.environ["BUILDER_EMAIL"],
    "password": os.environ["BUILDER_PASSWORD"],
}))
PY
  curl -sS -o "$resp_file" -w "%{http_code}" \
    --max-time 30 \
    -b "$cookie_jar" -c "$cookie_jar" \
    -H "Content-Type: application/json" \
    -H "X-CSRF-Token: ${csrf_token}" \
    --data-binary @- \
    "${API_BASE}/api/v1/auth/login" || echo "000"
)"

if [[ ! "$login_code" =~ ^2[0-9]{2}$ ]]; then
  echo "backfill-dev: login failed for ${BUILDER_EMAIL} (HTTP ${login_code})." >&2
  cat "$resp_file" >&2 || true
  exit 1
fi

# Re-fetch CSRF token post-login since authentication rotates the antiforgery session.
csrf_token="$(fetch_csrf_token)" || {
  echo "backfill-dev: failed to refresh post-login CSRF token" >&2
  exit 1
}
echo "[$(timestamp_utc)] Authenticated. Executing ${#plan_jobs[@]} ingestion step(s)..."

# Parallel summary record arrays.
declare -a sum_jobs=()
declare -a sum_statuses=()
declare -a sum_written=()
declare -a sum_unchanged=()
declare -a sum_quarantined=()
declare -a sum_elapsed=()

print_summary() {
  if ((summary_printed == 1)); then
    return
  fi
  summary_printed=1
  echo ""
  echo "=== Backfill Summary (from ${resolved_from}, target ${API_BASE}) ==="
  printf "%-24s %-12s %8s %9s %11s %8s\n" "JOB" "STATUS" "WRITTEN" "UNCHANGED" "QUARANTINED" "ELAPSED"
  printf "%-24s %-12s %8s %9s %11s %8s\n" "------------------------" "------------" "--------" "---------" "-----------" "--------"
  for i in "${!sum_jobs[@]}"; do
    printf "%-24s %-12s %8s %9s %11s %7ss\n" \
      "${sum_jobs[$i]}" \
      "${sum_statuses[$i]}" \
      "${sum_written[$i]}" \
      "${sum_unchanged[$i]}" \
      "${sum_quarantined[$i]}" \
      "${sum_elapsed[$i]}"
  done
}

any_failed=0

for idx in "${!plan_jobs[@]}"; do
  job="${plan_jobs[$idx]}"
  payload="${plan_payloads[$idx]}"
  step_num=$((idx + 1))

  # Refresh CSRF token before each job so long-running steps never fail on a stale token.
  if ! csrf_token="$(fetch_csrf_token)"; then
    echo "[$(timestamp_utc)] [${step_num}/${#plan_jobs[@]}] ${job}: failed to refresh CSRF token" >&2
    sum_jobs+=("$job")
    sum_statuses+=("csrf-error")
    sum_written+=("-")
    sum_unchanged+=("-")
    sum_quarantined+=("-")
    sum_elapsed+=("0")
    any_failed=1
    if ((continue_on_error == 0)); then
      print_summary
      exit 1
    fi
    continue
  fi

  echo "[$(timestamp_utc)] [${step_num}/${#plan_jobs[@]}] Starting ${job} with payload ${payload}..."
  step_start=$SECONDS
  http_code="$(
    curl -sS -o "$resp_file" -w "%{http_code}" \
      --max-time "$CURL_MAX_TIME" \
      -b "$cookie_jar" -c "$cookie_jar" \
      -H "Content-Type: application/json" \
      -H "X-CSRF-Token: ${csrf_token}" \
      --data "$payload" \
      "${API_BASE}/api/v1/admin/ingest/${job}/run" || echo "000"
  )"
  elapsed=$((SECONDS - step_start))

  parsed_line="$(python3 - "$resp_file" <<'PY' 2>/dev/null || echo "PARSE_ERROR"
import json
import sys

try:
    with open(sys.argv[1], encoding="utf-8") as fh:
        data = json.load(fh)
except Exception:
    print("PARSE_ERROR")
    sys.exit(0)

if not isinstance(data, dict):
    print("PARSE_ERROR")
    sys.exit(0)

status = str(data.get("status") or "")
written = data.get("written")
unchanged = data.get("unchanged")
quarantined = data.get("quarantined")
run_id = data.get("runId")

def fmt(val):
    return "-" if val is None else str(val)

print("\t".join([status or "-", fmt(written), fmt(unchanged), fmt(quarantined), fmt(run_id)]))
PY
  )"

  step_ok=0
  if [[ "$http_code" =~ ^2[0-9]{2}$ ]] && [[ "$parsed_line" != "PARSE_ERROR" ]]; then
    IFS=$'\t' read -r job_status written unchanged quarantined run_id <<<"$parsed_line"
    case "$job_status" in
      succeeded | partial | skipped)
        step_ok=1
        ;;
      *)
        step_ok=0
        ;;
    esac
  else
    job_status="http-${http_code}"
    written="-"
    unchanged="-"
    quarantined="-"
    run_id="-"
  fi

  sum_jobs+=("$job")
  sum_statuses+=("$job_status")
  sum_written+=("$written")
  sum_unchanged+=("$unchanged")
  sum_quarantined+=("$quarantined")
  sum_elapsed+=("$elapsed")

  if ((step_ok == 1)); then
    echo "[$(timestamp_utc)] [${step_num}/${#plan_jobs[@]}] Finished ${job}: status=${job_status} runId=${run_id} written=${written} unchanged=${unchanged} quarantined=${quarantined} (${elapsed}s)"
  else
    echo "[$(timestamp_utc)] [${step_num}/${#plan_jobs[@]}] FAILED ${job}: http=${http_code} status=${job_status} (${elapsed}s)" >&2
    if [[ -s "$resp_file" ]]; then
      head -c 1000 "$resp_file" >&2 || true
      echo "" >&2
    fi
    any_failed=1
    if ((continue_on_error == 0)); then
      print_summary
      exit 1
    fi
  fi
done

print_summary

if ((any_failed == 1)); then
  echo "backfill-dev: completed with one or more failed jobs." >&2
  exit 1
fi

echo "backfill-dev: all ${#plan_jobs[@]} job(s) completed successfully."
