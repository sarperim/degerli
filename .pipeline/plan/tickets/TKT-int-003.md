# TKT-int-003: Global UX behavior sweep (language, disclaimers, a11y, responsive, feedback)

- Status: todo
- Size: L
- Scope: the cross-screen L4/L3 specs for global behaviors: TR default on first visit; language switch preserving context across data-bearing screens; language persistence (device + account); disclaimer sweep across the seven research surfaces; loading/pending feedback spot checks; plain-language error rendering with no technical leakage (5xx); axe-core zero-known-violations gate on key screens; responsive operability at 375px; keyboard-only journeys; no-real-time framing probes on market-data surfaces. Test specs + defect fixes only.
- Traces to: UXR-G-001..014, UXR-G-016, UXR-G-017 (cross-screen form), UXR-G-025..028; NFR-MOV-002/004/006; strategy decision C
- Acceptance: TC-XC-003, TC-XC-004, TC-XC-005, TC-XC-006, TC-XC-012, TC-XC-013, TC-XC-017, TC-XC-019, TC-XC-020, TC-XC-021 — TDD: specs first, then green; specs in `.pipeline/testing/cross-cutting.md`.
- Architecture refs: `01-system-architecture.md` §8.2 (global rules table), §6.1 (e2e job), §5 (a11y via shadcn/Radix); `03-api-design.md` §1 principle 1
- UX refs: UXR-G-001..017, UXR-G-025..028 (as cross-screen sweeps)
- Dependencies: TKT-mov-005, TKT-mov-006, TKT-scr-006, TKT-scr-007, TKT-res-007, TKT-res-008, TKT-res-009, TKT-val-006, TKT-val-007, TKT-val-008, TKT-acc-007, TKT-acc-008, TKT-acc-009, TKT-acc-010, TKT-mdf-012, TKT-foundation-010
- Parallel group: P-18
