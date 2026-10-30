---
scope:
  - src/Ingestion/Disclosures/
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/KapPublicDisclosuresFeedTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-084: Market-wide recent-disclosures feed on the public site — **GATED on the recorded OQ-1 evidence** (byCriteria market-wide shape unverified)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/168
- Size: M
- **Priority: P2 — lane ticket 6/6, explicitly last (the investigation's OQ-3 recommendation: statements/FR first — highest value, clearest parsing; the feed second).**
- **Gate (decision-grade, recorded before any implementation):** the market-wide shape of byCriteria — an **empty** `mkkMemberOidList` returning the market's disclosures — is **UNVERIFIED** (OQ-1). The precondition is TKT-foundation-079's committed probe evidence. **If the probe shows market-wide is not served (an error, an empty body, a silently-ignored filter), this ticket records the finding verbatim, flips to `blocked`, and surfaces to the user — no alternative design (per-company fan-out, scraping HTML pages, third-party mirrors) is invented silently.** The VYK disclosures lane (078's market feed) keeps serving in the meantime; the two feeds coexist as separate sources.
- **What to implement (only if the gate opens):** a public-site disclosures feed as its **own source** alongside the VYK lane — never a repointing of 078's adapter (two-source discipline): one date-watermarked market cursor (the public list is date-windowed, not index-paged; the seen public `disclosureIndex` set dedupes within the source's own space); only bound constituents written (via 080's mapping), the rest counted with the out-of-bound reason and never detail-fetched; every row `SourceRef = kap-public`; an honest empty window persists no backwards cursor; a source-down is `failed`; the walk bounded per the 078/082 budget semantics; the rich byCriteria row (disclosureClass, publishDate, subject, summary, relatedStocks, kapTitle…) mapped into the disclosure metadata store with the public evidence class — **never a VYK index, never the VYK lane's rows or cursors**.
- Traces to: FR-MDF-005 (disclosures ingest — recent KAP data), FR-MDF-008 (provenance), FR-MDF-011/012, NFR-MDF-001; `01` §10.9.15 (the VYK feed this runs beside — untouched).
- Acceptance: **TC-MDF-112** (test plan §25) — L2 doubles only (079's committed market-wide probe body is the double); red→green recorded. Non-regression: `StatementsAndEventsIngestionTests` (incl. the 078 lane TC-MDF-099/100/101), `EodChainTests`, `MorningChainTests` re-run green. **Live criterion (orchestrator, post-merge, only if the gate opened):** one paced feed run — recent market disclosures written for bound constituents, newest within the configured window of "now", second run incremental; recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.19 (feed subsection — landed by this ticket) + §13.1 **v1.5.58** (soft reservation; re-pin at merge); test plan §25 (TC-MDF-112).
- UX refs: none (feed lane; disclosure presentation already exists).
- Dependencies: TKT-foundation-079 (client/config/probe evidence), TKT-foundation-080 (bound-constituent mapping); **serializes behind TKT-foundation-069** (shared `src/Ingestion/Disclosures/` files — dependency edge, not a group).
- Parallel group: none.
