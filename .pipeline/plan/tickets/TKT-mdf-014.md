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

- Status: todo
- Size: M
- Scope: the statement ETL normalization boundary — `source_unit_scale`/`source_currency` columns, the payload scale/currency declaration parsing, normalization of every `fin_line_items.value` (and share-count lines) to raw single-unit TRY, the period-end FX read from `macro_values`, and the `MISSING_FX_RATE` reason code. Must NOT touch the version-sequence logic (TKT-mdf-013) or the metric formulas.
- Traces to: FR-MDF-002, FR-MDF-008; UC-MDF-001, UC-MDF-002; BR-MDF-001
- Acceptance: TC-MDF-055 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §1 (principle 8), §3.1 (`financial_statements` normalization paragraph), §5.9 (MDF→MOV FX read edge), §8; `03-api-design.md` §1 (provenance)
- UX refs: —
- Dependencies: TKT-mdf-013
- Parallel group: P-21
