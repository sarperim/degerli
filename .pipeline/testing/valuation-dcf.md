# Valuation & DCF Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved v1.1 (builder final review 2026-10-07 — F-VAL-1 resolved: total-debt reading (a); §6 interpretations upheld)
**Derives from:** `analysis/valuation-dcf.md` (FR-VAL-001..013, UC-VAL-001..003, NFR-VAL-001..004) · `ux/valuation-dcf.md` (SCR-006/011, UXR-VAL-001..021, confirmed 8-parameter set §4.1) · `ux/market-data-foundation.md` (UXR-MDF-025 — baseline regeneration) · `architecture/` v1.2 (`02` §6.4 formula, `03` §5, `01` AD-08) · strategy · fixture contract FU (incl. §11.5 baselines).
**TC ID convention:** `TC-VAL-NNN`, permanent. Levels: L1 · L2 · L3 · L4.

## 1. Scope

**Tested here:** FR-VAL-001..010 (10 non-Won't); UC-VAL-001..003 including alternates; NFR-VAL-001..004; UXR-VAL-001..021 (SCR-006/011); UXR-MDF-025 (SCR-012 baseline regeneration).

**Not tested here:** FR-VAL-011..013 (Won't); CSRF/rate limits/i18n parity/a11y (XC); account-deletion cascade of scenarios (ACC — TC-ACC-021; cross-referenced); the plain-language verdict's *comprehensibility* (NFR-VAL-002 — validated inside the builder's SC-006 research session, manual ledger).

**The golden DCF computation (all numbers hand-derived in FU §11.5):** ALFA baseline `base_fcf 100, growth_rate 0.10, horizon_years 5, terminal_growth 0.03, discount_rate 0.12, debt 400 (ST+LT), cash 200, share_count 100`, price 20.00 →
PV_explicit = 100·Σ(1.1/1.12)^t (t=1..5) = **473.8436**; TV = 100·1.1⁵·1.03/0.09 = **1843.1392**; TV_pv = TV/1.12⁵ = **1045.8467**; EquityValue = 473.8436 + 1045.8467 + 200 − 400 = **1319.6903**; **FairValuePS = 13.1969**; **MOS = (13.1969 − 20)/13.1969 = −0.5155**. Sensitivity grid (5×5, steps 0.01/0.01 centered on user values): center **13.1969**; corner (r 0.10, g_t 0.05) **24.0000**; corner (r 0.14, g_t 0.01) **8.99623** (corrected v1.1 — see §6 F-VAL-1).

## 2. Test Case Specification

### Group A — Model math (L1, pure `/src/Core`)

**TC-VAL-001 — DCF golden values**
- Traces: FR-VAL-003/005/006 math, `02` §6.4, AD-08 · Level: L1 · Design: golden values (EP — canonical valid class)
- Inputs: the ALFA baseline parameter set above.
- Expected: fairValuePerShare **13.1969**, marginOfSafety **−0.5155** (6 sig. digits; price 20.00); intermediate values per §1. *(Written to the builder-confirmed reading (a) of the `02` §6.4 formula/parameter issue — F-VAL-1 resolved 2026-10-07; goldens independently re-verified under exact arithmetic.)*
- Dependencies: —

**TC-VAL-002 — Sensitivity grid golden values**
- Traces: FR-VAL-007, SD-001, BR-VAL-009 · Level: L1 · Design: golden values (BVA — grid corners and center)
- Expected: 5×5 grid, axes discount_rate [0.10..0.14] × terminal_growth [0.01..0.05] (step 0.01 centered on user values); center cell = point result 13.1969; (0.10, 0.05) → 24.0000; (0.14, 0.01) → **8.99623** (corrected v1.1 — hand-arithmetic slip in the v1.0 golden, F-VAL-1 §6); all other parameters held at request values.
- Dependencies: —

**TC-VAL-003 — Not-computable constraints**
- Traces: UXR-VAL-009 analog, `03` §5 (422 DCF_NOT_COMPUTABLE) · Level: L1 · Design: BVA — constraint edges
- Expected: `discount_rate ≤ terminal_growth` → not computable (violated constraint named); horizon 0 and 11 → not computable; horizon **1** and **10** → computable (boundary valid); negative base_fcf → computable (user's model — RISK-VAL-001: no "correctness" validation of assumptions).
- Dependencies: —

**TC-VAL-004 — Pure function determinism**
- Traces: AD-08, NFR-VAL-004 · Level: L1 · Design: EP — repeated invocation class
- Expected: identical inputs → identical outputs across repeated calls (no hidden state, no clock/randomness dependence); nothing but the 8 params + price affects the result.
- Dependencies: —

### Group B — Endpoints (L2)

**TC-VAL-005 — GET baseline payload**
- Traces: FR-VAL-001/002/003, UC-VAL-001 step 2, `03` §5 sketch · Level: L2 · Design: EP — happy class
- Expected: GET `/api/v1/stocks/ALFA/dcf` (anonymous) → `{symbol, baseline: {version, buildDate, params (8), canonicalFactRefs (asOf + restated flags per fact)}, price: {value: 20.00, asOf: T, stale: false}}`; baseline version/buildDate from seed.
- Dependencies: FU §11.5.

**TC-VAL-006 — Every covered stock has a calculator; unknown symbol 404**
- Traces: FR-VAL-001, BR-VAL-001 · Level: L2 · Design: EP — universe coverage / unknown-symbol classes
- Expected: GET dcf for each of the 16 fixtures → 200 (PART with `state: "missing_inputs"` per TC-VAL-008 — still a usable calculator page); `NOPE` → 404 NOT_FOUND.
- Dependencies: —

**TC-VAL-007 — Baseline from restated facts carries markings**
- Traces: UXR-VAL-010, FR-MDF-019 consumption, UC-VAL-001 alternate b · Level: L2 · Design: EP — restated-default class
- Expected: GET `/stocks/REST/dcf` → `canonicalFactRefs` for facts sourced from restated statements carry `restated: true` (rendered as markings client-side, TC-VAL-018).
- Dependencies: —

**TC-VAL-008 — Missing statement inputs**
- Traces: UXR-VAL-009, UC-VAL-001 alternate a, `03` §5 · Level: L2 · Design: EP — missing-input class
- Expected: GET `/stocks/PART/dcf` → 200 with `state: "missing_inputs"` naming what is missing (CF-statement-derived facts, e.g., base FCF); **no computation on partial data**; POST compute with PART-derived params still works if the user supplies all 8 values (user's model).
- Dependencies: —

**TC-VAL-009 — POST compute happy path**
- Traces: FR-VAL-005, UC-VAL-001 steps 3–4, `03` §5 · Level: L2 · Design: EP — valid compute class
- Steps: POST `/api/v1/stocks/ALFA/dcf/compute` with the baseline 8 params (anonymous, no CSRF-session needed beyond the global token rule — XC).
- Expected: 200; `fairValuePerShare 13.1969`, `marginOfSafety −0.5155`, price object, full sensitivity grid per TC-VAL-002; `Cache-Control: no-store`; no auth required; **no persistence** (re-POST identical → identical response; no rows anywhere).
- Dependencies: —

**TC-VAL-010 — Compute not-computable → 422**
- Traces: `03` §5/§7 · Level: L2 · Design: EP — math-constraint class
- Expected: `discount_rate 0.03, terminal_growth 0.04` → 422 `DCF_NOT_COMPUTABLE` with the violated constraint in `params`; horizon 11 → same.
- Dependencies: —

**TC-VAL-011 — Compute input validation**
- Traces: `01` §10.5 (bounded numbers, strict types), UXR-VAL-008 server side · Level: L2 · Design: decision table — malformed classes
- Expected: non-numeric param, missing param, horizon 0/11 (as validation vs 422 — see I-VAL-2), out-of-range magnitudes → 400 VALIDATION_FAILED with `params.fields[]`; well-formed extremes (huge but finite values) compute.
- Dependencies: —

**TC-VAL-012 — Price consistency across surfaces**
- Traces: NFR-VAL-003, BR-VAL-003, UXR-G-011 · Level: L2 · Design: EP — same-source class
- Expected: the price object in GET dcf and POST compute equals the canonical `daily_prices` T row for ALFA (20.00, asOf T) — the same value the stock page and screener derive from (single source; sweep-asserted in XC-008).
- Dependencies: —

**TC-VAL-013 — Scenario CRUD**
- Traces: FR-VAL-009, UXR-VAL-012, UC-VAL-002, `03` §5 · Level: L2 · Design: decision table — anonymous/unverified/verified × save; duplicate; owner/non-owner
- Expected: POST `/me/scenarios` `{symbol: "ALFA", name, params}` — anonymous 401, unverified 403 EMAIL_NOT_VERIFIED, verified 201; duplicate name per (user, stock) → 409 DUPLICATE_NAME, nothing overwritten; same name for a *different* stock → 201; GET `/me/scenarios?symbol=ALFA` → that stock's scenarios; PATCH rename (collision within stock → 409); DELETE → gone; non-owner id → 404.
- Dependencies: —

**TC-VAL-014 — Scenario params round-trip exactly**
- Traces: FR-VAL-010, UC-VAL-002 step 2 · Level: L2 · Design: EP — exact-restore class
- Expected: save params (baseline except `discount_rate: 0.15`) → GET returns byte-equal params (all 8, exact values incl. the 0.15 override); loading seeds the calculator and recomputes from them (FR-VAL-010: outputs recompute from saved params — asserted at L3/L4).
- Dependencies: —

### Group C — Admin baseline regeneration (SCR-012 section)

**TC-VAL-015 — Regeneration bumps version and serves new active baseline**
- Traces: UXR-MDF-025, UC-VAL-003, `02` §5.8, `03` §8 · Level: L2 · Design: state transition — v_n active → v_n+1 active
- Steps: as builder, POST `/api/v1/admin/dcf-baselines/regenerate`; GET `/stocks/ALFA/dcf`.
- Expected: regeneration run appears in the ledger; baseline version increments; one active baseline per stock; the new active version is served on next GET; previous versions retained; non-builder → 403.
- Dependencies: —

**TC-VAL-016 — Regeneration confirmation (UI)**
- Traces: UXR-MDF-025, mutation matrix row · Level: L4 · Design: EP — confirm/cancel classes
- Expected: the action requires a confirmation stating rebuild-from-canonical-facts and versioning; cancel aborts, no run; on confirm the run is visible in the ledger; SCR-006 serves the new baseline on next load.
- Dependencies: —

### Group D — Components (L3)

**TC-VAL-017 — Calculator open: baseline, immediate result, all 8 params**
- Traces: UXR-VAL-001/002/003, FR-VAL-003/004, BR-VAL-002/008 · Level: L3 · Design: EP — initial-load class (MSW serves GET dcf)
- Expected: opens with the per-stock baseline; **immediately** shows baseline fair value, current price with as-of, and the plain-language comparison before any user change; all 8 parameters visible and editable (no hidden/locked assumptions — NFR-VAL-004); canonical-fact defaults labeled with source/as-of; restatement markings rendered when refs carry them (TC-VAL-007 payload); stale price → marker + asOf (UXR-VAL-011).
- Dependencies: —

**TC-VAL-018 — Recompute on change; invalid input handling**
- Traces: UXR-VAL-004/008, FR-VAL-005, mutation matrix row 6 · Level: L3 · Design: state transition — valid → invalid → valid; decision table
- Expected: changing any parameter recomputes fair value, comparison, verdict, and sensitivity **without page reload** (debounced ~300 ms — assert a single compute call per settled change); outputs always mirror the currently displayed inputs; invalid input (non-numeric/out-of-range) → inline validation, recompute suppressed, **last valid result stays visible**; unsaved-changes indication appears when deviating from baseline/loaded scenario (Should).
- Dependencies: —

**TC-VAL-019 — Plain-language verdict rendering**
- Traces: UXR-VAL-005/007, FR-VAL-006/008, BR-VAL-004/005, NFR-VAL-001 · Level: L3 · Design: EP — verdict classes (positive/negative MOS; TR/EN)
- Expected: verdict is a single sentence with the MOS percentage, framed "based on your assumptions" (kendi varsayımlarınıza göre / based on your assumptions) — never platform-estimate or buy/sell language; direction not color-only; disclaimer + framing visible; TR/EN from i18n catalogs.
- Dependencies: —

**TC-VAL-020 — Sensitivity table component**
- Traces: UXR-VAL-006, BR-VAL-009 · Level: L3 · Design: EP — grid classes (full; NULL cells)
- Expected: grid renders with row/column headers (r × g_t); the current single-point result identifiable within the grid; cells recomputed with every parameter change; not-computable cells (r ≤ g_t) render an honest not-computable state — **plan-specified**, flag I-VAL-1.
- Dependencies: —

**TC-VAL-021 — Save/load scenario flows**
- Traces: UXR-VAL-012/013/014/015/021, UXR-G-029/030, mutation matrix rows 7–8 · Level: L3 · Design: decision table — anonymous/unverified/duplicate/success; load over unsaved changes
- Expected: anonymous save → sign-in prompt (assumptions + name preserved); unverified → verify-your-e-mail message + path, preserved; duplicate → inline error per stock; success → confirmation, scenario appears in picker + SCR-011 (invalidation); load → params restore **exactly** + outputs recompute; load over unsaved changes → confirmation that the draft will be discarded; pending locks; failures preserve all input.
- Dependencies: —

**TC-VAL-022 — My DCF Scenarios list**
- Traces: UXR-VAL-016..020, SCR-011 · Level: L3 · Design: EP — grouped/empty/anonymous classes
- Expected: scenarios grouped by stock; activating one opens that stock's calculator loaded with it; rename in place with collision rejection; delete behind confirmation → removed from list and picker; anonymous → sign-in path; empty → guidance state.
- Dependencies: —

### Group E — End-to-end flows (L4)

**TC-VAL-023 — Value with own assumptions (critical flow 1, SC-004)**
- Traces: UC-VAL-001 main, SC-004, SCR-006 flow 1 · Level: L4 · Design: EP — anonymous happy path
- Expected: from ALFA's stock page open the calculator; baseline + immediate result shown; change the discount rate → fair value, comparison, verdict, sensitivity recompute without reload; verdict states in plain language how far the price sits from the user's fair value, framed on their assumptions.
- Dependencies: —

**TC-VAL-024 — Anonymous save → register → verify → save (critical flow 2)**
- Traces: UC-VAL-002 alternate a, UXR-VAL-013/021, UXR-G-023/024/030 · Level: L4 · Design: state transition — gated save lifecycle
- Expected: mirrors TC-SCR-028 on the calculator: prompt → register (MailPit) → return with assumptions + name intact → unverified gate → verify in-session → save completes; scenario appears in picker and SCR-011.
- Dependencies: —

**TC-VAL-025 — Load exactly; unsaved-changes guard (critical flows 3–4)**
- Traces: FR-VAL-010, UXR-VAL-014, SCR-006 flows 3–4 · Level: L4
- Expected: loading a saved scenario restores every parameter exactly (the 0.15 override visible) and recomputes; with unsaved modifications, loading requires the discard confirmation.
- Dependencies: —

**TC-VAL-026 — Missing inputs (critical flow 5)**
- Traces: UXR-VAL-009, SCR-006 flow 5, UC-VAL-001 alternate a · Level: L4
- Expected: PART's calculator explains what is missing instead of computing on partial data; supplying user values computes.
- Dependencies: —

**TC-VAL-027 — My DCF Scenarios management (SCR-011 flows)**
- Traces: UXR-VAL-016..020, UC-VAL-002 · Level: L4 · Design: EP — resume/rename-collision/delete classes
- Expected: open a scenario from SCR-011 → calculator loaded + recomputed; rename to a duplicate within the stock → inline error, original retained; delete → confirmation, gone from list and picker.
- Dependencies: —

*(The SC-002 core-loop DCF leg — dashboard → screener → stock page → DCF verdict, anonymous — is TC-XC-001; the research→valuation hand-off leg is TC-RES-028.)*

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** compute inputs — valid / math-constraint-violating / malformed (TC-009/010/011); callers — anonymous/unverified/verified (TC-013/021/024); stocks — full-baseline / restated-baseline / missing-inputs (TC-005/007/008); symbols — known/unknown (TC-006); scenario names — fresh/duplicate-intra-stock/duplicate-cross-stock (TC-013).
- **Boundary value analysis:** horizon 1/10 valid vs 0/11 invalid (TC-003/011); r = g_t exactly (not computable) vs r just above (TC-003, grid NULL cells TC-020); grid corners + center (TC-002).
- **Decision tables:** saver state × outcome (TC-013/021); malformed param classes (TC-011); load × unsaved-changes (TC-021/025).
- **State transition testing:** baseline → modified (recompute, unsaved indication) → saved → reloaded exactly (TC-018/021/025); blocked-save → verified → completes (TC-024); baseline v_n → regenerated v_n+1 active (TC-015/016); valid → invalid input → last-valid retained (TC-018).
- **Golden values:** TC-001/002 pin the single canonical formula (AD-08) to hand-derived numbers — the same result any client must get; TC-012 + XC-008 pin price consistency.

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** compute goldens to 6 significant digits; 422/400 code fidelity; exact param round-trips; UI lifecycle per mutation matrix; drafts/assumptions preserved through every failure path. **Fail:** any mismatch; any computation from inputs other than those displayed; any persistence of compute results. Flaky = fail.

**Suspension:** Testcontainers/compose unavailable >1 day; baseline-parameter or formula change without first re-deriving FU §11.5 (F-VAL-1 resolved 2026-10-07 — no longer a suspender). **Resumption:** blocker fixed + clean triaged run.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-VAL-001 | main | TC-005, 006, 023 | planned |
| FR-VAL-002 | main | TC-005, 012, 017 | planned |
| FR-VAL-003 | main | TC-001, 005, 017 | planned |
| FR-VAL-004 | main | TC-017 (all 8 editable) | planned |
| FR-VAL-005 | main | TC-009, 018, 023 | planned |
| FR-VAL-006 | main | TC-001, 019, 023 | planned |
| FR-VAL-007 | main | TC-002, 020 | planned |
| FR-VAL-008 | main | TC-019 | planned |
| FR-VAL-009 | main | TC-013, 021, 024 | planned |
| FR-VAL-010 | main | TC-014, 021, 025, 027 | planned |
| UC-VAL-001 | main; alt a; alt b; alt c | TC-023; TC-008, 026; TC-007, 017; TC-017 (stale marker) | planned |
| UC-VAL-002 | main; alt a | TC-013, 014, 027; TC-024 | planned |
| UC-VAL-003 | main; alt a (new stock) | TC-015, 016; regeneration covers universe (TC-015 asserts per-stock active) | planned |
| NFR-VAL-001 | bilingual | TC-019 (TR/EN verdict); XC parity | planned |
| NFR-VAL-002 | plain-language verdict | TC-019 rendering; comprehension → SC-006 manual ledger | planned |
| NFR-VAL-003 | price consistency | TC-012; XC-008 | planned |
| NFR-VAL-004 | no black box | TC-004, 017 (all inputs visible/editable, labeled defaults) | planned |
| UXR-VAL-001 | reachable from every stock page | TC-006, 023, RES TC-028 | planned |
| UXR-VAL-002 | baseline + immediate result | TC-017, 023 | planned |
| UXR-VAL-003 | all params editable | TC-017 | planned |
| UXR-VAL-004 | recompute w/o reload | TC-018, 023 | planned |
| UXR-VAL-005 | verdict | TC-019, 023 | planned |
| UXR-VAL-006 | sensitivity table | TC-020 | planned |
| UXR-VAL-007 | disclaimer + framing | TC-019 | planned |
| UXR-VAL-008 | invalid input | TC-011, 018 | planned |
| UXR-VAL-009 | missing inputs | TC-008, 026 | planned |
| UXR-VAL-010 | restatement markings | TC-007, 017 | planned |
| UXR-VAL-011 | stale price | TC-017 | planned |
| UXR-VAL-012 | save + duplicate | TC-013, 021 | planned |
| UXR-VAL-013 | anonymous prompt + preservation | TC-021, 024 | planned |
| UXR-VAL-021 | unverified gate | TC-021, 024 | planned |
| UXR-VAL-014 | load exactly + guard | TC-021, 025 | planned |
| UXR-VAL-015 | picker | TC-021, 022 | planned |
| UXR-VAL-016 | grouped list | TC-022, 027 | planned |
| UXR-VAL-017 | rename + collision | TC-013, 022, 027 | planned |
| UXR-VAL-018 | delete + confirmation | TC-013, 022, 027 | planned |
| UXR-VAL-019 | anonymous sign-in path | TC-022 | planned |
| UXR-VAL-020 | empty state | TC-022 | planned |
| UXR-MDF-025 | regeneration | TC-015, 016 | planned |

## 6. Flags and recorded interpretations (for final review)

- **F-VAL-1 (RESOLVED — builder ruling 2026-10-07, reading (a) adopted):** the `02` §6.4 formula/parameter issue is closed: the debt parameter carries **total debt (ST+LT)** and the formula stands exactly as written (`EquityValue = PV_explicit + TV/(1+r)^N + Cash − Debt`) — both `cash` and `debt` affect the result (no-black-box, NFR-VAL-004). **Independent verification (exact arithmetic, 2026-10-07):** PV_explicit 473.8436 · TV 1843.1392 · TV_pv 1045.8467 · EquityValue 1319.6903 → **FVPS 13.1969**, **MoS −0.5155** — confirmed; the literal net-debt reading would give 15.1969 (double-counted cash — rejected); coherent fix (b) gives the same 13.1969. **One error found during verification:** the (r 0.14, g_t 0.01) sensitivity corner golden in v1.0 was a hand-arithmetic slip — corrected 8.9952 → **8.99623** (PV 449.7668, TV_pv 649.8561) in TC-VAL-002 and FU §11.5. No other golden changed. The architect was asked to record the parameter clarification in `02` §6.4 but its dispatch repeatedly timed out; upstream note flagged (2026-10-07) — this plan is authoritative for implementation until that patch lands.
- **Change propagation (F-VAL-1):** modified TCs — TC-VAL-002 (corner golden 8.9952 → 8.99623); TC-VAL-001 (annotation only, no value change). Traces: FR-VAL-007 (TC-002). Not-yet-implemented: re-validation is by construction; any ticket tracing to TC-VAL-001/002/009 must note the corrected corner.
- **I-VAL-1 (plan-specified):** sensitivity cells where r ≤ g_t render an honest not-computable state (NULL) — the architecture specifies the DCF_NOT_COMPUTABLE constraint for the point result but not for grid cells; the honest-hole reading follows the same principle.
- **I-VAL-2 (interpretation):** horizon outside 1..10 surfaces as `422 DCF_NOT_COMPUTABLE` (math constraint, per `03` §5's explicit example) rather than 400 validation; non-numeric/missing params are 400 VALIDATION_FAILED.
- **I-VAL-3 (interpretation):** grid step sizes are 0.01 on both axes, centered on user values (inferred from the `03` §5 example payload's 0.13–0.17 / 0.02–0.06 grids).

---
*Change record: v1.0 2026-10-06 — initial VAL test plan, batch mode. v1.1 2026-10-07 — builder final review: F-VAL-1 ruled (reading (a) — total-debt parameter, formula as written); goldens independently re-verified under exact arithmetic; sensitivity corner (0.14, 0.01) corrected 8.9952 → 8.99623 in TC-VAL-002 + FU §11.5; §6 interpretations I-VAL-1..3 upheld; plan approved.*
