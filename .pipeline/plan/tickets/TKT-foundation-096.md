---
scope:
  - src/Core/Metrics/
  - src/Ingestion/Metrics/
  - src/Persistence/
  - src/Api/Stocks/StockKeyMetricsEndpoints.cs
  - src/Api/Program.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
  - tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs
  - tests/Api.IntegrationTests/KapPublicRecomputeTests.cs
  - tests/Api.IntegrationTests/ProfileMetricServingTests.cs
  - tests/Api.IntegrationTests/ScreenerMetricVisibilityTests.cs
  - tests/Api.IntegrationTests/RegimeBreakTests.cs
  - tests/Api.IntegrationTests/MigrationsAndSeedTests.cs
  - tests/e2e/src/metric-visibility.spec.ts
  - tests/Api.IntegrationTests/StockKeyMetricsEndpointsTests.cs
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/02-data-model.md
  - .pipeline/architecture/03-api-design.md
  - .pipeline/testing/market-data-foundation.md
---

# TKT-foundation-096: The key-metrics & capital-tracker backend — new canonical metric codes through the Metrics Engine, the 5Y company median served from stored metric history, sector medians extended to the panel's set, and the per-stock serving endpoint

- Status: in-review
- Size: M
- **Scope widened (USER-APPROVED 2026-10-28, implementation lane; FIFTH FILE ADDED 2026-10-29, review round 1):** four test files added to the scope list — `MetricsRecomputeIdempotencyTests.cs`, `KapPublicRecomputeTests.cs`, `ScreenerMetricVisibilityTests.cs` and `RegimeBreakTests.cs`. All four are named in this ticket's non-regression list and all four pin **fixture-contract constants bound to the old catalog size**: `MetricRows = 16 * 26` / `MedianRows = 3 * 5` (re-pinned to `16 * 43` / `3 * 32`), the screener catalog's visible count `17` (a 32-row catalog with `roe` hidden → `31`), and the growth-code loop over `MetricCodes.All.Where(IsGrowth)` (scoped to the twelve v1 families, plus an explicit assertion that the panel's six new CAGR families are honestly NULL with no flag on the L2 world). Constants, loops and their doc comments only — no assertion is added, removed or weakened, and no product behaviour changes. The original scope list omitted them: a planning defect, resolved by the user rather than improvised. **A fifth file of the same mechanical class was added in review round 1:** `tests/Api.IntegrationTests/MigrationsAndSeedTests.cs`, whose `Seed_is_idempotent_and_rerunnable` asserted `18` metric-catalog rows and now asserts `32` (18 through v1 + the 14 panel concepts; comment naming the rows added, no other assertion in the file touched). It was missed by both the original scope list and the four-file widening, which is why CI went red on head `ca886d9` — a fixture-contract constant bound to the catalog size, exactly the approved class. **Approved at merge (2026-10-29, orchestrator G-2 ruling) and listed in `scope:` above.** **A sixth file of the same class was added in review round 2 (2026-10-29):** `tests/e2e/src/metric-visibility.spec.ts` pinned the admin metric-visibility panel at `toHaveCount(19)` (header + the 18-row catalog); the panel lists the whole `metric_catalog`, which this ticket grows to 32 (18 v1 + the 14 panel concepts), so the constant is now `33` with the comment naming both groups. It only surfaced in review round 2 because round 1's E2E job was skipped while the Backend check was red, then the branch needed the `dev` merge that unblocked the required run. The count is the only line changed — the toggle, confirmation, SCR-003 and restore assertions are untouched (they all still pass), and no product behaviour changes. **Approved at merge (2026-10-29, orchestrator G-2 ruling, DR-17) and listed in `scope:` above.**
- **Priority: P1 — slice 4 of the 2026-10-28 financial-statements view wave (serves the KEY VALUE METRICS & MOAT INDICATORS panel and the CAPITAL & DILUTION TRACKER; both render in TKT-foundation-097 — the user spec, verbatim, lives on TKT-foundation-095).**
- **Where the computation lives (USER-CONFIRMED 2026-10-28):** BR-RES-004/UXR-G-011 require the panel's canonical values to be the same numbers the screener serves, and `derived_metrics` is the formula-versioned single source (`02` §1 principle 3, §6.2, 070's versioning discipline). **New codes go through the Metrics Engine like the existing 26; the 5Y historical company median computes serving-side from stored `derived_metrics` history; sector medians extend the existing job.** No second formula implementation anywhere (AD-06, §5.1).
- **What to implement:**
  1. **New canonical metric codes (the `02` §6.2 catalog amendment + a `CanonicalMetrics.Version` bump — 070's discipline: every new and re-stamped row carries the bumped version, historical rows keep theirs):** quality/value — `roa`, `croic`, `ev_ebit`, `p_fcf`, `p_tbv`, `fcf_ni` (current) and `fcf_ni_3y` (3Y average, the window-suffix convention), `cfo_ebit`, `monetary_gain_pretax` (the TMS 29 distortion alert input), `cfo_interest` (CFO / cash interest), `net_debt_fcf` (N/M when FCF ≤ 0 — the existing not-meaningful rule family); growth — `ebit_cagr_3y/5y`, `bvps_cagr_3y/5y`, `fcf_ps_cagr_3y/5y` (window-suffixed; **no 10Y — user exclusion #5**; `window_years` honesty stands; per-share CAGRs are corporate-action-adjusted per the §6.2 EPS/DPS doctrine and gated on share counts — honest N/M where none exists, exclusion #4). Definitions are authored into `02` §6.2 at implementation (each formula pinned beside the existing table, never paraphrased in code comments alone). Where a formula needs 093's new line codes (`TOTAL_ASSETS` for ROA, goodwill+intangibles for P/TBV, `NI_PARENT` for ROE-parent/EPS-parent), the dependency is explicit.
  2. **Accounting-profile gating extended (`02` §6.2 doctrine):** the industrial-shaped new codes (`croic`, `ev_ebit`, `p_tbv`, `net_debt_fcf`, `cfo_interest`, `ebit_cagr_*`, `fcf_ps_cagr_*`, …) are N/M by profile for bank/insurance — a stray mapped line must never produce a plausible number. `metric_catalog` seeds the new codes (labels, family, screenability defaults to the builder's later call).
  3. **The 5Y historical company median — serving-side:** computed over the instrument's stored `derived_metrics` history for each code (median of non-null values within the 5Y window); **honest N/M where the ~3.5-year depth is insufficient** (a recorded minimum-observation bound pinned at implementation — the user's own "5Y is marginal" honesty). Never a fabricated longer window; `window_years`-style disclosure served beside it.
  4. **Sector medians extended:** `sector_metric_medians` currently holds the five valuation-family codes; the medians job extends to the panel's set (per-code peer counts and loss-maker-style exclusions carried per the UXR-RES-024/025 disclosure analog; profile-gated codes exclude financial-sector peers from their medians, disclosed by count).
  5. **The serving endpoint:** `GET /stocks/{symbol}/key-metrics` (new file, registered in `Program.cs`) — per metric: current value, 5Y company median, sector median + peer count, honest N/M states; plus the **capital-tracker block**: paid-in capital + as-of (the `share_counts` store, D4 — money, never a count), split-adjusted share count + as-of (the 086 price-derived store), share-count change Y/Y (honest N/M — no count history exists; never a guess), **bedelli/bedelsiz % history from `corporate_actions`** (the ingested terms_json ratios, dated, corporate-action-adjusted disclaimer per UXR-RES-020), and the `payout_ratio` reference. All figures carry as-of; the endpoint computes only the medians/aggregations named above — canonical values come from `derived_metrics`.
- **Pinned non-goals:** no screener criteria/UI changes (the catalog rows exist; making the new codes screenable is a builder toggle later); no DCF change; no statement/decoder work (093's codes are consumed, not extended); no UI (097); no USD/real basis; no 10Y windows.
- Traces to: FR-MDF-013/014 (canonical metrics + window honesty), FR-RES-006/007 (valuation metrics + vs-sector supply), FR-RES-009 (profitability supply), FR-RES-010 (growth CAGRs), FR-RES-027 (per-share book values), FR-MDF-018 (basis markers), FR-MDF-004 (corporate actions — the capital tracker's bedelli/bedelsiz source).
- Acceptance: **TC-MDF-124** (test plan §35, v1.47 reservation — planner-authored, test-planner confirmation pending) — L2 doubles only over the production engine/jobs on the fixture + committed-capture world (zero live requests); red→green recorded (red = the new codes absent from `derived_metrics`/the endpoint 404s on the current head). Assertions: every new row stamped with the bumped `formula_version` (070's fast-path/order discipline intact); served current values cross-checked against the stored lines' own arithmetic; the 5Y median's honest N/M on the depth-limited fixture world; sector-median peer/exclusion counts; profile gating (a bank computes `pe`/`roe` but never `ev_ebit`); the capital tracker's bedelli/bedelsiz history from seeded corporate actions; the share-count N/M honesty. Non-regression: `MetricsEngineTests`, `MetricsRecomputeIdempotencyTests`, `ProfileMetricServingTests`, `RestatementMetricsTests`, `RegimeBreakTests`, `Screener*` suites re-run green (the existing 26 codes byte-identical — a new code added, nothing renamed; `CanonicalMetrics.Version` bump verified by the versioning tests). **Live criteria (orchestrator-owned, recorded verbatim):** one `metrics-recompute` + `medians` pass over the walked world writes the new codes stamped with the bumped version; `GET /stocks/TUPRS/key-metrics` serves current + medians with honest N/Ms verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.13 (recompute/versioning doctrine, cited) + §13.1 **v1.5.78** (landed 2026-10-29 — the soft v1.5.72 reservation is re-pinned); `02-data-model.md` §6.2 (amended — new codes, version bump, the five exclusions, the gate at 26, the company-median definition); `02-data-model.md` §6.3 (amended — the 32-code median set and the financial-peer exclusion); `03-api-design.md` §3 (endpoint added); test plan §35 (TC-MDF-124, authored in v1.53).
- UX refs: SCR-005 (§2 Valuation / §4 Profitability / §5 Growth — the panel's supply; rendering is 097's), UXR-RES-024/025 (peer-count and N/M disclosure analogs).
- Dependencies: TKT-foundation-093 (the new line codes the formulas consume). No code dependency on 094/095 — may dispatch directly after 093 if the user prefers (the 091-wave precedent); shares the three permanent docs (last-lander rebase).
- Parallel group: none.

## Implementation record — 2026-10-29 (implementation lane)

- **PR:** https://github.com/sarperim/degerli/pull/158 (branch `tkt-foundation-096`, base `dev`)

**What landed, file by file.**

- `src/Core/Metrics/MetricCodes.cs` — **17 new codes**, `MetricCodes.All` 26 → 43 (25 non-growth + 18
  windowed): `roa`, `croic`, `ev_ebit`, `p_fcf`, `p_tbv`, `fcf_ni`, `fcf_ni_3y`, `cfo_ebit`,
  `monetary_gain_pretax`, `cfo_interest`, `net_debt_fcf`; `ebit_cagr_3y/5y`, `bvps_cagr_3y/5y`,
  `fcf_ps_cagr_3y/5y`. `KeyMetricPanel` is the 31-code set the endpoint serves. No code was renamed.
- `src/Core/Metrics/CanonicalMetrics.cs` — **`Version` 1 → 2** (070's discipline: a row republished by
  a new definition carries the new stamp; historical rows keep theirs until their own day is
  recomputed, and the fast-path still reports a matching row `unchanged`). The new formulas, each
  pinned in `02` §6.2 and not paraphrased here: `fcf_ni_3y` is the **all-or-nothing** mean of the
  three most recent FY ratios; `p_tbv = mcap / (TOTAL_EQUITY − (GOODWILL + INTANGIBLES_NET))` with an
  absent itemized component contributing 0 and absent equity → NULL; `cfo_interest` divides by
  `|INTEREST_PAID|` (the KAP cash-flow sign is an outflow, never the divisor); `monetary_gain_pretax`
  only where pretax profit > 0; `net_debt_fcf` N/M when FCF ≤ 0. `IsProfileGated` / `IsFinancialProfile`
  are now **public** so the median lane shares one gate definition; the gate covers **26** codes.
- `src/Core/Metrics/MetricMath.cs` — `Median` made public (the company median and the sector medians
  are the same statistic).
- `src/Ingestion/Metrics/MetricsEngine.cs` — the new line codes consumed **as stored** (no re-aggregation,
  DR-1/DR-2 fenced), the FY series for the windowed families, and the **per-family** basis marker:
  `BuildInput` returns `(Input, EpsDividendAdjusted, PanelPerShareAdjusted)`, so a panel-family
  adjustment never re-stamps a v1 EPS/DPS row.
- `src/Ingestion/Metrics/SectorMedianEngine.cs` — the covered set is now **32 codes** (3 fixture sectors
  × 32 = 96 rows); for a profile-gated code the financial-sector peers are removed **before** the median
  and counted in `excluded_count`.
- `src/Persistence/Seed/DegerliSeed.cs` — `metric_catalog` gains the **14 conceptual rows** with
  `is_screenable = false` (the screener's offered set is unchanged; FR-SCR-017 owns the later flip).
- `src/Api/Stocks/StockKeyMetricsEndpoints.cs` (new) + `src/Api/Program.cs` — `GET /api/v1/stocks/{symbol}/key-metrics`:
  the 31 panel codes (current value + 5Y company median with its depth disclosure + sector median with
  peer/exclusion counts + N/M states), the `financialSector` block, and the capital-tracker block
  (bedelli/bedelsiz history from `corporate_actions`, paid-in capital with its as-of, the honest
  `no-count-history` Y/Y N/M, `payout_ratio`). It computes **no** canonical value — every current value
  is the stored `derived_metrics` row (BR-RES-004).

**Permanent docs, same lane:** `02` §6.2 (title, the new code table, the five pinned exclusions, the
per-share doctrine, gating to 26, the company-median paragraph), `02` §6.3 (the 32-code covered set and
the financial-peer exclusion), `03` §3 (the endpoint row), `01` §13.1 **v1.5.78** (the 5-column change
row with its affected items and evidence), test plan §35 + version record **v1.53**.

**The user-approved scope widening (2026-10-28) as delivered:** four test files re-pinned their
fixture-contract constants only — `MetricsRecomputeIdempotencyTests.cs` / `KapPublicRecomputeTests.cs`
(`MetricRows 16*26 → 16*43`, `MedianRows 3*5 → 3*32`), `ScreenerMetricVisibilityTests.cs` (visible
catalog count 17 → 31, 18 → 32), `RegimeBreakTests.cs` (the growth loop scoped to the computable v1
families plus an explicit honest-NULL assertion for the panel's new CAGR families). No assertion was
added, removed or weakened beyond that; no product behaviour changed for them.

**Fifth file of the same class (2026-10-29, review round 1):**
`tests/Api.IntegrationTests/MigrationsAndSeedTests.cs` — `Seed_is_idempotent_and_rerunnable`'s
`metric_catalog` count `18 → 32` (18 through v1 + the panel's 14 concepts), with a comment naming
the rows. It is the one fixture-contract constant the original widening missed, and it is what made
the Backend CI check red on head `ca886d9` (round 1, G-1/DR-1). Constants and a comment only — no
other assertion in the file touched, no product behaviour changed. Added to the `scope:` list;
approved at merge (2026-10-29, orchestrator G-2 ruling).

## Test evidence

- **Acceptance TC:** TC-MDF-124, test plan §35 → `tests/Api.IntegrationTests/StockKeyMetricsEndpointsTests.cs`,
  **9 cases, every one tagged `TC_MDF_124`**: (a) `…_engine_writes_the_panel_codes_stamped_with_the_bumped_formula_version`,
  (b) `…_key_metrics_endpoint_serves_the_stored_canonical_values`, (b cont.) `…_the_new_formulas_compute_from_the_stored_lines`,
  (b cont., basis marker) `…_the_basis_marker_is_per_family` (added in review round 1 — DR-4),
  (c) `…_company_median_is_withheld_on_thin_history_and_exact_when_deep_enough`,
  (d) `…_sector_medians_cover_the_panel_set_with_peer_and_exclusion_counts`,
  (e) `…_a_bank_serves_pe_and_roe_but_never_ev_ebit_and_is_excluded_from_gated_medians`,
  (f)/(g) `…_capital_tracker_serves_the_bedelli_bedelsiz_history_and_the_share_count_nm`,
  (h) `…_a_symbol_outside_the_covered_universe_is_not_found`.
- **Review round 1 fix evidence (2026-10-29):** the CI-red fixture constant re-pinned
  (`MigrationsAndSeedTests`: `metric_catalog` 18 → 32, G-1/DR-1) and the new per-family basis case
  (DR-4) — narrow local filters, Docker/Testcontainers: `TC_MDF_124` **9/9**,
  `Seed_is_idempotent_and_rerunnable` + `Migrations_apply_to_a_fresh_container_and_rerun_is_a_noop`
  **2/2**. The full suite stays CI's (the PR's required Backend check).
- **Review round 2 fix evidence (2026-10-29):** the PR conflicted with `dev` after **PR #157
  (TKT-foundation-094)** merged behind this branch's fork point — the tickets share the three permanent
  docs, so this PR takes its last-lander duty: `origin/dev` merged into `tkt-foundation-096`, one real
  content conflict in `.pipeline/testing/market-data-foundation.md` resolved by **keeping both sides in
  version order** (#157's §34 / TC-MDF-123 / v1.46 and this PR's §35 / TC-MDF-124 / v1.53+v1.54, with
  §36 following), while `01`, `02`, `03` and `DegerliDbContextModelSnapshot.cs` auto-merged with both
  sides' rows present and no duplication. Build after the merge: `dotnet build Degerli.sln`
  **0 warnings / 0 errors**. The four round-2 nits are recorded in test plan **v1.55** and in
  `01` v1.5.78's red→green sentence; the only assertion added is `Assert.NotNull` on
  `bvps_cagr_3y`'s value in the per-family basis case (DR-14 — a strengthening of the plan's own
  plural "the BVPS CAGRs carry a value", verified against the injected series), re-measured green as
  `TC_MDF_124` **9/9** on this head. The new head's CI run exposed one more fixture-contract
  constant of the same mechanical class, one layer out: the E2E metric-visibility panel pinned 19 rows
  (header + the 18-row catalog) and now reads 33 (header + 32) — fixed in
  `tests/e2e/src/metric-visibility.spec.ts` with a comment naming both groups (sixth scope file, see
  the scope bullet above).
- **Red, measured (Docker/Testcontainers available; re-run, not remembered):** with
  `StockKeyMetricsEndpoints.cs` removed and `Program.cs`/`MetricsEngine.cs` reverted to the pre-ticket
  files (the code catalog and formulas kept, so the red is behaviour — never a compile or environment
  failure) the `TC_MDF_124` filter reads **8 failed / 0 passed** — (a) at `Assert.NotNull()` on the
  first new code's stored value, (e) `Expected: 0.06, Actual: null`, the six endpoint cases
  `Expected: OK, Actual: NotFound`, (h) on an empty response body.
- **Green:** `TC_MDF_124` **8/8**. Targeted suite on the final head,
  `StockKeyMetricsEndpointsTests | MetricsEngineTests | ProfileMetricServingTests |
  MetricsRecomputeIdempotencyTests | KapPublicRecomputeTests | RestatementMetricsTests | Screener* |
  RegimeBreakTests | StockOverviewAndValuationEndpointsTests | StockFundamentalsEndpointsTests` →
  **Failed: 0, Passed: 78, Skipped: 0, Total: 78** (37 s); `dotnet build` **0 warnings / 0 errors**.
- Golden arithmetic the endpoint is cross-checked against (ALFA FY2025, from the fixture's own stored
  lines): `croic` 0.208333, `ev_ebit` 7.333333, `p_fcf` 20, `p_tbv` 2, `fcf_ni` 0.5, `cfo_ebit`
  0.833333, `net_debt_fcf` 2, `fcf_ni_3y` 0.824798, `fcf_ps_cagr_3y` −0.206300 / `_5y` −0.089718;
  injected-input cases `roa` 0.10, `p_tbv` 2000/850, `monetary_gain_pretax` 0.20, `cfo_interest` 5,
  `ebit_cagr_3y` 0.077217 / `_5y` 0.063995, `bvps_cagr_3y` 0.077217 / `_5y` 0.073941.

## Merge-time rulings (orchestrator, 2026-10-29)

- **DR-16 — the shared-doc version collision: merged again, this side renumbered.** `dev` advanced to
  `6f86999` (PR #160 / TKT-foundation-099) and both shared ledgers collided on the same number — `01`
  §13.1's `v1.5.75` row (099's dotless-ı identity resolution vs this ticket's key-metrics row) and the
  test plan's `v1.50` entry (099's §37 vs this ticket's §35). `origin/dev` was merged into this branch
  and **this side renumbered; every dev row is untouched**. `01` §13.1: `v1.5.75 → **v1.5.78**` (the
  first free above dev's landed `v1.5.75` and the wave's soft `v1.5.76`/`v1.5.77` reservations for
  100/101). Test plan: `v1.50 → **v1.53**`, `v1.51 → **v1.54**`, `v1.52 → **v1.55**` (the first free
  above dev's landed `v1.50` and its soft `v1.51`/`v1.52` reservations for 100/101). The renumbered
  rows sit at the ledger tails — `01` … `v1.5.74`, `v1.5.75` (099), `v1.5.78` (this ticket); test plan
  … `v1.49`, `v1.50` (099), `v1.53`, `v1.54`, `v1.55` (this ticket) — and every self-reference was
  re-pinned with them: `03` §3's endpoint row, test plan §35's section header and its Traces line, the
  §35 case-lettering note (`v1.54`), and this ticket's own architecture/version refs. No row's content
  changed beyond its number. `dev` then advanced once more mid-pass (**PR #159 / TKT-foundation-095**,
  landing `01` `v1.5.76`) and was merged again on the same rule: 095's row stays untouched and
  **`v1.5.78` is still the first free number** on `01` (dev's landed top is `v1.5.76`, with `v1.5.77`
  still soft-reserved by 101), so this ticket's row keeps it; the test plan needed no further move
  (dev's landed top there is still `v1.50`). Both ledgers re-verified after the final merge: **no
  duplicate version on either**, and both strictly increasing (the single out-of-order pair in `01`'s
  table, `v1.5.52 → v1.5.25`, is dev's own pre-existing ordering, unchanged on dev, and is untouched).
- **G-2 — the sixth scope file, APPROVED.** `tests/e2e/src/metric-visibility.spec.ts` (the admin
  panel's row-count constant `19 → 33` for the now-32-row catalog) is approved at merge as the sixth
  member of the catalog-size-constant class — the same mechanical class as the four 2026-10-28 files,
  constant and comment only, no assertion added, removed or weakened — and is now listed in the
  `scope:` front-matter (DR-17: the prose already recorded it; the machine-readable list did not).
- **G-3 — `missing_tc=TC-MDF-123`, WAIVED (recorded).** A false positive of a new class: the id appears
  in this ticket only inside a **provenance sentence** citing **TKT-foundation-094's** already-merged
  acceptance case (PR #157); no test of it belongs in this diff, and this ticket's own acceptance case
  **TC-MDF-124** is present and tagged (`TC_MDF_124`, nine cases). Recorded as a scoped standing
  waiver in `.pipeline/plan/reviews/WAIVERS.md` ("cross-ticket citation"); the provenance sentence is
  kept verbatim — honest content is not reworded to dodge a tool limit.
- **DR-18** — `tests/e2e/src/metric-visibility.spec.ts`'s header docstring said "the panel lists the 18
  screenable metrics" while the panel lists the whole 32-row catalog (18 of them screenable);
  corrected. Comment only.
- **No code change, no assertion change, zero live requests in this pass**; CI re-runs on the new head.

## Flagged items

1. **Full CI owns the full gate.** This lane ran the targeted suite (78/78) and `dotnet build`, not the
   whole repository suite (e.g. `EodChain`/`MorningChain` are untouched by this diff but are not
   re-run here); the PR's required checks are the authority.
2. **The four test-file re-pins are the user-approved scope widening** (constants/loops only) — recorded
   above and in test plan v1.53; a reviewer seeing `16*43`/`3*32`/`31`/`32` in those files should read
   them as catalog-size contracts, not as relaxed assertions. **A fifth file joined the class in review
   round 1** — `MigrationsAndSeedTests.cs` (catalog count 18 → 32) — added to the `scope:` list above
   and **approved at merge (2026-10-29, orchestrator G-2 ruling)**. **A sixth file joined in review round 2** —
   `tests/e2e/src/metric-visibility.spec.ts` (the admin panel's row count 19 → 33 for the 32-row catalog) —
   **approved at merge (2026-10-29, orchestrator G-2 ruling, DR-17) and listed in `scope:`**.
3. **`is_screenable = false` on the 14 new catalog rows** — the screener offers the same set as before;
   flipping it is a later builder decision (FR-SCR-017), deliberately not done here.
4. **Company median is withheld on today's store depth** (12-observation floor, ~3.5-year history) — by
   design (the user's "5Y is marginal" caveat); the endpoint discloses `windowYears`/`observations`/`depthYears`.
5. **The L2 world lacks several inputs** (`TOTAL_ASSETS`, `NET_MONETARY_GL`, `INTEREST_PAID`, multi-year
   EBIT/equity), so those codes are honestly NULL there; (b cont.) proves the formulas on injected lines.
6. **Live criteria (orchestrator, post-merge, paced, never CI):** one `metrics-recompute` + `medians`
   pass over the walked world writing the new codes stamped with the bumped version, then
   `GET /stocks/TUPRS/key-metrics` serving current values, the company median and the sector medians
   verbatim.
7. **The L1 profile-gate matrix is behind the gate's width (review round 1, DR-9) — not fixed here.**
   `tests/Core.UnitTests/Metrics/ProfileMetricMatrixTests.cs` (TC-MDF-057) still enumerates the twelve
   v1 gated codes while `02` §6.2 now gates **26**. Nothing fails (the matrix's codes are a subset of
   the gate) and no assertion is wrong — it is a coverage gap in an L1 contract, and the file is **not**
   in this ticket's `scope:` list. Extending it is a sixth-file scope decision, so it is routed rather
   than improvised: the follow-up is a TC-MDF-057 amendment enumerating the 26 (planner/test-planner,
   with the file added to a scope list when it is next touched).
8. **`percent` is documented, not redesigned (review round 1, DR-10).** The capital tracker serves the
   ingested ratio × 100 uniformly — a 1:5 split is **500%**, the post-action count as a multiple, not the
   +400% increase — because TC-MDF-124 (f)/(g) pins 500% and an expected result is not changed in a fix
   loop. `03` §3 now states the semantics and tells 097 to read a split's percentage as the multiple.
   Changing the payload to `(n−1)×100` is a requirements call (097 + the user), not a fix.
9. **Routed upstream, untouched (review round 1, DR-2/DR-3).** DR-2: the company median's floor is a
   **session/observation** count, not a depth floor — a definition decision for the architect + user
   (floor on `depthYears`, per-period bucketing, or an explicit statement in `02`/`03` that this is a
   session-count statistic). DR-3: the panel serves group-basis `roe`/`eps_cagr_*` where the user spec
   asks for Parent basis — a requirements gap (add the parent-basis pair, or pin the group-basis decision
   in `02` §6.2 and 097). Neither is a code defect in this PR's scope.
