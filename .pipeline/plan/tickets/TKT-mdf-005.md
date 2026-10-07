# TKT-mdf-005: Scheduler, run ledger, retry ladder & alerting

- Status: todo
- Size: M
- Scope: C3c in-process `IHostedService` workers + Cronos cron (20:30 Europe/Istanbul, trading days, holiday calendar as config) with the DB run ledger (`ingest_runs`); retry ladder 5/15/60 min with backoff; C3d alerting (builder e-mail via SMTP + Serilog alert events); last-known-good posture groundwork via `v_data_freshness`. Job codes registered per the TKT-mdf-002 registration convention. Must NOT implement the EOD chain's cross-domain job order (that wiring is TKT-int-001).
- Traces to: FR-MDF-009; NFR-MDF-001; UC-MDF-001 (alternate a)
- Acceptance: TC-MDF-011, TC-MDF-012 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §3 (C3c, C3d), §5 (background jobs row), §7 (FR-MDF-009 row), §9 (NFR-MDF-001 translation); `02-data-model.md` §3.1 (`ingest_runs`, `v_data_freshness`); `03-api-design.md` §9
- UX refs: —
- Dependencies: TKT-mdf-002
- Parallel group: P-8

## Notes

- Note: TC-MDF-010 (full EOD chain) and TC-MDF-013/014 (stale serving through the public endpoint) land in TKT-int-001 once snapshot/medians/macro jobs and the MOV endpoints exist.
