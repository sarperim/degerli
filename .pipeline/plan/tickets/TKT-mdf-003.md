# TKT-mdf-003: Statements, dividends, corporate actions & KAP disclosures ingestion

- Status: todo
- Size: L
- Scope: `/src/Ingestion` adapters on the TKT-mdf-002 framework: `statements` job (XBRL/PDF parse → `financial_statements` + `fin_line_items` mapped to the canonical chart of accounts, versioned as_reported/restated, `OTHER_*` passthrough, GROSS_PROFIT/EBITDA derivation when unreported), `dividends` job, `corporate-actions` job (terms JSON), `disclosures` job (KAP metadata; documents to disk outside the DB). Must NOT touch the prices adapter or universe sync.
- Traces to: FR-MDF-002, FR-MDF-003, FR-MDF-004, FR-MDF-005; BR-MDF-004
- Acceptance: TC-MDF-003, TC-MDF-004, TC-MDF-005, TC-MDF-006 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-MDF-002..005 rows); `02-data-model.md` §3.1 (`financial_statements`, `fin_line_items`, `dividends`, `corporate_actions`, `kap_disclosures`), §6.1 (chart of accounts)
- UX refs: —
- Dependencies: TKT-mdf-002
- Parallel group: P-8
