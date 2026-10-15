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
---

# TKT-mdf-018: TMS 29 regime-break flag on spanning CAGRs

- Status: todo
- Size: M
- Scope: the CAGR regime-break marker — add `derived_metrics.regime_break` (nullable bool) and compute it (window spans 2023-12-31), write it from the metrics engine, and serve it in the metric-flags object of the screener and stock-page payloads. Server-side only; the UI warning rendering is a deferred alignment ticket. Must NOT touch the deflation follow-up (TKT-mdf-019) or the DCF.
- Traces to: FR-MDF-013, FR-MDF-014; UC-MDF-005
- Acceptance: TC-MDF-060 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §3.1 (`derived_metrics.regime_break`), §6.2 (regime-break flag); `03-api-design.md` §4 (metric flags)
- UX refs: SCR-005, SCR-003 — regime-break warning marker (**ux-designer flow-back pending**: UXR not yet written; UI rendering ticket deferred until it lands)
- Dependencies: TKT-mdf-017
- Parallel group: P-26
