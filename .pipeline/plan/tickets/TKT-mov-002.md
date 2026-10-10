# TKT-mov-002: Macro ingestion jobs

- Status: done — https://github.com/sarperim/degerli/pull/29
- Size: L
- Scope: `/src/Ingestion` macro adapters + jobs on the TKT-mdf-002 adapter framework and TKT-mdf-005 scheduler: `macro-daily` (USD_TRY, EUR_TRY, GOLD — daily by 09:00 next day), `macro-cpi` (TÜİK + independent measure, within 24h of release), per-release CBRT repo rate; revisions append (never overwrite), canonical = latest `recorded_at`; per-series staleness + failure alerts (mail double + Serilog). Real sources (TÜİK/ENAG/CBRT/FX/gold) are builder V0 validation; tests run against WireMock. Must NOT touch the snapshot job or public endpoints.
- Traces to: FR-MOV-014, FR-MOV-015, FR-MOV-016; UC-MOV-002 (main + alternates), UC-MOV-005; NFR-MOV-001 (macro rows); FR-MDF-015 (revision append — MDF-plan-owned TC below)
- Acceptance: TC-MOV-006, TC-MOV-007, TC-MOV-008, TC-MOV-009, TC-MOV-010, TC-MOV-011 + **TC-MDF-035** (MDF-plan-owned: facts never overwritten, canonical = latest, both rows retained — asserted here through the macro revision path) — TDD: tests first, then green; specs in `.pipeline/testing/market-overview.md` and `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-MOV-014..016 rows), §9 (NFR-MDF-001 macro rows); `02-data-model.md` §3.2 (`macro_series`, `macro_values`), §5.6; `03-api-design.md` §9 (macro jobs)
- UX refs: — (headless; display side in TKT-mov-004/006)
- Dependencies: TKT-mdf-005, TKT-foundation-006, TKT-foundation-008
- Parallel group: P-9

## Notes

- Note: the statement-side of TC-MDF-035 is cross-covered by TC-MDF-032, gated in TKT-mdf-009.
