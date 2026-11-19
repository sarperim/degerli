---
id: SLICE-012
status: done
scope:
  - .pipeline/slices/012-startup-data-refresh.md
  - src/Ingestion/Scheduling/IngestionSchedulerOptions.cs
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - src/Ingestion/Scheduling/IngestionSchedulerHostedService.cs
  - src/Api/appsettings.json
  - compose.ci.yml
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - tests/Api.IntegrationTests/IntradayQuotesTests.cs
---

# SLICE-012: Startup Data Refresh & Missing-Session Price Catch-Up on Container Boot

## Context & Goal

Previously, `IngestionSchedulerHostedService.ExecuteAsync` logged `"Ingestion scheduler started."` and immediately slept until the next cron boundary (`NextWake(now)`). When the Docker stack (`compose.dev.yml` or `compose.prod.yml`) was stopped overnight or during the day and later started back up:
1. No intraday quote sweep ran until the next `:15` hourly boundary during `10:15–18:15` (or the next trading day if opened after `18:15`).
2. If the container was off during the `20:30` EOD chain and/or `08:30` morning chain, the latest completed trading day's `daily_prices`, `index_levels` (`XU100`), `derived_metrics`, `dcf_baselines`, `market_snapshots`, and `sector_medians` remained missing until the next scheduled chain.

**Goal:**
1. Add `RunStartupAsync(DateTimeOffset now, CancellationToken cancellationToken = default)` to `IngestionScheduler` and invoke it once at the start of `IngestionSchedulerHostedService.ExecuteAsync` before entering the scheduled wait loop.
2. Support per-job `RunOnStartup` (`JobScheduleOptions.RunOnStartup`, enabled on `intraday-quotes` in `src/Api/appsettings.json`) so that starting Docker immediately performs the 1-request bulk `GetHissePerformans` intraday sweep (< 1 second), targeting the active BIST session (`localDate` when `IsTradingDay(localDate)` and `localTime >= 10:00` Europe/Istanbul; otherwise `ResolvePreviousTradingDay(localDate)` so opening Docker before `10:00` or on a weekend/holiday attributes quotes to the last active session rather than an unopened session).
3. Support `CatchUpPricesOnStartup` (`IngestionSchedulerOptions.CatchUpPricesOnStartup`, enabled in `src/Api/appsettings.json`) so that if the latest completed EOD session (`localDate` when `IsTradingDay(localDate)` and `localTime >= 20:30` Europe/Istanbul; otherwise `ResolvePreviousTradingDay(localDate)`) does not have complete `daily_prices` coverage (`!DailyPriceStore.GetDayCoverageAsync(indexCode, latestCompletedEodDate).IsComplete`), the scheduler automatically arms a `startup-catchup` stream (`universe-sync` → `prices` → `metrics-recompute` → `dcf-baselines` → `market-snapshot` → `medians`) concurrently with `intraday-quotes`. When `coverage.IsComplete` is already true, `startup-catchup` is skipped at zero source cost.
4. Keep `compose.ci.yml` deterministic and offline (`Ingestion__Scheduler__Enabled: "false"`) so the L4 Playwright suite never runs background live scheduler sweeps against the seeded `FixtureUniverse`.

## Rules & Invariants

1. **Zero behavioral drift for default in-memory options:** `IngestionSchedulerOptions.CatchUpPricesOnStartup` and `JobScheduleOptions.RunOnStartup` both default to `false` in C#, preserving byte-for-byte compatibility with all existing `SchedulerTests` and `MorningChainTests`.
2. **Session-aware startup date resolution in `Europe/Istanbul`:**
   - For `TradingDaysOnly` per-job streams with `RunOnStartup = true` (`intraday-quotes`):
     - If `_calendar.IsTradingDay(localDate)` and `localTime >= new TimeOnly(10, 0)`, target `localDate`.
     - Otherwise (before 10:00 Istanbul time on a trading day, or on a weekend/holiday), target `ResolvePreviousTradingDay(localDate)`.
   - For `CatchUpPricesOnStartup` (`startup-catchup` stream):
     - If `_calendar.IsTradingDay(localDate)` and `localTime >= new TimeOnly(20, 30)`, target `localDate`.
     - Otherwise, target `ResolvePreviousTradingDay(localDate)`.
3. **Coverage-gated startup EOD catch-up:**
   - Check `DailyPriceStore.GetDayCoverageAsync(indexCode, latestCompletedEodDate, cancellationToken)` before arming `startup-catchup`.
   - If `coverage.IsComplete` is `true`, skip `startup-catchup` completely (0 EOD source requests).
   - If `!coverage.IsComplete`, arm `startup-catchup` (`[UniverseSyncJob.Code, PricesJob.Code, MetricsRecomputeJob.Code, DcfBaselineGenerationJob.Code, MarketSnapshotJob.Code, SectorMediansJob.Code]`) for `latestCompletedEodDate`.
4. **Zero live calls in tests:** Pin the startup date resolution, `RunOnStartup` execution, and `CatchUpPricesOnStartup` coverage gate in `tests/Api.IntegrationTests/SchedulerTests.cs` and config assertions in `tests/Api.IntegrationTests/IntradayQuotesTests.cs`.

## Blockers & Dependencies

- Depends on `SLICE-011` (completed in working tree).

## Verification

- [x] `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~SchedulerTests|FullyQualifiedName~IntradayQuotesTests|FullyQualifiedName~Xc016ReproducibilityTests"` passes.

## Result

- **Scheduler Options (`src/Ingestion/Scheduling/IngestionSchedulerOptions.cs`):**
  - Added `CatchUpPricesOnStartup` (defaults to `false` in code for zero behavioral drift).
  - Added `RunOnStartup` on `JobScheduleOptions` (defaults to `false` in code).
- **Scheduler Engine (`src/Ingestion/Scheduling/IngestionScheduler.cs`):**
  - Added `RunStartupAsync(DateTimeOffset now, CancellationToken cancellationToken = default)` executing startup checks before the regular cron loop.
  - Added `StartupOnly` stream `startup-catchup` (`[universe-sync, prices, metrics-recompute, dcf-baselines, market-snapshot, medians]`) with `GatePricesOnStoredCoverage = true`, which is only armed on startup when `CatchUpPricesOnStartup = true` and `daily_prices` coverage for the latest completed EOD session (`localTime >= 20:30` on trading day, or `PreviousTradingDay`) is incomplete (`!coverage.IsComplete`). When coverage is complete, `startup-catchup` is skipped at zero source cost.
  - Added per-job `RunOnStartup` execution: if a `TradingDaysOnly` stream with `RunOnStartup = true` runs on boot, it targets `localDate` if `localTime >= 10:00` on a trading day, otherwise `ResolvePreviousTradingDay(localDate)`.
  - Added `EnsureScheduled` guard so `StartupOnly` streams never register recurring cron occurrences.
- **Hosted Service Lifecycle (`src/Ingestion/Scheduling/IngestionSchedulerHostedService.cs`):**
  - Invokes `_scheduler.RunStartupAsync(_clock.GetUtcNow(), stoppingToken)` immediately after logging `"Ingestion scheduler started."`, with proper cancellation and error logging.
- **Production Configuration (`src/Api/appsettings.json` & `compose.ci.yml`):**
  - In `src/Api/appsettings.json`: added `"CatchUpPricesOnStartup": true` under `Ingestion:Scheduler`, and added `"RunOnStartup": true` to the `intraday-quotes` job schedule.
  - In `compose.ci.yml`: added `Ingestion__Scheduler__Enabled: "false"` under `x-ci-environment` to guarantee deterministic offline Playwright runs without live background sweeps.
- **Integration Tests (`tests/Api.IntegrationTests/`):**
  - In `IntradayQuotesTests.cs`: verified `scheduler.CatchUpPricesOnStartup` and `intraday.RunOnStartup` configuration constants.
  - In `SchedulerTests.cs`: added `SLICE_012_RunStartupAsync_resolves_session_date_and_gates_eod_catchup_on_coverage` confirming that `RunStartupAsync` at 02:30 TRT on Tuesday targets Monday `2026-10-05` and skips `startup-catchup` when EOD prices are already covered, and at 14:00 TRT targets Tuesday `2026-10-06`.
  - All targeted integration tests (19 in `SchedulerTests`, 4 in `IntradayQuotesTests`, 2 in `Xc016ReproducibilityTests`) passing cleanly.
