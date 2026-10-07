# TKT-mov-007: Sector performance period selector (Could — implement last)

- Status: todo
- Size: S
- Scope: FR-MOV-017 (Could): `period=1w|1m|ytd` parameter on the sector-performance block — snapshot period aggregates, selector UI on SCR-001, invalid period → 400. **Blocked-on-formula flag F-MOV-1 (accepted by the builder 2026-10-07):** the period-aggregate formula is undefined; the recommended formula (cap-weighted total return over the period, mirroring the locked daily formula) is adopted only when the feature is promoted; fixture history table + goldens are added at that time. TC-MOV-015 stays contract-only until then.
- Traces to: FR-MOV-017 (Could); UXR-MOV-012
- Acceptance: TC-MOV-015 (contract-only: parameter plumbed, envelope served, 400 on invalid period — golden values deferred per F-MOV-1) — TDD: contract test first, then green; spec in `.pipeline/testing/market-overview.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-MOV-017 row — "implement last"); `03-api-design.md` §3 (period param)
- UX refs: SCR-001; UXR-MOV-012
- Dependencies: TKT-mov-003, TKT-mov-004, TKT-mov-005
- Parallel group: P-18
