---
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/KapPublicReportModels.cs
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - tests/Api.IntegrationTests/RealSourceStatementsTests.cs
  - tests/Api.IntegrationTests/StatementNormalizationTests.cs
  - tests/Api.IntegrationTests/StatementVersioningTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
  # 2026-10-25 SCOPE AMENDMENT (user-supplied pre-dispatch intelligence, live-verified against
  # ASELS index 1643141): an HTML parser package (AngleSharp or HtmlAgilityPack — implementer's
  # choice, stated in the PR) MAY be added. Without it, nested-table label/value extraction is a
  # fragile hand-written parser (verified: simple regex row matching fails). The package addition
  # touches the owning .csproj, which the original scope omitted — planning defect, same class as
  # 079's G-2 (scope derived from where logic lives, not where the effect happens).
---

# TKT-foundation-081: Public-site FR report decoder — the excel-HTML tables onto the canonical statement model, **including the cash-flow statement (supersedes TKT-foundation-051)**

- Status: todo
- Size: L
- **Priority: P1 — lane ticket 3/6 and the user's headline goal (current fundamentals; the CF statement is the 051/DCF/SNA unblock).**
- **Supersession decision (planner, 2026-10-25 — recorded on 051's file too):** 051 is **superseded by this ticket, not duplicated.** Reasons: (a) 051's own verified evidence shows the VYK dev archive carries **zero cash-flow statements** (72 IS / 84 BS / 74 EQ) — the paced VYK capture set 051 waited for cannot exist; (b) the public-site excel-HTML report carries the full indirect-method CF statement (`Nakit Akış Tablosu (Dolaylı Yöntem)`, current + comparative — investigation-verified, line ~887 of the flattened text with the hedging note family); (c) 051's pinned approach ("extend `KapXbrlStatementDecoder.ConceptMap` from captured shapes") was VYK-presentation-tree-shaped — this source is HTML tables, a different decoder with its own label→canonical map; duplicating the CF mapping in two decoders would violate the one-definition doctrine (`02` §6.1 collapses spellings onto the canonical chart — both maps target the same canonical codes, `StatementMapper` untouched); (d) **planning defect found and repaired:** the TC-MDF-089/090 IDs 051 referenced were never authored in the test plan (verified by search — the v1.12 precedent class); the goal is authored as **TC-MDF-108**. The VYK `ConceptMap` CF extension remains honestly impossible without real VYK CF shapes (never guessed spellings); if production VYK ever arrives carrying CF data, that is a new ticket from real captures.
- **What to implement (capture-first, never guessed):** the excel-HTML report decoder (new files under `src/Ingestion/Statements/` — additive; the VYK/XBRL decode paths untouched): header metadata decode (`Gönderim Tarihi`, `Bildirim Tipi`, `Yıl`, `Periyot`, `Sunum Para Birimi` — **`1.000 TL` → `SourceUnitScale = thousands`**, `Finansal Tablo Niteliği` Konsolide, `Cari`/`Önceki Dönem` period ends); statement typing per table heading (IS/BS/CF/EQ); the label→canonical map built **only** from labels present in 079's committed captures (map provenance recorded — which capture supplied which spelling); Turkish number parsing (`.` thousands / `,` decimal, presentation unit) pinned by fixture goldens; the honesty rules — every decoded line traceable to its table+label, unattributable value = honest-null drop with a counted reason (never a guess, never zero), unmapped label = `OTHER_*` passthrough, duplicate-label rule per the TC-MDF-065 doctrine (equal collapse / unequal quarantine); the **CF statement decode** (OCF/CAPEX + the companions genuinely present — never invented) feeding the existing FCF formula (`CanonicalMetrics`, NULL-if-missing preserved) and the SNA supply.
- **Pinned non-scope (flow-back rule):** if the captured report carries evidenced share-count lines (pay adedi / `SHARES_OUT` material), **record the finding and route it to TKT-foundation-065** (its Option B/C decision) — this ticket never consumes, divides or relabels a share count.
- Traces to: FR-MDF-002/003 (statement ingest incl. CF), FR-MDF-008 (provenance `kap-public`), FR-MDF-012 (quarantine), FR-RES-005..018 (SNA/FCF display supply), FR-VAL-001 (DCF baseline — FCF), `02` §3.1 (unit/currency normalization) + §6.1 (concept-map doctrine, extended to the HTML label space), `CanonicalMetrics` FCF v1.3.
- Acceptance: **TC-MDF-106, TC-MDF-107, TC-MDF-108** (test plan §22) — L2 doubles only (the committed excel-HTML capture is the entire input); red→green recorded (red today: zero CF rows anywhere in the store — 051's evidence). Non-regression: `RealSourceStatementsTests`, `StatementNormalizationTests`, `StatementVersioningTests`, `VykStatementsSourceTests` re-run green with pre-listed additive deltas only (re-ingest of an identical report is a no-op; a changed report versions per the existing rules — no silent rewrites). **Live criterion (orchestrator, post-merge, gated on 082 wiring):** one real current report decoded end-to-end (a second live capture beyond 1643141), OCF/CAPEX rows landed, FCF/SNA populated for that company; recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.19 (decoder subsection — landed by this ticket) + §13.1 **v1.5.55** (soft reservation; re-pin at merge); test plan §22 (TC-MDF-106/107/108).
- UX refs: none (decoder lane; the SNA column's presentation already exists).
- Dependencies: TKT-foundation-079 (the committed excel-HTML capture + client/config). 080 is NOT a dependency (the decoder is capture-driven; identity is consumed by the walk in 082).
- Parallel group: none (shares the two permanent docs with the lane; last-lander rebase).

## Pre-dispatch intelligence (user-supplied AI report, 2026-10-25; live-verified against index 1643141)

**De-risking discovery:** every value cell in the excel-HTML export carries the **raw integer in
an attribute** (`title="39468926"` beside the displayed `39.468.926`). The decoder reads the
attribute directly; displayed text is a cross-check only. **Turkish number-format parsing — the
riskiest part of the original ticket — is removed.** Verified real values (thousand TL): OCF
`21,061,426`, CAPEX `-17,190,837`, revenue `88,494,252 / 70,956,004 / …`, total assets
`549,748,035 / 508,228,606`.

**Fixture state:** 079's committed `KapPublicReportHtml` double is a bounded-trim of this exact
report — 630 of 3,484 value cells, including the OCF and CAPEX labels. Red-green doubles are
already in the repo.

**Known structural work (the coder will hit these):**
1. **No HTML parser referenced anywhere in the project** — resolved by the scope amendment above
   (AngleSharp or HtmlAgilityPack; nested tables defeat regex row matching — verified).
2. **Column→period mapping must come from column headers, not position** — value columns are
   marked by CSS class (`col-order-class-4/-5/-6/-7`); income statement has 4 columns (half-year
   current/prior + quarter current/prior), balance sheet 2.
3. **Duplicate labels across statements** — `Dönem Karı (Zararı)` appears in both the income
   statement and the cash-flow statement (0-valued there). Each line must be assigned to its
   SECTION before label mapping, or the duplicate-label quarantine rule discards legitimate
   values.
4. **Cash-flow is cumulative year-to-date** — a half-year report carries 6 months of CF.
   Quarterly/TTM free cash flow requires subtraction between reports. **Handling pinned by user
   decision — see the decision record below (do not guess).**
5. **The export still uses the old GWT CSS classes** (the ones pykap targeted). Served today,
   but a KAP redesign of this export breaks the decoder — the 068 source-contract drift probes
   must cover this export (follow-up slice owed; not this ticket's scope).

## User decision — cash-flow basis (2026-10-25, pinned before dispatch)

**Annual FCF + labeled interims.** DCF baselines use the most recent ANNUAL report's full-year
FCF (e.g. ASELS 2025 Yıllık, index 1561039, filed 24.02.2026). Interim reports populate the
financials display with honest period labels ("6 months ended 30.06.2026") — never annualized,
never silently mixed. TTM-by-subtraction is explicitly a future refinement, not this ticket.
The decoder must therefore surface the **report period type** (Yıllık / 6 Aylık / 3 Aylık) from
the report metadata so downstream consumers can select annuals and label interims — the row
shape already carries `year`/`ruleType`/`period` from byCriteria.
