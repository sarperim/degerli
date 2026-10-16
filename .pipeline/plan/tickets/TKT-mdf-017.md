---
scope:
  - src/Core/Metrics/CanonicalMetrics.cs
  - src/Ingestion/Statements/StatementMapper.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Ingestion/Valuation/DcfBaselineGenerationJob.cs
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - fixtures/Degerli.Fixtures/FixtureModels.cs
  - fixtures/Degerli.Fixtures/FixtureSeeder.cs
  - fixtures/Degerli.Fixtures/FixtureDerivedData.cs
  - fixtures/Degerli.Fixtures/FixtureUniverse.cs
  - tests/Core.UnitTests/Metrics/OcfFcfTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
  - tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs
  - tests/Api.IntegrationTests/DcfBaselineRegenerationTests.cs
---

# TKT-mdf-017: `OCF` in the chart of accounts; `FCF = OCF − |CAPEX|`

- Status: todo — unblocked 2026-10-17 by user-sanctioned scope amendment; re-dispatch for implementation.
- Scope amendment (2026-10-17, user-sanctioned): added `fixtures/Degerli.Fixtures/FixtureUniverse.cs` — TC-MDF-059 requires the `OCF` line authored on ALFA FY2025 and the deep-history series, whose only home is that file (mirrors the ratified TKT-mdf-016 amendment).
- Size: L
- Scope: the canonical FCF definition — add `OCF` to the chart of accounts and the statement mapper, replace `FreeCashFlow` with `OCF − |CAPEX|`, thread `OCF` through the metrics engine and the DCF baseline derivation, and re-baseline the fixture OCF values (authored as `FCF + CAPEX` so existing FCF goldens are unchanged). Must NOT touch the per-share CAGR adjustment (TKT-mdf-016) or the profile gating (TKT-mdf-020).
- Traces to: FR-MDF-013; UC-MDF-005; BR-MDF-009
- Acceptance: TC-MDF-059 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §6.1 (chart of accounts, `OCF`), §6.2 (FCF formula), §6.4 (baseline base FCF)
- UX refs: —
- Dependencies: TKT-mdf-016 (metrics files), TKT-mdf-021 (payload catalog)
- Parallel group: P-25
