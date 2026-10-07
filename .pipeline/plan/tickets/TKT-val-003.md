# TKT-val-003: DCF baseline generation & GET endpoint

- Status: todo
- Size: M
- Scope: the baseline generation job (C3b, own job files per the TKT-mdf-008 convention): `dcf_baselines` rows derived from canonical facts (FCF, total debt, cash, share count) + builder default rules for growth/horizon/rates (config), one active version per stock, version bump on regeneration; and `/src/Api` DCF module `GET /api/v1/stocks/{symbol}/dcf` (baseline + params + `canonicalFactRefs` with as-of/restated flags + canonical price; `state: "missing_inputs"` naming what is missing; unknown symbol 404). Must NOT touch the compute endpoint (TKT-val-004) or regeneration admin trigger (TKT-val-009).
- Traces to: FR-VAL-001, FR-VAL-002, FR-VAL-003; UC-VAL-001 (steps 1–2 + alternates a/b/c), UC-VAL-003 (generation side); BR-VAL-001, BR-VAL-008
- Acceptance: TC-VAL-005, TC-VAL-006, TC-VAL-007, TC-VAL-008 — TDD: tests first, then green; specs in `.pipeline/testing/valuation-dcf.md` (FU §11.5 baselines).
- Architecture refs: `02-data-model.md` §3.5 (`dcf_baselines`), §5.8, §6.4; `03-api-design.md` §5 (GET dcf sketch incl. missing_inputs); `01-system-architecture.md` §7 (FR-VAL-001..003 rows)
- UX refs: UXR-VAL-009 (missing-inputs payload contract), UXR-VAL-010 (restated refs contract)
- Dependencies: TKT-val-002, TKT-mdf-008, TKT-mdf-009, TKT-foundation-004
- Parallel group: P-12
