---
scope:
  - src/Ingestion/Macro/
  - src/Api/appsettings.json
  - src/Api/appsettings.Development.json
  - .env.example
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - tests/Api.IntegrationTests/MacroIngestionTests.cs
  - tests/Api.IntegrationTests/MacroEvdsAdapterTests.cs
---

# TKT-mov-008: Consolidated macro ingestion via TCMB EVDS

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/78
- Size: M
- Scope: the macro source adapter — fetch the five official series (`TUIK_CPI`, `CBRT_REPO`, `USD_TRY`, `EUR_TRY`, `GOLD`) from a single TCMB EVDS API adapter (free API key, config), replacing the scattered per-source plan. Resolves OQ-MOV-005. Keeps the existing revision/staleness behavior. `INDEP_CPI` is out of scope (TKT-mov-009). Must NOT touch the statement FX reader (TKT-mdf-014) or the fund adapter.
- Traces to: FR-MOV-014; UC-MOV-001, UC-MOV-002; BR-MOV-003
- Acceptance: TC-MOV-026 — TDD: tests first, then green; spec in `.pipeline/testing/market-overview.md` §7.
- Architecture refs: `.pipeline/analysis/market-overview.md` §8/OQ-MOV-005; `02-data-model.md` §3.2 (`macro_series`, `macro_values`)
- UX refs: SCR-001 (macro strip — data feeding it; no UI change)
- Dependencies: TKT-mdf-013 (payload-catalog edit serialization)
- Parallel group: P-23
