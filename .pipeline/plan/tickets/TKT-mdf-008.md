# TKT-mdf-008: Metrics Engine — canonical metrics core

- Status: done
- PR: https://github.com/sarperim/degerli/pull/30
- Size: L
- Scope: `/src/Core` canonical metric formulas (the 18 concepts: valuation/quality/growth/financial-health/dividends families; FCF indirect method; `t_eff` with 0.25 fallback; TTM = FY with ΣQ consistency) and the `metrics-recompute` job writing window-suffixed metric codes (26 codes, e.g. `rev_cagr_3y` — Q5 resolution) into `derived_metrics` with `window_years`, `is_adjusted`, `is_rested`, idempotent on `(instrument, metric, as_of_date)`; NULL not-meaningful rules per `02` §6.2 (never 0-for-missing). Establishes the job/registration conventions snapshot/medians/baseline jobs extend. Must NOT touch adjustment-factor math (TKT-mdf-009) or snapshot/medians computation (MOV/RES tickets).
- Traces to: FR-MDF-013, FR-MDF-014; UC-MDF-005 (main + short-window); NFR-MDF-002; BR-MDF-009
- Acceptance: TC-MDF-026, TC-MDF-027, TC-MDF-028, TC-MDF-031, TC-MDF-033, TC-MDF-034 — TDD: tests first, then green; specs + golden values in `.pipeline/testing/market-data-foundation.md` and FU §11.1/§11.2 (hand-derived; never re-derived from the implementation).
- Architecture refs: `01-system-architecture.md` §3 (C3b), §5 (shared quantitative core), §7 (FR-MDF-013/014 rows), AD-06; `02-data-model.md` §3.1 (`derived_metrics`), §6.1–§6.2
- UX refs: —
- Dependencies: TKT-mdf-002, TKT-mdf-003, TKT-mdf-004
- Parallel group: P-10
