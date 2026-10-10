# TKT-mdf-009: Adjustment factors & restatement marking

- Status: in-progress
- Size: M
- Scope: corporate-action adjustment factors in `/src/Core` (split 1/n; bonus 1/(1+b); rights TERP `(P_C + q·P_S)/((1+q)·P_C)` per FU §2/Q3 — including degenerate-input rejection; multiplicative composition), `close_adjusted` computation over the price series, and restatement handling in metrics (latest-restated serves, `is_rested` propagation, as-reported retained). Must NOT touch the public series endpoint (TC-MDF-030 lands with the valuation contract in TKT-res-003).
- Traces to: FR-MDF-018, FR-MDF-019; BR-MDF-010, BR-MDF-011
- Acceptance: TC-MDF-029, TC-MDF-032, TC-MDF-053 — TDD: tests first, then green; specs + goldens in `.pipeline/testing/market-data-foundation.md`, FU §11.6.
- Architecture refs: `01-system-architecture.md` §7 (FR-MDF-018/019 rows); `02-data-model.md` §3.1 (`daily_prices`, `corporate_actions` — adjustment-factor system), §5.7
- UX refs: —
- Dependencies: TKT-mdf-003, TKT-mdf-008
- Parallel group: P-11
