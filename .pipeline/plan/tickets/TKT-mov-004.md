# TKT-mov-004: Public market endpoints

- Status: todo
- Size: M
- Scope: `/src/Api` market module: `GET /api/v1/market/overview` (one snapshot row, honest envelope, anonymous) and `GET /api/v1/market/macro` (six series with per-series as-of/stale/state; inflation pair always both entries) — separate endpoints because equity and macro have separate failure domains; caching headers per `03` §13. Must NOT touch snapshot computation or the dashboard UI.
- Traces to: FR-MOV-007..010; UC-MOV-001, UC-MOV-003; BR-MOV-002, BR-MOV-007
- Acceptance: TC-MOV-012, TC-MOV-013, TC-MOV-014 — TDD: tests first, then green; specs in `.pipeline/testing/market-overview.md`.
- Architecture refs: `03-api-design.md` §3 (overview + macro endpoints, failure-domain note); `01-system-architecture.md` §8.1 (M-1 envelope), §7 (FR-MOV-007..010 rows)
- UX refs: UXR-MOV-009 (unavailable-state payload contract)
- Dependencies: TKT-mov-002, TKT-mov-003, TKT-foundation-004
- Parallel group: P-13
