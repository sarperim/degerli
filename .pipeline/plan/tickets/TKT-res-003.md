# TKT-res-003: Overview & valuation section endpoints + sector medians job

- Status: todo
- Size: L
- Scope: `/src/Api` stocks module: `GET /stocks/{symbol}/overview` (published description in both languages, `preparing` state, `lastReviewedAt`) and `GET /stocks/{symbol}/valuation` (current multiples, historical series with `adjusted: true`, vs.-sector strip `{value, sectorMedian, peerCount, excludedCount}` + not-meaningful states); plus the `medians` job (own job files per the TKT-mdf-008 convention) computing `sector_metric_medians` — five valuation-family metrics, sector-level peers, current basis, P/E medians excluding loss-makers with counts. Must NOT touch the other section endpoints or the C4 CLI.
- Traces to: FR-RES-005, FR-RES-006, FR-RES-007, FR-RES-022, FR-RES-026; SD-002; UC-RES-002 (main + alt a), UC-RES-003; FR-MDF-018 (serving side)
- Acceptance: TC-RES-005, TC-RES-006, TC-RES-007, TC-RES-008 + **TC-MDF-030** (MDF-plan-owned: EPSL raw-vs-adjusted series divergence asserted through the valuation endpoint contract, `adjusted: true` on historical payload) — TDD: tests first, then green; goldens in `.pipeline/testing/stock-research.md` + FU §11.4.
- Architecture refs: `03-api-design.md` §3 (overview + valuation endpoints); `01-system-architecture.md` §7 (FR-RES-005..007 rows), §8.1 (M-1), §4 (RES row — medians computed by C3b, served by stocks module); `02-data-model.md` §3.4 (`business_descriptions`, `sector_metric_medians`), §5.1/§5.2, §6.3
- UX refs: UXR-RES-025 (not-meaningful payload contract)
- Dependencies: TKT-mdf-008, TKT-mdf-009, TKT-foundation-007
- Parallel group: P-12
