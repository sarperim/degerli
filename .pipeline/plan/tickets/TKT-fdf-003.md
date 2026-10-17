---
scope:
  - src/Ingestion/Funds/
  - src/Ingestion/Coverage/
  - tests/Api.IntegrationTests/FundHoldingsGapTests.cs
  - tests/Api.IntegrationTests/FundIngestionTests.cs
  - tests/Api.IntegrationTests/FundSchedulingCoverageTests.cs
---

# TKT-fdf-003: Ticker-level fund holdings descoped to a recorded coverage gap

- Status: done — merged to `dev` 2026-10-17 (PR [#74](https://github.com/sarperim/degerli/pull/74), merge commit `38e404c`).
- PR: https://github.com/sarperim/degerli/pull/74
- Size: S
- Scope: the fund ingest — stop attempting ticker-level holdings from TEFAS (the source exposes NAV/performance/asset-class allocation only), keep `fund_navs`/`fund_performances`, and record the per-fund holdings gap in `coverage_metadata` (BR-FDF-004). The `fund_holdings` schema/columns stay for future look-through phases. Must NOT touch the equity statements/dividends adapters.
- Traces to: FR-FDF-003, FR-FDF-004; UC-FDF-001, UC-FDF-003; BR-FDF-004
- Acceptance: TC-FDF-013 — TDD: tests first, then green; spec in `.pipeline/testing/fund-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §3.7 (`fund_holdings`, `coverage_metadata`); `.pipeline/analysis/fund-data-foundation.md` FR-FDF-003/OQ-FDF-002; `01-system-architecture.md` §13.1 (v1.4)
- UX refs: SCR-012 (fund coverage report shows the recorded gap — no new UI)
- Dependencies: none
- Parallel group: P-20
