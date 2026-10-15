---
scope:
  - src/Ingestion/Universe/
  - src/Ingestion/Coverage/
  - tests/Api.IntegrationTests/ConstituentSeedTests.cs
  - tests/Api.IntegrationTests/UniverseSyncTests.cs
  - tests/Api.IntegrationTests/BackfillAndCoverageTests.cs
---

# TKT-mdf-022: Constituents — launch seed + forward logging + recorded pre-launch gap

- Status: todo
- Size: S/M
- Scope: the universe-membership strategy — seed the current BIST 100 constituents at launch (effective_from = launch, effective_to NULL), keep logging subsequent quarterly changes append-only via the daily sync, and record the absent pre-launch constituent history as an explicit `coverage_metadata` (`universe` scope) gap. No free structured 10Y history exists (resolves the FR-MDF-007 assumption). Must NOT touch the daily-sync change-detection logic beyond the seed + coverage record.
- Traces to: FR-MDF-007, FR-MDF-011; UC-MDF-002, UC-MDF-003; BR-MDF-005, BR-MDF-007
- Acceptance: TC-MDF-062 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §3.1 (`index_constituents`, `coverage_metadata`); `.pipeline/analysis/market-data-foundation.md` FR-MDF-007/UC-MDF-003
- UX refs: SCR-012 (coverage report shows the recorded gap — no new UI)
- Dependencies: TKT-fdf-003 (shared coverage-recorder edit)
- Parallel group: P-21
