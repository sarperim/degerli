# TKT-mdf-002: Price ingestion adapter & fact-storage invariants

- Status: todo
- Size: L
- Scope: the source-adapter framework in `/src/Ingestion` (adapter interface, HTTP source client, idempotent upserts keyed on the fact PKs, append-only writes, provenance enforcement, conflicting-value detection → `quarantined_facts`) and the `prices` job (EOD prices + volume; real-source wiring against İşbank/KAP is builder V0 validation — automated tests run against WireMock doubles). Establishes the per-job registration-extension convention later jobs follow. Must NOT touch other adapters or the scheduler (TKT-mdf-005).
- Traces to: FR-MDF-001, FR-MDF-008, FR-MDF-015; UC-MDF-001 (steps 1–3)
- Acceptance: TC-MDF-001, TC-MDF-002, TC-MDF-008, TC-MDF-009, TC-MDF-052 — TDD: write these test cases first (red), then implement to green; case specs live in `.pipeline/testing/market-data-foundation.md`; never weaken a case.
- Architecture refs: `01-system-architecture.md` §3 (C3a), §7 (FR-MDF-001/008/015 rows); `02-data-model.md` §3.1 (`daily_prices`, `quarantined_facts`, `ingest_runs`), §1 (principles 1–2); `03-api-design.md` §9
- UX refs: — (headless; user-visible effects are honest-data states tested in their domains)
- Dependencies: TKT-foundation-005, TKT-foundation-006, TKT-foundation-008
- Parallel group: P-7
