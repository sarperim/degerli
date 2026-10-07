# TKT-mov-003: Market snapshot computation

- Status: todo
- Size: L
- Scope: the `snapshot` job (Metrics Engine extension, own job files per the TKT-mdf-008 convention): nightly `market_snapshots` row per trading day — XU100/XU30 levels + change, breadth, total volume, market P/E (cap-weighted aggregate Σmcap/ΣNI_TTM, loss-makers excluded + counted, D-05), market dividend yield, gainers/losers top-10 with volume-eligibility threshold (config), sector daily performance (cap-weighted, locked formula Q2), idempotent by snapshot_date, latest-completed served. Must NOT touch macro jobs or the public endpoints.
- Traces to: FR-MOV-001..006, FR-MOV-021; BR-MOV-009, BR-MOV-010, BR-MOV-011; UC-MOV-001 step 2
- Acceptance: TC-MOV-001, TC-MOV-002, TC-MOV-003, TC-MOV-004, TC-MOV-005 — TDD: tests first, then green; golden values in `.pipeline/testing/market-overview.md` + FU §11.3 (hand-derived; never re-derived from the implementation).
- Architecture refs: `01-system-architecture.md` §7 (FR-MOV-001..006/021 rows), AD-10, §9 (NFR-MOV-001); `02-data-model.md` §3.2 (`market_snapshots`), §6.3 (market aggregates)
- UX refs: —
- Dependencies: TKT-mdf-004, TKT-mdf-008
- Parallel group: P-12
