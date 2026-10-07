# TKT-fdf-002: Fund scheduling, coverage & failure handling

- Status: todo
- Size: M
- Scope: fund jobs on the TKT-mdf-005 scheduler (cron per TEFAS cadence — unattended; run-ledger rows with stats), backfill mode with achieved-depth recording into `coverage_metadata` (fund scope; source limitations recorded, never fabricated), failure path (retries → failed run, builder alert naming the fund job, last-known retained, staleness visible in the freshness view/admin summary), and the no-public-fund-surface boundary (OpenAPI contains no `/funds` routes). Must NOT touch the admin UI (fund-scope rows render via TKT-mdf-012) or the TEFAS adapters' ingest logic.
- Traces to: FR-FDF-004, FR-FDF-005, FR-FDF-006; UC-FDF-001 (main + alt a), UC-FDF-002 (main + alt a), UC-FDF-003 (main + alt a); NFR-FDF-001; SC-005 (fund side); UXR-MDF-012 (data side)
- Acceptance: TC-FDF-005, TC-FDF-006, TC-FDF-007, TC-FDF-008, TC-FDF-010 — TDD: tests first, then green; specs in `.pipeline/testing/fund-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-FDF-004..006 rows), §3 (C3c/C3d); `02-data-model.md` §3.1 (`coverage_metadata` fund scope); `03-api-design.md` §8–§9 (coverage `scope=fund`, fund job codes)
- UX refs: —
- Dependencies: TKT-fdf-001, TKT-mdf-005, TKT-mdf-010
- Parallel group: P-14

## Notes

- Note: TC-FDF-012 — fund-scope coverage render in the admin UI — lands with the admin surface in TKT-mdf-012.
