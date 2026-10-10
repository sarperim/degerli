# TKT-res-007: SCR-002 Stock List UI

- Status: blocked
- Blocker (2026-10-10): review-lead returned ESCALATED on PR #36. The PR content is clean (full review passed), but CI is red on the pre-existing flaky `HarnessSampleTests.Serilog_test_sink_captures_application_logs` (TKT-foundation-006 harness) — unattributable to this web-only PR — and that failure skips the E2E job where this ticket's pinned TC-RES-035/036 run. The red-CI fixer dispatch disputed the fix as out-of-scope (backend vs web-only). Needs either the test-planner/infra owner to stabilize the Serilog test-sink assertion, or an explicit builder waiver of F1. Artifact: `.pipeline/plan/reviews/TKT-res-007-round-1.md`.
- Size: M
- Scope: `/src/Web` SCR-002 route per the TKT-res-001 design: full universe list (name, code, sector), sector filter + search applying together and shown as active, no-match empty state, dashboard sector-link prefill via URL params, header-search query application, row activation → stock page, filter/search/active-state preservation across navigation (draft store, M-3), disclaimer, loading/error states. Must NOT implement the global header search itself (that ships with this ticket's route contracts in the shared shell from TKT-foundation-002 — wire it here).
- Traces to: FR-RES-001..004 (UI side); UC-RES-001 (main + alt a); NFR-RES-005
- Acceptance: TC-RES-022, TC-RES-035, TC-RES-036 — TDD: specs first, then green; specs in `.pipeline/testing/stock-research.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-3), §8.3 (UXR-RES-001..007 row); `03-api-design.md` §3 (stocks + search)
- UX refs: SCR-002; UXR-RES-001..007; UXR-G-001, UXR-G-003, UXR-G-005, UXR-G-021
- Dependencies: TKT-foundation-011, TKT-res-002, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-14
