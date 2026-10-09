# TKT-val-004: DCF compute endpoint

- Status: done
- PR: https://github.com/sarperim/degerli/pull/23
- Size: M
- Scope: `/src/Api` DCF module `POST /api/v1/stocks/{symbol}/dcf/compute`: pure, anonymous, stateless, `no-store`; single response path for point result + sensitivity grid; input validation (400 `VALIDATION_FAILED` w/ `params.fields[]` for non-numeric/missing/out-of-range magnitudes) vs math constraints (422 `DCF_NOT_COMPUTABLE` naming the violated constraint — horizon outside 1..10 is 422 per I-VAL-2); canonical price from `daily_prices` (same source as every surface); no persistence. Must NOT touch the GET endpoint or scenario endpoints.
- Traces to: FR-VAL-005; UC-VAL-001 (steps 3–4); NFR-VAL-003; AD-08
- Acceptance: TC-VAL-009, TC-VAL-010, TC-VAL-011, TC-VAL-012 — TDD: tests first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `03-api-design.md` §5 (compute request/response, 422 contract), §13 (`no-store`); `01-system-architecture.md` AD-08, §7 (FR-VAL-005 row)
- UX refs: —
- Dependencies: TKT-val-002, TKT-mdf-002, TKT-foundation-004
- Parallel group: P-11
