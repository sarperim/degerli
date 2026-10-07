# TKT-int-001: EOD chain orchestration & stale-serving lifecycle

- Status: todo
- Size: M
- Scope: cross-domain wiring of the full EOD chain on the TKT-mdf-005 scheduler: prices → statements/dividends/corporate-actions/disclosures → metrics-recompute → snapshot → medians (plus macro jobs at their cadences), run in order each trading day with run-ledger rows per job; the exhausted-retries → last-known-good → stale-served → recovery cycle observed through the public `/market/overview` endpoint. Touches only scheduler wiring/registration and the cross-domain test specs. Must NOT change individual job logic.
- Traces to: FR-MDF-009 (chain), FR-MDF-016 (stale serving); UC-MDF-001 (main + alternate a end-to-end); NFR-MDF-001; `03` §9 chain
- Acceptance: TC-MDF-010, TC-MDF-013, TC-MDF-014 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md` Group B.
- Architecture refs: `03-api-design.md` §9 (EOD ingest chain row); `01-system-architecture.md` §3 (C3c), §7 (FR-MDF-009/016 rows), §9 (NFR-MDF-001)
- UX refs: UXR-G-008 (stale marking, end-to-end side)
- Dependencies: TKT-mdf-005, TKT-mdf-002, TKT-mdf-003, TKT-mdf-004, TKT-mdf-007, TKT-mdf-008, TKT-mov-002, TKT-mov-003, TKT-res-003 (medians job), TKT-mov-004
- Parallel group: P-17
