---
id: SLICE-014
status: done
scope:
  - src/Api/Degerli.Api.csproj
  - src/Api/Program.cs
  - src/Api/appsettings.json
  - src/Api/Infrastructure/OpenTelemetrySetup.cs
  - src/Ingestion/Telemetry/DegerliIngestionMetrics.cs
  - src/Ingestion/Jobs/Jobs.cs
  - compose.dev.yml
  - deploy/README.md
  - tests/Api.IntegrationTests/IngestionTelemetryTests.cs
  - .pipeline/slices/014-api-opentelemetry-ingestion-meters.md
---

# SLICE-014 — API OpenTelemetry: Ingestion Meters + Standalone Aspire Dashboard

## Context & Goal

The API has structured Serilog logging but **no metrics/traces** (`OpenTelemetry` appears
nowhere in `src/`). The wanted payoff is the Aspire dashboard's telemetry view without
the AppHost orchestration tax: **OpenTelemetry in the API, exported over OTLP to the
standalone Aspire dashboard container** in dev only. Production keeps compose untouched
and OTel off by default (a VPS has no budget for a telemetry stack at demo scale).

Design decision that shapes everything: **instrumentation is unconditional, export is
gated.** `System.Diagnostics.Metrics` instruments are no-ops without a listener, so the
Ingestion meters always record at effectively zero cost, while the OTLP exporter is
registered only when `OpenTelemetry:Enabled` is true. This keeps tests exporter-free
(zero-live-calls invariant) and prod behavior byte-identical unless explicitly enabled.

1. **Ingestion meters (`src/Ingestion/Telemetry/DegerliIngestionMetrics.cs`, new)** — a
   static `Meter` named `Degerli.Ingestion` (namespace `Degerli.Ingestion.Telemetry`,
   separate from the `Metrics` folder, which is the MetricsEngine domain):
   - `Histogram<double> degerli.ingestion.job.duration` (unit `s`), tags `job_code`,
     `outcome` (the `IngestResult.Status`).
   - `Counter<long> degerli.ingestion.job.rows`, tags `job_code`, `disposition`
     (`written` / `unchanged` / `quarantined` / `skipped`).
   - Instrumented in exactly one place: `IngestionJobRunner.RunCoreAsync`
     (`src/Ingestion/Jobs/Jobs.cs`) around the job execution, reading
     `IngestResult(JobCode, Written, Unchanged, Quarantined, Status, Skipped)`. **No
     individual job class is touched** — the runner is the per-job seam every scheduler
     and admin trigger already rides.
2. **OTel composition (`src/Api/Infrastructure/OpenTelemetrySetup.cs`, new)** —
   `AddDegerliOpenTelemetry(this IServiceCollection, IConfiguration)`:
   - Off unless `OpenTelemetry:Enabled` is `true` (default false — no OTel services
     registered at all when off).
   - When on: `AddOpenTelemetry()` with AspNetCore + HttpClient + System.Runtime
     instrumentation, the `Degerli.Ingestion` meter, and `AddOtlpExporter()` pointed at
     `OpenTelemetry:OtlpEndpoint`.
   - **Freshness gauge:** an `ObservableGauge<double> degerli.market.eod_session_lag_seconds`
     whose callback opens a scope, calls the existing
     `EodSessionFreshness.FromDailyPricesAsync(db, calendar, clock, ct)` seam, and
     publishes the lag; the callback must never throw into the exporter (catch, skip).
3. **Wiring:** `src/Api/Program.cs` calls `AddDegerliOpenTelemetry` beside the other
   `Add*` compositions; `src/Api/appsettings.json` gains an `OpenTelemetry` section
   (`Enabled: false`, `OtlpEndpoint: http://localhost:18889`); packages
   (`OpenTelemetry.Extensions.Hosting`, `.Instrumentation.AspNetCore`,
   `.Instrumentation.Runtime`, `.Instrumentation.Http`, `.Exporter.OpenTelemetryProtocol`)
   go in `src/Api/Degerli.Api.csproj` (no central package management in this repo).
4. **Dev dashboard (`compose.dev.yml`)** — new `aspire-dashboard` service under the
   existing `tools` profile (same pattern as `contentpipeline`):
   `mcr.microsoft.com/dotnet/aspire-dashboard:latest`, publishing `127.0.0.1:18888:18888`
   (UI) and listening on 18889 (OTLP, internal). The `x-api-environment` anchor gains
   `OpenTelemetry__Enabled: ${OpenTelemetry__Enabled:-false}` and
   `OpenTelemetry__OtlpEndpoint: ${OpenTelemetry__OtlpEndpoint:-http://aspire-dashboard:18889}`,
   so enabling is one `.env` flip. `compose.prod.yml` is **not** touched.
5. **Runbook note (`deploy/README.md`)** — a short paragraph in §Monitoring: optional
   local telemetry via the dev dashboard; prod stays off by default.
6. **L2 test (`tests/Api.IntegrationTests/IngestionTelemetryTests.cs`, new)** — follows
   the existing patterns (`PriceIngestionTests` and friends: Testcontainers PostgreSQL +
   the FU §10 WireMock source double): run a real job through the runner, attach a
   `MeterListener` on `Degerli.Ingestion`, assert the duration histogram and the rows
   counter fired with the right tags. **No OTLP exporter in tests** (zero live calls).

## Rules & Invariants

- **Zero live calls in tests:** OTel is disabled by default; no test may export over
  OTLP or touch any external endpoint. `MeterListener` is in-process only.
- **No /api/v1 contract change:** response shapes are untouched — the OpenAPI drift gate
  does not apply (no `generate-openapi.sh` run needed).
- **No secrets in telemetry:** connection strings, passwords, API keys never appear in
  tag values; tags are `job_code` / `outcome` / `disposition` only (bounded, non-sensitive).
- **Honest-data contract unaffected:** meters describe ingestion runs, not user-facing
  data claims; no investment-advice vocabulary anywhere.
- **Prod default-off:** `compose.prod.yml` untouched; enabling OTel on the VPS would be
  an explicit `.env` decision, not this slice's default.
- **Minimize collision with in-flight slices:** do NOT touch
  `src/Ingestion/Scheduling/*`, `src/Ingestion/Intraday/*`, `compose.ci.yml`, or the
  L2 tests the other agent holds. `src/Api/appsettings.json` is the one expected overlap
  (rebase if needed). Instrument at the runner seam only.

## Blockers & Dependencies

- None hard. SLICE-011/012 (in flight elsewhere) touch `appsettings.json` and
  scheduler/intraday files; a rebase on `dev` may be required before merge.

## Verification

- `dotnet build Degerli.sln` — compiles clean.
- `dotnet test tests/Api.IntegrationTests --filter IngestionTelemetry` — the new L2 test
  green (Testcontainers; run from the repo root with Docker up).
- `docker compose -f compose.dev.yml config -q` — compose change validates.
- Smoke (optional, if Docker running): `docker compose -f compose.dev.yml --profile tools
  up -d aspire-dashboard`, start the API with `OpenTelemetry__Enabled=true`, confirm the
  dashboard at `http://localhost:18888` shows the `Degerli.Ingestion` metrics; tear down.
- `git diff --stat` shows only the scoped files.

## Result

Done (2026-11-05). PR: https://github.com/sarperim/degerli/pull/185

- **Meters** — `src/Ingestion/Telemetry/DegerliIngestionMetrics.cs`: static `Meter`
  `Degerli.Ingestion` with `degerli.ingestion.job.duration` (histogram, unit `s`, tags
  `job_code`/`outcome`) and `degerli.ingestion.job.rows` (counter, tags
  `job_code`/`disposition`). Recorded in exactly one place —
  `IngestionJobRunner.RunCoreAsync` — via `RecordMetrics`; only non-zero dispositions are
  reported and a thrown run records `outcome=failed`. No job class was touched.
- **Composition** — `src/Api/Infrastructure/OpenTelemetrySetup.cs`:
  `AddDegerliOpenTelemetry(IConfiguration)` returns the collection untouched unless
  `OpenTelemetry:Enabled` is true; when on it adds AspNetCore + HttpClient + Runtime
  instrumentation, the `Degerli.Ingestion` meter, the OTLP exporter at
  `OpenTelemetry:OtlpEndpoint`, and `EodSessionLagGaugeHostedService`, which owns the
  `degerli.market.eod_session_lag_seconds` gauge over
  `EodSessionFreshness.FromDailyPricesAsync` (failures skipped, never thrown into the
  exporter). Wired in `Program.cs`; `appsettings.json` gains the section (`Enabled: false`,
  `OtlpEndpoint: http://localhost:18889`); five OTel packages added to the API csproj
  (matching the existing per-package `Version` style).
- **Dev dashboard** — `compose.dev.yml` gains `aspire-dashboard` under the `tools` profile
  (UI published loopback on 18888, OTLP 18889 internal) and the `x-api-environment` anchor
  gains the two `OpenTelemetry__*` passthroughs defaulting to off. `compose.prod.yml`
  untouched. `deploy/README.md` §Monitoring documents the opt-in dev flow.
- **Tests** — `tests/Api.IntegrationTests/IngestionTelemetryTests.cs`, 4 tests: duration +
  written rows for a real `prices` run, `unchanged` on an idempotent re-run (and no
  `written` measurement at all), `failed` outcome on a source-down run, and the
  config gating (off registers no OTel descriptor, on does). `MeterListener` only — no
  OTLP exporter in any test.

Verification: `dotnet build Degerli.sln` clean (0 warnings, 0 errors); `dotnet test
tests/Api.IntegrationTests --filter IngestionTelemetry` 4/4 passed (red-checked: with the
runner's `RecordMetrics` call removed the first test fails with "The collection was
empty"); `PriceIngestionTests|HealthEndpointTests` 6/6 passed; `docker compose -f
compose.dev.yml config -q` OK; end-to-end smoke through the dev compose network with
`OpenTelemetry__Enabled=true` — the Aspire dashboard reported the `Degerli.Api` resource
with `hasMetrics: true`.

