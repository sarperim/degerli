# TKT-scr-003: Run endpoint — validation, staleness & on-demand recompute

- Status: todo
- Size: M
- Scope: `/src/Api` screener module edge behaviors on the TKT-scr-002 run endpoint: malformed-criterion classes (missing bound value, non-numeric, window 7, range min>max, window on non-growth metric) → 400 with `params.fields[]`; staleness envelope (`asOf` + `stale` under a forced freshness gap); results computed on demand with no persisted result sets (schema has none — data-day advance changes results). Must NOT touch saved screens or metric visibility.
- Traces to: FR-SCR-003 (validation), FR-SCR-005 (staleness), UC-SCR-001 (alternate c), UC-SCR-003 step 2, NFR-SCR-004
- Acceptance: TC-SCR-008, TC-SCR-010, TC-SCR-011 — TDD: tests first, then green; specs in `.pipeline/testing/stock-screening.md` (interpretations I-SCR-2/I-SCR-4 binding).
- Architecture refs: `03-api-design.md` §4, §7 (`VALIDATION_FAILED`); `01-system-architecture.md` §10.5 (input validation)
- UX refs: —
- Dependencies: TKT-scr-002
- Parallel group: P-12
