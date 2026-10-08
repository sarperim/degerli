# TKT-res-005: Content pipeline CLI (build-time AI drafting)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/14
- Size: M
- Scope: `/src/ContentPipeline` `draft` command (`docker compose run --rm contentpipeline draft [--symbol=]`): reads MDF `kap_disclosures`, calls the evren API (doubled by recorded fixtures in tests — never in CI), writes bilingual draft rows into `business_descriptions` (status `draft`, version lineage, `source_refs_json` pointing at disclosure ids); drafts never publicly served. Must NOT touch the admin endpoints or the API host.
- Traces to: FR-RES-019; UC-RES-004 step 1; NFR-RES-006; BR-RES-002
- Acceptance: TC-RES-016, TC-RES-017 — TDD: tests first, then green; specs in `.pipeline/testing/stock-research.md` (evren API doubled at the HTTP boundary; drafting logic, status transitions, source-ref recording tested for real).
- Architecture refs: `01-system-architecture.md` §3 (C4), §7 (FR-RES-019 row), §9 (NFR-RES-006); `02-data-model.md` §3.4, §5.5; `03-api-design.md` §9 (AI drafting CLI row)
- UX refs: —
- Dependencies: TKT-foundation-001, TKT-foundation-005, TKT-foundation-006, TKT-foundation-008
- Parallel group: P-11
