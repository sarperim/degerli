# TKT-mdf-010: Admin API — summary, coverage, ledger, triggers, stats

- Status: todo
- Size: L
- Scope: `/src/Api` admin endpoints (builder role-gated): `GET /api/v1/admin/summary` (ops one-glance), `GET /api/v1/admin/coverage?scope=instrument|fund`, `GET /api/v1/admin/ingest-runs?job=&status=`, `POST /api/v1/admin/ingest/{job}/run` (job + backfill trigger), `GET /api/v1/admin/stats` (aggregate-only). Establishes the admin route-group file that TKT-mdf-011, TKT-res-006, TKT-scr-005, TKT-val-009 extend with their own endpoint files. Must NOT touch quarantine endpoints (TKT-mdf-011) or the admin UI.
- Traces to: FR-MDF-010 (trigger), FR-MDF-011 (report), FR-MDF-016 (freshness display); UC-MDF-002, UC-MDF-004, UC-MOV-005; UXR-MDF-003..006, 011, 013, 014, 016, 017, 026 (API side); SC-008/OBJ-005 (stats — recorded trace exception per `03` §10)
- Acceptance: TC-MDF-015, TC-MDF-017, TC-MDF-039, TC-MDF-040, TC-MDF-041, TC-MDF-042, TC-MDF-043, TC-MDF-044 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §10.1 (roles), AD-13; `03-api-design.md` §8 (endpoints + summary/stats sketches, job codes), §12 (admin rate limit)
- UX refs: UXR-MDF-003, UXR-MDF-004, UXR-MDF-005, UXR-MDF-006, UXR-MDF-011, UXR-MDF-013, UXR-MDF-014, UXR-MDF-016, UXR-MDF-017, UXR-MDF-026 (API side; UI side in TKT-mdf-012)
- Dependencies: TKT-mdf-005, TKT-mdf-007, TKT-foundation-004, TKT-acc-002, TKT-acc-003
- Parallel group: P-13
