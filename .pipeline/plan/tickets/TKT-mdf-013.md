---
scope:
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Ingestion/Statements/FinancialStatementStore.cs
  - src/Ingestion/Statements/StatementsJob.cs
  - src/Ingestion/Statements/StatementsModels.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Ingestion/Metrics/MarketSnapshotEngine.cs
  - src/Ingestion/Valuation/DcfBaselineGenerationJob.cs
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - fixtures/Degerli.Fixtures/FixtureModels.cs
  - fixtures/Degerli.Fixtures/FixtureSeeder.cs
  - tests/Api.IntegrationTests/StatementVersioningTests.cs
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
  - tests/Api.IntegrationTests/RestatementMetricsTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
  - tests/Api.IntegrationTests/MarketSnapshotTests.cs
  - tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
  - tests/Api.IntegrationTests/MigrationsAndSeedTests.cs
  - tests/Api.IntegrationTests/SchemaConformanceTests.cs
  - tests/Api.IntegrationTests/CannedSourceCatalogTests.cs
---

# TKT-mdf-013: Statement `version` → integer restatement sequence (end-to-end)

- Status: todo
- Size: L
- Scope: the statement versioning path — the `financial_statements.version` column type change and its one-time value migration, the store's sequence assignment, and every serving/serving-reader of the version (metrics, snapshot, DCF baselines, stock fundamentals). Payload wire format keeps declaring `as_reported`/`restated`; the store assigns the sequence. Must NOT touch the metric formulas, the normalization of values, or unrelated admin endpoints.
- Traces to: FR-MDF-002, FR-MDF-019; BR-MDF-011; UC-MDF-001, UC-MDF-005
- Acceptance: TC-MDF-054 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `02-data-model.md` §1 (principle 5), §3.1 (`financial_statements`, `fin_line_items`, `derived_metrics.is_rested`), §5.7, §8 (migration note); `01-system-architecture.md` §13.1 (v1.4)
- UX refs: — (restatement display markers are existing; no UX change — serving keeps the same flags)
- Dependencies: none
- Parallel group: P-20
