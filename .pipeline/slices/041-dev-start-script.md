---
id: SLICE-041
status: done
scope:
  - .pipeline/slices/041-dev-start-script.md
  - scripts/dev-up.sh
---

# SLICE-041 — One-Line Local Dev Startup Script

## Context & Goal

Starting the full Değerli local development stack currently requires running `docker compose -f compose.dev.yml up -d` (PostgreSQL, MailPit, API) and separately launching the containerized Vite dev server (`degerli-dev-web` using `node:22-slim`, bind-mounting `src/Web` and the `degerli-web-node-modules` volume) because Node.js is not required on the host.

Provide a single, idempotent CLI script (`scripts/dev-up.sh`) so a developer can start, check, or stop the entire local app stack (Postgres, MailPit, API, and Web SPA) in one line.

## Rules & Invariants

- **Idempotent & non-destructive:** Re-running `./scripts/dev-up.sh` when containers are already running must never restart healthy running services or interrupt in-flight ingestion/backfill jobs.
- **No host Node.js required:** Runs `degerli-dev-web` in `node:22-slim` with `--network host`, `-v "$REPO_ROOT/src/Web:/app"`, `-v degerli-web-node-modules:/app/node_modules`, `-w /app`, `-e VITE_API_PROXY_TARGET=http://127.0.0.1:8080`, `--restart unless-stopped`, automatically running `npm ci` inside the volume if `/app/node_modules/.bin/vite` is missing.
- **Clean CLI:** Supports default start, `--migrate` (runs `compose.dev.yml` `migrate` profile before starting API), `--down` (stops `degerli-dev-web` and `compose.dev.yml`), `--status` (prints container and endpoint health), and `-h`/`--help`.
- **No external repo changes:** Does not touch `compose.dev.yml`, `src/`, or OpenAPI contracts.

## Blockers & Dependencies

- Requires `docker` (with Compose v2) and `curl` on the host.

## Verification

- `bash -n scripts/dev-up.sh` passes with exit 0.
- `./scripts/dev-up.sh --help` prints usage and exits 0.
- `./scripts/dev-up.sh --status` reports current container and endpoint status cleanly without restarting anything.
- `./scripts/dev-up.sh` against the already-running stack is a safe no-op that verifies health and prints URLs without restarting running containers.

## Result

- Added [scripts/dev-up.sh](../../scripts/dev-up.sh) (`chmod +x`, `set -euo pipefail`, zero host Node.js dependency).
- Verified `bash -n`, `--help`, `--status`, unknown-flag handling (exit 2), and idempotent `./scripts/dev-up.sh` execution against the live dev stack without disrupting running containers or the active backfill.
