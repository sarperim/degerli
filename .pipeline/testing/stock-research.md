# Stock Research & Company Content Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved (builder final review 2026-10-07 — §6 interpretations upheld; v1.0 unchanged)
**Derives from:** `analysis/stock-research.md` (FR-RES-001..027, UC-RES-001..005, NFR-RES-001..006) · `ux/stock-research.md` (SCR-002/005, UXR-RES-001..025) · `ux/market-data-foundation.md` (UXR-MDF-018..022 — admin descriptions) · `architecture/` v1.2 (`02` §3.4, `03` §3/§8, C4) · strategy · fixture contract FU (incl. §9b description fixtures).
**TC ID convention:** `TC-RES-NNN`, permanent. Levels: L1 · L2 · L3 · L4.

## 1. Scope

**Tested here:** FR-RES-001..022, 026, 027 (24 non-Won't); UC-RES-001..005 including alternates; NFR-RES-001..006; UXR-RES-001..025 (SCR-002/005); UXR-MDF-018..022 (SCR-012 description review/publish).

**Not tested here:** FR-RES-023/024/025 (Won't); the C4 AI-drafting *quality* (builder review is the gate — NFR-RES-002 manual ledger); i18n parity, disclaimers sweep, a11y, metric-equality sweep (XC); MDF-side fact flags (tested in MDF; here they are asserted as *consumed* by the section payloads).

## 2. Test Case Specification

### Group A — Stock list & search (L2)

**TC-RES-001 — Universe list**
- Traces: FR-RES-001, UC-RES-001 step 2, BR-RES-001 · Level: L2 · Design: EP — full-universe class
- Expected: GET `/api/v1/stocks` (anonymous) returns all 16 fixtures with symbol, name, sector identity, listingDate; UNSEC served with its unclassified state (sector absent/null — flagged, never guessed).
- Dependencies: —

**TC-RES-002 — Sector filter**
- Traces: FR-RES-002, UC-RES-001 step 3 · Level: L2 · Design: EP — filter classes
- Expected: `?sector=A` → the 5 SEC-A stocks only; `?sector=` unknown → empty list (200), not an error.
- Dependencies: —

**TC-RES-003 — Search by name/code**
- Traces: FR-RES-003, UC-RES-001 step 3 · Level: L2 · Design: EP — match/no-match; case classes
- Expected: `?q=alfa` matches ALFA (case-insensitive ILIKE on name/symbol); `?q=Alfa Teknoloji` matches; `?q=zzz` → empty list 200; combined `?sector=A&q=alfa` applies both conjunctively.
- Dependencies: —

**TC-RES-004 — Header search endpoint**
- Traces: FR-RES-004, UXR-RES-007, OQ-RES-001 · Level: L2 · Design: EP — lightweight class
- Expected: GET `/api/v1/stocks/search?q=alfa` returns lightweight matches (symbol + name only — smaller payload than the list endpoint); no-match → empty.
- Dependencies: —

### Group B — Stock page sections (L2)

**TC-RES-005 — Overview: published description**
- Traces: FR-RES-005, UC-RES-002 main, BR-RES-003 · Level: L2 · Design: EP — published class
- Expected: GET `/stocks/ALFA/overview` → `{status: "published", textTr, textEn, lastReviewedAt: 2026-09-10, asOf}` — **both** language texts served (client renders the choice); anonymous 200.
- Dependencies: FU §9b.

**TC-RES-006 — Overview: preparing state**
- Traces: FR-RES-026, UC-RES-002 alternate a, OQ-RES-003 · Level: L2 · Design: EP — no-published-description class
- Expected: GET `/stocks/BETA/overview` → 200 with `status: "preparing"` (honest state, never 404, never draft text leaked).
- Dependencies: —

**TC-RES-007 — Valuation section incl. vs.-sector strip (golden)**
- Traces: FR-RES-006, FR-RES-007, UXR-RES-013, SD-002, OQ-UX-002 · Level: L2 · Design: golden values
- Expected: GET `/stocks/ALFA/valuation` → current multiples (per FU: pe 10.0, pb 2.0, ev_ebitda 5.5, ev_fcf 22.0, fcf_yield 0.05); historical series with `adjusted: true` (FR-RES-006 historical); strip entries `{value, sectorMedian, peerCount, excludedCount}` — P/E: value 10.0, median 10.0, peerCount 3, excludedCount 2; same shape for the other four valuation metrics; EPSL variant: P/E value 26.6667, median 25.8333, peerCount 6, excludedCount 0 (FU §11.4).
- Dependencies: —

**TC-RES-008 — Strip not-meaningful state**
- Traces: UXR-RES-025, BR-RES-008 · Level: L2 · Design: EP — NULL-multiple class
- Expected: GET `/stocks/BETA/valuation` → P/E strip entry carries the explicit not-meaningful marker for the stock's own value (no value, never 0) while the sector median, peer count, and exclusion count are still served.
- Dependencies: —

**TC-RES-009 — Financials section with restated flags**
- Traces: FR-RES-008, FR-RES-014 (data side), UC-RES-003 · Level: L2 · Design: EP — per-period classes; restated variant
- Expected: GET `/stocks/ALFA/financials` → per-period rows revenue → EBITDA → EBIT → NI → FCF (FY2025: 1000/400/300/200/100; quarterly rows summing identically); GET `/stocks/REST/financials` → FY2025 NI served as **75** with per-figure `restated: true` (latest restated version; as-reported 60 retained in storage, not served here).
- Dependencies: —

**TC-RES-010 — Profitability section**
- Traces: FR-RES-009 · Level: L2 · Design: EP — meaningful/NULL classes
- Expected: ALFA → ROIC 0.20, ROE 0.20, gross margin 0.40, operating margin 0.30; DELTA → ROE NULL with not-meaningful state (negative equity); values identical to screener goldens (canonical, BR-RES-004).
- Dependencies: —

**TC-RES-011 — Growth section with windows**
- Traces: FR-RES-010, BR-RES-013, FR-MDF-014 display side · Level: L2 · Design: BVA — window/history classes
- Expected: ALFA → 3/5/10Y CAGRs 0.160397/0.201124/0.174619 (rev) etc. with `windowYears` 3/5/10; ZETA → same value across requested windows with `windowYears: 1` (available-history indication); NEWP → explicit no-data state with coverage boundary naming listing date 2025-08-01 (never fabricated).
- Dependencies: —

**TC-RES-012 — Balance sheet incl. book value and BVPS**
- Traces: FR-RES-011, FR-RES-027, OQ-RES-004 · Level: L2 · Design: EP — BS class
- Expected: GET `/stocks/ALFA/balance-sheet` → BS line items per FU §5 + book value (total equity 1000) + BVPS 10.0 (equity/shares — canonical derivation, BR-MDF-009).
- Dependencies: —

**TC-RES-013 — Dividends section; never-paid state**
- Traces: FR-RES-012, UC-RES-003, SCR-005 §7 · Level: L2 · Design: EP — paid/never-paid classes
- Expected: ALFA → dated dividend history (TTM 4 rows + FY series); LAMDA → explicit never-paid state ("no dividends paid" with coverage boundary), not an error, not blanks.
- Dependencies: —

**TC-RES-014 — Unknown symbol across all section endpoints**
- Traces: `03` §7 NOT_FOUND · Level: L2 · Design: EP — unknown-symbol class
- Expected: each of the 8 stock endpoints with `symbol=NOPE` → 404 `NOT_FOUND` (stocks outside the covered universe).
- Dependencies: —

**TC-RES-015 — Honest-data envelope across sections**
- Traces: FR-RES-015, FR-RES-016, UXR-RES-021/022, BR-RES-007/008 · Level: L2 · Design: decision table — section × {data, missing, stale}
- Expected: every section payload carries `asOf` (+ `stale` when applicable); missing data → 200 with `coverage`/`state` (NEWP growth; PART's FCF absent from financials with explicit state); no section ever serves blank-as-zero.
- Dependencies: —

### Group C — Content pipeline (L2)

**TC-RES-016 — C4 drafting produces bilingual drafts with provenance**
- Traces: FR-RES-019, UC-RES-004 step 1, NFR-RES-006 · Level: L2 · Design: EP — draft class (evren API doubled via recorded fixtures)
- Steps: run the content CLI `draft --symbol=THETA` against the doubled evren API.
- Expected: `business_descriptions` row status `draft` with textTr + textEn, `source_refs_json` pointing at THETA's `kap_disclosures` ids, version lineage started; **never** status published; never served publicly.
- Dependencies: —

**TC-RES-017 — Drafts are never publicly served**
- Traces: NFR-RES-006, BR-RES-002, FR-RES-020 (publish-only visibility) · Level: L2 · Design: EP — draft/published serving classes
- Expected: public overview serves only `published` rows — REST (draft per FU §9b) → `preparing` on its public page despite drafts existing in storage.
- Dependencies: —

### Group D — Admin descriptions (SCR-012 section, L2)

**TC-RES-018 — Review queue with status filter and KAP source refs**
- Traces: UXR-MDF-018/020, UC-RES-004 step 2, `03` §8 v1.2 sketch · Level: L2 · Design: EP — status classes
- Expected: GET `/api/v1/admin/descriptions?status=draft` as builder → entries (REST, ZETA) with stock, status, version, **both texts**, `sourceRefs` resolved against `kap_disclosures` (disclosureId, disclosureType, publishDate, title, sourceUrl); an unresolvable id in `source_refs_json` serves a `{disclosureId}`-only stub — never silently dropped; `status=published` → ALFA; non-builder → 403.
- Dependencies: FU §9b.

**TC-RES-019 — Editor saves both languages; provenance not editable**
- Traces: FR-RES-021, UXR-MDF-019, `03` §8 (PATCH text-only) · Level: L2 · Design: decision table — editable fields vs provenance write
- Expected: PATCH `/admin/descriptions/{id}` `{textTr, textEn}` → saved (queue reflects on next GET); PATCH attempting `sourceRefs` → `400 VALIDATION_FAILED` (strict contract — provenance is pipeline-written only); save failure preserves both texts (client-side, asserted at L3 TC-RES-024).
- Dependencies: —

**TC-RES-020 — Publication gate**
- Traces: FR-RES-020, BR-RES-003, `02` §3.4 CHECK, UXR-MDF-021/022 · Level: L2 · Design: decision table — both/neither/one language filled
- Expected: publish REST (both texts) → status `published`, `publishedAt`/`lastReviewedAt` set; publish ZETA (EN empty) → rejected `400 VALIDATION_FAILED` naming the missing language, both texts preserved, status unchanged; the DB CHECK is the backstop (TC-MDF-009).
- Dependencies: —

**TC-RES-021 — Publication effect on the public page**
- Traces: UC-RES-004 step 4, mutation matrix row (publish) · Level: L2 · Design: state transition — preparing → published
- Expected: after publishing REST, GET `/stocks/REST/overview` flips to `published` with `lastReviewedAt` — no cache clearing; the preparing state is gone.
- Dependencies: —

### Group E — Components (L3)

**TC-RES-022 — Stock list component**
- Traces: UXR-RES-001..006, UC-RES-001 · Level: L3 · Design: EP — list/filter/search/empty/prefill classes
- Expected: full list renders; sector filter + search apply together and show as active; no-match → explicit no-matching-stock state with clear path; dashboard sector link prefills the filter (UXR-RES-006); filter/search/active state preserved across navigation (UXR-G-021); rows activate the stock page.
- Dependencies: —

**TC-RES-023 — Section independence and per-section retry**
- Traces: UXR-RES-008 §5, architecture M-5, UXR-G-003 · Level: L3 · Design: EP — single-section failure class (MSW rejects one section)
- Expected: one failing section shows its own plain-language error + retry while the other six render; retry recovers on restored handler; sections load with independent loading states.
- Dependencies: —

**TC-RES-024 — Honest-data markers**
- Traces: UXR-RES-019/020/021/022, FR-RES-013/014/015/016 · Level: L3 · Design: decision table — flag × marker rendering
- Expected: `restated: true` → asterisk on affected figures + bottom-of-display footnote + warning (all textual, reachable); `adjusted: true` → adjusted-data disclaimer; `stale: true` → badge + asOf; missing data → no-data state with coverage boundary; none set → clean render with asOf only.
- Dependencies: —

**TC-RES-025 — Vs.-sector strip component**
- Traces: UXR-RES-013/024/025 · Level: L3 · Design: EP — populated/not-meaningful/no-median classes
- Expected: stock value next to sector median with peer count ("median of N peers") and the P/E loss-maker exclusion disclosure; not-meaningful own-value state; unavailable median → honest no-data for that metric.
- Dependencies: —

**TC-RES-026 — Description rendering and preparing state**
- Traces: UXR-RES-009/010/011 · Level: L3 · Design: EP — published/preparing; language classes
- Expected: published description renders in the selected language (both texts pre-served); last-reviewed date displayed; preparing state renders honestly while numbers sections remain fully populated; language toggle re-renders the description without refetch.
- Dependencies: —

**TC-RES-027 — Active-section preservation**
- Traces: UXR-G-021, SCR-005 §2 · Level: L3 · Design: state transition — navigate away/back
- Expected: the active stock-page section (e.g., Balance Sheet) is preserved across in-session navigation and return.
- Dependencies: —

**TC-RES-039 — Financial-statements view (SCR-005 §3, the user's v0 spec)**
- Traces: FR-RES-008, FR-RES-013/014/015/016, UC-RES-003, UXR-RES-014 (superseded in breadth by the 2026-10-28 user spec), UXR-RES-019/021/022, UXR-G-006/007/021, BR-RES-004 · Level: L3 · Design: EP — served-payload classes (full line set / null figure / unserved row / spanned interim / unspanned interim / bank layout / pre-094 payload), decision table — filing flag × badge
- Expected: (a) the served `lines`/`derived` payload renders the full IS/BS/CF tables bound to the served codes, with the filing badges (Konsolide/Solo, TMS 29, KAP Kaynak) and the served EV operands plus their own as-of; (b) a served-null derived row renders the honest dash — the page never computes a margin, ratio, sum or EV total (BR-RES-004); (c) a spec row the serving surface does not carry renders the dash, never a zero; (d) the period toggle filters to FY by default; (e) a cumulative interim is labelled from its served span (`2026 6 Aylık (01.01–30.06)`), and an interim with no served span degrades to the quarter label, never a fabricated range (closes 090 R-1 on the surface); (f) the chosen basis survives navigation (UXR-G-021); (g) each filing badge renders only on its own served evidence (absent flag → no badge); (h) a financial-sector filing renders the layout notice, never half-filled industrial tables; (i) per-share rows render "—" where no served share count exists (exclusion #4); (j) the EV header renders the served operands with their own as-of and no computed total; (k) a pre-094 payload (no served line set) keeps the legacy five-figure summary; (l) TR and EN both label the served row set from the catalogs.
- Dependencies: TC-RES-009 (the served financials content it restructures).

**TC-RES-041 — Key value metrics & moat panel + capital & dilution tracker (SCR-005, the user's v0 spec's last two blocks)**
- Traces: FR-RES-006/007 (valuation metrics + vs-sector strip honesty fields), FR-RES-009 (profitability), FR-RES-010 (growth CAGRs), FR-RES-012/FR-RES-027 (payout / per-share book values), FR-MDF-004 (corporate actions), FR-MDF-014 (window honesty), UC-RES-003, UXR-RES-012/013/015/016/017, UXR-RES-020 (adjusted disclaimer), UXR-RES-024/025 (peer count, N/M), UXR-RES-026/027 (regime-break marker), UXR-G-006/007, BR-RES-004 · Level: L3 (with its L4 extension in `tests/e2e/src/stock-page.spec.ts`) · Design: EP — served-payload classes (value / served-null / no sector median / withheld company median / spanning vs one-side CAGR / profile-gated row / financial-sector profile), decision table — served flag × marker
- Expected: (a) both blocks render every served panel code (31) and every capital-tracker row (paid-in capital, split-adjusted share count, share-count Y/Y change, bedelli/bedelsiz history, payout ratio); (b) a served value renders as served and a served-null value renders "—", never zero — the page computes nothing (BR-RES-004); (c) the 5Y company median renders with its served `windowYears`/`observations`/`depthYears`/`minimumObservations` disclosure, and a withheld median is a dash, never a fabricated statistic; (d) the sector median renders with its peer count and, for a gated code, the served exclusion-count disclosure; a null sector median is the honest dash; (e) the regime-break marker renders on a CAGR whose served `regimeBreak` is true, is absent on a one-side CAGR, is distinguishable from the restatement marker, and the footnote accompanies it; (f) the TMS 29 alert renders from the served `monetary_gain_pretax` value (and not when the ratio is unserved) — no client threshold; (g) the bedelli/bedelsiz history renders dated with its served percentages and the corporate-action-adjusted disclaimer; (h) the share-count Y/Y change renders the honest `no-count-history` N/M, never a guessed change; (i) the served group-basis `roe` and `eps_cagr_*` rows are labelled group/consolidated — never the spec's Parent wording over a group figure (the routed parent-basis pair is not fabricated); (j) a financial-sector profile renders the notice and only its profile-appropriate (non-`profileGated`) rows; (k) TR and EN label the row set from the catalogs (parity). **L4 (fixture world):** ALFA's panel renders the served current values and the withheld company median's disclosure, the absent sector medians are the honest dash (the fixture world runs no medians job), a code with no fixture input (`roa`) is the dash, and the tracker's share-count N/M renders.
- Dependencies: TC-RES-039 (same section), TC-MDF-124 (the served payload).

### Group F — End-to-end flows (L4)

**TC-RES-028 — Research hand-off (SC-002 research stage)**
- Traces: UC-RES-003 main, UC-RES-005, FR-RES-018, UXR-RES-023, SCR-005 critical flow 1 · Level: L4 · Design: EP — full-section study path
- Expected: from a screener result row, the stock page opens; all seven sections available and navigable; canonical figures with as-of dates; DCF hand-off opens the calculator for this stock.
- Dependencies: —

**TC-RES-029 — Description in preparation (critical flow 2)**
- Traces: UXR-RES-011, SCR-005 flow 2 · Level: L4
- Expected: BETA's page shows the preparing state in Overview while all numbers sections are fully populated.
- Dependencies: —

**TC-RES-030 — Restatement marking visible (critical flow 3)**
- Traces: UXR-RES-019, SCR-005 flow 3 · Level: L4
- Expected: on REST's page, affected figures carry the asterisk, the bottom footnote explains the restatement, and the warning indicator is present.
- Dependencies: —

**TC-RES-031 — Short history (critical flow 4)**
- Traces: UXR-RES-016/022, SCR-005 flow 4 · Level: L4
- Expected: ZETA's growth section indicates actual windows; NEWP's shows the honest no-data state with the listing-date boundary.
- Dependencies: —

**TC-RES-032 — Adjusted historical data (critical flow 5)**
- Traces: UXR-RES-020, SCR-005 flow 5 · Level: L4
- Expected: EPSL's historical valuation series is adjusted (post-split/bonus continuity — no fake 80% crash) and carries the adjusted-data disclaimer.
- Dependencies: —

**TC-RES-033 — Back-navigation preserves section (critical flow 6)**
- Traces: UXR-G-021, SCR-005 flow 6 · Level: L4
- Expected: from Balance Sheet, navigate away and return → Balance Sheet still active.
- Dependencies: —

**TC-RES-034 — Admin description review → publish → public effect**
- Traces: UC-RES-004, UXR-MDF-018..022, SCR-012 critical flows 4–5, mutation matrix rows · Level: L4 · Design: state transition — draft → edited → published → publicly served
- Expected: builder opens the draft queue (filter by status), edits both texts (saved, preserved on forced failure), sees KAP source refs alongside; publish control disabled while a language is empty with an explanation (ZETA); publishing REST (confirmation states public visibility) → status published, coverage counts update without manual refresh, and the public page serves the description with its last-reviewed date on next visit.
- Dependencies: —

**TC-RES-035 — Header search → stock page; sector prefill; search miss**
- Traces: UXR-RES-007, SCR-002 critical flows 1–3, FR-RES-004 · Level: L4 · Design: EP — search classes
- Expected: header search on any screen → selecting a match opens that stock's page directly; viewing all matches opens the list with the query applied; a query matching nothing → explicit no-match state; dashboard sector link → list pre-filtered.
- Dependencies: —

**TC-RES-036 — Public access without an account**
- Traces: BR-RES-001, NFR-RES-005 · Level: L4 · Design: EP — anonymous class
- Expected: every SCR-002/005 surface and flow above runs anonymous end-to-end; no save prompt or auth wall appears anywhere in the research path.
- Dependencies: —

**TC-RES-040 — Statements view end-to-end on the fixture world (TC-RES-039's L4 extension)**
- Traces: FR-RES-008, UXR-RES-014, UXR-RES-021/022, UXR-G-006/021, BR-RES-004 · Level: L4 · Design: EP — served-payload classes on the seeded stack
- Expected: on the seeded compose.ci world, a company with served statements (ALFA) renders the three statement tables under the annual basis with the served fiscal-year labels; the Çeyreklik toggle shows the served interims labelled honestly (a fixture interim carries no served span, so the quarter label renders — never a fabricated range) and filters the annual column out; a spec row the serving surface does not carry renders the "—" dash; the EV header renders its served operands with no computed total; and the chosen basis survives navigating away and back.
- Dependencies: TC-RES-039.

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** list queries — full/filtered/searched/combined/empty (TC-001..004, 022); descriptions — published/draft/none (TC-005/006/016/017/018); figures — meaningful/NULL/not-computable (TC-008/010/011/013); section payloads — data/missing/stale (TC-015); symbols — known/unknown (TC-014); callers — anonymous/builder/non-builder (TC-018/036).
- **Boundary value analysis:** CAGR windows 3/5/10 vs actual-history 1 (TC-011); FY-count edges via ZETA/NEWP (2 FYs vs 1); adjusted-series rows immediately before/between/after action dates (TC-032, EPSL series).
- **Decision tables:** honest-data flags × markers (TC-024); publication gate — both/neither/one language (TC-020); editor fields — text vs provenance (TC-019); strip states — value/not-meaningful/no-median (TC-025).
- **State transition testing:** draft → edited → published → publicly served (TC-016/019/020/021/034); preparing → published (TC-021); section navigation with preservation (TC-027/033); restatement arrival → marked serving (consumed from MDF TC-032, asserted visible in TC-009/030).
- **Golden values:** FU §11.1/§11.4-derived — valuation multiples, strip medians/counts, financials rows, profitability ratios, growth CAGRs, BVPS. The same canonical numbers the screener and DCF assert (single Metrics Engine, UXR-G-011).

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** section payloads match FU goldens (6 sig. digits); states render exactly (preparing, never-paid, not-meaningful, no-data with boundary); admin flows flip public serving per the mutation matrix; drafts never visible publicly. **Fail:** any mismatch, any draft leak, any blank-as-zero. Flaky = fail.

**Suspension:** Testcontainers/compose unavailable >1 day; description schema or strip definition change (re-derive FU §9b/§11.4 first); C4 CLI blocker suspends TC-016 only. **Resumption:** clean triaged run of the affected group.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-RES-001 | main | TC-001, 022 | planned |
| FR-RES-002 | main | TC-002, 022 | planned |
| FR-RES-003 | main | TC-003, 022 | planned |
| FR-RES-004 | main | TC-004, 035 | planned |
| FR-RES-005 | main | TC-005, 026 | planned |
| FR-RES-006 | main (current + historical) | TC-007 | planned |
| FR-RES-007 | main | TC-007, 008, 025 | planned |
| FR-RES-008 | main | TC-009, 039, 040 | planned |
| FR-RES-009 | main | TC-010 | planned |
| FR-RES-010 | main + window indication | TC-011 | planned |
| FR-RES-011 | main | TC-012 | planned |
| FR-RES-012 | main | TC-013 | planned |
| FR-RES-013 | adjusted disclaimer | TC-024, 032 | planned |
| FR-RES-014 | restatement marks | TC-009, 024, 030, 039 | planned |
| FR-RES-015 | per-section as-of/stale | TC-015, 024, 039 | planned |
| FR-RES-016 | no-data with boundary | TC-011, 013, 015, 031, 039 | planned |
| FR-RES-017 | disclaimer | XC-006 sweep; TC-028 render | planned |
| FR-RES-018 | DCF hand-off | TC-028 | planned |
| FR-RES-019 | pipeline drafts | TC-016 | planned |
| FR-RES-020 | publication gate | TC-017, 020, 034 | planned |
| FR-RES-021 | builder edits | TC-019, 034 | planned |
| FR-RES-022 | last-reviewed date | TC-005, 021, 026 | planned |
| FR-RES-026 | preparing state | TC-006, 026, 029 | planned |
| FR-RES-027 | book value + BVPS | TC-012 | planned |
| UC-RES-001 | main; alt a | TC-001..004, 022, 035; TC-003/022 (no-match) | planned |
| UC-RES-002 | main; alt a | TC-005, 026; TC-006, 029 | planned |
| UC-RES-003 | main; alt a (stale); alt b (short history) | TC-007..015, 028, 039, 040; TC-015/024 (stale payloads); TC-011, 031 | planned |
| UC-RES-004 | main; alt a (re-draft); alt b (new filings cycle) | TC-016, 018..021, 034; re-draft = CLI re-run (TC-016 variant); alt b → manual/content-cycle ledger note | planned |
| UC-RES-005 | main | TC-028 | planned |
| NFR-RES-001 | bilingual | XC parity; publish gate TC-020 | planned |
| NFR-RES-002 | plain-language quality | builder review ledger (manual) | manual |
| NFR-RES-003 | consistency | goldens TC-007/010/011 = screener goldens; XC-008 sweep | planned |
| NFR-RES-004 | freshness split | TC-005 (lastReviewedAt) + TC-015 (asOf) | planned |
| NFR-RES-005 | public availability | TC-036 (+ anonymous throughout) | planned |
| NFR-RES-006 | AI governance | TC-016, 017 | planned |
| UXR-RES-001..005 | list | TC-001, 022, 035 | planned |
| UXR-RES-006 | sector prefill | TC-022, 035 | planned |
| UXR-RES-007 | header search | TC-004, 035 | planned |
| UXR-RES-008 | seven sections | TC-023, 028 | planned |
| UXR-RES-009/010 | description language/date | TC-005, 026 | planned |
| UXR-RES-011 | preparing | TC-006, 026, 029 | planned |
| UXR-RES-012 | valuation display | TC-007 | planned |
| UXR-RES-013 | strip | TC-007, 025 | planned |
| UXR-RES-014 | financials (restructured per the 2026-10-28 user spec) | TC-009, 039, 040 | planned |
| UXR-RES-015 | profitability | TC-010 | planned |
| UXR-RES-016 | growth windows | TC-011, 031 | planned |
| UXR-RES-017 | book value | TC-012 | planned |
| UXR-RES-018 | dividends | TC-013 | planned |
| UXR-RES-019 | restatement marks | TC-024, 030, 039 | planned |
| UXR-RES-020 | adjusted disclaimer | TC-024, 032 | planned |
| UXR-RES-021 | as-of/stale | TC-015, 024, 039 | planned |
| UXR-RES-022 | no-data boundary | TC-011, 013, 015, 031, 039 | planned |
| UXR-RES-023 | DCF hand-off | TC-028 | planned |
| UXR-RES-024 | peer count | TC-007, 025 | planned |
| UXR-RES-025 | not-meaningful | TC-008, 025 | planned |
| UXR-MDF-018 | queue + filter | TC-018, 034 | planned |
| UXR-MDF-019 | editor save/preserve | TC-019, 034 | planned |
| UXR-MDF-020 | KAP source refs | TC-018, 034 | planned |
| UXR-MDF-021 | publish disabled + explanation | TC-020, 034 | planned |
| UXR-MDF-022 | publish confirmation | TC-034 | planned |

## 6. Flags and recorded interpretations (for final review)

- **I-RES-1 (plan-specified):** publish rejection for a missing language → `400 VALIDATION_FAILED` with the missing field named (the `03` §7 catalog has no dedicated code; the UX requires plain-language explanation which the code+params carries).
- **I-RES-2 (plan-specified):** PATCH `/admin/descriptions/{id}` rejects a `sourceRefs` field with `400 VALIDATION_FAILED` — strict contract protecting pipeline-written provenance (payload-evolution "ignore unknown fields" applies to *clients*, not to this documented strict editor contract).
- **I-RES-3 (interpretation):** UC-RES-004 alternate b (re-draft on material new filings) is a content-cycle workflow, not an automatable behavior — the draft CLI re-run (TC-016 variant) exercises the mechanism; the *trigger policy* is a builder manual-ledger item.
- **I-RES-4 (interpretation):** sector identity serialization in list/rows is asserted as identity, not shape (mirrors I-SCR-3).

---
*Change record: v1.0 2026-10-06 — initial RES test plan, batch mode. Approved at the builder's final review 2026-10-07 — §6 interpretations I-RES-1..4 upheld.*

*Change record: v1.1 2026-10-29 — added **TC-RES-039** (L3, the SCR-005 financial-statements view per the user's 2026-10-28 v0 spec) and **TC-RES-040** (its L4 extension on the fixture world), plus their coverage-matrix traces. Rendered by TKT-foundation-095; test-planner confirmation pending (the UXR-RES-014 breadth supersession is a ux flow-back item on the ticket). Note: TC-RES-037/038 were already taken by the dividends-availability lane (TKT-foundation-052), so the statements cases take the next free IDs 039/040.*

*Change record: v1.2 2026-10-29 — added **TC-RES-041** (L3 + its L4 extension, the SCR-005 key value metrics & moat panel and the capital & dilution tracker per the same user spec), plus its coverage-matrix traces. Rendered by TKT-foundation-097; test-planner confirmation pending. Note: the ticket pinned `TC-RES-038`, which belongs to the dividends-availability lane (TKT-foundation-052, `Dividends.test.tsx`); with 039/040 taken by the statements lane, this case takes the next free ID **041** — the ticket's acceptance line and implementation notes carry the renumbering.*
