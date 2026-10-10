# TKT-scr-006: SCR-003 Screener UI

- Status: todo
- Size: L
- Scope: `/src/Web` SCR-003 route per the TKT-scr-001 design: criteria builder (families, bounds, window selector, remove/clear), run flow with pending lock, results table (per-criterion columns, counts, as-of, row links), zero-match empty state, exclusion count, save flow with anonymous prompt / unverified gate / duplicate inline error / failure preservation (MSW-driven states), draft preservation across navigation via the zustand draft store (M-3), disclaimer. Must NOT implement the My Saved Screens list screen (TKT-scr-007).
- Traces to: FR-SCR-002..007, FR-SCR-012..014 (UI side); UC-SCR-001 (main + alternates a/b), UC-SCR-002 (main); NFR-SCR-002
- Acceptance: TC-SCR-022, TC-SCR-023, TC-SCR-024, TC-SCR-025, TC-SCR-026, TC-SCR-027, TC-SCR-029 — TDD: specs first, then green; specs in `.pipeline/testing/stock-screening.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-2, M-3, M-6 n/a, M-7, M-10), §8.3 (UXR-SCR-001..011/019 row); `03-api-design.md` §4
- UX refs: SCR-003; UXR-SCR-001..011, UXR-SCR-019; UXR-G-001..006, UXR-G-018..024, UXR-G-029, UXR-G-030
- Dependencies: TKT-foundation-011, TKT-scr-002, TKT-scr-003, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-14

## Notes

- Note: TC-SCR-028 and TC-SCR-032 — the full gated-save lifecycle with real registration/verification — land in TKT-int-005.
