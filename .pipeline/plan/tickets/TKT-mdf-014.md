---
scope:
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Ingestion/Statements/StatementsJob.cs
  - src/Ingestion/Statements/StatementsModels.cs
  - src/Ingestion/Statements/FinancialStatementStore.cs
  - src/Ingestion/Validation/QuarantineReason.cs
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - tests/Api.IntegrationTests/StatementNormalizationTests.cs
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
  - tests/Api.IntegrationTests/ValidationQuarantineTests.cs
  - tests/Api.IntegrationTests/SchemaConformanceTests.cs
  - tests/Api.IntegrationTests/MigrationsAndSeedTests.cs
---

# TKT-mdf-014: Statement unit-scale & reporting-currency normalization

- Status: blocked — planning/scope defect (see Blocker below); dependency TKT-mdf-013 is merged (`3fd00e0`).
- Blocker: TC-MDF-055's mandated behavior (extend `StatementsPayload`/`StatementFact` with the `unitScale`/`currency` declaration, add the `source_unit_scale`/`source_currency` columns, and normalize/store every `fin_line_items.value` to raw single-unit TRY) cannot land green within this ticket's `scope:` list. (1) The only construction sites of `StatementsPayload`/`StatementFact` outside `scope:` are `tests/Api.IntegrationTests/StatementVersioningTests.cs` (lines 128/132/149/153/181): adding the required declaration members breaks its compile if required, or makes every payload undeclared → `SCHEMA_MISMATCH` quarantine (or silently defaults to units, which fails TC-MDF-055) if optional — either way red CI. (2) The normalization changes the stored absolute values asserted by `StatementVersioningTests.cs:83-85` (NI 60/75/80) and `RestatementMetricsTests.cs:116-117` (NI 75/60) — both out of `scope:` and owned by the merged TKT-mdf-013. (3) The fixture seed path (`FixtureUniverse.cs`/`FixtureSeeder.cs`) and its absolute-value assertions (`FixtureUniverseSeedTests.cs:130-148`, `MarketSnapshotTests.cs`) are also out of `scope:`; per FU §2 / 02 §1.8 they must also become raw (tabulated × 10⁶) to keep the store canonically consistent. A human scope-amendment (as sanctioned for TKT-mdf-013) or re-plan is required before implementation can proceed.
- Size: M
- Scope: the statement ETL normalization boundary — `source_unit_scale`/`source_currency` columns, the payload scale/currency declaration parsing, normalization of every `fin_line_items.value` (and share-count lines) to raw single-unit TRY, the period-end FX read from `macro_values`, and the `MISSING_FX_RATE` reason code. Must NOT touch the version-sequence logic (TKT-mdf-013) or the metric formulas.
- Traces to: FR-MDF-002, FR-MDF-008; UC-MDF-001, UC-MDF-002; BR-MDF-001
- Acceptance: TC-MDF-055 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §1 (principle 8), §3.1 (`financial_statements` normalization paragraph), §5.9 (MDF→MOV FX read edge), §8; `03-api-design.md` §1 (provenance)
- UX refs: —
- Dependencies: TKT-mdf-013
- Parallel group: P-21
