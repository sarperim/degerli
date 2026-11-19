---
id: SLICE-014
status: done
scope:
  - .pipeline/slices/014-data-freshness-and-staleness-honesty.md
  - src/Ingestion/Scheduling/EodIngestChain.cs
  - src/Ingestion/Scheduling/IngestionSchedulerOptions.cs
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Api/Market/MarketEndpoints.cs
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - tests/Api.IntegrationTests/TradingCalendarTests.cs
---

# SLICE-014 — Data Freshness & Staleness Honesty (EOD Chain Order, Statement Staleness Decoupling, 2026 Holidays & Disabled Unconfigured Fund Jobs)

## Context & Goal

Live diagnostics (`degerli-20261105.log`) and backend freshness audits identified four root causes behind surfaces showing `stale: true` even when data is new, plus nightly log noise from unconfigured TEFAS fund jobs:

1. **EOD chain runs slow KAP walks before `metrics-recompute` / `market-snapshot` / `medians`:**
   - `EodIngestChain.JobCodes` previously ordered `universe-sync → prices → statements → dividends → corporate-actions → disclosures → metrics-recompute → market-snapshot → medians`.
   - Because `statements` (up to 90 min) and `disclosures` (up to 90 min) ran before `metrics-recompute` and `market-snapshot`:
     - `MarketEndpoints.OverviewAsync` (`snapshot.SnapshotDate < latestStoredTradingDay`) flipped to `stale: true` for 1–3+ hours every evening right after `prices` landed.
     - `DerivedMetrics` landed after `EodSessionFreshness.DefaultEodCompletionCutoff` (`19:45 UTC` / `22:45 TRT`), causing `/valuation`, `/key-metrics`, and `/screener/run` to report `stale: true` even though `daily_prices` was fresh.
   - **Fix:** Reorder `EodIngestChain.JobCodes` to `universe-sync → prices → metrics-recompute → market-snapshot → medians → statements → dividends → corporate-actions → disclosures` so daily price-derived metrics, market overview, and sector medians refresh within minutes of `prices` landing.

2. **`FinancialsAsync` and `BalanceSheetAsync` inherit daily EOD price staleness + tight spring annual cutoff:**
   - In `StockFundamentalsEndpoints.cs`, `FinancialsAsync` (`line 375`) and `BalanceSheetAsync` (`line 536`) OR'd statement freshness with `eodStale` (`FromDailyPricesAsync`), so quarterly/annual financial statements flipped to `stale: true` every night after `19:45 UTC` if EOD prices hadn't landed yet.
   - `MaxFreshAnnualBalanceSheetAgeDays = 450` (~March 26) also falsely marked annual balance sheets stale during Turkish TMS 29 spring filing extensions (April–May).
   - **Fix:** Compute statement `stale` from statement age only (not `eodStale`) when statements exist, and widen `MaxFreshAnnualBalanceSheetAgeDays` from `450` to `500`.

3. **Missing 2026 BIST holidays in `src/Api/appsettings.json`:**
   - Only `"2026-10-29"` was listed for 2026 under `Ingestion:TradingCalendar:Holidays`, causing `EodSessionFreshness` to expect a trading session and flag every surface `stale: true` on unlisted 2026 holidays.
   - **Fix:** Add all 2026 Turkish public/BIST market holidays (`2026-01-01`, `2026-03-20`, `2026-04-23`, `2026-05-01`, `2026-05-19`, `2026-05-27`, `2026-05-28`, `2026-05-29`, `2026-07-15`, `2026-10-29`).

4. **Unconfigured TEFAS fund jobs (`fund-nav`, `fund-performance`, `fund-holdings`) crash 9× every night:**
   - `appsettings.json` schedules `fund-nav`, `fund-performance`, and `fund-holdings` daily at `19:00–19:10 TRT`, even though `Ingestion:Funds:BaseUrl` is intentionally unset.
   - `TradingCalendarTests.cs` asserts `fund-nav` exists in `appsettings.json` `JobSchedules` with `TradingDaysOnly` unset/false.
   - **Fix:** Add `public bool Enabled { get; set; } = true;` to `JobScheduleOptions` (default `true` for zero behavioral drift), skip disabled `JobScheduleOptions` in `IngestionScheduler` stream registration, and set `"Enabled": false` on `fund-nav`, `fund-performance`, and `fund-holdings` in `src/Api/appsettings.json`.

## Rules & Invariants

- **Honest-data contract:** Outdated financial statements (>365 days for `/financials`, >500 days for `/balance-sheet`) still honestly report `stale: true` (preserving SLICE-008's contract).
- **Backward compatibility:** `JobScheduleOptions.Enabled` defaults to `true`; `TradingCalendarTests` continues to find `fund-nav` in `appsettings.json` without `TradingDaysOnly: true`.
- **Zero live calls in tests:** All verification uses offline build and unit/integration tests.

## Blockers & Dependencies

- Builds on working-tree `SLICE-011`, `SLICE-012`, `SLICE-013`.

## Verification

- [ ] `dotnet build Degerli.sln` succeeds with 0 warnings / 0 errors.
- [ ] `dotnet test tests/Core.UnitTests/Degerli.Core.UnitTests.csproj` passes.
- [ ] Integration tests updated/added in `SchedulerTests.cs` / `TradingCalendarTests.cs` compile cleanly (Docker/Testcontainers unavailable in local WSL2; CI will run L2).
