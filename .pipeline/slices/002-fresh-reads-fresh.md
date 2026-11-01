---
id: SLICE-002
status: done
scope:
  - .pipeline/slices/002-fresh-reads-fresh.md
  - src/Api/Infrastructure/EodSessionFreshness.cs
  - src/Api/Stocks/StockListEndpoints.cs
  - src/Api/Stocks/StockOverviewAndValuationEndpoints.cs
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Api/Screener/ScreenerEndpoints.cs
  - src/Api/appsettings.json
  - .pipeline/architecture/01-system-architecture.md
  - tests/Api.IntegrationTests/EodFreshnessSemanticsTests.cs
  - tests/Api.IntegrationTests/IntradayQuotesTests.cs
---

# SLICE-002: Fresh Reads Fresh — Honest EOD Freshness on the Stock/Screener Surfaces + an Intraday KAP Cadence

## Context & Goal

User complaint (2026-10-31, verbatim intent): *"it updates data too infrequently … I always see something is stale, that is for everything, but also kap reports have this especially worse."* Investigation found **two distinct root causes** that both render as "stale" in the UI — one is a semantics defect, one is a cadence gap.

**Cause A — the permanent stale badge is a semantics bug, not missing updates.** Four endpoint families compute freshness with the naive weekday rule `asOf < LatestTradingDayOnOrBefore(today)` (weekday-backing only, no trading calendar, no notion of a *completed* session):

- `src/Api/Stocks/StockListEndpoints.cs` (`FreshnessAsync`, ~line 128)
- `src/Api/Stocks/StockOverviewAndValuationEndpoints.cs` (`FreshnessAsync` ~line 241 + the inline copy in `ValuationAsync` ~line 152)
- `src/Api/Stocks/StockFundamentalsEndpoints.cs` (`FreshnessAsync`, ~line 1023)
- `src/Api/Screener/ScreenerEndpoints.cs` (`EvaluateAsync`, ~line 314)

BIST price data is EOD: the freshest *completed* session during a trading Tuesday is **Monday's close** (Tuesday's session closes 18:00 TRT and its EOD chain lands ~20:30). But the weekday rule compares against *today*, so these four surfaces serve `stale: true` from midnight until ~20:45 **every trading day**, even when ingestion is perfectly healthy. The Web renders that flag as a `StaleBadge` (`src/Web/src/components/honest-data/markers.tsx`) — hence "always something is stale". The market overview already does this correctly (TC-MOV-032 / TKT-foundation-037, `01` §10.9.13 stored-prices-as-truth): a row is stale only when the store proves a newer completed session exists. The `01` v1.5.44 row explicitly flags the `StockOverviewAndValuationEndpoints` copy as a known follow-up; this slice discharges that flag for all four families. The rule is also calendar-blind in these four (a holiday Monday reads stale on Tuesday — the pre-048 posture).

**Cause B — the KAP lanes run only twice a day.** `disclosures`, `statements`, `dividends` and `corporate-actions` run only inside the 20:30 EOD chain and the 08:30 morning chain. A KAP disclosure or financial report published at 09:30 waits up to ~10.5 hours before it is servable. The `intraday-quotes` lane got its own `JobSchedules` cadence (v1.5.27); the KAP lanes never did. Meanwhile both public-site lanes are **cursor-incremental** (date-watermark cursors, `KapPublicDisclosuresCursor` / `KapPublicStatementCursor`), and the disclosures market feed makes **no per-disclosure request at all** (the byCriteria row *is* the metadata) — an idle pass costs a few paced (3 s) list calls, so extra cadence is nearly free.

**Goal:** (A) unify the four stock/screener surfaces to the honest completed-session freshness rule, calendar-aware, with no envelope-shape change; (B) give the cheap cursor-incremental KAP lanes (`disclosures`, `statements`) an intraday cadence through the existing per-job `JobSchedules` stream; (C) bump the `intraday-quotes` sweep from four-a-day to hourly (the user's own v1.5.27 requirement allowed "hour or 2 hour periods"; hourly is now asked for).

## Rules & Invariants

1. **Envelope shape unchanged.** Every touched response keeps its `asOf`/`stale` keys, types and HTTP shape (`03` §1.2 is a presence contract, not a semantics pin). Only the boolean's *meaning* changes. The Web (`StaleBadge`, `asOf` display) consumes the flag as-is — no Web file is in scope.
2. **The rule (A).** `stale = asOf < calendar.LatestTradingDayOnOrBefore(today − 1 day)` — the newest session that **should have completed** by now. Consequences, all pinned by tests: Monday's close is fresh all Tuesday (Tuesday's EOD lands 20:30); a genuinely missed Monday EOD surfaces `stale: true` from Tuesday until the morning chain repairs it (the missed-run signal survives, only honest-late); a weekend or configured holiday serves the last completed session fresh (calendar-aware, closing the v1.5.44 flag); an empty store keeps `asOf = today`, `stale = false` (the pre-existing unavailable-world posture of these surfaces). The rule lives in **one** shared helper (`EodSessionFreshness`, `src/Api/Infrastructure/`) — the four private `FreshnessAsync` copies delegate to it; no fifth weekday-only copy may be born. `IIngestionCalendar` is injected into the endpoint handlers the same way `MarketEndpoints` already receives it (registered singleton, `IngestionServiceCollectionExtensions` line 84).
3. **As-of sources unchanged per surface.** The three price-backed surfaces keep `max(daily_prices.price_date)`; the screener and the valuation section keep `max(derived_metrics.as_of_date)` (metrics recompute rides prices in both chains). No new store read is introduced.
4. **Cadence changes are configuration only (B, C).** Two new `Ingestion:Scheduler:JobSchedules` entries — `disclosures` and `statements`, cron `0 0 9-19/2 * * *` (09:00/11:00/13:00/15:00/17:00/19:00 Europe/Istanbul), `TradingDaysOnly: true` — and the `intraday-quotes` cron `0 15 11,13,15,17 * * 1-5` → `0 15 10-17 * * 1-5` (10:15…17:15, eight sweeps). No scheduler, runner, job, pacer or walk code changes; the retry ladder, hung-run watchdog, run ledger and `holiday-skip` rows all apply to the new streams automatically (per-job streams are the v1.5.27/v1.5.44 machinery).
5. **Politeness posture unchanged.** No pacing knob, UA, quota-backoff or TLS-shape value is touched (v1.5.77's access posture stands). The disclosures market feed spends zero detail/export calls; a statements pass spends exports only on genuinely new reports (cursor watermark), and the per-IP 429 export quota (v1.5.77) remains the authoritative bound — an intraday statements pass can never "catch up harder" than the EOD chain already may.
6. **Honest limits, recorded not papered over.** (i) The KAP sweeps are trading-day gated, so a weekend KAP filing surfaces Monday 08:30 via the morning chain — a bounded, recorded delay, not a silent gap. (ii) The freshness clock keeps the codebase's existing UTC-day convention (`DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)`); the ~3 h TRT/UTC midnight skew is the pre-existing posture of every surface and is not widened here. (iii) Dividends and corporate-actions stay twice-daily by design: the corporate-actions walk is 58–74 min of paced detail fetches (the 041 ceiling rationale) and its events surface as disclosures within ~2 h anyway through the disclosures feed.
7. **Concurrent-stream note (recorded risk).** Per-job streams and the chain streams are separate scheduler streams, so a 19:00 sweep still inside its `MaxWalkDuration` (90 min) can overlap the 20:30 EOD chain's same job. The lanes are idempotent and cursor-transactional (the v1.5.84 cursor/fact transaction-window doctrine), so overlap at worst duplicates a few paced fetches; a scheduler-wide same-job mutex is **out of scope** (routed as a follow-up if the ledger ever shows it).
8. **Tests flip with dated justification, never relaxed.** `TC_MDF_084_Intraday_cadence_and_source_are_shipped_config_constants` re-pins the four-sweep cron to the hourly one (the case's own doc comment states the supersession, the 048 amendment precedent). New `EodFreshnessSemanticsTests` cases assert red on the old rule where the clock/world makes it possible (Tuesday + Monday prices is exactly the reported defect).

## Blockers & Dependencies

- **None blocking.** All merged precedents this rides are `done`: TC-MOV-032 (037, stored-prices freshness on the overview), the holiday calendar (048), the per-job streams (v1.5.27/048), the KAP public-site lanes (079–084, 100), the cursor transaction window (069/v1.5.84).
- **Doc contract:** no `03` §1.2 break (presence contract only). The stale-rule wording is doctrine in `01` §10.9.13's lane — a dated amendment row is recorded at implementation time under **§13.1 reservation v1.5.88** (next free above the landed v1.5.87).
- **Call-budget note (C):** the hourly sweep roughly doubles İş Yatırım paced calls (~400 → ~800/day at XU100; NFR-MDF-001's recorded ≈400/day budget is superseded by the dated row). Config-reversible in one line.
- **Source-selection note (B):** cadence entries run whatever `Ingestion:Disclosures:Source` / `Ingestion:Statements:Source` selects. The shipped default is `vyk` (API-key gated; on a keyless host the sweeps fail-named on the retry ladder exactly like the existing chains already do — no new posture). The live-capable configuration is `kap-public` (the 082/084 lanes this slice's cadence is really for).
- **Serialization risk:** the four endpoint files are also touched by nobody in-flight (post-102 pipeline is closed; the slice list holds only SLICE-001, `done`).

## Verification

- [x] New `tests/Api.IntegrationTests/EodFreshnessSemanticsTests.cs`: (a) Tuesday clock + Monday stored prices → `stale: false` on `/api/v1/stocks` (red on the old weekday rule — the reported defect); (b) configured holiday Monday + Friday stored prices → fresh on the Tuesday clock; (c) missed Monday session (Friday stored) on the Tuesday clock → `stale: true` (missed-run signal survives); (d) Saturday clock + the preceding Friday → fresh (rule-level); (e) the screener carries the same rule over real `metrics-recompute` Monday rows, `/stocks/{symbol}/overview` likewise; (f) an empty store keeps `stale: false` (unavailable posture). **9/9 green.**
- [x] **Red→green, measured:** with `EodSessionFreshness.AgainstCompletedSession` temporarily restored to the pre-slice weekday rule, the new suite reads **6 failed / 3 passed** (the three passes being the cases the old rule already answered correctly: missed-session-stale, empty-store-fresh, the config-constants case); **9/9 green** on the shipped rule. The probe was removed after the measurement.
- [x] `IntradayQuotesTests` re-pinned: the shipped cron constant case asserts the hourly `0 15 10-17 * * 1-5` with the supersession stated in its doc comment (the 048 amendment precedent); the new suite's config-constants case asserts both KAP `JobSchedules` entries parse to `0 0 9-19/2 * * *` with `TradingDaysOnly: true` (read from the shipped `appsettings.json`).
- [x] Non-regression, unrelaxed — **158/158 green** across: `StockListEndpointsTests`, `StockFundamentalsEndpointsTests`, `StockOverviewAndValuationEndpointsTests`, `ScreenerRunEndpointTests`, `ScreenerMetricVisibilityTests`, `IntradayQuotesTests`, `MarketOverviewFreshnessTests`, `MarketEndpointsTests`, `SchedulerTests`, `EodChainTests`, `MorningChainTests`, `TradingCalendarTests`, `EodStaleServingTests`, `SavedScreenEndpointTests`, `Xc018OpenApiTests`, `SchemaConformanceTests`, `DataEnvelopeTests`, `StockKeyMetricsEndpointsTests`, `DcfBaselineEndpointTests`, plus the new suite.
- [x] `dotnet build` 0 warnings / 0 errors (full solution).
- [x] Dated `01` §13.1 **v1.5.88** amendment row recorded (freshness semantics + cadence + the restated ≈800/day call budget).

## Result

- **Freshness (A):** the four weekday-rule copies are gone; one shared `EodSessionFreshness` (`src/Api/Infrastructure/EodSessionFreshness.cs`) serves `StockListEndpoints` (list + typeahead), `StockOverviewAndValuationEndpoints` (overview + the valuation section's inline copy), `StockFundamentalsEndpoints` (all five sections) and `ScreenerEndpoints` (the run envelope), each through the DI-injected `IIngestionCalendar`. During a trading day the surfaces now serve yesterday's completed close **fresh**; the stale badge appears only when a session that should have completed is actually missing from the store, and weekends/holidays read fresh through the calendar (the v1.5.44 flag discharged).
- **KAP cadence (B):** `disclosures` and `statements` sweep every two hours 09:00–19:00 Europe/Istanbul on trading days (shipped `JobSchedules` entries) — a mid-day KAP filing is servable within ~2h instead of up to ~10.5h. Idle sweeps cost a few paced `byCriteria` list calls; no pacing/quota/TLS value changed.
- **Intraday sweep (C):** `intraday-quotes` re-pinned to hourly (10:15…17:15 TRT), ≈800 paced İş Yatırım calls/day at XU100 (recorded in `01` v1.5.88; one-line config to revert).
- Working-tree changes are complete and locally verified but **uncommitted** — commit/PR left to the user's direction.
- Routed follow-ups (recorded, not in scope): a scheduler-wide same-job mutex if the ledger ever shows a 19:00-sweep/20:30-chain overlap; weekend KAP filings surface Monday 08:30 (the trading-day gate's recorded limit).
