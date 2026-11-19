---
id: SLICE-013
status: done
scope:
  - src/Ingestion/Universe/UniverseSourceAdapter.cs
  - src/Ingestion/Metrics/MarketSnapshotEngine.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/UniverseRealEnvelopeTests.cs
  - tests/Api.IntegrationTests/PriceDerivedShareCountsTests.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
---

# SLICE-013 — Market Overview Indices, Valuation/Sector Aggregates (`PriceDerivedShareCounts` Fallback), and Startup Macro Refresh

## Context & Goal

Three data gaps were observed on the live Market Overview (`SCR-001`, `GET /api/v1/market/overview`) and Macro (`GET /api/v1/market/macro`) surfaces after container startup on `2026-11-04`:

1. **Indices (`XU100`, `XU030`) unavailable (`value: null`, `dailyChangePct: null`):**
   - `IndexLevelsSourceAdapter.ResolveIndexLevelsPath` (`src/Ingestion/Universe/UniverseSourceAdapter.cs`) formats `{to}` as `date.ToString("yyyyMMdd") + "235959"`.
   - İş Yatırım's `ChartData.aspx/IndexHistoricalAll` treats the `to` date parameter as an **exclusive upper bound** on the trading date (returning bars strictly before `to`'s date). Passing `to=20261104235959` returns bars only up to `2026-11-03`, omitting `2026-11-04`.
   - Advancing the `{to}` bound by one calendar day (`date.AddDays(1).ToString("yyyyMMdd") + "235959"`) includes `date`'s bar while `UniverseSyncJob` still filters/stores levels up to the run's target date.

2. **Sector Performance (`{}`), Market Valuation (`marketPe: null`, `marketDivYield: null`), and Per-Stock Valuation Multiples (`pe`, `pb`, `ev_ebitda`, `p_fcf` all `null`):**
   - Real KAP IFRS balance sheets (`BS`) in `fin_line_items` do not report `SHARES_DILUTED` (`0` rows across the real universe).
   - `TKT-foundation-086` (`01` v1.5.60 §10.9.11, USER DECISION D5) established the two-tier share-count precedence: statement-reported `SHARES_DILUTED` first, falling back to `PriceDerivedShareCounts.SharesOutstanding` (`PD ÷ close`).
   - While `DcfBaselineGenerationJob` and `StockFundamentalsEndpoints` wired this second-tier fallback, `MarketSnapshotEngine` and `MetricsEngine` still read `SHARES_DILUTED` exclusively from `fin_line_items`. Without a share count, `marketCap` is `null` for every real stock in `MarketSnapshotEngine` (making cap-weighted `sectorPerformance`, `marketPe`, and `marketDivYield` empty/null) and `SharesDiluted` is `null` in `MetricsEngine` (making `pe`, `pb`, `p_fcf`, `p_tbv`, `ev_ebit`, `ev_ebitda`, `ev_fcf`, `fcf_yield`, and per-share CAGRs `null`).
   - Wire `_db.PriceDerivedShareCounts` (`SharesOutstanding > 0`) as the second-tier fallback when `SHARES_DILUTED` is absent in both `MarketSnapshotEngine.ComputeAsync` and `MetricsEngine.ComputeAsync`.

3. **Macro indicators (`USD_TRY`, `EUR_TRY`, `GOLD`) stale on container startup:**
   - `macro-daily` (`0 0 8 * * *`) was not configured with `"RunOnStartup": true` in `src/Api/appsettings.json`, so starting Docker after 08:00 left daily macro series stale until the next morning.
   - Add `"RunOnStartup": true` to the `macro-daily` schedule entry in `src/Api/appsettings.json`.

## Rules & Invariants

- **Two-tier share-count precedence (`01` v1.5.60 §10.9.11, USER DECISION D4 & D5):** Statement-reported `SHARES_DILUTED` (`> 0`) always wins when present (preserving 100% of `FixtureUniverse` golden outputs). When absent, fall back to `PriceDerivedShareCounts.SharesOutstanding` (`> 0`). Never use `share_counts.paid_in_capital` (a TRY money figure, not a share count) as a share count.
- **Honest-data contract:** Instruments with neither `SHARES_DILUTED` nor a valid `PriceDerivedShareCount` continue to produce `null` share-dependent metrics/weights without fabrication.
- **Zero live calls in tests:** All L2 integration tests must run against `FixtureUniverse` / `RealSourceCatalog` / WireMock doubles.

## Blockers & Dependencies

- Depends on `SLICE-012` (`RunOnStartup` support in `IngestionSchedulerOptions` and `IngestionScheduler.RunStartupAsync`).

## Verification

- [x] Targeted L2 integration tests:
  - `dotnet test tests/Api.IntegrationTests --filter "FullyQualifiedName~UniverseRealEnvelopeTests|FullyQualifiedName~PriceDerivedShareCountsTests|FullyQualifiedName~SchedulerTests"`
- [x] Live Docker verification on `degerli-dev-api-1` confirming `/api/v1/market/overview` serves `XU100` & `XU030` index levels, non-empty `sectorPerformance`, and populated `marketPe`, and `/api/v1/market/macro` refreshes `USD_TRY`, `EUR_TRY`, and `GOLD`.

## Result

- **Index Levels `{to}` Bound Resolution:** Updated `IndexLevelsSourceAdapter.ResolveIndexLevelsPath` in `src/Ingestion/Universe/UniverseSourceAdapter.cs` to format `{to}` with `date.AddDays(1)` to satisfy İş Yatırım's exclusive date-bound semantics on `ChartData.aspx/IndexHistoricalAll`. Added unit/integration test `SLICE_013_Index_levels_adapter_resolves_templated_to_bound_to_next_calendar_day` in `tests/Api.IntegrationTests/UniverseRealEnvelopeTests.cs`.
- **MarketSnapshotEngine Share Count Fallback:** Updated `MarketSnapshotEngine.ComputeAsync` to query `PriceDerivedShareCounts` for covered instruments and fall back to `PriceDerivedShareCounts.SharesOutstanding` (`> 0`) when `SHARES_DILUTED` is null or `<= 0m`. This enables cap-weighted `SectorPerformance` and `MarketPe` calculation for real-market constituents without statement-reported share counts.
- **MetricsEngine Share Count Fallback:** Updated `MetricsEngine.ComputeAsync` and `BuildInput` to load `PriceDerivedShareCounts` for target instruments and fall back to `PriceDerivedShareCounts.SharesOutstanding` (`> 0`) for per-share historical metrics and `CanonicalMetricsInput.SharesDiluted`. Added integration test `SLICE_013_PriceDerivedShareCounts_fallback_populates_metrics_and_market_snapshot` in `tests/Api.IntegrationTests/PriceDerivedShareCountsTests.cs`.
- **Startup Macro Refresh:** Configured `"RunOnStartup": true` for `macro-daily` in `src/Api/appsettings.json`.
- **Test Suite Results:** Passed all 34 matching tests across `UniverseRealEnvelopeTests`, `PriceDerivedShareCountsTests`, and `SchedulerTests`.
