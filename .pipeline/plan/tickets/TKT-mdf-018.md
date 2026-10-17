---
scope:
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Core/Metrics/MetricCodes.cs
  - src/Core/Metrics/MetricMath.cs
  - src/Core/Metrics/CanonicalMetrics.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Ingestion/Metrics/DerivedMetricStore.cs
  - src/Api/Screener/
  - src/Api/Stocks/
  - tests/Api.IntegrationTests/RegimeBreakTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
  - tests/Api.IntegrationTests/ScreenerRunEndpointTests.cs
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
  - fixtures/Degerli.Fixtures/FixtureUniverse.cs
---

# TKT-mdf-018: TMS 29 regime-break flag on spanning CAGRs

- Status: done — merged to `dev` 2026-10-17 (PR [#84](https://github.com/sarperim/degerli/pull/84), merge commit `c3b82cb`).
- Size: M
- Scope: the CAGR regime-break marker — add `derived_metrics.regime_break` (nullable bool) and compute it (window spans 2023-12-31), write it from the metrics engine, and serve it in the metric-flags object of the screener and stock-page payloads. Server-side only; the UI warning rendering is a deferred alignment ticket. Must NOT touch the deflation follow-up (TKT-mdf-019) or the DCF.
- Traces to: FR-MDF-013, FR-MDF-014; UC-MDF-005
- Acceptance: TC-MDF-060 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §3.1 (`derived_metrics.regime_break`), §6.2 (regime-break flag); `03-api-design.md` §4 (metric flags)
- UX refs: SCR-005 (UXR-RES-026, UXR-RES-027 — Growth-section regime-break marker; a UI-alignment ticket follows); SCR-003 (screener consuming side — UXR follow-up pending in `stock-screening.md`)
- Dependencies: TKT-mdf-017
- Parallel group: P-26
