---
scope:
  - src/Ingestion/Valuation/DcfBaselineGenerationJob.cs
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Ingestion/Metrics/
  - tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs
  - tests/Api.IntegrationTests/DcfBaselineRegenerationTests.cs
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
  - tests/Api.IntegrationTests/KapPublicRecomputeTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-089: The per-share DCF completes on KAP data — `debt`/`cash` facts from the public-site balance sheets flow into the baseline with FCF-grade provenance, and the financials table serves revenue/NI (EBITDA when evidenced) for kap-public periods

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/151 (base `dev`)
- Size: M
- **Priority: P1 — follow-up slice 1b of the 2026-10-27 live-run wave: the user's headline. With FCF (081/082 live) + share counts (086 live: 20 exact price-derived counts) + debt/cash (088's decode), `state` goes from `missing_inputs` to computable and `fairValuePerShare` is served on real KAP data.**
- **USER REQUIREMENT (recorded verbatim in intent, 2026-10-27):** the DCF's `debt` (borç — financial debt lines from the balance sheet) and `cash` (nakit ve benzerleri) inputs **must come from the KAP reports, with the same honest-data provenance as FCF** — canonicalFactRefs pointing at the statements that supplied them, as-of and restated flags per fact. This ticket verifies and, where broken, repairs exactly that chain end-to-end.
- **The current state of the consumer (verified on `dev`, 2026-10-26 — the wiring mostly exists; this ticket is verification + repair, not a rebuild):** `DcfBaselineGenerationJob` already reads `CASH`/`ST_DEBT`/`LT_DEBT` from the latest FY balance sheet through the one pinned `StatementSourcePreference`, derives `debt = ST+LT`, and records both facts with refs; `GET /stocks/{symbol}/dcf` serves `state: "missing_inputs"` with `missingInputs: ['debt','cash']` for TUPRS/TAVHL purely because no BS lines decode. **Pinned rule (unchanged unless the user decides otherwise):** debt requires *both* sides — a report carrying only one of ST/LT debt leaves `debt` in `missingInputs` (a partial sum is never silently served; if 088's captures show filers reporting only one side, that finding is routed to the user, not relaxed here).
- **What to implement (investigation-first, repair where the effect happens):**
  1. **The baseline chain on the mixed world:** with 088's BS lines landed, one `dcf-baselines` regeneration over the covered companies must produce baselines whose `params` carry `base_fcf`, `debt`, `cash`, `share_count` together, each with its `canonicalFactRefs` entry (source `statement` for the three statement facts, `price-derived` for the count per 086's precedence), `missingInputs` empty, and `POST …/dcf/compute` returning `fairValuePerShare`. Where the chain breaks (a fact read path, a preference tie, an FY-filter interaction), the fix lands in this ticket's scope.
  2. **The financials table's kap-public periods:** `GET /stocks/{symbol}/financials` serves `revenue`/`netIncome` (and `ebitda` **only** when EBIT + D&A are evidenced on the IS row — the existing `Line(EBITDA) ?? Sum(EBIT, DEPR_AMORT)` seam; an honest null otherwise, never a derived guess from CF D&A alone unless the IS row carries it). No new lines, no label work (the FY/Qn labelling is 057's landed surface).
  3. **The derived jobs (083's OPEN live criterion rides here):** `metrics-recompute` over the IS-bearing mixed world must write current-period metric rows stamped with the current `formula_version` (the 2026-10-26 live run wrote 0 — CF-only statements were the likely input; if the investigation finds a deeper gap, the repair is in `src/Ingestion/Metrics/`, in scope), `medians`/`market-snapshot` complete honestly, market P/E stays honestly null where share counts are unresolved (BR-MOV-010).
- **Pinned non-goals:** no DCF formula/growth/threshold change; no per-share metrics or market-P/E consumption of share counts (the 086 non-goal stands); no UI structural change (SCR-006's calculator and prefill already render the debt/cash parameters and `fairValuePerShare`); no cumulative-semantics change (090).
- Traces to: FR-VAL-001/002/003 (baseline supply + per-share), UC-VAL-003 (baseline generation), FR-RES-005..018 (served financials), FR-MDF-013/014 (recompute over the mixed world), FR-MDF-008 (provenance), FR-MOV-006/BR-MOV-010 (P/E honest-null, cited, stands).
- Acceptance: **TC-MDF-118** (test plan §29, v1.37 reservation — planner-authored, test-planner confirmation pending) — L2 doubles only over the production jobs on the seeded kap-public world (088's committed captures as the IS/BS doubles; zero live requests); red→green recorded (red = the current head's own behaviour: `missingInputs: ['debt','cash']`, revenue null). Non-regression: `DcfBaselineEndpointTests` (incl. TC-VAL-008), `DcfBaselineRegenerationTests`, `DcfFinancialSectorTests`, `StockFundamentalsEndpointsTests`, `MetricsEngineTests`, `ProfileMetricServingTests`, `KapPublicRecomputeTests` re-run green. **Live criteria (orchestrator-owned, recorded verbatim):** (i) `GET /stocks/TUPRS/dcf` (and TAVHL) returns `state` ≠ `missing_inputs` with `debt`/`cash` in `params` and their refs pointing at kap-public statements; `fairValuePerShare` served; (ii) the financials payload shows revenue/NI for kap-public periods; (iii) `metrics-recompute`/`medians`/`market-snapshot` write current-period rows (formula_version stamped) — closing 083's open live criterion or recording honestly why not.
- Architecture refs: `01-system-architecture.md` §10.9.19 (consumer subsection note) + §10.9.13 (recompute doctrine, cited) + §13.1 **v1.5.63** (soft reservation; re-pin at merge); test plan §29 (TC-MDF-118).
- UX refs: SCR-006 (DCF calculator — parameters #6 `debt`/#7 `cash` gain real baseline values; `fairValuePerShare` rendering; **no structural change**), UXR-VAL-009 (missing-inputs explanation — `debt`/`cash` leave the missing set only when a statement supplied them), UXR-VAL-010 (per-fact provenance/as-of carried into the parameter display); SCR-005 (financials section — the revenue/NI/EBITDA rows for kap-public periods; presentation already exists).
- Dependencies: TKT-foundation-088 (the BS/IS decode). Shares the two permanent docs with 088/090/091/092 (last-lander rebase).
- Parallel group: none.

## Implementation record (2026-10-27)

- Branch `tkt-foundation-089`, worktree `/tmp/opencode/worktrees/TKT-foundation-089`.

### The investigation (recorded before the fix — it corrects the ticket's premise)

Run on the frozen head with 088's decode landed, over the **committed** TÜPRAŞ
`kap-public-report-1554106` capture through the production decoder + store and the
production `dcf-baselines` job:

- **The declared consumer chain was already correct.** One decode + store writes **6**
  `kap-public` statements; one `dcf-baselines` pass produces `missingInputs: []` with
  `debt` 37,385,875,000 (`ST_DEBT` 8,957,100,000 + `LT_DEBT` 28,428,775,000),
  `cash` 107,237,211,000, `base_fcf` 30,346,395,000 and `share_count` 2,500,000,000
  (the price-derived lane), each with its own ref. `GET /stocks/TUPRS/dcf` = **200**
  without `missing_inputs`; `POST …/dcf/compute` serves a positive `fairValuePerShare`.
- **The financials surface already served the periods**: FY2024 `REV` 830,356,131,000 /
  `NI` 29,872,672,000 (and FY2025), with **`ebitda` honestly null** — the capture's IS
  carries neither `EBITDA` nor `EBIT` + `DEPR_AMORT`, so the existing
  `Line(EBITDA) ?? Sum(EBIT, DEPR_AMORT)` seam answers null. **No line was added and no
  label work was done.**
- **The one real gap** was 083's open live criterion: `metrics-recompute` and `medians`
  used `request.Date` blindly, so a request for a day whose covered universe owned no
  stored prices wrote nothing and reported a green `written: 0, unchanged: 0` — the
  2026-10-26 live reading, indistinguishable from "all current".

### What was implemented (verification + one repair)

- **`src/Ingestion/Metrics/MarketSnapshotStore.cs`** — the stored-prices-as-truth rule
  (`ResolveLatestStoredTradingDayAsync`) gains a **static** entry (the instance method now
  delegates to it), so the day-resolution rule has exactly one implementation reachable by
  the other derived lanes.
- **`src/Ingestion/Metrics/MetricsEngine.cs` / `SectorMedianEngine.cs`** — each gains
  `ResolveComputeDayAsync`, delegating to that one implementation (no lane re-derives a day).
- **`src/Ingestion/Metrics/MetricsRecomputeJob.cs` / `SectorMediansJob.cs`** — resolve the
  day through their engine's resolver instead of using `request.Date`; with no stored price
  at or before the requested date there is no session to describe, so the pass writes
  nothing and claims nothing. **Constructors, DI composition and the fixture harness are
  unchanged** — no consumer outside the lanes moved (checked: `fixtures/Degerli.Fixtures/
  FixtureDerivedData.cs` composes the job and still compiles untouched).
- **No DCF code changed**: `DcfBaselineGenerationJob.cs` was verified, not modified. No
  formula/threshold/precedence change, no decoder change, no semantics change, no UI work,
  no dependency.

### Test evidence (executed in this environment — Docker/Testcontainers available)

- **TC-MDF-118 (a)** `TC_MDF_118_the_per_share_dcf_completes_on_the_decoded_public_site_statements`
  — green; asserts the completion, the eight-parameter set, the three `statement` refs +
  `price-derived` count ref, the served values **cross-checked against the stored balance
  sheet's own `ST_DEBT`+`LT_DEBT` / `CASH`** and the CF statement's `OCF − |CAPEX|`, and the
  served `fairValuePerShare`.
- **TC-MDF-118 (a) control** `TC_MDF_118_the_cash_flow_only_world_leaves_debt_cash_and_revenue_missing`
  — green; the recorded red reproduced (CF-only world: `missing_inputs` = `['cash','debt']`,
  no fair value, `400` naming `debt`, revenue/NI null) so the completion assertion is
  falsifiable.
- **TC-MDF-118 (b)** `TC_MDF_118_the_derived_lanes_describe_the_latest_stored_session_not_a_priceless_date`
  — **red → green, observed**: with `var day = request.Date;` restored the case fails
  **`Expected: 416, Actual: 0`** (the silent zero); green after the repair (416 metric rows
  on the resolved session, none on the requested priceless date, every row stamped
  `CanonicalMetrics.Version`; medians on the same session).
- Suites: `DcfBaselineEndpointTests` **10/10** (incl. TC-VAL-005..008), `KapPublicRecomputeTests`
  **7/7**, `TC_MDF_118` filter **3/3**.
- Non-regression, all green and unrelaxed: `DcfBaselineRegenerationTests` +
  `DcfFinancialSectorTests` + `StockFundamentalsEndpointsTests` **22/22**;
  `MetricsEngineTests` + `ProfileMetricServingTests` **8/8**;
  `MetricsRecomputeIdempotencyTests` + `RestatementMetricsTests` + `StatementVersioningTests`
  + `MorningChainTests` + `EodChainTests` + `RegimeBreakTests` **26/26**;
  `Screener*` + `SavedScreenEndpointTests` + `AdminEndpointsTests` +
  `StockOverviewAndValuationEndpointsTests` + `Xc014PerformanceBudgetTests` +
  `StatementsAndEventsIngestionTests` **102/102**; `Core.UnitTests` **75/75**.
  `dotnet build` 0 warnings, 0 errors (src + tests).
- Nothing was skipped or weakened; no expected result was adjusted to match the
  implementation.

### Docs landed

- Test plan §29 (TC-MDF-118) + change record **v1.37** — `.pipeline/testing/market-data-foundation.md`.
- Architecture §10.9.19 consumer subsection + §13.1 **v1.5.63** — `.pipeline/architecture/01-system-architecture.md` (§10.9.13 cited and applied, not amended).

### Flagged / routed

- **DR-2 dependency (088, still open):** `debt` excludes `Uzun Vadeli Borçlanmaların Kısa
  Vadeli Kısımları` (TÜPRAŞ current portion 12.86bn vs the served 37.39bn ≈ 25.6%), so the
  KAP-sourced `debt` is **partial** until the user decides. This ticket consumes the stored
  `ST_DEBT`/`LT_DEBT` exactly as stored and does **not** widen the definition.
- **DR-1 (088, still open):** `TOTAL_EQUITY` is read exactly as stored (NCI-inclusive),
  unfiltered here.
- **Honest-null seam, recorded not "fixed":** `ebitda` is null for the TÜPRAŞ capture because
  its IS carries no `EBITDA`/`DEPR_AMORT` line — the pinned seam's own branch; deriving it from
  CF D&A would be a guess the ticket forbids.
- **Live criteria remain open (orchestrator, post-merge, paced, never CI):** (i)
  `GET /stocks/TUPRS/dcf` (and TAVHL) ≠ `missing_inputs` with `debt`/`cash` in `params` and
  kap-public refs, `fairValuePerShare` served; (ii) the financials payload's revenue/NI for
  `kap-public` periods; (iii) `metrics-recompute`/`medians`/`market-snapshot` writing
  current-period rows stamped with `formula_version` — closing 083's open criterion.
