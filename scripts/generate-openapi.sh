#!/usr/bin/env bash
#
# Generate (or verify) the checked-in OpenAPI snapshot — architecture
# `01-system-architecture.md` §6.1, `03-api-design.md` header note.
#
# The canonical machine contract is the document the API actually serves
# (`GET /api/v1/openapi.json`, Microsoft.AspNetCore.OpenApi). This script starts
# the built API with no database and writes that document to
# `src/Api/openapi.json`, so a contract change shows up as a reviewable diff and
# a *served* shape that no longer matches the snapshot fails CI.
#
# Usage:
#   scripts/generate-openapi.sh            # rewrite src/Api/openapi.json
#   scripts/generate-openapi.sh --check    # fail (exit 1) if the snapshot is stale
#   scripts/generate-openapi.sh --update   # alias of the default (documented intent)
#
# Options (environment):
#   OPENAPI_PORT             port to bind the throwaway API on (default 5198)
#   OPENAPI_STARTUP_TIMEOUT  seconds to wait for the document (default 120)
#   OPENAPI_MIN_PATHS        sanity floor on the path count (default 40; 0 disables)
#   OPENAPI_SKIP_BUILD=1     reuse the existing Release build (CI already built it)
#
# The port is pre-flighted: if anything already answers on `$OPENAPI_PORT` the
# script fails immediately instead of letting a stale server satisfy the check
# with an old document (a false "matches"). The fetch is followed by a liveness
# assertion on `$api_pid`: a document is accepted only from this run's own
# process, so a responder that won the port cannot be believed (SEC-4).
#
# Determinism (`01` §6.1, acceptance (iv) of TKT-foundation-071): the snapshot is
# normalised — `servers` is dropped (it carries the throwaway bind URL, not a
# contract), keys are sorted and the file is written with a fixed 2-space indent
# and a trailing newline — so two runs against the same code produce a
# byte-identical file and CI never flakes on serialisation noise.
#
# Nothing here writes into the working tree except the snapshot itself: the API
# runs from a throwaway working directory (its Serilog file sink and crash-
# evidence writer stay in there), so a check run leaves `git status` clean.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

snapshot="src/Api/openapi.json"
port="${OPENAPI_PORT:-5198}"
startup_timeout="${OPENAPI_STARTUP_TIMEOUT:-120}"
min_paths="${OPENAPI_MIN_PATHS:-40}"
base_url="http://127.0.0.1:${port}"

mode="write"
case "${1:-}" in
  --check) mode="check" ;;
  --update | "") mode="write" ;;
  *)
    echo "generate-openapi: unknown argument '$1' (expected --check or --update)" >&2
    exit 2
    ;;
esac

# Pre-flight the port. A leftover API from an earlier run would answer this run's
# `curl` with the *old* document, so `--check` would compare old against old and
# report "matches" on a drifted tree. Refuse to run rather than risk that.
if python3 - "$port" <<'PY'
import socket
import sys

sock = socket.socket()
sock.settimeout(1.0)
try:
    sock.connect(("127.0.0.1", int(sys.argv[1])))
except OSError:
    sys.exit(1)
finally:
    sock.close()
sys.exit(0)
PY
then
  echo "generate-openapi: something already answers on ${base_url} — stop it first." >&2
  echo "  (a stale API from a previous run would satisfy the check with an old document," >&2
  echo "   so this script refuses to run on an occupied port; set OPENAPI_PORT to pick another)" >&2
  exit 1
fi

if [[ "${OPENAPI_SKIP_BUILD:-0}" != "1" ]]; then
  echo "generate-openapi: building the API (Release)…"
  dotnet build src/Api/Degerli.Api.csproj -c Release --nologo -v quiet >/dev/null
fi

api_dll="src/Api/bin/Release/net10.0/Degerli.Api.dll"
if [[ ! -f "$api_dll" ]]; then
  echo "generate-openapi: '$api_dll' not found — run without OPENAPI_SKIP_BUILD=1" >&2
  exit 1
fi

workdir="$(mktemp -d "${TMPDIR:-/tmp}/degerli-openapi.XXXXXX")"
raw_json="$workdir/openapi.raw.json"
normalized_json="$workdir/openapi.json"
api_log="$workdir/api.log"
api_pid=""
tmp_snapshot=""

cleanup() {
  if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then
    kill "$api_pid" 2>/dev/null || true
    # Wait for the graceful shutdown, then insist: an orphan here would hold the
    # port and let the next run's check be answered by a stale document.
    for _ in $(seq 1 25); do
      kill -0 "$api_pid" 2>/dev/null || break
      sleep 0.2
    done
    kill -9 "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
  if [[ -n "$tmp_snapshot" ]]; then
    rm -f "$tmp_snapshot"
  fi
  rm -rf "$workdir"
}
trap cleanup EXIT

# Production environment: the non-production `_probe` endpoints are not mapped
# there, so the snapshot describes exactly what ships. The connection string is a
# placeholder — the document is built from endpoint metadata and never opens a
# connection; the scheduler is off so no worker touches the placeholder.
echo "generate-openapi: starting the API on ${base_url}…"
# `exec` matters: without it this subshell is a separate process whose child
# `dotnet` survives the `kill "$api_pid"` in cleanup (bash does not exec-optimise
# a command carrying a redirection) — leaving a Production-mode API listening on
# the port after the script exits. With `exec`, `$!` *is* the API process.
(
  cd "$workdir"
  exec env \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_CONTENTROOT="$repo_root/src/Api" \
    ASPNETCORE_URLS="$base_url" \
    ConnectionStrings__Default="Host=127.0.0.1;Port=1;Database=openapi;Username=openapi;Password=openapi;Timeout=1" \
    Ingestion__Scheduler__Enabled=false \
    dotnet "$repo_root/$api_dll" >"$api_log" 2>&1
) &
api_pid=$!

deadline=$((SECONDS + startup_timeout))
until curl -fsS -o "$raw_json" "${base_url}/api/v1/openapi.json" 2>/dev/null; do
  if ! kill -0 "$api_pid" 2>/dev/null; then
    echo "generate-openapi: the API exited before serving the document." >&2
    cat "$api_log" >&2
    exit 1
  fi
  if ((SECONDS >= deadline)); then
    echo "generate-openapi: the document was not served within ${startup_timeout}s." >&2
    cat "$api_log" >&2
    exit 1
  fi
  sleep 1
done

# SEC-4: a successful fetch proves *something* answered on the port, not that it
# was this run's API — the liveness check inside the loop only runs when a fetch
# fails, so a server that won the port and answered would exit the loop green. Once
# the API cannot bind it exits, so a live `$api_pid` proves the responder is ours.
kill -0 "$api_pid" 2>/dev/null || {
  echo "generate-openapi: the served document did not come from this run's API" >&2
  cat "$api_log" >&2
  exit 1
}

python3 - "$raw_json" "$normalized_json" <<'PY'
import json
import sys

raw_path, out_path = sys.argv[1], sys.argv[2]
with open(raw_path, encoding="utf-8") as handle:
    document = json.load(handle)

# `servers` is the throwaway bind URL, not part of the contract: keeping it would
# make the snapshot differ on every port and leak runner details into review.
document.pop("servers", None)

with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
    handle.write(json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False))
    handle.write("\n")
PY

path_count="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))["paths"]))' "$normalized_json")"

# Sanity floor (DR-4): a truncated-but-valid document would otherwise be written
# or matched silently — a gate that accepts an empty contract is not a gate.
# The API serves 46 paths; the default floor of 40 leaves room for real removals
# while catching a document that lost most of its surface. `OPENAPI_MIN_PATHS=0`
# disables the guard for an intentional large deletion.
if ((min_paths > 0 && path_count < min_paths)); then
  echo "generate-openapi: the served document has only ${path_count} paths (floor ${min_paths})." >&2
  echo "  That looks truncated, not intentional. If it is intended, re-run with OPENAPI_MIN_PATHS=0." >&2
  cat "$api_log" >&2
  exit 1
fi

if [[ "$mode" == "check" ]]; then
  if [[ ! -f "$snapshot" ]]; then
    echo "generate-openapi: '$snapshot' is missing — run 'scripts/generate-openapi.sh --update' and commit it." >&2
    exit 1
  fi
  if ! diff -u "$snapshot" "$normalized_json"; then
    echo "" >&2
    echo "generate-openapi: the served OpenAPI document does not match '$snapshot'." >&2
    echo "The contract drifted. If the change is intended, run" >&2
    echo "  scripts/generate-openapi.sh --update" >&2
    echo "and commit '$snapshot' with the code that changed it." >&2
    exit 1
  fi
  echo "generate-openapi: '$snapshot' matches the served document (${path_count} paths)."
  exit 0
fi

# Refuse to follow a symlink: `cp`/`>` onto a symlinked snapshot would write to
# whatever it points at instead of failing (SEC-3). And write atomically — a temp
# file in the same directory, then `mv` — so an interrupted run can never leave a
# half-written snapshot behind.
if [[ -L "$snapshot" ]]; then
  echo "generate-openapi: refusing to write '$snapshot' — it is a symlink." >&2
  echo "  The snapshot must be a regular file tracked in git; replace the link with the file." >&2
  exit 1
fi
tmp_snapshot="$(mktemp "$(dirname "$snapshot")/.openapi.json.XXXXXX")"
cp "$normalized_json" "$tmp_snapshot"
chmod 644 "$tmp_snapshot"
mv -f "$tmp_snapshot" "$snapshot"
tmp_snapshot=""
echo "generate-openapi: wrote '$snapshot' (${path_count} paths)."
echo "generate-openapi: regenerate the web types with 'npm --prefix src/Web run generate:api'."
