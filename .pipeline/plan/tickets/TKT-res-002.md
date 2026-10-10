# TKT-res-002: Stock list & search endpoints

- Status: done
- PR: https://github.com/sarperim/degerli/pull/27
- Size: M
- Scope: `/src/Api` stocks module: `GET /api/v1/stocks?sector=&q=` (current-universe view: symbol, name, sector identity, listingDate; ILIKE on name/symbol; unknown sector → empty 200) and `GET /api/v1/stocks/search?q=` (lightweight symbol + name for header typeahead). Must NOT touch the stock-page section endpoints.
- Traces to: FR-RES-001..004; UC-RES-001 (main + alt a)
- Acceptance: TC-RES-001, TC-RES-002, TC-RES-003, TC-RES-004 — TDD: tests first, then green; specs in `.pipeline/testing/stock-research.md` (I-RES-4: sector identity asserted as identity, not serialization).
- Architecture refs: `03-api-design.md` §3 (stocks + search endpoints); `01-system-architecture.md` §7 (FR-RES-001..004 rows); `02-data-model.md` §3.1 (`v_current_universe`)
- UX refs: —
- Dependencies: TKT-mdf-004, TKT-foundation-004
- Parallel group: P-11
