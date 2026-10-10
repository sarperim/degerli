# TKT-mdf-007: Backfill mode & coverage metadata recording

- Status: done
- PR: https://github.com/sarperim/degerli/pull/28
- Size: M
- Scope: backfill mode in the adapters (historical range ingestion with `{backfillFrom}` semantics at the job level), achieved-depth recording into `coverage_metadata` (never fabricating rows before the source limit), coverage notes for source limitations, and the no-silent-gaps consistency rule (every instrument × data type has data or an explicit gap record). Must NOT implement the admin HTTP trigger (that endpoint is TKT-mdf-010, which carries TC-MDF-015/043).
- Traces to: FR-MDF-010, FR-MDF-011 (recording side); UC-MDF-002 (main + alt a); NFR-MDF-003
- Acceptance: TC-MDF-016, TC-MDF-018 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `01-system-architecture.md` §7 (FR-MDF-010/011 rows); `02-data-model.md` §3.1 (`coverage_metadata`)
- UX refs: —
- Dependencies: TKT-mdf-002, TKT-mdf-003, TKT-mdf-004
- Parallel group: P-10
