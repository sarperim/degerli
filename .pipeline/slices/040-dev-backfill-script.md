---
id: SLICE-040
status: done
scope:
  - .pipeline/slices/040-dev-backfill-script.md
  - scripts/backfill-dev.sh
---

# SLICE-040 — Local Dev Multi-Year Historical Backfill Script

## Context & Goal

A fresh `compose.dev.yml` database starts with only the application seed (metric catalog,
macro series, and `builder` account). The scheduler's startup catch-up fetches only the
current trading day's prices, which leaves `market_snapshots` empty (no prior trading day
to compute daily changes) and leaves historical charts, screener metrics, and KAP
fundamentals empty until an operator manually triggers backfill jobs via
`POST /api/v1/admin/ingest/{job}/run`.

Provide a reusable, documented CLI script (`scripts/backfill-dev.sh`) that authenticates
as the dev builder account and orchestrates the canonical admin ingestion endpoints to
backfill the last N years (default: 3 years) of real market, macro, and KAP data in the
correct dependency order.

## Rules & Invariants

- **Honest-data & canonical ingestion only:** The script must exclusively call existing
  `POST /api/v1/admin/ingest/{job}/run` endpoints — never insert synthetic or fabricated
  rows into PostgreSQL directly.
- **Dependency-ordered phases:**
  - **Phase 1 (`--fast`):** `universe-sync` -> `prices` (with `backfillFrom`) ->
    `macro-daily` -> `macro-cpi` -> `intraday-quotes` -> derived recompute
    (`metrics-recompute`, `dcf-baselines`, `market-snapshot`, `medians`). Populates
    dashboard overview, screener, and 3-year price charts quickly (~1–2 min).
  - **Phase 2 (full mode):** `kap-public-identity` -> `shares` -> `dividends` ->
    `corporate-actions` -> `disclosures` -> `statements` (pass 1 with `backfillFrom`
    unless `--resume-statements`, subsequent passes with `{}` so cursors are not reset) ->
    derived recompute (`metrics-recompute`, `dcf-baselines`, `market-snapshot`, `medians`).
- **Partial tolerance:** Upstream real sources (İş Yatırım, KAP, TCMB EVDS) legitimately
  return HTTP 200 with `"status": "partial"`; treat `partial` as non-fatal and surface a
  clear per-job summary table at the end.
- **Credential safety:** Default builder credentials are the documented dev/CI values
  (`builder@degerli.local` / `Degerli-Builder-2026`), overridable via `BUILDER_EMAIL` and
  `BUILDER_PASSWORD`; cookie jar uses `mktemp` and is removed via `trap`.
- **Clean working tree & zero side effects:** No temporary files left in the repository.

## Blockers & Dependencies

- Requires a running API (`compose.dev.yml`) with migrations applied and network access to
  upstream real data sources.
- No OpenAPI contract changes (reuses existing `/api/v1/auth/*` and `/api/v1/admin/ingest/*`
  endpoints).

## Verification

- `bash -n scripts/backfill-dev.sh` passes with exit 0.
- `./scripts/backfill-dev.sh --help` prints usage and exits 0.
- `./scripts/backfill-dev.sh --dry-run` and `./scripts/backfill-dev.sh --fast --dry-run`
  print the expected ordered endpoints and payloads with a 3-year lookback date and exit 0.
- Live execution of `./scripts/backfill-dev.sh --fast` against local `compose.dev.yml`
  populates 3 years of `daily_prices`, creates `market_snapshots`, and makes
  `/api/v1/market/overview` and `/api/v1/screener` serve real data.

## Result

- Added [scripts/backfill-dev.sh](../../scripts/backfill-dev.sh) (`chmod +x`, `set -euo pipefail`, pure bash + `curl` + `python3`).
- Verified `bash -n`, `--help`, and `--dry-run` across default (19 steps), `--fast` (9 steps), `--from`/`--statements-passes`, `--resume-statements`, and `--only` modes.
- Executed `./scripts/backfill-dev.sh --fast --continue-on-error` against the running `compose.dev.yml` stack: all 9 Phase 1 jobs completed cleanly (`prices`: 73,717 rows written across 100/100 XU100 symbols from `2023-11-27` to `2026-11-26`; `macro-daily`: 191; `macro-cpi`: 4; `intraday-quotes`: 100; `metrics-recompute`: 1,161; `dcf-baselines`: 629; `market-snapshot`: 1; `medians`: 576).
- Verified `/api/v1/market/overview` (`state: available`, breadth 81/16/3), `/api/v1/market/macro` (`TUIK_CPI`, `CBRT_REPO`, `USD_TRY`, `EUR_TRY`, `GOLD` available), and `/api/v1/stocks/THYAO/valuation` (3-year price series) through the SPA proxy on `:5173`.
