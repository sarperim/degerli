# TKT-fdf-001: TEFAS ingestion adapters

- Status: todo
- Size: L
- Scope: `/src/Ingestion` TEFAS adapters on the TKT-mdf-002 framework: `fund-nav`, `fund-performance`, `fund-holdings` jobs — fund universe sync bounded to equity + equity-heavy mixed funds (out-of-scope types skipped-and-counted in run stats per I-FDF-1, never quarantined), dated idempotent NAV rows, performances as published, holdings snapshots with symbol matching to MDF instruments (`instrument_id` nullable, `name_raw` retained for unmatched — never dropped), append-only retention, conflicting values follow the Q1 quarantine policy. Real TEFAS access is builder V0 validation (OQ-FDF-002); tests run against WireMock. Must NOT touch the scheduler wiring or admin coverage endpoints.
- Traces to: FR-FDF-001, FR-FDF-002, FR-FDF-003; UC-FDF-001 (step 2); BR-FDF-003/004/006; NFR-FDF-002
- Acceptance: TC-FDF-001, TC-FDF-002, TC-FDF-003, TC-FDF-004, TC-FDF-009, TC-FDF-011 — TDD: tests first, then green; specs in `.pipeline/testing/fund-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-FDF-001..003 rows), §3 (C3a); `02-data-model.md` §3.7 (fund tables); `03-api-design.md` §9 (fund jobs)
- UX refs: — (headless by design, BR-FDF-001)
- Dependencies: TKT-mdf-002, TKT-foundation-006, TKT-foundation-008
- Parallel group: P-11
