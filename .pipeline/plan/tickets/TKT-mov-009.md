---
scope:
  - src/Api/Admin/
  - src/Ingestion/Macro/MacroStore.cs
  - src/Ingestion/Macro/MacroModels.cs
  - tests/Api.IntegrationTests/AdminMacroEntryTests.cs
  - tests/Api.IntegrationTests/MacroIngestionTests.cs
---

# TKT-mov-009: Manual independent-CPI entry (admin)

- Status: blocked — **USER DECISION 2026-10-23 (Option A): stays parked.** The ENAG-class independent inflation source webpage itself is unreachable for now ("even the webpage is unreachable"); nothing depends on it and SCR-001 serves official CPI only. Re-probe the source once before release; do not build a scraper against an unreachable site.
- Size: S/M
- Blocker: the ENAG-class independent inflation source is currently unreachable (builder observation 2026-10-15); the manual-vs-scrape approach is to be re-confirmed once the source site is back up. Builder decision D4 (2026-10-15). Contained — nothing depends on this ticket; meanwhile SCR-001 shows official CPI only with the existing "independent measure unavailable" note (UXR-MOV-008). **The v1.4 design record (manual admin entry) is provisional until the source is reachable again.**
- Scope: the manual path for the independent inflation measure — `POST /api/v1/admin/macro/indep-cpi {valueDate, value}` (builder-only) appending a `macro_values` row for `INDEP_CPI` (canonical = latest `recorded_at`), replacing an impossible automated scraper (ENAG-class sources have no API/stable HTML). The SCR-012 entry control is a deferred alignment ticket (server-side lands here). Must NOT touch the EVDS adapter (TKT-mov-008).
- Traces to: FR-MOV-008, FR-MOV-014; UC-MOV-002, UC-MOV-003; BR-MOV-002
- Acceptance: TC-MOV-027 — TDD: tests first, then green; spec in `.pipeline/testing/market-overview.md` §7.
- Architecture refs: `03-api-design.md` §8 (admin endpoint); `.pipeline/analysis/market-overview.md` OQ-MOV-001; `02-data-model.md` §3.2 (`macro_values` revision rule)
- UX refs: SCR-012 (admin entry control — **ux-designer flow-back held with this ticket**)
- Dependencies: none
- Parallel group: none (blocked)
