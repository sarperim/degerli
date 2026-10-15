---
scope:
  - src/Ingestion/Prices/
  - src/Ingestion/Sources/
  - src/Api/appsettings.json
  - src/Api/appsettings.Development.json
  - .env.example
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - fixtures/Degerli.Fixtures/FixtureModels.cs
  - tests/Api.IntegrationTests/PriceSourceContractTests.cs
  - tests/Api.IntegrationTests/PriceIngestionTests.cs
  - tests/Api.IntegrationTests/BackfillAndCoverageTests.cs
  - tests/Api.IntegrationTests/CannedSourceCatalogTests.cs
---

# TKT-mdf-021: Price source replacement — İş Yatırım (primary) with per-row provenance

- Status: todo
- Size: M
- Scope: the daily-price/index-level source — replace the way the prices adapter is wired and parsed for the İş Yatırım public endpoints (10Y historical EOD + index levels), keep Yahoo Finance `.IS` as a documented contingency source, and ensure each stored `daily_prices`/`index_levels` row records its concrete `source_ref` (`isyatirim` / `yahoo`). Resolves OQ-MDF-001. Must NOT touch the statement, macro, or fund adapters.
- Traces to: FR-MDF-001, FR-MDF-006, FR-MDF-008, FR-MDF-018; UC-MDF-001, UC-MDF-002; BR-MDF-001
- Acceptance: TC-MDF-061 — TDD: tests first, then green; spec in `.pipeline/testing/market-data-foundation.md` §7.
- Architecture refs: `.pipeline/analysis/market-data-foundation.md` OQ-MDF-001 (resolved here); `03-api-design.md` §1.2 (per-row source provenance); `02-data-model.md` §3.1 (`daily_prices.source_ref`)
- UX refs: —
- Dependencies: TKT-mdf-013 (payload-catalog edit serialization)
- Parallel group: P-24
