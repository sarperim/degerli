---
scope:
  - src/Ingestion/Statements/StatementsSourceAdapter.cs
  - src/Ingestion/Statements/StatementsJob.cs
  - src/Ingestion/Statements/StatementsModels.cs
  - src/Ingestion/KapPublic/
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-082: Statements lane public-site mode — config-selected source, the bounded byCriteria walk, own cursors, per-company isolation, and the two-source coexistence rule

- Status: todo
- Size: M
- **Priority: P1 — lane ticket 4/6 (the statements lane actually ingesting current data).**
- **What to implement:**
  1. **Source selection by configuration:** `Ingestion:Statements:Source` = `vyk` (the incumbent, stays the shipped default — flipping the dev stack to `kap-public` is the orchestrator's recorded post-merge live step, never a silent default change) | `kap-public`. The VYK adapter stays wired and green — switching back must be a config change, not a rewrite.
  2. **The walk (the 078 `WalkBudget` pattern re-applied):** per bound company (via 080's stored mapping), a byCriteria `disclosureClass="FR"` walk over date windows (`fromDate`/`toDate` — the public list is **date-windowed, not index-paged**; the cursor is a date watermark plus the seen public `disclosureIndex` set within the source's own space); per-company isolation (one unfetchable detail = quarantine with the verbatim body + counted skip, run `partial`; an all-isolated zero-write run is `partial`, never `failed`; a genuine source-down is `failed` — the 063 doctrine); bounded in work and wall clock (page caps + walk deadline < `MaxStepDuration`; truncation persists the last completed unit and resumes exactly there); backfill lookback recorded as achieved-depth provenance (`bootstrapped`-style), never as "no statements exist" for what was not walked.
  3. **Own cursors, never mixed:** the public-site lane's cursor rows are **own** coverage-metadata entries (distinct data-type names from the VYK `kap-disclosure-cursor` rows); a public `disclosureIndex` never appears in a VYK cursor row nor a VYK index in a public-site row — asserted by test (the 1643141 vs 1231017 spaces).
  4. **Decode wiring:** the walk feeds 081's decoder; written statements carry `SourceRef = kap-public` with `SourceUnitScale`/`SourceCurrency` from the header decode; versioning/idempotency per the existing statement-store rules.
  5. **The two-source coexistence rule (landed in `01` §10.9.19 by this ticket):** both sources' facts coexist — no deletion, no cross-source in-place overwrites; for a period both sources cover, the **pinned preference rule** decides the read (public-site current wins for the periods it covers; VYK rows retained and as-of queryable); every row answers its evidence class (`kap` vs `kap-public`); a recompute over the mixed world reports per-source counts honestly.
- Traces to: FR-MDF-002 (statements ingest), FR-MDF-008 (provenance), FR-MDF-010 (backfill/achieved depth), FR-MDF-011 (coverage metadata), FR-MDF-012 (isolation), NFR-MDF-001; `01` §10.9.11 (the VYK lane this runs beside), §10.9.15/§10.9.16 (walk-budget heritage).
- Acceptance: **TC-MDF-109, TC-MDF-110** (test plan §23) — L2 doubles only, through the production `statements` job runner; red→green recorded. Non-regression: `VykStatementsSourceTests`, `StatementsAndEventsIngestionTests`, `StatementsLoopTests`, `EodChainTests`, `MorningChainTests` re-run green (the incumbent source's behaviour is unchanged when unselected). **Live criteria (orchestrator, post-merge):** (i) one paced run with `Source = kap-public` over the bound universe — current statements written for the mapped companies (FY2025/2026 periods visible in the store), honest skips named and counted, the run completes inside its step bound; (ii) a second run is incremental from the watermark; (iii) the served stock financials show the current periods. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.19 (walk + coexistence subsections — landed by this ticket) + §13.1 **v1.5.56** (soft reservation; re-pin at merge); test plan §23 (TC-MDF-109/110).
- UX refs: none (ingest lane; the served presentation already exists).
- Dependencies: TKT-foundation-080, TKT-foundation-081.
- Parallel group: none.
