# TKT-mdf-004: Universe, sectors, indices & constituent sync

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/21
- Size: M
- Scope: `/src/Ingestion` `universe-sync` job: instruments, sectors (bilingual labels, 2-level hierarchy), indices, effective-dated append-only `index_constituents`, `index_levels` sync, sector reclassification handling, unclassified-sector anomaly flagging. Must NOT touch prices/statements adapters or the scheduler.
- Traces to: FR-MDF-006, FR-MDF-007; UC-MDF-003 (main + alt a); BR-MDF-005
- Acceptance: TC-MDF-007, TC-MDF-025, TC-MDF-037, TC-MDF-038 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-MDF-006/007 rows); `02-data-model.md` §3.1 (`instruments`, `sectors`, `indices`, `index_constituents`, `index_levels`, `v_current_universe`)
- UX refs: —
- Dependencies: TKT-mdf-002
- Parallel group: P-9

## Notes

Note: TC-MDF-037's effective_to-close mechanism is the implementation's choice (deferred flag Q6) — the observable history contract is what is asserted.
