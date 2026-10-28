---
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/KapPublicReportModels.cs
  - src/Ingestion/Statements/StatementMapper.cs
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/02-data-model.md
---
# TKT-foundation-093: Statements-view label-map expansion — the full industrial IS/BS/CF row set mapped capture-first to new canonical codes, and the Konsolide/Solo filing flag the metadata bar needs
- Status: done (PR #155 squash-merged; user waived the facet-gate missing-tc false positive 2026-10-28; DR-1/2/3/6/7 + ticket-text contradiction routed to planner amendment; DR-4/5 routed as follow-up fail-safe ticket)
- PR: https://github.com/sarperim/degerli/pull/155
- Size: M
- **Priority: P1 — slice 1 of the 2026-10-28 financial-statements view wave (the user's v0 spec, recorded verbatim on TKT-foundation-095; this ticket is its decode half, 094 the serving half, 095 the surface).**
- **The wave's premise (live-verified 2026-10-27/28, the 088 record):** every row the user's statements view needs is already in the KAP reports and already lands as `OTHER_*` passthrough with values (GARAN Q2-2026 IS: 80 lines, 78 `OTHER_*`; TÜPRAŞ BS decodes 146–174 lines) — the gap is canonical label mapping, not data. 088 mapped the debt/cash core and the income ladder (9 IS / 6 BS codes); the user's view needs the remaining families (A/R, inventories, PP&E, right-of-use, goodwill+intangibles, A/P, leases, retained earnings, NCI equity lines, paid-in capital, total assets, CF detail lines, parent NI, TMS 29 monetary lines).
- **What to implement (capture-first, never guessed — the 088 doctrine unchanged):**
  1. **The row-set inventory, diagnosed first:** run the four committed capture bodies (ASELS annual `1561039`, TUPRS annual `1554106`, MGROS interim `1648240`, ASELS `1643141`) through the current decoder and enumerate, per statement type, which of the view's row families already land as `OTHER_*` passthrough with values and which do not appear at all. Record the table on this ticket before any map entry is written (the 088 diagnosis precedent).
  2. **The capture gate:** every new map entry names a committed capture via `KapPublicLabelRule.Provenance` and its exact spelling is present in the body that provenance names. Grounding verified on `RealSourceCatalog.cs` (grep counts, 2026-10-28): Ticari Alacaklar 23, Ticari Borçlar 18, Stoklar 7, Maddi Duran Varlıkl 10, Maddi Olmayan Duran 20, Şerefiye 3, Kullanım Hakkı 8, Geçmiş Yıllar Karları 3, Kontrol Gücü Olmayan 15, Ödenmiş Sermaye 7, TOPLAM VARLIKLAR 4, Yatırım/Finansman Faaliyetlerinden 6/3, Temettü 9, Geri Alın 6, Ertelenmiş 26, Araştırma/Geliştirme 2, Enflasyon 3, Parasal/Net Para 9. **Correction (amendment round 2026-10-28, coder-verified):** the original text expected lease-liability lines unevidenced on the spelling `Finansal Kiralama` — the wrong spelling. The taxonomy's lease-liability label is **`Kiralama İşlemlerinden Borçlar`**, present in **all four committed bodies** (6 label cells per body; THYAO FY2025 values 86.152/64.571 in the short-term block, 552.824/358.353 long-term), and `Kullanım Hakkı Varlıkları` (ROU assets) carries 4–5 hits per body. **The THYAO `1565996` body (already fetched, `/tmp/opencode/captures/kap-excel-1565996.html`, 1,349,431 B; metadata: Gönderim Tarihi 04.03.2026 18:55:17, Bildirim Tipi FR, Yıl 2025 / Periyot 4, Sunum Para Birimi 1.000.000 TL — millions, `ParsePresentationUnit` supports the scale — Finansal Tablo Niteliği Konsolide, `<h1>` = `TÜRK HAVA YOLLARI A.O.` / `Finansal Rapor`) is committed as a bounded-trim double labeled as such as this ticket's first step** — the capture already exists; no new live request is needed or permitted for it. With THYAO committed, **every family in the v0 row set is expected evidenced — the honest-absent list should end up empty** (verified per body at implementation and recorded on the ticket; any family that genuinely stays unevidenced is served as an honest absent row by 094, never guessed here — the orchestrator-owned paced capture route remains available for a family the five bodies somehow all lack).
  3. **The map extension + the `02` §6.1 amendment:** new canonical codes follow the existing doctrine (targets reuse the VYK concept map's assignments where they exist; `02` §6.1 gains the codes with their definitions and the VYK-side spellings where evidenced). **`02` §6.1's chart extension is the vehicle for the ~40 new codes (confirmed, amendment round 2026-10-28)** — additive, never a rename of an existing code — and every code must join `StatementMapper.CanonicalCodes` (the registry `StatementMapper.Map` checks; a code missing there is rewritten to `OTHER_{code}` on write and the ticket's deliverable silently vanishes — the coder-verified mechanical gap this amendment round closed by adding `StatementMapper.cs` to scope). The v0 view's target set (from the user spec, each mapped only when evidenced): IS — `RND` (R&D), other operating income/expense net, investing inc/exp net, financing inc/exp net (income/expense families distinct from the existing `FIN_EXP`), TMS 29 net monetary position G/L, **`NI_PARENT`** (Ana Ortaklık Payları — evidenced; `NI` keeps its total meaning, the new code sits beside it, never replaces it); BS — trade receivables, inventories, net PP&E, right-of-use (TFRS 16), goodwill+intangibles, other current assets/liabilities, other non-current, trade payables, deferred revenue, ST/LT lease liabilities, retained earnings, NCI equity block (`MINORITY_INT` already exists in §6.1 — wire its KAP spelling), paid-in capital (the statement line; the `share_counts` store's D4 figure is separate and untouched), **`TOTAL_ASSETS`**, total liabilities, equity (parent); CF — D&A, impairments & provisions, monetary gain/loss adjustment, working-capital changes (`ΔWC` exists), asset sale proceeds, cash acquisitions, rights-issue cash, buybacks, dividends paid, cash interest paid, net debt issued/(repaid). Exact code names are pinned at implementation into `02` §6.1.
  4. **The BS block dimension (PLANNER RULING, amendment round 2026-10-28 — recorded in `01` §10.9.19 and `02` §6.1, both dated):** the label-rule key is `(StatementType, Label)` only (`KapPublicReportModels.cs:244-248`); BS/IS rows always resolve to `DefaultSection` (decoder `:1033-1039`); only CF has section resolution (`KapPublicSections`, `CashFlowSectionFor`). But the balance sheet legitimately repeats labels across its current/non-current blocks with different values — coder-verified per body: `Ticari Alacaklar` ×2, `Ticari Borçlar` ×2, `Kiralama İşlemlerinden Borçlar` ×6, `Ertelenmiş Gelirler (…Dışında Kalanlar)` ×2, `Finansal Yatırımlar` ×2, `Ana Ortaklık Payları` ×2 — so a plain 1:1 label→canonical-code map would raise `DUPLICATE_LABEL_CONFLICT` and quarantine the whole BS row (the 088 failure mode, currently papered over only for `OTHER_*`). **The ruled design:** the label-rule key gains a BS block dimension (current/non-current), **resolved before label mapping exactly as CF sections are today** — from the capture's own block structure (the `DÖNEN VARLIKLAR`/`DURAN VARLIKLAR`/`KISA VADELİ YÜKÜMLÜLÜKLER`/`UZUN VADELİ YÜKÜMLÜLÜKLER` anchors the tables carry), with an **honest fail-safe when a row's block cannot be determined** (a named, counted finding; a block-qualified rule never matches an undetermined row; never a guessed block). The mechanism is **additive**: a rule without the block qualifier matches any block, so every existing single-block rule is unchanged and all 088/090/091/092 goldens hold. The dimension materializes as **distinct canonical codes** (e.g. `ST_LEASE_LIAB` vs `LT_LEASE_LIAB`, current `Ticari Alacaklar` → trade receivables while its non-current repeat feeds the other-non-current family or stays `OTHER_*` per the §6.1 amendment) — serving (094) keeps a flat code→figure map with no block logic of its own. **The rejected alternative, recorded with its consequence:** routing repeated families to `OTHER_*` would leave 094's canonical block mostly empty (the BS is organized in current/non-current blocks and the user's v0 spec structurally requires the ST/LT dimension — separate ST/LT rows for debt and leases) — rejected. **The TC-MDF-107(b) invariant (`KapPublicReportDecoderTests.cs:426`) needs no amendment:** it asserts every rule target ∈ `StatementMapper.CanonicalCodes` (membership, not the registry's size), so the enlarged registry keeps the invariant; if any test enumerates the registry exhaustively, its expected set grows additively — a pre-listed additive delta, never a weakened assertion.
  5. **The Konsolide/Solo filing flag (USER-CONFIRMED in v0, 2026-10-28; wording corrected in the amendment round):** the declaration is **not** in the `<h1>` title — it is the header-table field **`Finansal Tablo Niteliği`**, already read into `KapPublicReportHeader.ConsolidationType` (`KapPublicReportModels.cs:99,112`). `KapPublicReport.Consolidated` derives from that field (Konsolide→true, Solo→false, absent/any other value→honest null). Persistence and serving are 094's; this ticket only decodes and carries it through the models.
- **Pinned non-goals (the user's five exclusions, verbatim intent, recorded on 095):** no DR-2 widening — `ST_DEBT`/`LT_DEBT` stay exactly the two borrowings lines, the current portion of long-term borrowings stays `OTHER_BS_*` (the fenced 25.6% TÜPRAŞ question is untouched); no DR-1 resolution — `TOTAL_EQUITY` stays NCI-inclusive as stored; no share-count work (065/086 routing stands — the price-derived count store is the per-share fallback, never a statement line); no cumulative-semantics change (090 stands — sub-period columns stay skipped, spans stay per-line decode provenance); no VYK decoder change; no equity-matrix decode change; no serving/UI work (094/095).
- Traces to: FR-MDF-002/003 (statement ingest), FR-MDF-008 (provenance `kap-public`), FR-RES-005..018 (the served financials this supplies), `02` §6.1 (concept-map doctrine — this ticket amends it).
- Acceptance: **TC-MDF-122** (test plan §33, v1.45 reservation — planner-authored per the lane precedent, test-planner confirmation pending) — L2 doubles only (the committed captures — **now five, THYAO `1565996` included** — are the entire input; zero live requests from CI); red→green recorded (red = the view's row families absent from `fin_line_items` canonical codes on the current head — the 088-recorded world). Every new entry's provenance is a committed capture whose body carries the exact spelling (the TC-MDF-117 (f)/TC-MDF-107 (i) invariant generalized to the expanded set). **The block dimension is load-bearing in the cases:** a repeated BS label (e.g. `Ticari Alacaklar` current + non-current, `Kiralama İşlemlerinden Borçlar` ST + LT) maps per block to its distinct codes with **no** `DUPLICATE_LABEL_CONFLICT` and no quarantine; a row whose block cannot be determined is a named, counted finding that no block-qualified rule matched; a qualifier-free rule still matches in both blocks (backward compatibility, goldens hold); every new code is present in `StatementMapper.CanonicalCodes` and survives `StatementMapper.Map` to `fin_line_items` under its own name. Non-regression: `KapPublicReportDecoderTests` 088/090/091/092 goldens byte-identical (existing map entries and the 1643141 goldens unchanged — the additive-key design is what guarantees this), `KapPublicStatementsIngestTests`, `RealSourceStatementsTests`, `StatementNormalizationTests`, `StatementVersioningTests` re-run green with pre-listed additive deltas only. **Live criteria (orchestrator-owned, post-merge, paced, recorded verbatim):** one `Source = kap-public` pass writes the expanded canonical row set — per-family coverage counts verbatim, and the families that stayed honest-absent named (expected: none, per the corrected capture premise).
- Architecture refs: `01-system-architecture.md` §10.9.19 (the label-map subsection extends it) + §13.1 **v1.5.69** (soft reservation, next free above the landed v1.5.68; re-pin at merge); `02-data-model.md` §6.1 (amended — new codes, provenance); test plan §33 (TC-MDF-122).
- UX refs: none (decoder lane; the served presentation is 094/095's).
- Dependencies: none hard (088–092 merged). **Serializes with 094 on the statements store/models** and shares the three permanent docs with the whole wave (last-lander rebase, the lane doctrine).
- Parallel group: none.
## Amendment record — 2026-10-28 (planner, orchestrator-authorized blocker round; the 093 coder stopped pre-code with a well-evidenced blocker — no code, no branch, worktree clean at `c5957ff`)
Four findings, all coder-verified from committed bodies and `/tmp/opencode/captures/`, amended here before dispatch:
1. **Scope gap (mechanical):** `src/Ingestion/Statements/StatementMapper.cs` holds `CanonicalCodes` (23 codes today) and was omitted from the planner's `scope:` — but every new map entry's target must join that registry or `StatementMapper.Map` rewrites it to `OTHER_{code}` on write (`FinancialStatementStore.cs:227` maps again), silently defeating the ticket. **`StatementMapper.cs` is added to scope** (the 091 v1.40 precedent — a planner omission, not a widened ticket; TKT-mdf-017 `1cb6abd` is the registry-growth precedent). **`02` §6.1's chart extension is confirmed as the vehicle for the ~40 new codes**, and the §13.1 reservation needs **no adjustment**: v1.5.69 covers the lane's doc amendments regardless of the added file — the scope frontmatter is the boundary that was wrong, not the allocation.
2. **Design gap (ruled, clause 4 above):** the `(StatementType, Label)`-only rule key cannot express the BS's legitimate cross-block label repetition; the ruled design is the additive BS block dimension (CF-section-resolution precedent, honest fail-safe, distinct codes per block, goldens hold). The rejected alternative — routing repeated families to `OTHER_*` — would leave 094's canonical block mostly empty; consequence recorded, rejected. Recorded in `01` §10.9.19 and `02` §6.1 (both dated, riding this ticket's v1.5.69 reservation; the lane subsumes them into its subsection when it lands).
3. **Capture-premise correction (clause 2 above):** the lease-liability spelling premise was wrong (`Finansal Kiralama` → `Kiralama İşlemlerinden Borçlar`, evidenced in all four committed bodies); the THYAO `1565996` body is already fetched and is committed as a bounded-trim double as the ticket's first step; every v0 family is now expected evidenced (honest-absent list expected empty).
4. **Flag wording correction (clause 5 above):** the Konsolide/Solo declaration comes from the header-table field `Finansal Tablo Niteliği` (`KapPublicReportHeader.ConsolidationType`, already read), not the `<h1>` title.
**Ripple check across the wave (verified this round):** 094 gains an amendment note (the ST/LT distinction survives serving as distinct canonical codes materialized at decode — its `lines` block stays a flat code→figure map, no serving-side block logic); 095/096/097 need **no** amendment (095 renders served codes; 096 consumes `fin_line_items` codes — the block dimension is decode-side only). The five exclusions and all guardrails are unchanged by this round.
## Implementation record — 2026-10-28 (implementation lane)
**Branch** `tkt-foundation-093` (from `dev@7f7e88d`). **Commits:** `4b8cdda` — the THYAO `1565996`
bounded-trim capture, its provenance and its catalog registration, `ThyaoAnnualCaptureName`,
`CaptureNames` (now five) and the TC-MDF-117 (f) capture-set extension; this commit — the map
expansion, the row-scope dimension, the chart extension, the filing flag, the nine TC-MDF-122
cases, the six approved re-pins and the three permanent-document amendments.
### Diagnosis, taken before any map entry was written (the 088 precedent)
Every row family the statements view needs **already decoded as a valued `OTHER_*` passthrough** on
the four then-committed bodies — the gap was the canonical mapping, never the data. Two structural
facts made it non-trivial:
1. The balance sheet repeats captions across its current/non-current blocks (`Ticari Alacaklar` ×2,
   `Ticari Borçlar` ×2, `Kiralama İşlemlerinden Borçlar` per block pair, `Ertelenmiş Gelirler (…)`
   ×2) and the income statement repeats `Ana Ortaklık Payları` across its profit-or-loss/OCI parts —
   a 1:1 `(statement, label)` map would overwrite a value or re-raise 088's `DUPLICATE_LABEL_CONFLICT`
   quarantine.
2. The consolidation declaration is the header field `Finansal Tablo Niteliği`, not the `<h1>`.
Post-change decode diagnostics (throwaway dumper, committed bodies only, **zero live requests**):
| body | lines | statements | `DUPLICATE_LABEL_CONFLICT` | `BLOCK_UNDETERMINED` | disambiguated |
|---|---|---|---|---|---|
| THYAO `1565996` 2025 Yıllık | 497 | 6 | 0 | 0 | 0 |
| TUPRS `1554106` 2025 Yıllık | 346 | 6 | 0 | 0 | 18 |
| ASELS `1561039` 2025 Yıllık | 383 | 6 | 0 | 0 | 34 |
| MGROS `1648240` 2026-06 | 314 | 6 | 0 | 0 | 16 |
| ASELS `1643141` 2026-06 | 116 | 6 | 0 | 0 | 0 |
Every committed body resolves all four block anchors from its own rows, so the fail-safe's incidence
on captured filings is **unmeasured** — its case is a mutation, stated rather than hidden.
### What landed
- **`KapPublicReportModels.cs`** — `KapPublicLabelRule.Scope` (additive, default `null`); the closed
  `KapPublicScopes` vocabulary (`CURRENT`/`NONCURRENT`/`PL`/`OCI`) beside `KapPublicSections`;
  `KapPublicReportReasons.BlockUndetermined` (the closed reason set now 17 codes); and
  `KapPublicReport.Consolidated`, the decode-only flag derived from `Header.ConsolidationType`.
- **`StatementMapper.cs`** — 42 additive codes, registry 23 → 65. No code renamed or redefined.
- **`KapPublicReportDecoder.cs`** — `ScopedRules` (23 entries: 8 BS block-qualified, 1 IS `NI_PARENT`,
  11 CF block-scoped, plus the `ST`/`LT` trade-receivable/payable pairs), `ScopedRulesByStatement`,
  `ScopedLabelMap`, the row-scope switch that resolves each row's scope **before** mapping
  (`BalanceSheetBlockFor` / `IncomeStatementScopeFor`, mirroring `CashFlowSectionFor`), the
  `BLOCK_UNDETERMINED` emission (once per row), the scope-aware `MapCode`, `HasScopedRule` and
  `ConsolidatedFor`. 29 new unqualified entries wired. `LabelMap` stays a pure function of
  `(statement, label)`.
- **Tests** — nine TC-MDF-122 cases (a)–(i) in `KapPublicReportDecoderTests.cs`, every one tagged.
- **Docs** — test plan §33 + v1.45; `01` v1.5.69 §10.9.19 (the subsection supersedes the planner's
  dated design note; the closed reason-set list gains `DUPLICATE_LABEL_DISAMBIGUATED` and
  `BLOCK_UNDETERMINED`); `02` §6.1's chart extension rewritten to the landed names, including the
  recorded choice that non-current trade receivables/payables get `LT_TRADE_RECV`/`LT_TRADE_PAY`
  rather than joining the `NONCUR_*_OTHER` families.
### Test evidence
- **Red (measured, not remembered):** with the 41 new map entries stripped (models, scope machinery
  and assertions kept) the `TC_MDF_122` filter reads **7 failed / 2 passed** — (a) `Assert.NotEmpty()
  Failure: Collection was empty` on `ScopedLabelMap`; (b) and (c) `KeyNotFoundException:
  'ST_TRADE_RECV'`; (d) `Assert.Single() Failure: The collection did not contain any matching items`
  for `BLOCK_UNDETERMINED`; (f) `'NI_PARENT'`; (g) `'IMPAIRMENT'`; (i) `'ST_LEASE_LIAB'`. (e) and (h)
  pass by construction — the additive goldens are what the unmodified map produces, and the flag is
  model-side — recorded as such rather than manufactured red.
- **Green:** `KapPublicReportDecoderTests` **39/39** (30 pre-existing + 9 new). Non-regression
  `KapPublicStatementsIngestTests | RealSourceStatementsTests | StatementNormalizationTests |
  StatementVersioningTests` **31/31**. `dotnet build` 0 warnings.
### Amendment executed (user-approved 2026-10-28)
Six assertions unavoidably invalidated by the expansion were re-pinned, **every TC id kept, no intent
weakened** — recorded in test plan v1.45, `01` v1.5.69 and here: TC-MDF-107 (iii) (the trade
receivable's code, its passthrough proof moved to the still-unmapped related-party caption, and its
financing companion moved one row over because that caption is now `DEBT_ISSUED`); TC-MDF-108 (v)
(mutate every `CF` entry of both projections instead of six hand-written labels — set-closed and
therefore stronger); TC-MDF-117 (b) (exhaustive canonical surface 6 → 25 codes, same claim, same
`ST_DEBT`+`LT_DEBT` narrowness); TC-MDF-117 (d)/(e) (disambiguation totals 22 → 18 and 42 → 34
because the repeated families are now placed per block; both cases' conflict claims untouched).
### Flagged items
1. **Konsolide/Solo is decode-only here.** `KapPublicReport.Consolidated` is carried on the decode
   result; persisting and serving it is 094's (no schema change was made and none is needed now).
2. **DR-1/DR-2 remain open and fenced, untouched.** `TOTAL_EQUITY` is still read NCI-inclusive;
   `debt` is still `ST_DEBT` + `LT_DEBT` only — the new lease/parent/NCI codes sit *beside* them and
   never widen either. The current portion of long-term borrowings still stays `OTHER_BS_*`.
3. **`BLOCK_UNDETERMINED` is unmeasured on real filings** — all five committed bodies resolve their
   blocks, so the case is a mutation. The live criterion re-checks it on the production walk.
4. **Live criteria (orchestrator, post-merge, paced, never CI):** one `Source = kap-public` statements
   pass writing the expanded canonical row set — per-family coverage counts verbatim, honest-absent
   families named (expected: none).
