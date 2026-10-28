---
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/KapPublicReportModels.cs
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-088: IS/BS decode coverage — the public-site FR decoder maps the income-statement and balance-sheet lines (capture-first), so the DCF's `debt`/`cash` facts and the financials table's revenue/NI rows have a source

- Status: done
- Size: M
- **Priority: P1 — follow-up slice 1a of the 2026-10-27 live-run wave (the user's this-week goal: fair value per share on real KAP data; this ticket is the decode half, TKT-foundation-089 the consumption half).**
- **The live evidence this slice answers (2026-10-26/27 first live `kap-public` run, recorded on 081/082/083/086/087):** 62 statements written across 7 companies — **60 CF / 2 IS / 0 BS**. The FR excel-HTML reports *contain* income statements and balance sheets (the ASELS capture decodes BS 4+4 / IS 3+3 lines), but live reports from the walked companies produced no IS/BS statement rows. Consequences, all recorded live: `GET /stocks/{symbol}/dcf` for TUPRS/TAVHL returns `state: "missing_inputs"` naming `['debt','cash']`; revenue on kap-public financials periods is null (083's live note); `metrics-recompute` wrote 0 rows (083's open criterion).
- **USER REQUIREMENT (recorded verbatim in intent, 2026-10-27):** in the DCF calculator, the `debt` input (**borç** — the financial debt lines from the balance sheet) and the `cash` input (**nakit ve benzerleri** — cash and cash equivalents) **must come from the KAP reports, with the same honest-data provenance as FCF** (canonicalFactRefs pointing at statements). The consumer side already exists and is honest — `DcfBaselineGenerationJob` reads `ST_DEBT`/`LT_DEBT`/`CASH` from balance-sheet line items and carries per-fact refs — the gap is purely decode coverage. This ticket never changes the DCF formula, threshold or precedence.
- **What to implement (capture-first, never guessed — the 081 doctrine unchanged):**
  1. **The capture set (orchestrator-owned, pre-dispatch or riding the ticket's first step; paced, ≥3 s, single-flight, honest UA, one report at a time):** (a) **ASELS 2025 Yıllık, public index `1561039`** — this also discharges 081's OPEN annual-report live criterion (its committed byCriteria list row exists; no body is committed); (b) **TUPRS 2025 Yıllık** (the debt/cash evidence for the ticket's headline consumer); (c) a bounded set of the live-walked reports whose IS/BS produced nothing (e.g. one MGROS/KCHOL interim) — the diagnosis input. Each body committed to `RealSourceCatalog.cs` as a **bounded-trim double, labeled as such** (the 1643141 precedent). No map entry is ever authored from an uncommitted body.
  2. **The diagnosis (investigation-first):** decode the captured live bodies through the current decoder and read the findings ledger — name, per reason code, why the live IS/BS tables decoded to nothing (candidate classes, to be verified not assumed: label-spelling drift across filers/taxonomy versions, `COLUMN_HEADER_MISMATCH` on the IS 4-column shape, heading variants selecting no statement type). The diagnosis is recorded on this ticket verbatim before the fix.
  3. **The map extension:** IS lines (REV/COGS exist; add EBIT/NI and any other canonical IS codes the captures evidence — never a spelling a committed capture does not carry, every entry naming its capture, `KapPublicLabelRule.Provenance`); **BS lines — the debt/cash core: `CASH` (exists), the financial-debt lines → `ST_DEBT`/`LT_DEBT`** (financial borrowings only — never trade payables, never operating liabilities; where the capture carries only a combined debt figure with no current/non-current split, that is a recorded finding routed to the planner/user, never a guessed split and never a new canonical code), and `TOTAL_EQUITY`/other canonical BS codes as evidenced. The duplicate-label, section-before-label, honest-null and `OTHER_*` passthrough rules are unchanged.
  4. **Red→green:** a captured live report that yields no IS/BS statement rows on the current head must yield them after the fix — that regression case is the suite's red.
- **Pinned non-goals:** no cumulative-vs-period semantics change (that verification is TKT-foundation-090 — sub-period columns keep being skipped `SUB_PERIOD_COLUMN`, interim columns keep their current handling); no DCF/consumer change (089); no share-count mapping (065/086 routing stands); no equity-statement decode (the `NO_COLUMN_HEADERS` drop stands); no VYK decoder change.
- Traces to: FR-MDF-002/003 (statement ingest), FR-MDF-008 (provenance `kap-public`), FR-MDF-012 (quarantine/honest gaps), FR-VAL-001 (DCF baseline — debt/cash facts), FR-RES-005..018 (served financials supply), `02` §6.1 (concept-map doctrine over the HTML label space).
- Acceptance: **TC-MDF-117** (test plan §28, v1.36 reservation — planner-authored per the lane precedent, test-planner confirmation pending) — L2 doubles only (the committed captures are the entire input; zero live requests from CI); red→green recorded (red = the captured live report's IS/BS rows absent on the current head — the live walk's own observed state). Non-regression: `KapPublicReportDecoderTests` ASELS goldens byte-identical (OCF/CAPEX/FCF from 1643141 unchanged), `KapPublicStatementsIngestTests`, `RealSourceStatementsTests`, `StatementNormalizationTests`, `StatementVersioningTests` re-run green with pre-listed additive deltas only. **Live criteria (orchestrator-owned, post-merge, recorded verbatim):** (i) one paced `Source = kap-public` pass writes IS and BS statements (not just CF) for the covered companies — per-statement-type counts verbatim; (ii) the ASELS `1561039` annual decode (081's open criterion) — annual-column OCF/CAPEX goldens derived from its own cells, recorded verbatim; (iii) `fin_line_items` gains ST_DEBT/LT_DEBT/CASH rows for TUPRS's FY2025 BS.
- Architecture refs: `01-system-architecture.md` §10.9.19 (decoder subsection — the IS/BS coverage subsection extends it) + §13.1 **v1.5.62** (soft reservation, next free above the landed v1.5.61; re-pin at merge); test plan §28 (TC-MDF-117).
- UX refs: none (decoder lane; the served presentation exists — 089 verifies it).
- Dependencies: none hard (079/081/082/083 merged). **Serializes with TKT-foundation-090/091 on `KapPublicReportDecoder.cs` + `RealSourceCatalog.cs` + the two permanent docs** (last-lander rebase, the lane doctrine); dispatch order 088 → 089 → 090 recommended (the user's slice order).
- Parallel group: none.

## Implementation record (2026-10-27)

- PR: https://github.com/sarperim/degerli/pull/150 — branch `tkt-foundation-088`, base `dev`.
- Size M, one lane, no parallel group. Scope touched exactly: `src/Ingestion/Statements/KapPublicReportDecoder.cs`, `src/Ingestion/Statements/KapPublicReportModels.cs`, `fixtures/Degerli.Fixtures/RealSourceCatalog.cs`, `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs`, `.pipeline/testing/market-data-foundation.md`, `.pipeline/architecture/01-system-architecture.md`. `tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs` was **not** touched (its 16 cases re-run green unchanged).

### The diagnosis (investigation-first, recorded before the fix)

**The ticket's premise is wrong on the current head, and the correction is the diagnosis: the
captured live IS/BS bodies do not decode to nothing — they decode in full and are then
quarantined.** Measured on the three committed bodies through the pre-fix decoder:

| capture | tables | decoded lines (current column) | canonical codes | `DUPLICATE_LABEL_CONFLICT` (pre-fix) | stored rows (pre-fix) |
|---|---|---|---|---|---|
| TUPRS 2025 Yıllık `1554106` | BS/IS(×2)/CF/EQ | BS 73, IS 47, CF 53 | BS 2, IS 3 | BS 5 codes × 2 columns, IS 3 | 2 (CF only) |
| ASELSAN 2025 Yıllık `1561039` | BS/IS/CF/EQ | BS 87, IS 47, CF 58 | BS 2, IS 3 | BS 18, IS 2 | 2 (CF only) |
| MİGROS 2026-06 `1648240` | BS/IS(×2)/CF/EQ | BS 73, IS 33, CF 51 | BS 2, IS 6 | BS 11, IS 1 | 2 (CF only) |

Cause, per reason code, in the decoder's own vocabulary: the statements decode; the label map
covers only 2–6 canonical codes per statement; every remaining row is an `OTHER_{ST}_*`
passthrough; the balance sheet **legitimately repeats** labels in its current and non-current
blocks (`Ticari Alacaklar`, `Peşin Ödenmiş Giderler`, `Finansal Yatırımlar`, `Türev Araçlar` ×4,
and the `ARA TOPLAM` sub-total) and the income statement repeats `Ana Ortaklık Payları` across its
profit-allocation and comprehensive-income sections. The pre-fix duplicate rule treated those as
unequal-value duplicates of one synthesised code, wrote `DUPLICATE_LABEL_CONFLICT`, set
`ConflictingItemCodes`, and `FinancialStatementStore` quarantined the **whole statement row** as
`CONFLICTING_VALUE`. That is exactly the live walk's observed shape — **60 CF / 2 IS / 0 BS**, with
2 stored rows per report (the CF pair) and `debt`/`cash` therefore absent for the DCF.

Candidate classes the ticket listed, each verified rather than assumed: **label-spelling drift**
— a contributing cause, not the blocking one (it explains the 2–6 canonical codes, fixed by the map
extension); **`COLUMN_HEADER_MISMATCH` on the IS 4-column shape** — **not** observed (the IS pairs
correctly; the 4-column MİGROS/TUPRS IS shape is handled, and the sub-period pair is skipped
`SUB_PERIOD_COLUMN` as pinned); **heading variants selecting no statement type** — **not** observed
(`UNKNOWN_TABLE_HEADING` is 0 on all three; MİGROS's `Kar veya Zarar Tablosu` and `Diğer Kapsamlı
Gelir Tablosu` both type as `IS`).

Further recorded facts on this head, unchanged by this ticket: TÜPRAŞ and MİGROS file separate
income-statement and comprehensive-income tables, so the OCI rows type as `IS` and are named
`NO_CANONICAL_LINES` (44 TÜPRAŞ, 20 MİGROS); the equity matrix is still an honest-null drop
(`NO_COLUMN_HEADERS`); the 1643141 interim cannot evidence debt or annual columns at all (6-month
trim, BS 4 / IS 3 rows) — which is why the three new doubles exist.

### What was implemented

1. **The capture set, committed as bounded-trim doubles** (`RealSourceCatalog.cs`, labeled as
   trims with kept/total row counts and file sizes): `KapPublicReportTuprs`
   (`kap-public-report-1554106`, TÜPRAŞ 2025 Yıllık, Yıl 2025 / Periyot 4, filed 06.02.2026
   18:34:29), `KapPublicReportAselsAnnual` (`kap-public-report-1561039`, ASELSAN 2025 Yıllık,
   filed 24.02.2026 18:27:36 — also the annual body 081 recorded as still owed), and
   `KapPublicReportMgros` (`kap-public-report-1648240`, MİGROS 2026-06, filed 11.08.2026
   21:43:53). All three: `Sunum Para Birimi: 1.000 TL`, Konsolide. The trim derivation is
   reproducible byte-for-byte; no map entry was authored from an uncommitted body.
2. **The map extension** (decoder `Rules`): IS `ESAS FAALİYET KARI (ZARARI)`→EBIT, `Genel Yönetim
   Giderleri`→SGA, `Finansman Giderleri`→FIN_EXP, `SÜRDÜRÜLEN FAALİYETLER VERGİ ÖNCESİ KARI
   (ZARARI)`→PRETAX_INC, `Sürdürülen Faaliyetler Vergi (Gideri) Geliri`→TAX_EXP, `DÖNEM KARI
   (ZARARI)`→NI; BS `Kısa Vadeli Borçlanmalar`→ST_DEBT, `Uzun Vadeli Borçlanmalar`→LT_DEBT,
   `TOPLAM ÖZKAYNAKLAR`→TOTAL_EQUITY, `TOPLAM DÖNEN VARLIKLAR`→CUR_ASSETS, `TOPLAM KISA VADELİ
   YÜKÜMLÜLÜKLER`→CUR_LIAB. Every entry names its capture via `KapPublicLabelRule.Provenance`; the
   closed set is `KapPublicReportDecoder.CaptureNames`. Targets reuse the VYK concept map's
   canonical assignments (`02` §6.1 not forked).
3. **The duplicate rule, scoped** (`Group.Add`/`Group.Disambiguate`, `GroupOutcome.Disambiguated`,
   `KapPublicReportReasons.DuplicateLabelDisambiguated = "DUPLICATE_LABEL_DISAMBIGUATED"`): an
   unequal second value for an `OTHER_*` passthrough code is kept as `{code}_{2..n}`, counted, and
   is **not** a conflict; canonical codes keep the conflict → `ConflictingItemCodes` →
   `CONFLICTING_VALUE` path exactly as before; first-occurrence passthrough codes are unchanged.
4. **The routed gap recorded in the class doc**, not guessed: the current portion of long-term
   borrowings stays `OTHER_BS_*`; `debt` = `ST_DEBT` + `LT_DEBT` is the two borrowings lines only.
5. **Tests**: TC-MDF-117 (a)–(f) in `KapPublicReportDecoderTests.cs`, each tagged `TC_MDF_117`;
   test plan §28 + v1.36; architecture §10.9.19 subsection + §13.1 v1.5.62.

Post-fix stored facts (thousands → single TL by the store's scale), all six rows storing per
report: TUPRS FY2025 BS `ST_DEBT` 8.957.100 / `LT_DEBT` 28.428.775 / `CASH` 107.237.211 /
`TOTAL_EQUITY` 369.827.994 / `CUR_ASSETS` 236.931.136 / `CUR_LIAB` 169.748.201 (debt 37.385.875);
TUPRS FY2025 IS `REV` 830.356.131 / `COGS` −749.123.853 / `GROSS_PROFIT` 81.232.278 / `SGA`
−22.373.179 / `EBIT` 41.646.897 / `FIN_EXP` −21.143.896 / `PRETAX_INC` 43.768.722 / `TAX_EXP`
−13.896.050 / `NI` 29.872.672; ASELSAN FY2025 BS `ST_DEBT` 13.125.911 / `LT_DEBT` 5.028.364 /
`CASH` 29.086.476 / `TOTAL_EQUITY` 251.786.294; ASELSAN FY2025 IS `REV` 180.444.938 / `EBIT`
49.145.855 / `PRETAX_INC` 26.390.612 / `TAX_EXP` 3.527.115 / `NI` 29.917.727; MİGROS 2026-06 BS
`ST_DEBT` 4.974.529 / `LT_DEBT` 34.053.178 / `CASH` 32.550.445. `DUPLICATE_LABEL_DISAMBIGUATED`
counts: 22 / 42 / 24; `DUPLICATE_LABEL_CONFLICT`: 0 / 0 / 0.

### Approved deviations (user decisions, 2026-10-27)

1. **The duplicate rule is extended for `OTHER_*` passthrough codes only** (question tool,
   2026-10-27). Canonical-code conflicts keep today's behaviour; a repeated passthrough label with
   an unequal value keeps **both** values (second under a counted `{code}_n`), adds no conflict and
   the row stores. First-occurrence passthrough codes stay byte-identical so 081's 1643141 goldens
   hold. Recorded in the ticket, test plan §28/v1.36 and architecture v1.5.62, and pinned by
   TC-MDF-117 (d)/(e).
2. **TC-MDF-107 (i) is generalized from one capture to the capture set** (question tool,
   2026-10-27). Its assertion was `rule.Provenance == KapPublicReportDecoder.CaptureName` plus the
   label's presence in the 1643141 body — unsatisfiable once clause 3 requires each entry to name
   *its own* capture. It now asserts the provenance is one of `CaptureNames` **and** the label is
   present in the body that provenance names: the same invariant, nothing previously pinned
   unpinned. TC-MDF-117 (f) carries it again from the catalog side. Recorded in the test file's
   own doc comment, test plan §28 and v1.36.

### Test evidence (executed in this environment — Docker/Testcontainers available)

- **Red 1** (passthrough branch restored to the pre-ticket conflict behaviour): `Failed: 5, Passed: 21,
  Total: 26` — (a) four of six `StatementFact`s carrying `ConflictingItemCodes` and `Inserted`
  **Expected 6, Actual 2**; (b) `Inserted` **Expected 6, Actual 2** with the TÜPRAŞ balance sheet
  quarantined; (c) the ASELSAN income statement quarantined; (d) `Assert.Null` on
  `ConflictingItemCodes` with `["OTHER_BS_Finansal_Yatırımlar", "OTHER_BS_Türev_Araçlar", …]`;
  (e) `DuplicateLabelConflict` **Expected 2, Actual 44**.
- **Red 2** (map extension alone reverted, duplicate rule kept): `Failed: 4, Passed: 22` —
  `KeyNotFoundException: 'ST_DEBT'` (b), `KeyNotFoundException: 'SGA'` (c), the provenance case's
  filter, and (e). Both halves of the fix are load-bearing.
- **Green**: `KapPublicReportDecoderTests` **26/26 passed** (the 20 pre-existing TC-MDF-106/107/108
  cases included, unamended except the user-approved 107 (i) generalization; the 1643141 goldens —
  OCF 15.206.440 / CAPEX −33.746.150 / FCF and every line count — are byte-identical:
  `report.Lines.Count == 116`, BS 4+4, IS 3+3, CF 51+51 still hold).
- **Non-regression**: `KapPublicStatementsIngestTests` + `RealSourceStatementsTests` +
  `StatementNormalizationTests` + `StatementVersioningTests` + `RealSourcePricesTests` +
  `KapPublicSourceTests` → **51/51 passed**. `dotnet build` 0 warnings (tests + src).
- Nothing was skipped or weakened; expected results were never adjusted to match the
  implementation.

### Flagged / routed

- **Routed to the planner/user (modelling decision, not decode):** should the current portion of
  long-term borrowings (`Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları`) be aggregated into
  `ST_DEBT`? It stays an `OTHER_BS_*` companion here; merging presentation lines into a canonical
  code would either overwrite a reported value or manufacture an unreported sum.
- **Recorded, not fixed:** TUPRS/MİGROS OCI rows type as `IS` and are counted
  `NO_CANONICAL_LINES` (44 / 20) because those filers file a separate comprehensive-income table —
  pre-existing behaviour on this head, outside this ticket's acceptance.
- **Live criteria remain open (orchestrator, post-merge, paced, never CI):** (i) one
  `Source = kap-public` pass writing IS **and** BS rows with per-type counts verbatim; (ii) the
  ASELSAN `1561039` annual decode with its own OCF/CAPEX goldens (081's open criterion);
  (iii) TUPRS FY2025 `fin_line_items` gaining `ST_DEBT`/`LT_DEBT`/`CASH`.

## Merge record — 2026-10-27 (orchestrator)

- **PR #150 squash-merged to `dev` as `05f4079`** after review round 1 (MERGEABLE, 0 blocking findings). Round artifact: `.pipeline/plan/reviews/TKT-foundation-088-round-1.md`.
- **Two `major` findings open and routed (NOT waived — merge did not close them):**
  - **DR-1:** `TOTAL_EQUITY` writes the NCI-inclusive total under the code `02` §6.1 defines as attributable (TÜPRAŞ FY2025: 369.8bn total vs 364.1bn attributable; ASELSAN 251.8 vs 250.4). The VYK decoder already maps the total under the same code, so this PR matches the established cross-source behavior; the §6.1 clarification (or the attributable mapping) is a planner/user decision recorded for the morning report.
  - **DR-2:** the routed current-portion-of-long-term-borrowings question is materially larger than the ticket stated — TÜPRAŞ current portion 12.86bn vs stored debt 37.39bn = **25.6% understatement of true borrowings**, affecting DCF `debt`, `ev_ebitda`, `net_debt_ebitda`, `roic` (§6.4 "total debt ST+LT"). The line currently lands in `OTHER_BS_*`. **Open user decision:** map `Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları` into `ST_DEBT` (a small follow-up), or keep the strict split. Until decided, the DCF's `debt` is KAP-sourced but partial.
- Nits DR-3/4/5 recorded in the round artifact, unfixed.
- Post-merge live criteria (orchestrator-owned) still owed: paced `kap-public` pass writing IS+BS rows; ASELS 1561039 annual OCF/CAPEX goldens; TUPRS FY2025 `fin_line_items` gaining `ST_DEBT`/`LT_DEBT`/`CASH`.

## Live criteria — 2026-10-27/28 (orchestrator, dev stack, recorded verbatim)

- **Criterion (i) PARTIAL — IS yes, BS no.** Statements run 363 (post-restart): written 18 (all GARAN — first newly-resolved company walked: IS 80 line-items/period, CF 43/period, periods through Q2-2026; netIncome + pretax map, 78 IS lines honest `OTHER_*` — bank format). Totals now 11 IS / 71 CF / **0 BS** / 9 companies. **BS=0 root cause (two-part):** (a) previously-walked companies' reports are in their cursors' seen-sets — the walk never re-fetches them, and the code docs confirm re-ingestion "needs an explicit cursor reset" (no such admin capability exists for this lane); (b) the only freshly-walked company (GARAN) is bank-format with unmapped BS labels. **Follow-up (prerequisite for BS data on the original 7 companies + ASELS): kap-public statement re-decode re-walk (063-class cursor reset for reports decoded under pre-088 code).**
- **Criteria (ii) ASELS 1561039 goldens and (iii) TUPRS FY2025 ST_DEBT/LT_DEBT/CASH — blocked on the same re-walk** (both reports are already in seen-sets). `/stocks/TUPRS/dcf` live state: `missing_inputs ['debt','cash']`, base_fcf 30,346,395,000 (FY2025, unchanged).
- **Bank-format finding (recorded for the statements-page slice):** GARAN's IS carries Net Faiz Geliri-family labels — only NI/PRETAX_INC map; the industrial label set does not cover bank statements. Bank labels are a separate capture-first slice; `/stocks/GARAN/dcf` correctly serves `sector_not_applicable`.
