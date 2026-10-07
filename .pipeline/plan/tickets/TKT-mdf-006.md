# TKT-mdf-006: Validation & quarantine pipeline

- Status: todo
- Size: M
- Scope: the validation framework behind ingestion: reason-code catalog per FU §2 (`NEGATIVE_PRICE`, `MISSING_PROVENANCE`, `SCHEMA_MISMATCH`, `UNPARSEABLE_PAYLOAD`, `MISSING_CLASSIFICATION`, `CONFLICTING_VALUE`), partial-batch behavior (valid items in a payload ingest while invalid ones quarantine), `quarantined_facts` payload retention, alert-on-quarantine (mail double + Serilog event). Builds on the quarantine write path started in TKT-mdf-002. Must NOT touch admin endpoints (TKT-mdf-010/011).
- Traces to: FR-MDF-012; UC-MDF-001 (alternate b); BR-MDF-007 (recorded-gap posture)
- Acceptance: TC-MDF-019, TC-MDF-020 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §3 (C3a validation), §7 (FR-MDF-012 row), AD-13 (quarantine store); `02-data-model.md` §3.1 (`quarantined_facts`)
- UX refs: — (queue UX is SCR-012, built in TKT-mdf-011/012)
- Dependencies: TKT-mdf-002
- Parallel group: P-9
