---
scope:
  - src/Core/Metrics/CanonicalMetrics.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - tests/Core.UnitTests/Metrics/ProfileMetricMatrixTests.cs
  - tests/Api.IntegrationTests/ProfileMetricServingTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
  - tests/Api.IntegrationTests/Harness/FixtureBuilder.cs
---

# TKT-mdf-020: Financial-profile metric NULL matrix

- Status: done — merged to `dev` 2026-10-17 (PR [#85](https://github.com/sarperim/degerli/pull/85), merge commit `71ba944`).
- Size: M
- Scope: the canonical metrics' profile gating — for `accounting_profile ∈ {bank, insurance}`, force the ten industrial metrics (`ev_ebitda`, `ev_fcf`, `fcf_yield`, `roic`, `gross_margin`, `operating_margin`, `fcf_cagr_*`, `net_debt_ebitda`, `interest_coverage`, `current_ratio`) to NULL even when stray lines exist, while `pe`/`pb`/`roe`/`eps_cagr_*`/`div_*`/`payout_ratio` compute normally. Must NOT touch the DCF sector handling (TKT-val-010).
- Traces to: FR-MDF-013; UC-MDF-005; BR-MDF-009
- Acceptance: TC-MDF-057 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §6.2 (accounting-profile gating), §3.1 (`instruments.accounting_profile`)
- UX refs: SCR-003, SCR-005 (NULL metrics render the existing not-meaningful state — no new UX)
- Dependencies: TKT-mdf-015, TKT-mdf-018
- Parallel group: P-27
