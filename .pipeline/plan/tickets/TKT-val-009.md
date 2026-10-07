# TKT-val-009: DCF baseline regeneration — admin API & admin UI section

- Status: todo
- Size: M
- Scope: `/src/Api` `POST /api/v1/admin/dcf-baselines/regenerate` (builder-gated; triggers the C3b regeneration job — version bump, one active per stock, previous versions retained, run visible in the ledger) and the `/src/Web` admin baseline-regeneration section (own per-section files on the TKT-mdf-012 shell): confirmation stating rebuild-from-canonical-facts + versioning, cancel aborts, pending lock, run visible in ledger, SCR-006 serves the new active baseline on next load. Must NOT touch other admin sections or the calculator.
- Traces to: UC-VAL-003; UXR-MDF-025; BR-VAL-008; `02` §5.8
- Acceptance: TC-VAL-015, TC-VAL-016 — TDD: tests first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `03-api-design.md` §8 (regenerate endpoint); `02-data-model.md` §5.8, §3.5; `01-system-architecture.md` §7 (VAL rows), AD-13
- UX refs: SCR-012 (DCF baseline regeneration section); UXR-MDF-025
- Dependencies: TKT-mdf-010, TKT-mdf-012, TKT-val-003
- Parallel group: P-16
