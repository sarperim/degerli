# TKT-val-006: SCR-006 DCF Calculator UI — core

- Status: todo
- Size: L
- Scope: `/src/Web` SCR-006 route per the TKT-val-001 design: opens with baseline + immediate result (price w/ as-of, plain-language comparison), all 8 parameters visible/editable with canonical-fact default labels + restatement markings, recompute on change (debounced ~300 ms through the compute endpoint — no client-side model), invalid input → inline validation + last valid result retained + recompute suppressed, unsaved-changes indication, plain-language verdict + disclaimer + "based on your assumptions" framing (TR/EN), sensitivity grid with NULL cells rendered honestly, stale price marker, draft preservation (M-3). Must NOT implement scenario save/load flows (TKT-val-007).
- Traces to: FR-VAL-001..008 (UI side); UC-VAL-001 (main + alternates b/c); NFR-VAL-001, NFR-VAL-004; SC-004
- Acceptance: TC-VAL-017, TC-VAL-018, TC-VAL-019, TC-VAL-020, TC-VAL-023 — TDD: specs first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-1, M-3, M-6), §8.3 (UXR-VAL-001..011 row); `03-api-design.md` §5
- UX refs: SCR-006; UXR-VAL-001..011; UXR-G-003, UXR-G-018, UXR-G-019, UXR-G-020, UXR-G-021, UXR-G-025..028
- Dependencies: TKT-foundation-011, TKT-val-003, TKT-val-004, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-15
