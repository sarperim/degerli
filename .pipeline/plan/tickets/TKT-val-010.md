---
scope:
  - src/Ingestion/Valuation/
  - src/Api/Valuation/
  - src/Core/Valuation/
  - tests/Api.IntegrationTests/DcfFinancialSectorTests.cs
  - tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs
  - tests/Api.IntegrationTests/DcfComputeEndpointTests.cs
---

# TKT-val-010: Financial-sector DCF verdict (`DCF_NOT_COMPUTABLE` reason `financial_sector`)

- Status: todo
- Size: S/M
- Scope: the DCF path for financial profiles — the baseline job records `accounting_profile ∈ {bank, insurance}` instruments with a `sector_not_applicable` state (no FCF-derived baseline), and the compute endpoint returns `422 DCF_NOT_COMPUTABLE` with `params.reason = "financial_sector"` + an honest explanation, distinct from the missing-inputs state. Must NOT touch the base-FCF formula (TKT-mdf-017) or the metric gating (TKT-mdf-020).
- Traces to: FR-VAL-001, FR-VAL-003; UC-VAL-001, UC-VAL-003; BR-VAL-001/008
- Acceptance: TC-VAL-028 — TDD: tests first, then green; spec in `.pipeline/testing/valuation-dcf.md` §7.
- Architecture refs: `02-data-model.md` §6.4 (financial-sector exclusion); `03-api-design.md` §5 (DCF compute), §7 (`DCF_NOT_COMPUTABLE` reason)
- UX refs: SCR-006 — sector-specific not-computable state (**ux-designer flow-back pending**: UXR not yet written; a small alignment ticket follows)
- Dependencies: TKT-mdf-015, TKT-mdf-017, TKT-mdf-020
- Parallel group: P-28
