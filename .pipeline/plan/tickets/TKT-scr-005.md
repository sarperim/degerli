# TKT-scr-005: Metric visibility — admin API, hidden-metric behaviors & admin panel

- Status: todo
- Size: M
- Scope: `/src/Api` `PATCH /api/v1/admin/screener-metrics/{code} {isScreenable}` (builder-gated; catalog serves visible only; ad-hoc hidden/unknown metric → 400 `METRIC_NOT_AVAILABLE`; saved-screen re-run drops hidden criteria with `droppedCriteria[]`) and the `/src/Web` admin metric-visibility section (own per-section files on the TKT-mdf-012 shell): the 18 metrics with state, toggle behind a consequence-stating confirmation, catalog reflection on next load. Must NOT touch other admin sections.
- Traces to: FR-SCR-017; UC-SCR-004 (main + alt a); UXR-MDF-023/024; UXR-SCR-011, UXR-SCR-015
- Acceptance: TC-SCR-007, TC-SCR-018, TC-SCR-020, TC-SCR-021 — TDD: tests first, then green; specs in `.pipeline/testing/stock-screening.md`.
- Architecture refs: `03-api-design.md` §8 (screener-metrics endpoint), §4 (droppedCriteria contract); `01-system-architecture.md` §7 (FR-SCR-017 row), AD-13; `02-data-model.md` §3.3 (`metric_catalog.is_screenable`)
- UX refs: SCR-012 (metric-visibility section); UXR-MDF-023, UXR-MDF-024; UXR-SCR-011, UXR-SCR-015
- Dependencies: TKT-mdf-010, TKT-mdf-012, TKT-scr-002, TKT-scr-004
- Parallel group: P-16
