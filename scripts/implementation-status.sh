#!/usr/bin/env bash
# implementation-status.sh — the mechanical half of IMPLEMENTATION_STATUS.md.
#
# TKT-foundation-074. This checker exists so the status file cannot quietly rot:
# every "live" claim in it names the code/config it stands on, and this script
# fails when a documented claim no longer matches the repository.
#
# It is READ-ONLY (no network, no database, no writes outside mktemp in --self-test)
# and deterministic (no GitHub state; the clock can be pinned with --today).
#
# Usage
#   scripts/implementation-status.sh                     # check the repo at the git root
#   scripts/implementation-status.sh --self-test         # prove the checker itself detects breakage
#   scripts/implementation-status.sh --help
#
# Exit codes: 0 = every documented claim holds; 1 = at least one claim is stale/false;
#             2 = usage/environment error (missing file, missing python3, bad flag).
#
# Requires bash and python3 (python3 is used only to read src/Api/appsettings.json;
# the repository's README already documents python3 for exactly this kind of check).

set -uo pipefail

PROG="$(basename "$0")"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SELF="$SCRIPT_DIR/$(basename "${BASH_SOURCE[0]}")"

ROOT=""
STATUS_FILE=""
APPSETTINGS=""
TICKETS=""
README=""
TODAY=""
MAX_AGE_DAYS=60
SELF_TEST=0

die() { echo "$PROG: $*" >&2; exit 2; }

usage() {
  sed -n '2,26p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
  case "$1" in
    --root)          ROOT="${2:-}"; shift 2 ;;
    --status-file)   STATUS_FILE="${2:-}"; shift 2 ;;
    --appsettings)   APPSETTINGS="${2:-}"; shift 2 ;;
    --tickets)       TICKETS="${2:-}"; shift 2 ;;
    --readme)        README="${2:-}"; shift 2 ;;
    --today)         TODAY="${2:-}"; shift 2 ;;
    --max-age-days)  MAX_AGE_DAYS="${2:-}"; shift 2 ;;
    --self-test)     SELF_TEST=1; shift ;;
    -h|--help)       usage; exit 0 ;;
    *)               die "unknown argument '$1' (try --help)" ;;
  esac
done

# ---------------------------------------------------------------------------
# self-test: build a tiny valid fixture, then break it eighteen ways and
# require the checker to fail every time (and to say why). No network, no repo
# state, fixed clock.
# ---------------------------------------------------------------------------
write_valid_base() { # $1 = target dir, $2 = last-verified date
  local d="$1" verified="$2"
  mkdir -p "$d/src/Api" "$d/src/Ingestion/Prices" "$d/.pipeline/plan/tickets"
  cat > "$d/src/Api/appsettings.json" <<'JSON'
{
  "Ingestion": {
    "Prices": { "UniverseIndex": "XU100" }
  }
}
JSON
  printf -- '- Status: todo\n' > "$d/.pipeline/plan/tickets/TKT-demo-001.md"
  printf '# Demo\n\nSee [implementation status](IMPLEMENTATION_STATUS.md).\n' > "$d/README.md"
  cat > "$d/IMPLEMENTATION_STATUS.md" <<MD
# Implementation status fixture

Last verified: $verified

## Status vocabulary

| Term | Meaning |
|------|---------|
| \`live\` | serving real data, observed live |
| \`live-degraded\` | serving, with an observed live defect |
| \`ci-only\` | proven only on doubles/seeded stacks |
| \`wired-not-configured\` | code exists, configuration absent |
| \`scaffolded\` | structure only |
| \`blocked-upstream\` | blocked on an external source |

## Domain status

<!-- status:domains -->
| Domain | Status | Evidence | Checks |
|--------|--------|----------|--------|
| Prices | \`live\` | demo (LC-01) | dir:src/Ingestion/Prices;config:Ingestion:Prices:UniverseIndex;ticket:TKT-demo-001 |
| Funds | \`wired-not-configured\` | demo | dir:src/Ingestion/Prices;noconfig:Ingestion:Funds:BaseUrl |
<!-- /status:domains -->

## Live-criteria ledger

<!-- status:ledger -->
| ID | Claim | What CI proves | What only a live run proves | Last live observation |
|----|-------|----------------|-----------------------------|-----------------------|
| LC-01 | demo claim | parsing with doubles | the real source's shape | $verified (run 1) |
<!-- /status:ledger -->

## Open tickets

<!-- status:tickets -->
| Ticket | Ticket status | Blocker / next step |
|--------|---------------|---------------------|
| TKT-demo-001 | \`todo\` | demo |
<!-- /status:tickets -->

## Update rule

1. demo rule.
MD
}

self_test() {
  local tmp base n=0 failures=0
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/impl-status-selftest.XXXXXX")" || die "mktemp failed"
  base="$tmp/base"
  write_valid_base "$base" "2026-10-24"

  run_case() { # $1 = label, $2 = expected exit (0|1|2), $3 = expected output regex, $4 = --today
    local label="$1" expected="$2" pattern="${3:-}" today="${4:-2026-10-24}" code
    "$SELF" --root "$CASE" --today "$today" >"$tmp/out.txt" 2>&1
    code=$?
    n=$((n + 1))
    if [ "$code" -ne "$expected" ]; then
      printf 'FAIL  %s (expected exit %s, got %s)\n' "$label" "$expected" "$code"
      sed 's/^/      /' "$tmp/out.txt" | head -10
      failures=$((failures + 1))
    elif [ -n "$pattern" ] && ! grep -qE -- "$pattern" "$tmp/out.txt"; then
      printf 'FAIL  %s (exit %s as expected, but no output matched /%s/)\n' "$label" "$code" "$pattern"
      sed 's/^/      /' "$tmp/out.txt" | head -10
      failures=$((failures + 1))
    else
      printf 'PASS  %s\n' "$label"
    fi
  }

  CASE="$base"; run_case "valid fixture passes" 0 'OK — '

  CASE="$tmp/missing-path"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's|dir:src/Ingestion/Prices;config|dir:src/Ingestion/Absent;config|'
  run_case "a live domain whose implementation path is gone fails" 1 "directory 'src/Ingestion/Absent' does not exist"

  CASE="$tmp/absent-config"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's|config:Ingestion:Prices:UniverseIndex|config:Ingestion:Prices:NoSuchKey|'
  run_case "a claimed config key that is absent fails" 1 "claimed config 'Ingestion:Prices:NoSuchKey' is absent"

  CASE="$tmp/empty-config"; cp -a "$base" "$CASE"
  python3 - "$CASE/src/Api/appsettings.json" <<'PY'
import json, sys
path = sys.argv[1]
doc = json.load(open(path, encoding="utf-8"))
doc["Ingestion"]["Prices"]["UniverseIndex"] = ""
json.dump(doc, open(path, "w", encoding="utf-8"))
PY
  run_case "a config key that is present but empty is not reported as absent" 1 "is present but empty"

  CASE="$tmp/present-but-claimed-absent"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's|noconfig:Ingestion:Funds:BaseUrl|noconfig:Ingestion:Prices:UniverseIndex|'
  run_case "a section claimed absent that is present fails" 1 "is absent, but it is configured"

  CASE="$tmp/vocabulary"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's/| Prices | `live` | demo (LC-01) |/| Prices | `basically-fine` | demo (LC-01) |/'
  run_case "an undeclared status word fails" 1 "not in the fixed vocabulary"

  CASE="$tmp/no-section"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's|^## Update rule$|## Something else|'
  run_case "a missing required section fails" 1 "required section missing: '## Update rule'"

  CASE="$tmp/undated-claim"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's/| 2026-10-24 (run 1) |/| - |/'
  run_case "a ledger row without a live-observation date fails" 1 "last-observation cell must start with"

  CASE="$tmp/missing-ticket-row"; cp -a "$base" "$CASE"
  printf -- '- Status: todo\n' > "$CASE/.pipeline/plan/tickets/TKT-demo-002.md"
  run_case "a newly sliced open ticket with no ledger row fails" 1 "open ticket TKT-demo-002 \('todo'\) is missing from the open-ticket ledger"

  CASE="$tmp/multiword-status"; cp -a "$base" "$CASE"
  printf -- '- Status: partially resolved\n' > "$CASE/.pipeline/plan/tickets/TKT-demo-003.md"
  run_case "an unknown/multi-word status is open, not terminal, and needs a row" 1 "open ticket TKT-demo-003 \('partially'\) is missing"

  CASE="$tmp/stale-date"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's/^Last verified: 2026-10-24$/Last verified: 2020-01-01/'
  run_case "a stale last-verified date fails" 1 "days old \(limit 60\)"

  CASE="$tmp/status-mismatch"; cp -a "$base" "$CASE"
  printf -- '- Status: done\n' > "$CASE/.pipeline/plan/tickets/TKT-demo-001.md"
  run_case "a ledger row whose class disagrees with the ticket fails" 1 "ledger row TKT-demo-001 says 'todo' but the ticket is 'done'"

  CASE="$tmp/no-readme-link"; cp -a "$base" "$CASE"
  printf '# Demo\n\nNo link here.\n' > "$CASE/README.md"
  run_case "a README without the pointer fails" 1 "does not link to IMPLEMENTATION_STATUS.md"

  CASE="$tmp/blank-evidence"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's/| Prices | `live` | demo (LC-01) |/| Prices | `live` |  |/'
  run_case "a domain row with an empty Evidence cell fails" 1 "empty Evidence cell"

  CASE="$tmp/live-without-lc"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's/| Prices | `live` | demo (LC-01) |/| Prices | `live` | demo |/'
  run_case "a live domain that cites no LC id fails" 1 "requires at least one LC-nn reference"

  CASE="$tmp/unknown-lc"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's|demo (LC-01)|demo (LC-09)|'
  run_case "a domain citing a ledger row that does not exist fails" 1 "LC-09.*not in the live-criteria ledger"

  CASE="$tmp/live-without-dated-lc"; cp -a "$base" "$CASE"
  mutate "$CASE/IMPLEMENTATION_STATUS.md" 's/| 2026-10-24 (run 1) |/| not yet observed |/'
  run_case "a live domain whose only LC row has no dated observation fails" 1 "record the observation or change the status"

  CASE="$base"; run_case "an impossible --today is a usage error, not a cascade of failures" 2 "real calendar date" "2026-13-01"

  rm -rf "$tmp"
  echo
  if [ "$failures" -eq 0 ]; then
    echo "self-test: $n/$n cases behaved as required"
    return 0
  fi
  echo "self-test: $failures/$n cases MISBEHAVED" >&2
  return 1
}

mutate() { # $1 = file, $2 = sed expression
  sed -i "$2" "$1" || die "self-test mutation failed: $2"
}

if [ "$SELF_TEST" -eq 1 ]; then
  self_test
  exit $?
fi

# ---------------------------------------------------------------------------
# resolve inputs
# ---------------------------------------------------------------------------
if [ -z "$ROOT" ]; then
  ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null || true)"
  [ -n "$ROOT" ] || ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
fi
[ -d "$ROOT" ] || die "root '$ROOT' is not a directory"
: "${STATUS_FILE:=$ROOT/IMPLEMENTATION_STATUS.md}"
: "${APPSETTINGS:=$ROOT/src/Api/appsettings.json}"
: "${TICKETS:=$ROOT/.pipeline/plan/tickets}"
: "${README:=$ROOT/README.md}"

[ -f "$STATUS_FILE" ] || die "status file not found: $STATUS_FILE"
[ -f "$APPSETTINGS" ] || die "appsettings not found: $APPSETTINGS"
[ -d "$TICKETS" ] || die "ticket directory not found: $TICKETS"
[ -f "$README" ] || die "README not found: $README"
command -v python3 >/dev/null 2>&1 || die "python3 is required (JSON lookup in $APPSETTINGS)"
case "$MAX_AGE_DAYS" in ''|*[!0-9]*) die "--max-age-days must be a non-negative integer" ;; esac
if [ -z "$TODAY" ]; then TODAY="$(date +%Y-%m-%d)"; fi
[[ "$TODAY" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || die "--today must be YYYY-MM-DD, got '$TODAY'"
python3 - "$TODAY" >/dev/null 2>&1 <<'PY' || die "--today must be a real calendar date (YYYY-MM-DD), got '$TODAY'"
import sys, datetime
datetime.date.fromisoformat(sys.argv[1])
PY

FAILURES=0
CHECKS=0
fail() { echo "FAIL  $*"; FAILURES=$((FAILURES + 1)); }
ok()   { CHECKS=$((CHECKS + 1)); }

# whole days between two YYYY-MM-DD dates (a - b)
days_between() {
  python3 - "$1" "$2" <<'PY'
import sys, datetime
a = datetime.date.fromisoformat(sys.argv[1])
b = datetime.date.fromisoformat(sys.argv[2])
print((a - b).days)
PY
}

# ---------------------------------------------------------------------------
# 1. required sections and the last-verified stamp
# ---------------------------------------------------------------------------
REQUIRED_SECTIONS=(
  "## Status vocabulary"
  "## Domain status"
  "## Live-criteria ledger"
  "## Open tickets"
  "## Update rule"
)
for section in "${REQUIRED_SECTIONS[@]}"; do
  if grep -qxF "$section" "$STATUS_FILE"; then ok; else fail "required section missing: '$section'"; fi
done

VERIFIED="$(grep -oE 'Last verified: *[0-9]{4}-[0-9]{2}-[0-9]{2}' "$STATUS_FILE" | head -1 | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}')"
if [ -z "$VERIFIED" ]; then
  fail "'Last verified: YYYY-MM-DD' is missing from $(basename "$STATUS_FILE")"
else
  AGE="$(days_between "$TODAY" "$VERIFIED")"
  if [ "$AGE" -lt 0 ]; then
    fail "last verified date $VERIFIED is in the future (today $TODAY)"
  elif [ "$AGE" -gt "$MAX_AGE_DAYS" ]; then
    fail "last verified date $VERIFIED is $AGE days old (limit $MAX_AGE_DAYS) — re-verify the status file"
  else
    ok
  fi
fi

# ---------------------------------------------------------------------------
# 2. vocabulary — the six fixed terms must be declared, and only they may be used
# ---------------------------------------------------------------------------
VOCAB_TERMS=(live live-degraded ci-only wired-not-configured scaffolded blocked-upstream)
for term in "${VOCAB_TERMS[@]}"; do
  if grep -qE "^\| \`$term\` \|" "$STATUS_FILE"; then ok; else fail "status vocabulary does not declare \`$term\`"; fi
done

section_body() { # $1 = heading, $2 = file
  awk -v h="$1" '
    $0 == h { inside = 1; next }
    inside && /^## / { inside = 0 }
    inside { print }
  ' "$2"
}

in_vocab() {
  local candidate="$1" term
  for term in "${VOCAB_TERMS[@]}"; do [ "$candidate" = "$term" ] && return 0; done
  return 1
}

trim() { printf '%s' "$1" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'; }

# ---------------------------------------------------------------------------
# 3. domain rows — every documented domain must stand on real code/config
# ---------------------------------------------------------------------------
DOMAINS="$(awk '
  /<!-- status:domains -->/ { inside = 1; next }
  /<!-- \/status:domains -->/ { inside = 0 }
  inside && /^\|/ { print }
' "$STATUS_FILE")"

if [ -z "$DOMAINS" ]; then
  fail "no domain rows between the 'status:domains' markers"
fi

# Every Evidence cell must be non-empty; a `live`/`live-degraded` cell must also carry an LC-nn
# citation, and every citation of an LC-nn row is resolved against the ledger below (section 4).
# A `live` row additionally needs at least one *dated* LC row, so a domain cannot be promoted to
# `live` on an unobserved criterion.
DOMAIN_LC_REFS=()   # "domain<TAB>LC-nn"
LIVE_DOMAINS=()     # domains claiming `live`

# Collect config/noconfig lookups and resolve them in one python3 pass.
CONFIG_QUERIES=()
while IFS= read -r line; do
  [ -n "$line" ] || continue
  cells="$(printf '%s' "$line" | awk -F'|' '{ for (i = 2; i < NF; i++) { gsub(/^ +| +$/, "", $i); printf "%s\x1f", $i } }')"
  domain="$(printf '%s' "$cells" | cut -d$'\x1f' -f1)"
  status_cell="$(printf '%s' "$cells" | cut -d$'\x1f' -f2 | tr -d '`')"
  evidence_cell="$(printf '%s' "$cells" | cut -d$'\x1f' -f3)"
  checks_cell="$(printf '%s' "$cells" | cut -d$'\x1f' -f4)"
  [ -n "$domain" ] || continue
  [ "$domain" = "Domain" ] && continue
  if printf '%s' "$domain" | grep -qE '^-+$'; then continue; fi

  if [ -z "$(trim "$evidence_cell")" ]; then
    fail "domain '$domain': empty Evidence cell — every domain claim needs an evidence line"
  else
    ok
  fi
  lc_refs="$(printf '%s' "$evidence_cell" | grep -oE 'LC-[0-9]{2,}' | sort -u || true)"
  if [ -n "$lc_refs" ]; then
    while IFS= read -r lc; do
      [ -n "$lc" ] || continue
      DOMAIN_LC_REFS+=("$domain"$'\t'"$lc")
    done <<< "$lc_refs"
  elif [ "$status_cell" = "live" ] || [ "$status_cell" = "live-degraded" ]; then
    fail "domain '$domain': status '$status_cell' requires at least one LC-nn reference in its Evidence cell"
  fi
  [ "$status_cell" = "live" ] && LIVE_DOMAINS+=("$domain")

  if [ -z "$status_cell" ]; then
    fail "domain '$domain' has no status"
  elif ! in_vocab "$status_cell"; then
    fail "domain '$domain' uses status '$status_cell', which is not in the fixed vocabulary"
  else
    ok
  fi

  has_impl=0
  IFS=';' read -r -a tokens <<< "$checks_cell"
  for token in "${tokens[@]}"; do
    token="$(trim "$token")"
    [ -n "$token" ] || continue
    case "$token" in
      dir:*)
        path="${token#dir:}"
        if [ -d "$ROOT/$path" ]; then ok; has_impl=1; else fail "domain '$domain': directory '$path' does not exist"; fi
        ;;
      file:*)
        path="${token#file:}"
        if [ -f "$ROOT/$path" ]; then ok; has_impl=1; else fail "domain '$domain': file '$path' does not exist"; fi
        ;;
      config:*|config-empty:*|noconfig:*)
        CONFIG_QUERIES+=("$domain"$'\t'"$token")
        ;;
      ticket:*)
        id="${token#ticket:}"
        if [ -f "$TICKETS/$id.md" ]; then ok; else fail "domain '$domain': cited ticket '$id' does not exist"; fi
        ;;
      *)
        fail "domain '$domain': unparseable check '$token' (expected dir:, file:, config:, config-empty:, noconfig: or ticket:)"
        ;;
    esac
  done
  if [ "$has_impl" -eq 0 ]; then
    fail "domain '$domain': no dir:/file: check — a documented domain must name the code it stands on"
  fi
done <<< "$DOMAINS"

if [ "${#CONFIG_QUERIES[@]}" -gt 0 ]; then
  RESOLVED="$(printf '%s\n' "${CONFIG_QUERIES[@]}" | python3 -c '
import json, sys
try:
    with open(sys.argv[1], encoding="utf-8") as fh:
        doc = json.load(fh)
except Exception as exc:  # unreadable appsettings is an environment error, not a status verdict
    print("PARSE-ERROR\t%s" % exc)
    raise SystemExit(0)

for raw in sys.stdin:
    raw = raw.rstrip("\n")
    if not raw:
        continue
    domain, token = raw.split("\t", 1)
    kind, path = token.split(":", 1)
    node = doc
    present = True
    for part in path.split(":"):
        if isinstance(node, dict) and part in node:
            node = node[part]
        else:
            present = False
            break
    print("%s\t%s\t%s" % (domain, token, "absent" if not present else ("empty" if node in ("", None, [], {}) else "present")))
' "$APPSETTINGS")"
  while IFS=$'\t' read -r domain token verdict; do
    [ -n "${token:-}" ] || continue
    if [ "$domain" = "PARSE-ERROR" ]; then
      die "appsettings is not valid JSON: $token"
    fi
    case "$token" in
      config:*)
        if [ "$verdict" = "present" ]; then
          ok
        elif [ "$verdict" = "empty" ]; then
          fail "domain '$domain': claimed config '${token#config:}' is present but empty in appsettings.json — the row does not hold"
        else
          fail "domain '$domain': claimed config '${token#config:}' is absent from appsettings.json"
        fi
        ;;
      config-empty:*)
        if [ "$verdict" = "empty" ]; then ok; else fail "domain '$domain': claims '${token#config-empty:}' is present but empty, and it is not (verdict: $verdict) — the row is stale"; fi
        ;;
      noconfig:*)
        if [ "$verdict" = "absent" ]; then ok; else fail "domain '$domain': claims '${token#noconfig:}' is absent, but it is configured — the row is stale"; fi
        ;;
    esac
  done <<< "$RESOLVED"
fi

# ---------------------------------------------------------------------------
# 4. live-criteria ledger — no claim without a dated (or explicitly unobserved) line
# ---------------------------------------------------------------------------
LEDGER="$(awk '
  /<!-- status:ledger -->/ { inside = 1; next }
  /<!-- \/status:ledger -->/ { inside = 0 }
  inside && /^\|/ { print }
' "$STATUS_FILE")"

if [ -z "$LEDGER" ]; then fail "no rows between the 'status:ledger' markers"; fi

declare -A SEEN_IDS=()
declare -A DATED_IDS=()
LEDGER_ROWS=0
while IFS= read -r line; do
  [ -n "$line" ] || continue
  cells="$(printf '%s' "$line" | awk -F'|' '{ for (i = 2; i < NF; i++) { gsub(/^ +| +$/, "", $i); printf "%s\x1f", $i } }')"
  id="$(printf '%s' "$cells" | cut -d$'\x1f' -f1)"
  claim="$(printf '%s' "$cells" | cut -d$'\x1f' -f2)"
  ci="$(printf '%s' "$cells" | cut -d$'\x1f' -f3)"
  live="$(printf '%s' "$cells" | cut -d$'\x1f' -f4)"
  observed="$(printf '%s' "$cells" | cut -d$'\x1f' -f5)"
  [ -n "$id" ] || continue
  [ "$id" = "ID" ] && continue
  if printf '%s' "$id" | grep -qE '^-+$'; then continue; fi

  if ! [[ "$id" =~ ^LC-[0-9]{2,}$ ]]; then
    fail "ledger row id '$id' is not of the form LC-nn"
    continue
  fi
  if [ -n "${SEEN_IDS[$id]:-}" ]; then fail "duplicate ledger id $id"; fi
  SEEN_IDS[$id]=1
  LEDGER_ROWS=$((LEDGER_ROWS + 1))

  for pair in "claim:$claim" "what-CI-proves:$ci" "what-live-proves:$live" "last-observation:$observed"; do
    if [ -z "$(trim "${pair#*:}")" ]; then fail "ledger $id: empty '${pair%%:*}' cell"; fi
  done

  observed_trim="$(trim "$observed")"
  date_part="$(printf '%s' "$observed_trim" | grep -oE '^[0-9]{4}-[0-9]{2}-[0-9]{2}' || true)"
  if [ -n "$date_part" ]; then
    age="$(days_between "$TODAY" "$date_part" 2>/dev/null || echo x)"
    if [ "$age" = "x" ]; then
      fail "ledger $id: last observation '$date_part' is not a real date"
    elif [ "$age" -lt 0 ]; then
      fail "ledger $id: last observation $date_part is in the future (today $TODAY)"
    else
      DATED_IDS[$id]=1
      ok
    fi
  elif printf '%s' "$observed_trim" | grep -qiE '(not yet observed|never observed)'; then
    ok
  else
    fail "ledger $id: last-observation cell must start with a YYYY-MM-DD date or say 'not yet observed'/'never observed'"
  fi
done <<< "$LEDGER"

if [ "$LEDGER_ROWS" -eq 0 ]; then fail "the live-criteria ledger has no claim rows"; fi

# Resolve every LC-nn a domain row cites against the ledger just parsed.
declare -A LIVE_DOMAIN_LCS=()   # domain -> space-separated LC ids that carry a date
for ref in "${DOMAIN_LC_REFS[@]:-}"; do
  [ -n "${ref:-}" ] || continue
  ref_domain="${ref%%$'\t'*}"
  ref_id="${ref#*$'\t'}"
  if [ -z "${SEEN_IDS[$ref_id]:-}" ]; then
    fail "domain '$ref_domain' cites $ref_id, which is not in the live-criteria ledger"
  elif [ -n "${DATED_IDS[$ref_id]:-}" ]; then
    LIVE_DOMAIN_LCS[$ref_domain]="${LIVE_DOMAIN_LCS[$ref_domain]:-} $ref_id"
  fi
done
for live_domain in "${LIVE_DOMAINS[@]:-}"; do
  [ -n "${live_domain:-}" ] || continue
  if [ -z "${LIVE_DOMAIN_LCS[$live_domain]:-}" ]; then
    fail "domain '$live_domain' is 'live' but no LC-nn it cites has a dated live observation — record the observation or change the status"
    continue
  fi
  ok
done

# ---------------------------------------------------------------------------
# 5. open-ticket ledger — the set of non-terminal tickets must match exactly
# ---------------------------------------------------------------------------
ticket_class() { # $1 = ticket file -> terminal class word
  local class
  class="$(grep -m1 -E '^- Status:' "$1" | sed -E 's/^- Status:[[:space:]]*([A-Za-z-]+).*/\1/' | tr '[:upper:]' '[:lower:]')"
  [ -n "$class" ] || class="unknown"
  printf '%s' "$class"
}

is_terminal_class() { # only an explicit terminal word closes a ticket; everything else is open
  case "$1" in done|superseded|cancelled) return 0 ;; *) return 1 ;; esac
}

declare -A DOC_TICKETS=()   # id -> row class
DOC_ORDER=()
OPEN_TICKETS_SEC="$(section_body "## Open tickets" "$STATUS_FILE")"
while IFS= read -r line; do
  [ -n "$line" ] || continue
  cells="$(printf '%s' "$line" | awk -F'|' '{ for (i = 2; i < NF; i++) { gsub(/^ +| +$/, "", $i); printf "%s\x1f", $i } }')"
  id="$(printf '%s' "$cells" | cut -d$'\x1f' -f1 | tr -d '`')"
  class="$(printf '%s' "$cells" | cut -d$'\x1f' -f2 | tr -d '`' | tr '[:upper:]' '[:lower:]')"
  blocker="$(printf '%s' "$cells" | cut -d$'\x1f' -f3)"
  [ -n "$id" ] || continue
  [ "$id" = "Ticket" ] && continue
  if printf '%s' "$id" | grep -qE '^-+$'; then continue; fi
  case "$id" in
    TKT-*) ;;
    *) fail "'$id' in the open-ticket ledger is not a ticket id"; continue ;;
  esac

  if [ -n "${DOC_TICKETS[$id]:-}" ]; then fail "ticket $id is listed twice in the open-ticket ledger"; fi
  DOC_TICKETS[$id]="$class"
  DOC_ORDER+=("$id")

  if [ ! -f "$TICKETS/$id.md" ]; then
    fail "ledger names ticket $id, but $TICKETS/$id.md does not exist"
    continue
  fi
  actual="$(ticket_class "$TICKETS/$id.md")"
  if [ "$class" = "$actual" ]; then
    ok
  elif [ "$class" = "in-review" ] && { [ "$actual" = "done" ] || [ "$actual" = "superseded" ]; }; then
    # documented tolerance: an in-review row may flip to done/superseded on merge without
    # a same-commit doc edit; the row is refreshed on the next landed change.
    ok
  elif [ "$actual" = "done" ] || [ "$actual" = "superseded" ]; then
    fail "ledger row $id says '$class' but the ticket is '$actual' — remove or refresh the row"
  else
    fail "ledger row $id says '$class' but the ticket file says '$actual'"
  fi
  if [ -z "$(trim "$blocker")" ]; then fail "ledger row $id has no blocker/next-step line"; fi
done <<< "$OPEN_TICKETS_SEC"

while IFS= read -r ticket_file; do
  [ -n "$ticket_file" ] || continue
  id="$(basename "$ticket_file" .md)"
  actual="$(ticket_class "$ticket_file")"
  if ! is_terminal_class "$actual"; then
    if [ -z "${DOC_TICKETS[$id]:-}" ]; then
      fail "open ticket $id ('$actual') is missing from the open-ticket ledger"
    fi
  fi
done < <(ls "$TICKETS"/TKT-*.md 2>/dev/null)

# ---------------------------------------------------------------------------
# 6. README pointer
# ---------------------------------------------------------------------------
if grep -q 'IMPLEMENTATION_STATUS\.md' "$README"; then
  ok
else
  fail "$(basename "$README") does not link to IMPLEMENTATION_STATUS.md"
fi

# ---------------------------------------------------------------------------
# verdict
# ---------------------------------------------------------------------------
echo
if [ "$FAILURES" -eq 0 ]; then
  echo "implementation-status: OK — $CHECKS checks passed (last verified $VERIFIED, today $TODAY)"
  exit 0
fi
echo "implementation-status: $FAILURES failure(s) out of $((CHECKS + FAILURES)) checks" >&2
echo "The status file is stale or over-claims. Fix the claim (or the code) and re-verify." >&2
exit 1
