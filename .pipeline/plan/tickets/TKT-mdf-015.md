---
scope:
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Persistence/Seed/
  - src/Ingestion/Universe/
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - fixtures/Degerli.Fixtures/FixtureModels.cs
  - fixtures/Degerli.Fixtures/FixtureSeeder.cs
  - tests/Api.IntegrationTests/AccountingProfileIngestTests.cs
  - tests/Api.IntegrationTests/UniverseSyncTests.cs
  - tests/Api.IntegrationTests/MigrationsAndSeedTests.cs
---

# TKT-mdf-015: `accounting_profile` classification & financial-statement ingest posture

- Status: done — merged to `dev` 2026-10-17 (PR [#80](https://github.com/sarperim/degerli/pull/80), merge commit `88198dd`).
- PR: https://github.com/sarperim/degerli/pull/80
- Size: M
- Scope: the `instruments.accounting_profile` column + a checked-in builder-curated seed list of BIST 100 financial-sector members, the universe/seed path (profile is a classification — a source sync must not overwrite it), and the ingest posture that bank/insurance statements with absent industrial lines are never quarantined. Must NOT touch the metric formulas (TKT-mdf-020) or the DCF (TKT-val-010).
- Traces to: FR-MDF-002, FR-MDF-012; UC-MDF-001 (alternate b), UC-MDF-003; BR-MDF-007
- Acceptance: TC-MDF-056 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §3.1 (`instruments.accounting_profile`), §6.2 (profile gating — ingest side), §8; `01-system-architecture.md` §13.1 (v1.4)
- UX refs: — (no user-facing change in this ticket)
- Dependencies: TKT-mdf-014, TKT-mdf-022
- Parallel group: P-22
