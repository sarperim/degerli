# TKT-val-007: SCR-006 UI — scenario save/load flows & missing inputs

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-006 scenario flows on the TKT-val-006 screen: save (anonymous prompt, unverified gate, duplicate per-stock inline error, pending locks, failure preserves assumptions + name), per-stock scenario picker, load restoring params exactly + recompute, load-over-unsaved-changes discard confirmation, missing-inputs state (explains what is missing; user-supplied values compute). Builds on the TKT-val-006 calculator files. **Should-level flows — deferral is the builder's call, recorded on the ticket.**
- Traces to: FR-VAL-009, FR-VAL-010 (UI side); UC-VAL-001 (alternate a), UC-VAL-002; UXR-VAL-009, 012..015, 021
- Acceptance: TC-VAL-021, TC-VAL-025, TC-VAL-026 — TDD: specs first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-2, M-3, M-7), §8.4 (invalidation contract); `03-api-design.md` §5 (scenarios)
- UX refs: SCR-006; UXR-VAL-009, UXR-VAL-012, UXR-VAL-013, UXR-VAL-014, UXR-VAL-015, UXR-VAL-021; UXR-G-022, UXR-G-023, UXR-G-029, UXR-G-030
- Dependencies: TKT-val-005, TKT-val-006
- Parallel group: P-16

## Notes

- Note: TC-VAL-024 — the full anonymous save→register→verify lifecycle — lands in TKT-int-005.
