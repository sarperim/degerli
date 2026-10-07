# TKT-val-008: SCR-011 My DCF Scenarios UI (Should)

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-011 route per the TKT-val-001 design: signed-in scenarios grouped by stock, activating one opens that stock's calculator loaded with it, rename in place with duplicate inline error, delete behind focus-trapping confirmation, anonymous sign-in path, empty guidance state, invalidation of the calculator picker (M-2). **Should-level screen (SCR-011) — deferral is the builder's call, recorded on the ticket.**
- Traces to: UC-VAL-002; FR-ACC-005 (entry point); Gate 1 decision C (rename/delete as Should)
- Acceptance: TC-VAL-022, TC-VAL-027 — TDD: specs first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-2), §8.3 (UXR-VAL-016..020 row), §8.4; `03-api-design.md` §5
- UX refs: SCR-011; UXR-VAL-016, UXR-VAL-017, UXR-VAL-018, UXR-VAL-019, UXR-VAL-020; UXR-G-005, UXR-G-022, UXR-G-029
- Dependencies: TKT-val-007
- Parallel group: P-17
