---
id: SLICE-004
status: done
scope:
  - .pipeline/slices/004-remaining-eod-freshness-surfaces.md
  - AGENTS.md
  - src/Api/Stocks/StockKeyMetricsEndpoints.cs
  - src/Api/Valuation/DcfBaselineEndpoints.cs
  - src/Api/Valuation/DcfComputeEndpoints.cs
  - tests/Api.IntegrationTests/EodFreshnessSemanticsTests.cs
---

# SLICE-004: Unify Remaining Stock Key-Metrics and DCF Surfaces onto `EodSessionFreshness`

## Context & Goal

In `SLICE-002`, four endpoint families (`StockListEndpoints`, `StockOverviewAndValuationEndpoints`, `StockFundamentalsEndpoints`, and `ScreenerEndpoints`) were unified onto `EodSessionFreshness` (`src/Api/Infrastructure/EodSessionFreshness.cs`). However, review finding `DR-3` on `SLICE-002` noted that three remaining surfaces still carry private weekday-only `LatestTradingDayOnOrBefore(today)` helpers:

1. `src/Api/Stocks/StockKeyMetricsEndpoints.cs` (`GET /api/v1/stocks/{symbol}/key-metrics`)
2. `src/Api/Valuation/DcfBaselineEndpoints.cs` (`GET /api/v1/stocks/{symbol}/dcf`)
3. `src/Api/Valuation/DcfComputeEndpoints.cs` (`POST /api/v1/stocks/{symbol}/dcf/compute`)

Because those three surfaces compare `asOf` / `price.PriceDate` against `today` using a calendar-blind weekday loop, they still serve `stale: true` all day from 00:00 until the evening EOD chain finishes, and they fail to recognize configured BIST market holidays.

**Goal:** Delegate all three surfaces to `EodSessionFreshness` (`FromDerivedMetricsAsync` for `StockKeyMetricsEndpoints` and `AgainstCompletedSession` for `DcfBaselineEndpoints` and `DcfComputeEndpoints`), remove their duplicate `LatestTradingDayOnOrBefore` helpers, and pin all three surfaces in `EodFreshnessSemanticsTests`. Also land the expanded `AGENTS.md` repository guide.

## Rules & Invariants

1. **Zero private weekday-only copies remain in `src/Api`.** `StockKeyMetricsEndpoints`, `DcfBaselineEndpoints`, and `DcfComputeEndpoints` must inject `IIngestionCalendar` and delegate to `EodSessionFreshness`; their private `LatestTradingDayOnOrBefore` methods are removed.
2. **Envelope shapes unchanged.** `GET /api/v1/stocks/{symbol}/key-metrics` keeps top-level `asOf` and `stale`; `GET /api/v1/stocks/{symbol}/dcf` and `POST /api/v1/stocks/{symbol}/dcf/compute` keep `price.asOf` and `price.stale`.
3. **As-of sources unchanged.** `StockKeyMetricsEndpoints` continues reading `max(derived_metrics.as_of_date)` via `EodSessionFreshness.FromDerivedMetricsAsync`; `DcfBaselineEndpoints` and `DcfComputeEndpoints` continue using the instrument's latest `daily_prices.price_date` via `EodSessionFreshness.AgainstCompletedSession`.

## Blockers & Dependencies

- Depends on `SLICE-002` (`EodSessionFreshness`, `done`).

## Verification

- [x] `grep -rn "LatestTradingDayOnOrBefore" src/Api/` matches only `src/Api/Infrastructure/EodSessionFreshness.cs`.
- [x] `EodFreshnessSemanticsTests` covers `GET /api/v1/stocks/{symbol}/key-metrics`, `GET /api/v1/stocks/{symbol}/dcf`, and `POST /api/v1/stocks/{symbol}/dcf/compute` (Monday data fresh at 18:00 UTC Tuesday, stale past the 19:45 UTC cutoff, and holiday-aware).
- [x] `dotnet test tests/Api.IntegrationTests --filter "FullyQualifiedName~EodFreshnessSemanticsTests|FullyQualifiedName~StockKeyMetricsEndpointsTests|FullyQualifiedName~DcfBaselineEndpointTests|FullyQualifiedName~DcfComputeEndpointTests"` passes.

## Result

- Unified `StockKeyMetricsEndpoints`, `DcfBaselineEndpoints`, and `DcfComputeEndpoints` onto `EodSessionFreshness` by injecting `IIngestionCalendar` and removing all duplicate private `LatestTradingDayOnOrBefore` methods.
- Verified that `LatestTradingDayOnOrBefore` in `src/Api/` exists solely in `src/Api/Infrastructure/EodSessionFreshness.cs`.
- Extended `EodFreshnessSemanticsTests` with five new integration tests covering mid-chain freshness, post-cutoff staleness, and holiday-awareness across the three surfaces.
- Targeted integration test suite passed cleanly (46/46 passed).
