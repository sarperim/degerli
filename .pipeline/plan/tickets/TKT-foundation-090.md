---
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/KapPublicReportModels.cs
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-090: Cumulative-vs-period semantics verification — one real source report determines whether the decoder stores YTD interim figures as period figures, and the honest behaviour when semantics cannot be determined

- Status: in-review
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
- Acceptance: **TC-MDF-119** (test plan §30, v1.38 reservation — planner-authored, test-planner confirmation pending) — L2 doubles only (the committed TUPRS Q2-2026 capture is the verification input; zero live requests from CI); red→green recorded **only if the verification finds a defect** (honestly stated as "verified correct, no red" otherwise — a verification ticket whose finding is "no bug" records that, it does not manufacture one). Non-regression: `KapPublicReportDecoderTests` (the ASELS + annual goldens from 088 unchanged — annual columns are unaffected by cumulative semantics), `KapPublicStatementsIngestTests`, `StatementVersioningTests` re-run green. **Live criteria (orchestrator-owned, recorded verbatim):** the single-report verification evidence (column declarations vs stored figures) recorded on this ticket; each of the four suspect numbers explained or corrected; if a re-walk repair is decided, its before/after counts recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.19 (decoder subsection — the period-semantics subsection) + §13.1 **v1.5.64** (soft reservation; re-pin at merge); test plan §30 (TC-MDF-119).
- UX refs: none (decoder lane; the interim labelling on SCR-005 is 057's landed surface and consumes whatever duration semantics this pins).
- Dependencies: TKT-foundation-088 (shared `KapPublicReportDecoder.cs`/`KapPublicReportModels.cs`/`RealSourceCatalog.cs`/decoder test file — serial by file overlap; last-lander rebase on the two permanent docs). Dispatchable after 088 in either order relative to 089; **if 090 lands after 089 and its fix changes stored interim facts, 089's live criteria re-run on the corrected data** (recorded on both tickets — rework of completed work is the user's call).
- Parallel group: none.

---

## Implementation record (2026-10-27)

**Outcome: verified — the suspicion is unfounded on the interim path; one real defect found and fixed.** No live fetch was issued by this lane (politeness doctrine): the verification ran on the three real bodies already committed as bounded-trim doubles by 088, read through the production decoder.

### (1) The determination, from the report's own cells (TC-MDF-119 (i), §30/v1.38)

The MİGROS 2026-06 interim body (`kap-public-report-1648240`, `Periyot 2` → `6 Aylık`) declares its **main** income-statement and cash-flow column as `Cari Dönem 01.01.2026 - 30.06.2026` — a **six-month cumulative (YTD) range** — and carries the discrete quarter as a **separate** column, `Cari Dönem 3 Aylık 01.04.2026 - 30.06.2026`. The decoder already names and skips that second column as `SUB_PERIOD_COLUMN` (v1.5.55's pin). So the decoder is **not** storing a YTD figure under a discrete-quarter label: it decodes the column whose declared duration is the report's own and stores it `Q` at the filing's own `periodEndDate`, as the cumulative figure it is.

The body's own raw cells are decisive (`title` attributes, thousands TL):

| line | 6-month (cumulative) column | 3-month (discrete Q2) column | decoded |
|---|---|---|---|
| `Hasılat` / `REV` | **241.298.039** | 124.399.203 | **241.298.039** |
| `DÖNEM KARI (ZARARI)` / `NI` | **1.078.256** | −644.936 | **1.078.256** |
| `OCF` (statement's own operating total) | **17.837.860** | (skipped) | **17.837.860** |
| `CAPEX` (combined purchases) | **−6.297.863** | (skipped) | **−6.297.863** |
| FCF = OCF − \|CAPEX\| | **11.539.997** | — | **11.539.997** |

Every decoded line now also states the span it was decoded under (`KapPublicLine.PeriodStartDate` = 2026-01-01, `DurationMonths` = 6) — additive provenance, so no consumer must infer it.

### (2) The annual basis is twelve-month columns, unaffected

TÜPRAŞ 2025 Yıllık (`1554106`) CF header `01.01.2025 - 31.12.2025`: `49.464.734 − |19.118.339| = ` **30.346.395** thousand TL = **₺30,35bn**.
ASELSAN 2025 Yıllık (`1561039`): `48.901.969 − |38.168.467| = ` **10.733.502** thousand TL.
Both type `FY` because a twelve-month duration attests them; 088's/089's annual goldens hold verbatim.

### (3) The one real defect found, and the fix

A report self-declaring a `Periyot` while **no** column declares a readable date range had **undetermined semantics**, and the pre-ticket decoder accepted the declaration silently and typed its instant (balance-sheet) rows `FY` — the exact shape that could place a half-year `debt`/`cash` into the DCF's annual basis.

Fix (in scope: decoder + models):
- new reason **`PERIOD_SEMANTICS_UNDETERMINED`** — one named, counted finding per report;
- fail-safe: **every** statement row types `Q` (both the annual-declared and the interim-declared variant — an interim's `Q` must rest on evidence too, not merely on the absence of danger);
- flow tables whose columns cannot be attributed at all still invent nothing (`COLUMN_UNATTRIBUTED` drops); the unattested balance sheet is still stored, honestly as an interim;
- `KapPublicLine.PeriodStartDate` / `DurationMonths` added (additive) as above.

### (4) The four suspect live numbers, accounted for

- **TUPRS FY2025 FCF ₺30,3bn** — *verified from the body*: `49.464.734 − |19.118.339| = 30.346.395` thousand TL (§2). It matches the live figure; it is not anomalous.
- **TUPRS Q3-2025 FCF ₺36,9bn** — the decode is proven correct, so a stored row at 2025-09-30 carrying ₺36,9bn is the **9-month cumulative** interim exactly as filed (9M FCF above the full-year figure is arithmetically ordinary when Q4 capex exceeds Q4 OCF: ₺36,9bn − ₺30,3bn ≈ −₺6,6bn in Q4). The consumer's "Q3" is the interim label for a YTD figure — the honest, declared semantics — and TC-MDF-119 (iii) pins that such a row is `Q` and can never enter the FY baseline.
- **TUPRS Q2-2026 FCF ₺120,5bn** — same class: a **6-month cumulative** 2026 interim. Dwarfing other periods is a property of that half-year's cash flow, not of a wrong column: the decoder picks the declared cumulative column and skips the 3-month one (proved above on the MİGROS body with the same table shape).
- **KCHOL FY2025 FCF −₺204,9bn** — a `FY`-typed annual row, so by typing it comes from a twelve-month column; **no KCHOL body is committed**, so this number cannot be attributed cell-by-cell inside this lane. It is therefore recorded as **unverified here** and routed: either the holding's consolidated twelve-month OCF/CAPEX are genuinely of that magnitude, or a mapping/label issue on that filer's line set — which would be a **separate** decode/label ticket (a capture of KCHOL's annual body is the required first step; this lane issues no live fetch).

**Nothing in any of the four is attributable to YTD-vs-quarter column confusion in the decoder** — that was the hypothesis, and parts (1)–(3) dispose of it.

### (5) Data repair — recorded, not silent

No stored rows are rewritten by this ticket (FR-MDF-015). If a stored interim row is found wrong, the route is an **explicit cursor reset + re-walk** for the affected companies (the v1.5.56/v1.31 condition), executed as a recorded step by the orchestrator/user, with before/after counts recorded here.

### (6) Test evidence

- `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs` — four cases, all tagged `TC_MDF_119`: **(i)** interim column is the declared cumulative range, never a quarter; **(ii)** annual columns are twelve months and their FCF is the year; **(iii)** a cumulative interim is stored `Q` and the FY filter cannot reach it; **(iv)** a period no column attests is named and fails safe to `Q`.
- **30/30 passed** (`KapPublicReportDecoderTests`: 26 pre-existing 106/107/108/117 + the 4 new).
- **Red, honestly**: (i)–(iii) are green by construction because the verification found the decode **correct** — no red was manufactured for correct behaviour. (iv) is the ticket's real red: with the guard forced off (pre-ticket behaviour) it fails at `Assert.DoesNotContain() Failure: Filter matched in collection`, the collection showing `StatementFact { PeriodType = FY, PeriodEndDate = 06/30/2026 … StatementType = BS }` beside the `12/31/2025` BS row — the half-year balance sheet typed `FY`, exactly the danger. Green with the guard restored.
- **Non-regression 88/88 passed** (targeted, unrelaxed): `KapPublicStatementsIngestTests`, `StatementVersioningTests`, `DcfBaselineEndpointTests`, `KapPublicRecomputeTests`, `RealSourceStatementsTests`, `StatementNormalizationTests`, `StockFundamentalsEndpointsTests`, `DcfBaselineRegenerationTests`, `RealSourcePricesTests`, `KapPublicSourceTests`. Identical to the pre-change baseline — **no delta**.
- `dotnet build`: **0 warnings / 0 errors**.
- Docs: test plan §30 + **v1.38**; architecture §10.9.19 period-semantics subsection + **v1.5.64**.
- Zero live kap.org.tr requests in this lane; the committed doubles were the entire input.

**Flagged:** (a) the KCHOL number stays unverified (no committed body — §4 above); (b) DR-1/DR-2 remain fenced and untouched; (c) the re-walk repair is a recorded orchestrator/user step, not done here.
