---
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/KapPublicReportModels.cs
  - src/Ingestion/Degerli.Ingestion.csproj
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

- Status: done — **PR #145 squash-merged 2026-10-26 ~00:55 UTC (`911f688` on dev), final head `db6729e`, MERGEABLE/CLEAN, CI green (round 4).** Review: 4 rounds, 3 fixer dispatches (rounds 1–3 artefacts + round-4 DR-2 decision round, `reviews/TKT-foundation-081-round-{1,2,3,4}.md`; round 4 = merge basis, user-authorized past the standard cap per the 074 precedent). **DR-2 DECIDED by user (2026-10-26): OCF = section total `İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI` (after tax and other operating payments); before-tax subtotal stays decoded as its own labeled line (`OTHER_CF_OPERATING_Faaliyetlerden_…`) but does not feed OCF/FCF.** Goldens re-derived from the fixture: OCF 15.206.440 / 18.013.334; FCF −18.539.710 / 822.497 (each needle unique in the capture). **FIXTURE LIMITATION (recorded, do not paper over): the capture is a 6-month interim (`Periyot:2`) — it has NO annual column; the annual-report golden (ASELS 2025 Yıllık, index 1561039) is a LIVE-CRITERION item.** All round-1–3 findings closed and verified in code (title= read path with display fallback removed and tested; annual fail-safe both directions incl. the duration-guard `declared ∈ observed` semantics; section-before-label; header-driven periods shift-guarded; the 98 no-header lines derived and honest). RR-1/2/3 landed (test plan v1.27: 19 cases + 3 reason codes; FY success-direction test; FromAttribute repaired). **Owed residue:** RR-4 stale PR-body sentence (fixed by orchestrator pre-merge via PR body edit — the merge record is corrected); UPSTREAM-1 (per-line provenance in `fin_line_items` = additive schema follow-up); share counts routed to 065; 068 drift probes must cover the excel-HTML export route. **Live criterion (orchestrator-owned, OPEN):** decode the annual report 1561039 through the merged decoder — one paced fetch (≥3 s discipline, single-flight, honest UA), derive the annual-column OCF/FCF goldens from its own cells, record verbatim; also validates the annual-attestation path end-to-end.
- Size: L
- PR: https://github.com/sarperim/degerli/pull/145 (branch `feature/TKT-foundation-081`, base `dev`)
- **Priority: P1 — lane ticket 3/6 and the user's headline goal (current fundamentals; the CF statement is the 051/DCF/SNA unblock).**
- **Supersession decision (planner, 2026-10-25 — recorded on 051's file too):** 051 is **superseded by this ticket, not duplicated.** Reasons: (a) 051's own verified evidence shows the VYK dev archive carries **zero cash-flow statements** (72 IS / 84 BS / 74 EQ) — the paced VYK capture set 051 waited for cannot exist; (b) the public-site excel-HTML report carries the full indirect-method CF statement (`Nakit Akış Tablosu (Dolaylı Yöntem)`, current + comparative — investigation-verified, line ~887 of the flattened text with the hedging note family); (c) 051's pinned approach ("extend `KapXbrlStatementDecoder.ConceptMap` from captured shapes") was VYK-presentation-tree-shaped — this source is HTML tables, a different decoder with its own label→canonical map; duplicating the CF mapping in two decoders would violate the one-definition doctrine (`02` §6.1 collapses spellings onto the canonical chart — both maps target the same canonical codes, `StatementMapper` untouched); (d) **planning defect found and repaired:** the two acceptance-TC identities 051 referenced were never authored in the test plan (verified by search — the v1.12 precedent class); the goal is authored as **TC-MDF-108**. The VYK `ConceptMap` CF extension remains honestly impossible without real VYK CF shapes (never guessed spellings); if production VYK ever arrives carrying CF data, that is a new ticket from real captures.
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

### Decision record — the OCF row (2026-10-26, DR-2; user-authorized round 3)

The 2026-10-25 record above pinned a **value** (`OCF 21,061,426`, the comparative column of the
before-tax subtotal) but not a **row**; the review escalated the ambiguity and the user chose the
row. `OCF` maps to the statement's own operating total **`İŞLETME FAALİYETLERİNDEN NAKİT
AKIŞLARI`** — the figure **after** tax paid and other operating payments — not the before-tax
subtotal `Faaliyetlerden Elde Edilen Nakit Akışları`.

- Candidate rows in the committed 6-month capture (thousand TL, current / comparative):
  `İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI` 15.206.440 / 18.013.334; `Faaliyetlerden Elde Edilen
  Nakit Akışları` 20.382.722 / 21.061.426.
- Chosen basis consequence: FCF = OCF − |CAPEX| with CAPEX 33.746.150 / 17.190.837 gives interim
  FCF **−18.539.710** / **822.497** instead of the superseded −13.363.428 / 3.870.589 — roughly a
  25% difference on the metric this ticket exists to supply in the current column.
- The before-tax subtotal **remains decoded** as a labelled `OTHER_CF_OPERATING_*` companion (it is
  not mapped to any canonical code), so the financials display keeps both measures and nothing
  downstream can mistake one for the other.
- No annual golden is pinned here: the committed capture is a 6-month interim (`Periyot:2`) with no
  annual column. The FY basis stays guarded by the declared-vs-observed duration fail-safe and is
  the post-merge live criterion.

## Live run — 2026-10-26/27 (orchestrator, dev stack, recorded verbatim)

- **The merged decoder processed live excel-HTML end-to-end:** 62 canonical statements written from real kap.org.tr FR reports across 7 companies (periods through Q2-2026), CF statements decoded with line items landing in `fin_line_items` (13,531+ total rows in the store). The interim-report path is live-proven. **The annual-report live criterion (ASELS 2025 Yıllık, index 1561039) remains OPEN** — this night's walk fetched interim reports; the annual attestation path still needs its paced single-report run.
- Decode-coverage observation (honest): the walk's written statements are 60 CF / 2 IS / 0 BS — the FR excel-HTML reports contain balance sheets, but no BS mapping landed from tonight's reports. IS/BS decode coverage (debt/cash lines for the DCF) is the natural next slice.
