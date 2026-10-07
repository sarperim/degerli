# TKT-mdf-012: Admin UI — SCR-012 shell & MDF sections

- Status: todo
- Size: L
- Scope: `/src/Web` `/admin` area per the TKT-mdf-001 design: role-gated nav entry + access-denied state, ops summary, quarantine queue (inspect payload, dismiss with note), coverage report with instrument/fund scope switch, run ledger with job/status filters + job & backfill triggers + in-progress auto-refresh (UXR-MDF-015) + manual refresh (UXR-MDF-028), aggregate stats panel, admin empty states. **Per-section route/component files** so TKT-scr-005, TKT-res-010, TKT-val-009 add their sections without touching shared files. Must NOT implement the description/metric-visibility/baseline sections (owned by RES/SCR/VAL tickets).
- Traces to: FR-MDF-011/012/016 (UI side); UC-MDF-001/002/004, UC-MOV-005, UC-FDF-002/003; UXR-MDF-001..017, 026..028 (screen side)
- Acceptance: TC-MDF-045, TC-MDF-046, TC-MDF-047, TC-MDF-048, TC-MDF-049, TC-MDF-050, TC-MDF-051 + **TC-FDF-012** (FDF-plan-owned; fund-scope coverage rows — fund content is seeded by TKT-foundation-007) — TDD: e2e specs first, then green; case specs in the MDF and FDF test plans.
- Architecture refs: `01-system-architecture.md` §3 (C1 admin area), §8.1 (M-2 invalidation), AD-13; `03-api-design.md` §8, §12 (admin 60 req/min accommodates ledger polling), §13 (`no-store`)
- UX refs: SCR-012; UXR-MDF-001..017, UXR-MDF-026, UXR-MDF-027, UXR-MDF-028; UXR-G-001..004, UXR-G-018..022, UXR-G-025..028 (global rules instantiated on this screen)
- Dependencies: TKT-mdf-001, TKT-mdf-010, TKT-mdf-011, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-15
