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
#   OPENAPI_SKIP_BUILD=1     reuse the existing Release build (CI already built it)
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

cleanup() {
  if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
  rm -rf "$workdir"
}
trap cleanup EXIT

# Production environment: the non-production `_probe` endpoints are not mapped
# there, so the snapshot describes exactly what ships. The connection string is a
# placeholder — the document is built from endpoint metadata and never opens a
# connection; the scheduler is off so no worker touches the placeholder.
echo "generate-openapi: starting the API on ${base_url}…"
(
  cd "$workdir"
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

cp "$normalized_json" "$snapshot"
echo "generate-openapi: wrote '$snapshot' (${path_count} paths)."
echo "generate-openapi: regenerate the web types with 'npm --prefix src/Web run generate:api'."
