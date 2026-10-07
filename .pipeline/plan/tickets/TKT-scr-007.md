# TKT-scr-007: SCR-004 My Saved Screens UI

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-004 route per the TKT-scr-001 design: signed-in list by name, re-run (fresh results in the screener context, never stored results), rename in place with duplicate inline error, delete behind focus-trapping confirmation, dropped-criterion notice on re-run, empty + anonymous states, query invalidation per the mutation matrix (M-2). Must NOT touch the SCR-003 screen files beyond the agreed active-screen display contract.
- Traces to: FR-SCR-008..011 (UI side); UC-SCR-003 (main + alternate a); NFR-SCR-005
- Acceptance: TC-SCR-030, TC-SCR-031 — TDD: specs first, then green; specs in `.pipeline/testing/stock-screening.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-2), §8.3 (UXR-SCR-012..018 row), §8.4 (invalidation contract); `03-api-design.md` §4
- UX refs: SCR-004; UXR-SCR-012, UXR-SCR-013, UXR-SCR-014, UXR-SCR-015, UXR-SCR-016, UXR-SCR-017, UXR-SCR-018; UXR-G-005, UXR-G-022, UXR-G-029
- Dependencies: TKT-scr-004, TKT-scr-006
- Parallel group: P-15
