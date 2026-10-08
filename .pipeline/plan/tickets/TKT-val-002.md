# TKT-val-002: DCF model core math (pure /src/Core)

- Status: done
- PR: https://github.com/sarperim/degerli/pull/12
- Size: M
- Scope: the DCF fair-value model as pure functions in `/src/Core` (AD-08): PV_explicit, TV (requires r > g_t), EquityValue = PV + TV_pv + Cash − Debt (**`debt` = total debt ST+LT** — F-VAL-1 ruling), FairValuePS, MOS, the 5×5 sensitivity grid (axes discount_rate × terminal_growth, step 0.01 centered on user values, r ≤ g_t cells → NULL per I-VAL-1), not-computable constraints (r ≤ g_t; horizon outside 1..10), full determinism (no state, clock, or randomness). No DB, no HTTP.
- Traces to: FR-VAL-003/005/006/007 (math); BR-VAL-002/008/009; NFR-VAL-004; SD-001
- Acceptance: TC-VAL-001, TC-VAL-002, TC-VAL-003, TC-VAL-004 — TDD: tests first, then green; golden values hand-derived in `.pipeline/testing/valuation-dcf.md` §1 + FU §11.5 (FVPS 13.1969, MoS −0.5155, corners 24.0000 / **8.99623** — the corrected v1.1 corner; never re-derive from the implementation).
- Architecture refs: `02-data-model.md` §6.4 (formula + confirmed 8-parameter set); `01-system-architecture.md` AD-08, §5 (shared quantitative core), §7 (FR-VAL rows); `03-api-design.md` §5
- UX refs: — (pure math)
- Dependencies: TKT-foundation-001, TKT-foundation-006
- Parallel group: P-9
