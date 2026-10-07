# TKT-scr-002: Metric catalog & screener run endpoint — core

- Status: todo
- Size: L
- Scope: `/src/Api` screener module: `GET /api/v1/screener/metrics` (visible catalog only — families, units, TR/EN labels, `isCagr`, sort order) and `POST /api/v1/screener/run` core (AND logic over `derived_metrics`, inclusive min/max/range bounds, per-criterion CAGR window, missing-data → exclude-and-count, zero-match as normal 200, `criteria: []` → 400 per I-SCR-1, on-demand compute with honest envelope). Must NOT touch saved-screen endpoints (TKT-scr-004) or admin visibility (TKT-scr-005).
- Traces to: FR-SCR-001..006, FR-SCR-014, FR-SCR-015; UC-SCR-001 (main); BR-SCR-001..003, BR-SCR-009
- Acceptance: TC-SCR-001, TC-SCR-002, TC-SCR-003, TC-SCR-004, TC-SCR-005, TC-SCR-006, TC-SCR-009 — TDD: tests first, then green; golden values in `.pipeline/testing/stock-screening.md` §1 (FU-derived).
- Architecture refs: `03-api-design.md` §4 (run request/response, saved-screen separation); `01-system-architecture.md` §7 (FR-SCR-001..006 rows), §8.3 (UXR-SCR-001..007 row); `02-data-model.md` §3.3 (`metric_catalog`, criterion model)
- UX refs: UXR-SCR-011 (catalog serves visible only — API side)
- Dependencies: TKT-mdf-008, TKT-foundation-004, TKT-foundation-005
- Parallel group: P-11
