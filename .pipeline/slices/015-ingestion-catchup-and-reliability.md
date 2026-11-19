---
id: SLICE-015
status: done
scope:
  - .pipeline/slices/015-ingestion-catchup-and-reliability.md
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - src/Ingestion/IngestionServiceCollectionExtensions.cs
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - tests/Api.IntegrationTests/KapPublicSourceTests.cs
---

# SLICE-015 — Ingestion Catch-Up & Scheduler Reliability (Coalesce Missed Cron Bursts, KAP `MaxListPages`, Startup Identity/CPI, EVDS Timeout)

## Context & Goal

Live logs (`degerli-20261105.log`) and code inspection exposed four ingestion reliability defects that leave data stale or incomplete:

1. **Missed-cron burst bug in `IngestionScheduler.StartDueAttempts`:**
   - When `stream.NextScheduled <= now` (e.g., after container pause/sleep or a long-running tick across multiple cron intervals), `StartDueAttempts` stepped `NextScheduled` forward by only **one** occurrence and `break`ed, leaving `NextScheduled` still in the past. On the next tick, every missed occurrence fired back-to-back (observed in `degerli-20261105.log`: `intraday-quotes` fired 6 times in 11 seconds at `12:48Z`).
   - **Fix:** Consume all occurrences `<= now` in `StartDueAttempts`, advancing `stream.NextScheduled` strictly past `now` while coalescing to a single `ActiveRun` for the **latest** eligible occurrence (still recording any holiday skips encountered).

2. **`Startup` EOD catch-up ignores missing `market_snapshots`:**
   - In `IngestionScheduler.RunStartupAsync`, `CatchUpPricesOnStartup` skipped `startup-catchup` whenever `daily_prices` was complete for `latestCompletedEodDate`, even if `market_snapshots` was missing for `latestCompletedEodDate` (e.g., when a prior EOD chain stopped after `prices`).
   - **Fix:** Also check whether `market_snapshots` has a row for `latestCompletedEodDate` (via `DegerliDbContext`); if either `daily_prices` coverage is incomplete **or** `market_snapshots` is missing for `latestCompletedEodDate`, arm `startup-catchup`.

3. **KAP `MaxListPages` starvation & missing startup refresh for `kap-public-identity` and `macro-cpi`:**
   - `Ingestion:KapPublic:MaxListPages` was `"40"` across 100 XU100 stocks, guaranteeing at least 60 companies hit `WALK_PAGE_CAP` on every steady-state run (`not-walked: 86` on Nov 5).
   - `kap-public-identity` and `macro-cpi` lacked `"RunOnStartup": true` in `appsettings.json`, causing `COMPANY_IDENTITY_UNRESOLVED` (~70/100 symbols) and late CPI/policy-rate series after restart.
   - **Fix:** Raise `Ingestion:KapPublic:MaxListPages` from `"40"` to `"120"` in `src/Api/appsettings.json`, and add `"RunOnStartup": true` to `kap-public-identity` and `macro-cpi` in `Ingestion:Scheduler:JobSchedules`. (Note: keep `Statements:UniverseIndex` and `Disclosures:UniverseIndex` checked against any existing config-constant tests before altering.)

4. **EVDS Macro `HttpClient` 30s timeout:**
   - `MacroSourceAdapter` issues 7–8 sequential EVDS requests per run under a 30s `HttpClient.Timeout`, timing out in live runs (`degerli-20261105.log`).
   - **Fix:** Raise the `Macro` `HttpClient` timeout in `src/Ingestion/IngestionServiceCollectionExtensions.cs` from 30s to 90s.

## Rules & Invariants

- **Zero live calls in tests:** All new tests use in-memory/WireMock/Testcontainers doubles.
- **No behavioral regression for normal on-time cron ticks:** When only a single occurrence is due (`occurrence <= now` once), `StartDueAttempts` behaves identically to before.
- **Keep existing config-constant tests green:** Verify `KapPublic*Tests`, `EodFreshnessSemanticsTests`, `IntradayQuotesTests`, and `SchedulerTests` expectations before editing config keys.

## Blockers & Dependencies

- Depends on `SLICE-014` (`done`).

## Verification

- [ ] `dotnet build Degerli.sln --no-restore` succeeds with 0 warnings / 0 errors.
- [ ] `dotnet test tests/Core.UnitTests/Degerli.Core.UnitTests.csproj --no-build` passes.
- [ ] New test in `SchedulerTests.cs` verifies missed-cron coalescing (advancing clock across multiple missed hourly ticks fires once, not N times).
