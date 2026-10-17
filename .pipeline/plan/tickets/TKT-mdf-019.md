---
scope:
  - src/Core/Metrics/CanonicalMetrics.cs
  - src/Core/Metrics/MetricMath.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Ingestion/Metrics/DerivedMetricStore.cs
  - fixtures/Degerli.Fixtures/FixtureModels.cs
  - fixtures/Degerli.Fixtures/FixtureDerivedData.cs
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - tests/Core.UnitTests/Metrics/DeflatedCagrTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
---

# TKT-mdf-019: (Optional — promotable) CPI-deflated multi-year CAGRs

- Status: blocked — coder blocker (run 2026-10-17): OPTIONAL/promotable ticket, never promoted; no acceptance contract exists — TC-MDF-060 (test plan v1.4 §7) is regime-break only (nominal CAGRs); no deflation golden/TC/fixture anywhere, and `02-data-model.md` §6.2 defers CPI deflation ("the value stays nominal"). Ticket requires the builder's explicit promotion before landing; none recorded. Human decision: promote (with a test-planner §7 deflation TC + pinned real-terms goldens + architecture §6.2 update) or drop/defer. Not a dependency block (TKT-mdf-018 merged).
- Size: M
- Scope: **Optional / promotable — the builder may promote or drop this** (decision D2, 2026-10-15; the regime-break flag TKT-mdf-018 is the Must-scope alternative). If promoted: deflate historical FY revenue/earnings to current purchasing power using `TUIK_CPI` before computing multi-year CAGRs, marking them `is_adjusted`. Additive to TKT-mdf-018; changes the *meaning* of historical CAGRs to real-terms, so it must not land without the builder's explicit promotion.
- Traces to: FR-MDF-013, FR-MDF-014, FR-MDF-018; UC-MOV-002, UC-MDF-005
- Acceptance: TC-MDF-060 (extended) + a deflation golden to be specified on promotion — spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §6.2 (regime-break flag — deferred deflation noted)
- UX refs: SCR-005, SCR-003 — adjusted-basis markers (**ux-designer flow-back pending if promoted**)
- Dependencies: TKT-mdf-018
- Parallel group: none (promotable)
