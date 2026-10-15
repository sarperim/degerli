---
scope:
  - src/Core/Adjustments/AdjustmentFactors.cs
  - src/Core/Adjustments/PriceAdjustment.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - fixtures/Degerli.Fixtures/FixtureModels.cs
  - fixtures/Degerli.Fixtures/FixtureSeeder.cs
  - tests/Core.UnitTests/Metrics/AdjustedCagrTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
---

# TKT-mdf-016: Corporate-action-adjusted EPS/DPS series for CAGRs

- Status: todo
- Size: M
- Scope: the metrics engine's per-share series construction — load an instrument's corporate actions and multiply the historical EPS/DPS series by the cumulative adjustment factor Π (actions after each fact's date, reused from `src/Core/Adjustments`) before the CAGR, marking those metrics `is_adjusted`. Must NOT touch the FCF formula (TKT-mdf-017) or the price-adjustment application itself.
- Traces to: FR-MDF-013, FR-MDF-018; UC-MDF-005; BR-MDF-009/010
- Acceptance: TC-MDF-058 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §6.2 (per-share CAGR basis), §3.1 (adjustment system)
- UX refs: — (adjusted-data markers already specified on consuming screens)
- Dependencies: TKT-mdf-013, TKT-mdf-014
- Parallel group: P-23
