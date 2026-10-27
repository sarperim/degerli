---
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/KapPublicReportModels.cs
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs  # declared for step 1's capture; NOT modified — no live capture was made; 088's committed bodies were the input
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-090: Cumulative-vs-period semantics verification — one real source report determines whether the decoder stores YTD interim figures as period figures, and the honest behaviour when semantics cannot be determined

- Status: done
- PR: [#152](https://github.com/sarperim/degerli/pull/152) → `dev`
- Size: M
- **Priority: P1 — follow-up slice 2 of the 2026-10-27 live-run wave. This protects every DCF baseline number: the FY baselines consume these statements, and the interim financials display labels them.**
- **The suspicion (recorded verbatim from the 2026-10-26/27 live run, on this ticket so it is never lost):** TUPRS **Q3-2025 FCF ₺36.9bn > FY2025 ₺30.3bn**; TUPRS **Q2-2026 FCF ₺120.5bn** dwarfs every other period; KCHOL **FY2025 FCF −₺204.9bn** is similarly suspect. Hypothesis (to be verified, not assumed): Turkish interim FR reports carry **cumulative (year-to-date) columns** — a "Q2" filing's main columns cover 01.01–30.06 — and the decoder may be storing the YTD figure under the interim's `periodEndDate` as if it were that period's discrete figure, so consumers (quarter-labelled display, any future TTM) misread it. An alternative hypothesis for the FY anomalies (a wrong row mapped, a comparative-column confusion) is equally live until the verification runs — the ticket determines which.
- **What to implement (investigation-first, single-report, paced — the politeness doctrine):**
  1. **The verification probe (orchestrator-owned or the ticket's first step): one paced live fetch of the TUPRS Q2-2026 FR excel-HTML report** (the report behind the ₺120.5bn figure; ≥3 s, single-flight, honest UA, one request at a time), committed to `RealSourceCatalog.cs` as a bounded-trim double labeled as such. From its own cells: read the column headers' declared date ranges (the `Cari Dönem 01.01.2026–30.06.2026`-shape declarations), the header's `Periyot`, and the CF figures; determine **which column semantics the decoder currently applies and which it should** — cumulative-YTD-labelled-as-duration vs discrete-period. The determination and its evidence are recorded verbatim on this ticket before any fix.
  2. **The fix (only if the verification finds the current behaviour wrong):** interim facts carry their **true declared duration** (the column's own date range), never a discrete-quarter label for a cumulative figure; the store's `periodType`/`periodEndDate` handling and the DCF's FY filter must be provably unable to consume a cumulative interim as an annual. **The honest behaviour when semantics cannot be determined** (a report whose headers declare no readable range, or a declaration that contradicts its own cells): a **named, counted finding** and the fail-safe already in the decoder's spirit — the statement is typed `Q` (never `FY`), the ambiguity is surfaced on the report's findings ledger, and no consumer silently treats the figure as a discrete period. Never a guess, never a silent default.
  3. **The suspect numbers explained or corrected:** the TUPRS Q3-2025/FY2025/Q2-2026 and KCHOL FY2025 FCF figures are each accounted for on this ticket (semantics, decode defect, or genuinely real — with the arithmetic shown from the report's own cells).
  4. **Data repair is a recorded follow-up, not silent:** if the fix changes what should be stored, already-written interim rows are **not** rewritten in place by this ticket — the repair route (explicit cursor reset + re-walk for the affected companies, the v1.5.56-recorded condition and the 063 doctrine) is recorded on this ticket and executed as the orchestrator's/user's recorded step, because historical immutability (FR-MDF-015) and the versioning rules stand.
- **Pinned non-goals:** no TTM-by-subtraction (the 081 decision record stands — future refinement); no annual-basis change for the DCF (FY-only consumption stands); no new statement-store schema unless the fix provably needs one (if it does, amend scope per the 034 Option-A precedent — never widen silently).
- Traces to: FR-MDF-002/003 (statement ingest), FR-MDF-008 (provenance), FR-MDF-015/NFR-MDF-002 (historical immutability — no silent rewrite of stored facts), FR-VAL-001 (the baselines this protects), FR-RES-005..018 (the interim-labelled display), `02` §3.1 (period semantics).
- Acceptance: **TC-MDF-119** (test plan §30, v1.38 reservation — planner-authored, test-planner confirmation pending) — L2 doubles only (**premise substitution, recorded:** step 1's TUPRS Q2-2026 body was never captured and no fixture changed; the verification input is 088's already-committed MİGROS `1648240` interim (plus its annual companions), so TUPRS-specific figures remain unattributed — zero live requests from CI); red→green recorded **only if the verification finds a defect** (honestly stated as "verified correct, no red" otherwise — a verification ticket whose finding is "no bug" records that, it does not manufacture one). Non-regression: `KapPublicReportDecoderTests` (the ASELS + annual goldens from 088 unchanged — annual columns are unaffected by cumulative semantics), `KapPublicStatementsIngestTests`, `StatementVersioningTests` re-run green. **Live criteria (orchestrator-owned, recorded verbatim):** the verification evidence (column declarations vs decoded figures) recorded on this ticket; each of the four suspect numbers accounted for **as far as a committed body attests it** — and explicitly recorded as unattributed where none does; if a re-walk repair is decided, its before/after counts recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.19 (decoder subsection — the period-semantics subsection) + §13.1 **v1.5.64** (soft reservation; re-pin at merge); test plan §30 (TC-MDF-119).
- UX refs: none (decoder lane; the interim labelling on SCR-005 is 057's landed surface and consumes whatever duration semantics this pins).
- Dependencies: TKT-foundation-088 (shared `KapPublicReportDecoder.cs`/`KapPublicReportModels.cs`/`RealSourceCatalog.cs`/decoder test file — serial by file overlap; last-lander rebase on the two permanent docs). Dispatchable after 088 in either order relative to 089; **if 090 lands after 089 and its fix changes stored interim facts, 089's live criteria re-run on the corrected data** (recorded on both tickets — rework of completed work is the user's call).
- Parallel group: none.

---

## Implementation record (2026-10-27)

**Outcome: verified — the suspicion is unfounded on the interim path; one real defect found and fixed.** No live fetch was issued by this lane (politeness doctrine): the verification ran on the three real bodies already committed as bounded-trim doubles by 088, read through the production decoder.

**Premise substitution, recorded:** step 1 named one paced live fetch of the **TUPRS Q2-2026** body as the verification input, committed to `RealSourceCatalog.cs`. That capture was **not made** and no fixture file changed; MİGROS `1648240` (committed by 088) stands in as the verification input. The mechanism is therefore verified generically on a committed interim, and TUPRS-specific numbers (Q3-2025, Q2-2026) remain **unattributed** — see §4 and Flagged (e).

### (1) The determination, from the report's own cells (TC-MDF-119 (i), §30/v1.38)

The MİGROS 2026-06 interim body (`kap-public-report-1648240`, `Periyot 2` → `6 Aylık`) declares its **main** income-statement and cash-flow columns as `Cari Dönem 01.01.2026 - 30.06.2026` — a **six-month cumulative (YTD) range**. Its **income-statement** (and OCI) tables carry the discrete quarter as a **separate** column, `Cari Dönem 3 Aylık 01.04.2026 - 30.06.2026`, which the decoder already names and skips as `SUB_PERIOD_COLUMN` (v1.5.55's pin). Its **cash-flow table (`role_520003`) has only two columns** — `Cari Dönem 01.01.2026 - 30.06.2026` and `Önceki Dönem 01.01.2025 - 30.06.2025` — with no `3 Aylık` sibling, so the CF figure's span is attested by its own declared range. So the decoder is **not** substituting a three-month cell for a cumulative figure: it decodes the column whose declared duration is the report's own and stores it `Q` at the filing's own `periodEndDate` carrying the cumulative figure.

The body's own raw cells are decisive (`title` attributes, thousands TL):

| line | 6-month (cumulative) column | 3-month (discrete Q2) column — IS/OCI only | decoded |
|---|---|---|---|
| `Hasılat` / `REV` | **241.298.039** | 124.399.203 | **241.298.039** |
| `DÖNEM KARI (ZARARI)` / `NI` | **1.078.256** | −644.936 | **1.078.256** |
| `OCF` (statement's own operating total) | **17.837.860** | — (no such column) | **17.837.860** |
| `CAPEX` (combined purchases) | **−6.297.863** | — (no such column) | **−6.297.863** |
| FCF = OCF − \|CAPEX\| | **11.539.997** | — | **11.539.997** |

Every decoded line now also states the span it was decoded under (`KapPublicLine.PeriodStartDate` = 2026-01-01, `DurationMonths` = 6) — additive **decode-result** provenance, available to the decode's own consumers and tests (instant balance-sheet lines carry `null`/`null`). It is **not** stored or served: `Group.ToFact()` drops it, `FinancialStatement` has no span field, and the financials payload emits only `periodType`/`fiscalYear`/`periodEndDate`, so the served label remains the store's `FY`/`Q` vocabulary (routing item (d) below).

### (2) The annual basis is twelve-month columns, unaffected

TÜPRAŞ 2025 Yıllık (`1554106`) CF header `01.01.2025 - 31.12.2025`: `49.464.734 − |19.118.339| = ` **30.346.395** thousand TL = **₺30,35bn**.
ASELSAN 2025 Yıllık (`1561039`): `48.901.969 − |38.168.467| = ` **10.733.502** thousand TL.
Both type `FY` because a twelve-month duration attests them; 088's/089's annual goldens hold verbatim.

### (3) The one real defect found, and the fix

A report self-declaring a `Periyot` while **no** column declares a readable date range had **undetermined semantics**, and the pre-ticket decoder accepted the declaration silently and typed its instant (balance-sheet) rows `FY` — the exact shape that could place a half-year `debt`/`cash` into the DCF's annual basis. **Its incidence is unmeasured:** no captured body lacks declared ranges, so the case is a mutation of the committed interim body, not an observed filing. The trade-off is named too: in such a report the 2025-12-31 **annual comparative** balance sheet now types `Q` as well and drops out of the FY-only `BalanceSheetAsync` read — an annual figure hidden from the baseline rather than a half-year let into it (the safe direction).

Fix (in scope: decoder + models):
- new reason **`PERIOD_SEMANTICS_UNDETERMINED`** — one named, counted finding per report;
- fail-safe: **every** statement row types `Q` (both the annual-declared and the interim-declared variant — an interim's `Q` must rest on evidence too, not merely on the absence of danger);
- flow tables whose columns cannot be attributed at all still invent nothing (`COLUMN_UNATTRIBUTED` drops); the unattested balance sheet is still stored, honestly as an interim;
- `KapPublicLine.PeriodStartDate` / `DurationMonths` added (additive) as above.

### (4) The four suspect live numbers, accounted for as far as the committed bodies allow

- **TUPRS FY2025 FCF ₺30,3bn** — *verified from the body*: `49.464.734 − |19.118.339| = 30.346.395` thousand TL (§2). It matches the live figure; it is not anomalous.
- **TUPRS Q3-2025 FCF ₺36,9bn — mechanism verified generically; this specific figure unattributed.** No TUPRS 9-month body exists in the repo (and this lane made no live capture), so nothing here attests that 2025-09-30 row's own cells. What *is* verified, on committed interims, is the mechanism: the main column is the filing's declared cumulative range, the decoder decodes that column and skips any three-month sibling, and the store types the row `Q` at the declared period end (TC-MDF-119 (i)/(iii), witnessed on MİGROS `1648240` and ASELSAN `1643141`). Under that mechanism a Q3-2025 row would hold the nine-month cumulative figure, which can exceed the full-year figure only if Q4 net cash flow is negative. The `₺36,9bn − ₺30,3bn ≈ −₺6,6bn in Q4` line is therefore an explicitly **labelled plausibility sketch, not evidence** — no report's cells show it — and the row's actual value stays unattributed pending a committed TUPRS 9M body (routing item (e)).
- **TUPRS Q2-2026 FCF ₺120,5bn — same class, same limit: mechanism verified generically, figure unattributed.** It would be a six-month cumulative 2026 interim decoded as declared, and that behaviour is witnessed on **two** committed 2026 interims by structurally different filers — MİGROS `1648240` and ASELSAN `1643141` (whose CF table declares `Cari Dönem 01.01.2026 - 30.06.2026`). That is a **mechanism** witness only: the TUPRS Q2-2026 body named in step 1 was never captured, so whether ₺120,5bn is that half-year's cash flow is **not asserted** here. The earlier "same table shape" generalisation is withdrawn — TUPRS files separate IS/OCI tables (088's recorded finding), which is exactly why a second, independently committed interim is cited instead.
- **KCHOL FY2025 FCF −₺204,9bn** — a `FY`-typed annual row, so by typing it comes from a twelve-month column; **no KCHOL body is committed**, so this number cannot be attributed cell-by-cell inside this lane. It is therefore recorded as **unverified here** and routed: either the holding's consolidated twelve-month OCF/CAPEX are genuinely of that magnitude, or a mapping/label issue on that filer's line set — which would be a **separate** decode/label ticket (a capture of KCHOL's annual body is the required first step; this lane issues no live fetch).

**No stored interim figure is attributable to YTD-vs-quarter column confusion in the decoder** — that was the hypothesis, and parts (1)–(3) dispose of it on the interim path. The four live numbers themselves are only partly attributed: FY2025 is proved from its own body; Q3-2025, Q2-2026 and KCHOL remain unattributed for want of a committed body, and for KCHOL the mapping/label alternative is still live.

### (5) Data repair — recorded, not silent

No stored rows are rewritten by this ticket (FR-MDF-015). If a stored interim row is found wrong, the route is an **explicit cursor reset + re-walk** for the affected companies (the v1.5.56/v1.31 condition), executed as a recorded step by the orchestrator/user, with before/after counts recorded here.

### (6) Test evidence

- `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs` — four cases, all tagged `TC_MDF_119`: **(i)** interim column is the declared cumulative range, never a quarter — including that the CF table has exactly two columns (both six-month declared ranges, no `3 Aylık` sibling); **(ii)** annual columns are twelve months and their FCF is the year; **(iii)** a cumulative interim is stored `Q` and the FY predicate cannot reach it; **(iv)** a period no column attests is named and fails safe to `Q`.
- **30/30 passed** (`KapPublicReportDecoderTests`: 26 pre-existing 106/107/108/117 + the 4 new).
- **Red, honestly**: (i)–(iii) are green by construction because the verification found the decode **correct** — no red was manufactured for correct behaviour. (iv) is the ticket's real red: with the guard forced off (pre-ticket behaviour) it fails at `Assert.DoesNotContain() Failure: Filter matched in collection`, the collection showing `StatementFact { PeriodType = FY, PeriodEndDate = 06/30/2026 … StatementType = BS }` beside the `12/31/2025` BS row — the half-year balance sheet typed `FY`, exactly the danger. Green with the guard restored.
- **Non-regression 88/88 passed** (targeted, unrelaxed): `KapPublicStatementsIngestTests`, `StatementVersioningTests`, `DcfBaselineEndpointTests`, `KapPublicRecomputeTests`, `RealSourceStatementsTests`, `StatementNormalizationTests`, `StockFundamentalsEndpointsTests`, `DcfBaselineRegenerationTests`, `RealSourcePricesTests`, `KapPublicSourceTests`. Identical to the pre-change baseline — **no delta**.
- `dotnet build`: **0 warnings / 0 errors**.
- Docs: test plan §30 + **v1.38**; architecture §10.9.19 period-semantics subsection + **v1.5.64**.
- Zero live kap.org.tr requests in this lane; the committed doubles were the entire input.

**Flagged:** (a) the KCHOL number stays unverified (no committed body — §4 above); (b) DR-1/DR-2 remain fenced and untouched; (c) the re-walk repair is a recorded orchestrator/user step, not done here; (d) **routing — the served-label gap (057's surface):** the interim's declared span never leaves the decode (`Group.ToFact()` drops it; `FinancialStatement` and the financials payload carry no span), so the served `periodType` `Q` + `2026-06-30` still renders "2026 Q2" on SCR-005. Closing it — expose the span through the payload/store, or label interims by their dated range — is a display/consumer ticket, not this one; no API/UI code was changed here. (e) **routing — TUPRS-specific attribution:** Q3-2025 ₺36,9bn and Q2-2026 ₺120,5bn stay unattributed because no TUPRS interim body is committed; attributing them needs a paced live capture of those bodies in a later lane — **no live capture was made in this lane**, and none is claimed.

## Merge record — 2026-10-27 (orchestrator)

- **PR #152 squash-merged to `dev` as `d7edc3c`** after 2 review rounds (1 fixer dispatch — 5 major record-accuracy findings fixed, no production-behavior change after round 1; final MERGEABLE). Round artifacts: `.pipeline/plan/reviews/TKT-foundation-090-round-{1,2}.md`.
- Review verified against the committed fixture: the YTD-vs-quarter hypothesis is disposed of on the interim path (the decoder picks the column whose declared duration is the report's own; MİGROS IS/OCI carry a discrete `3 Aylık` sibling already skipped). The new guard (`semanticsUndetermined` → fail safe to `Q`, one finding per report) is strictly narrowing — no unattested body can reach `FY`.
- **Routed (not waived):** R-1 interim span not served — SCR-005 renders `Q` as a discrete quarter while the stored interim value is cumulative (display/consumer follow-up, 057's surface); R-2 KCHOL FY2025 −₺204.9bn unattributed (capture/label investigation); R-3 TUPRS Q3-2025/Q2-2026 figures unattributed (paced live capture owed). DR-10 nit (ASELSAN 1643141 credited as TC witness vs inspection) left unfixed.
- 088's DR-1/DR-2 remain fenced.
- Post-merge live criteria (orchestrator-owned, owed): after a `kap-public` walk, no `FY`-typed half-year row; the `2026-06-30` `/financials` period reads `Q`. **Status: done** (code+review; live criteria recorded when run).
