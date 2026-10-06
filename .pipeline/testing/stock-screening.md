# Stock Screening Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved (builder final review 2026-10-07 — §6 interpretations upheld; v1.0 unchanged)
**Derives from:** `analysis/stock-screening.md` (FR-SCR-001..019, UC-SCR-001..004, NFR-SCR-001..005) · `ux/stock-screening.md` (SCR-003/004, UXR-SCR-001..019) · `ux/market-data-foundation.md` (UXR-MDF-023/024 — admin metric visibility) · `architecture/` v1.2 (`02` §3.3/§6.2, `03` §4, `01` §7) · strategy · fixture contract FU.
**TC ID convention:** `TC-SCR-NNN`, permanent. Levels: L1 · L2 · L3 · L4.

## 1. Scope

**Tested here:** FR-SCR-001..017 (17 non-Won't); UC-SCR-001..004 including alternates; NFR-SCR-001..005; UXR-SCR-001..019; UXR-MDF-023/024 (SCR-012 metric-visibility section).

**Not tested here:** FR-SCR-018/019 (OR-groups, sharing — Won't); CSRF/rate limits (XC); i18n parity (XC); the cross-surface equality sweep (XC-008 asserts it; TC-SCR-002 values feed it); lockout/verification mechanics (ACC — SCR asserts only the gate behavior on its own endpoints).

**Screener goldens (derived from FU; all values already asserted at the MDF level — here they are asserted *through the run endpoint*):** pe: ALFA 10.0, DELTA 20.0, REST 10.0, THETA 18.0, UNSEC 14.0, EPSL 26.6667, ZETA 34.3333, ETA 22.0, PART 25.0, NEWP 52.0, IOTA 50.0, KAPPA 102.5, LAMDA 45.8333, NU 67.5; BETA/GAMA NULL. roe: ALFA 0.20, REST 0.15, UNSEC 0.2143, DELTA −0.25, THETA 0.14, PART 0.15, others per FU; DELTA roe is a value (negative equity → NI/−200 = −0.25; NB: `pb`/`roe` NULL rule fires on equity ≤ 0 → **roe for DELTA is NULL per `02` §6.2** — the screener sees NULL, i.e., excluded-on-missing-data, not a failing value). div_yield: ALFA 0.05, REST 0.04, DELTA 0.02, EPSL 0.015, THETA 0.0238, IOTA 0.01, KAPPA 0.0033, NU 0.0222; never-paid stocks NULL.

## 2. Test Case Specification

### Group A — Run endpoint (L2)

**TC-SCR-001 — Visible metric catalog**
- Traces: FR-SCR-001, BR-SCR-002, UXR-SCR-011, UC-SCR-001 step 2 · Level: L2 · Design: EP — default-visible class
- Expected: GET `/api/v1/screener/metrics` (anonymous) returns the 18 metric concepts grouped by the five families with unit, TR/EN labels, `isCagr` flag on the three growth metrics, sort order; serves **only** `is_screenable = true` rows.
- Dependencies: —

**TC-SCR-002 — AND logic with min/max bounds (golden)**
- Traces: FR-SCR-001..004, BR-SCR-003, UC-SCR-001 main · Level: L2 · Design: golden values (EP — multi-criterion valid class)
- Steps: POST `/api/v1/screener/run` `{criteria: [{metricCode: "pe", bound: "max", maxValue: 15}, {metricCode: "roe", bound: "min", minValue: 0.15}]}`.
- Expected: 200; `matchCount: 3`; `excludedCount: 2` (BETA, GAMA — pe NULL); rows = ALFA (pe 10.0, roe 0.20), REST (10.0, 0.15), UNSEC (14.0, 0.2143) with per-criterion values and row identity (symbol, name, sector); `asOf: T`, `stale: false`; `droppedCriteria: []`.
- Dependencies: —

**TC-SCR-003 — Range bounds, inclusive edges (golden)**
- Traces: FR-SCR-003 · Level: L2 · Design: BVA — bounds inclusive at min and max
- Steps: run `{metricCode: "pe", bound: "range", minValue: 10, maxValue: 20}`; then `minValue: 10.0001`.
- Expected: range run → matches ALFA (10.0), DELTA (20.0), REST (10.0), THETA (18.0), UNSEC (14.0) — **5 rows** (values exactly at min and max included); shifted-min run → ALFA (exactly 10.0) excluded → 4 rows. *(Bound semantics: inclusive min/max — plan-specified; see I-SCR-2.)*
- Dependencies: —

**TC-SCR-004 — Growth criterion with window (golden)**
- Traces: FR-SCR-015, BR-SCR-005, UC-SCR-001 step 2 · Level: L2 · Design: EP — window classes (3/5/10) over available-history fixture
- Steps: run `{metricCode: "rev_cagr", bound: "min", minValue: 0.15, window: 3}`.
- Expected: matches ALFA (0.160397) and ZETA (0.25, computed over its 1 actual interval); `excludedCount: 14` (all other stocks lack FY series → NULL → excluded, never passing); a `window: 5` run returns the same two stocks with ALFA's value 0.201124 — the window parameter is honored per criterion.
- Dependencies: —

**TC-SCR-005 — Dividend criterion with never-paid exclusions (golden)**
- Traces: FR-SCR-001, BR-SCR-009 · Level: L2 · Design: EP — NULL-dividend class
- Steps: run `{metricCode: "div_yield", bound: "min", minValue: 0.04}`.
- Expected: matches ALFA (0.05) and REST (0.04 — inclusive edge); `excludedCount: 8` (never-paid stocks incl. LAMDA); DELTA (0.02) fails the bound but is *not* excluded (has data).
- Dependencies: —

**TC-SCR-006 — Zero-match result is a normal 200**
- Traces: FR-SCR-006, UC-SCR-001 alternate a, `03` §4 · Level: L2 · Design: EP — no-match class
- Steps: run `{metricCode: "pe", bound: "max", maxValue: 5}`.
- Expected: 200; `matchCount: 0`; `rows: []`; no error code — emptiness is data.
- Dependencies: —

**TC-SCR-007 — Hidden or unknown metric rejected (ad-hoc)**
- Traces: UXR-SCR-011, UC-SCR-004, `03` §4 · Level: L2 · Design: EP — hidden/unknown metric classes
- Preconditions: ROE hidden via admin toggle (TC-SCR-020 state).
- Steps: ad-hoc run with `metricCode: "roe"`; then with `metricCode: "not_a_metric"`.
- Expected: both → `400` with code `METRIC_NOT_AVAILABLE` — the builder never offered them; the catalog (TC-SCR-001) also excludes ROE in this state.
- Dependencies: TC-SCR-020 mechanism (re-created in-test).

**TC-SCR-008 — Malformed criteria rejected**
- Traces: `03` §7 VALIDATION_FAILED, FR-SCR-003 · Level: L2 · Design: decision table — malformed classes
- Steps: runs with (a) missing bound value; (b) non-numeric bound; (c) `window: 7` (not 3/5/10); (d) range with min > max; (e) window on a non-growth metric.
- Expected: each → `400 VALIDATION_FAILED` with `params.fields[]` naming the criterion and field; no partial execution.
- Dependencies: —

**TC-SCR-009 — Empty criteria array rejected**
- Traces: FR-SCR-002 (AND over ≥1 criteria) · Level: L2 · Design: BVA — criteria count 0 vs 1
- Expected: `criteria: []` → `400 VALIDATION_FAILED` (at least one criterion required — **plan-specified recommendation**, flag I-SCR-1); a single-criterion run succeeds (count 1 boundary).
- Dependencies: —

**TC-SCR-010 — Run response envelope and staleness**
- Traces: FR-SCR-005, BR-SCR-006, NFR-SCR-004 · Level: L2 · Design: decision table — data fresh/stale × run
- Expected: fresh state → `asOf: T`, `stale: false`; with a forced freshness gap (fixture variant as in TC-MDF-013) → `stale: true` with the older `asOf` — a run never presents unclear vintage.
- Dependencies: —

**TC-SCR-011 — Results are computed on demand, never persisted**
- Traces: UC-SCR-003 step 2, `03` §4 · Level: L2 · Design: state transition — data advances → results change
- Steps: run a criteria set; ingest day T+1 fixture prices; re-run the identical criteria.
- Expected: second run reflects T+1 (`asOf` advances, values change accordingly); no result-set rows exist anywhere (no persistence surface — schema backstop TC-MDF-009 has no result table).
- Dependencies: —

### Group B — Saved screens CRUD (L2)

**TC-SCR-012 — Save a named screen (happy)**
- Traces: FR-SCR-007, UC-SCR-002 main, SC-003 · Level: L2 · Design: EP — valid save class
- Steps: as verified user-a, POST `/api/v1/me/screens` `{name: "Ucuz kaliteli", criteria: [pe≤15, roe≥0.15, rev_cagr min 0.15 window 5]}`.
- Expected: 201; appears in GET `/me/screens` with name + criteria (CAGR windows preserved); criteria_json round-trips exactly.
- Dependencies: —

**TC-SCR-013 — Duplicate name rejected, nothing overwritten**
- Traces: UXR-G-029, FR-SCR-007, `02` §3.3 UNIQUE · Level: L2 · Design: EP — collision class
- Expected: second save with the same name (same user) → `409 DUPLICATE_NAME` with `params.name`; the original screen's criteria unchanged; names are unique per account only — user-c saving the same name → 201 (no cross-account collision).
- Dependencies: —

**TC-SCR-014 — Auth and verification gates on save**
- Traces: UXR-SCR-019, UXR-G-030, NFR-ACC-005 · Level: L2 · Design: decision table — anonymous / unverified / verified
- Expected: anonymous POST → 401 `UNAUTHENTICATED`; unverified (user-b) → 403 `EMAIL_NOT_VERIFIED`; verified → 201. Reading `GET /me/screens` requires auth only (401 anonymous).
- Dependencies: —

**TC-SCR-015 — Rename in place; collision rejected**
- Traces: FR-SCR-010, UXR-SCR-016 · Level: L2 · Design: EP — rename classes
- Expected: PATCH `/me/screens/{id}` `{name}` → 200, name updated in place (same id, updated_at bump); rename to an existing name → 409 `DUPLICATE_NAME`, previous name retained; rename with empty name → 400.
- Dependencies: —

**TC-SCR-016 — Criteria update via PATCH**
- Traces: `03` §4 (PATCH {name} and/or {criteria}) · Level: L2 · Design: EP — criteria-update class
- Expected: PATCH with new criteria → stored criteria replaced; subsequent re-run uses the new criteria; invalid criteria payload → 400 (same validation as run).
- Dependencies: —

**TC-SCR-017 — Delete; 404s; ownership isolation**
- Traces: FR-SCR-011, UXR-SCR-017 · Level: L2 · Design: decision table — owner / non-owner / missing × delete
- Expected: owner DELETE → 204/200, gone from list; second DELETE → 404 `NOT_FOUND`; user-a deleting user-c's screen id → 404 (never 403 — no existence leak); PATCH on another user's id → 404 likewise.
- Dependencies: —

**TC-SCR-018 — Re-run with stored criteria; dropped criterion on hidden metric**
- Traces: FR-SCR-009, UC-SCR-003 main + alternate a, UXR-SCR-015 · Level: L2 · Design: state transition — metric hidden after save
- Preconditions: saved screen containing ROE; ROE then hidden via admin toggle.
- Steps: re-run the saved screen.
- Expected: run proceeds **without** the ROE criterion; `droppedCriteria: ["roe"]` names it; results match the pe-only golden; after un-hiding, re-run includes ROE again and `droppedCriteria: []`.
- Dependencies: TC-SCR-020 mechanism.

**TC-SCR-019 — Re-run computes on latest data**
- Traces: FR-SCR-009, UC-SCR-003 step 2, NFR-SCR-004 · Level: L2 · Design: state transition — new trading day
- Expected: a saved screen re-run after day T+1 ingested returns T+1 `asOf` and recomputed values (mirrors TC-SCR-011 through the saved-screen path).
- Dependencies: —

### Group C — Admin metric visibility (SCR-012 section)

**TC-SCR-020 — Hide/unhide metric without code change (API)**
- Traces: FR-SCR-017, UC-SCR-004, UXR-MDF-023 · Level: L2 · Design: state transition — visible ↔ hidden
- Steps: as builder, PATCH `/api/v1/admin/screener-metrics/roe` `{isScreenable: false}`; GET catalog; re-run affected saved screen; toggle back.
- Expected: catalog excludes ROE while hidden (anonymous view); ad-hoc use → METRIC_NOT_AVAILABLE (TC-SCR-007); saved-screen re-run drops it (TC-SCR-018); non-builder PATCH → 403; toggle back restores everything.
- Dependencies: —

**TC-SCR-021 — Visibility toggle UI with consequence confirmation**
- Traces: UXR-MDF-024, UC-SCR-004 · Level: L4 · Design: EP — toggle classes
- Expected: the panel lists the 18 metrics with current state; toggling requires a confirmation stating both consequences (criteria builder stops offering it; saved screens report the dropped criterion on re-run); cancel aborts with no change; SCR-003's builder reflects the change on next load without manual refresh.
- Dependencies: —

### Group D — Components (L3)

**TC-SCR-022 — Criteria builder**
- Traces: UXR-SCR-001/002/003 · Level: L3 · Design: EP — build/remove/clear classes
- Expected: metrics organized by five families from the catalog; add criterion with min/max/range controls and (growth) window selector; remove individual criterion; clear all; labels/units from catalog (NFR-SCR-002 plain language); keyboard operable.
- Dependencies: —

**TC-SCR-023 — Draft preservation across navigation and auth hop**
- Traces: UXR-SCR-010, UXR-G-021/024, FR-SCR-013 · Level: L3 · Design: state transition — navigate away/return; auth hop
- Expected: criteria + entered save name preserved across route change and return, and across the register/sign-in hop (sessionStorage-backed store); restored on return.
- Dependencies: —

**TC-SCR-024 — Save-flow states**
- Traces: UXR-SCR-008/009/019, UXR-G-029, UXR-G-030, mutation matrix rows 2 · Level: L3 · Design: decision table — anonymous/unverified/duplicate/success/failure
- Expected (MSW-driven): anonymous → sign-in/register prompt (no error); unverified → verify-your-e-mail message with path, criteria+name preserved; duplicate → inline collision error identifying the name, nothing lost; pending → trigger locked; server 500 → plain-language error + retry, input preserved; success → confirmation names the screen.
- Dependencies: —

**TC-SCR-025 — Results, empty, and exclusion-count display**
- Traces: UXR-SCR-004/005/006/007 · Level: L3 · Design: EP — populated/zero/excluded classes
- Expected: results table with per-criterion columns and row links; `matchCount: 0` → explicit zero-state suggesting relaxed criteria (distinct from error); excludedCount displayed when > 0; as-of date rendered.
- Dependencies: —

### Group E — End-to-end flows (L4)

**TC-SCR-026 — Build and run anonymously (critical flow 1)**
- Traces: UC-SCR-001 main, SC-002 screen stage, BR-SCR-004 · Level: L4 · Design: EP — anonymous happy path
- Expected: anonymous user adds "P/E at most 15" + "ROE at least 15%", runs, sees ALFA/REST/UNSEC with values, match count 3, exclusion count 2, as-of date; row click opens the stock page.
- Dependencies: —

**TC-SCR-027 — Zero matches (critical flow 2)**
- Traces: UC-SCR-001 alternate a · Level: L4
- Expected: impossible criteria → explicit zero-results state with relax suggestion; not a blank list, not an error.
- Dependencies: —

**TC-SCR-028 — Anonymous save → register → verify → return → save (critical flow 3)**
- Traces: UC-SCR-002 alternate a, UXR-SCR-009/010/019, UXR-G-023/024/030, FR-SCR-012/013, FR-ACC-008 · Level: L4 · Design: state transition — full gated save lifecycle
- Expected: save attempt → prompt; register (verification e-mail read via MailPit); returned to screener with criteria + name intact; save blocked with verify-your-e-mail message; verify via the e-mailed link in the same session; save completes without re-authentication; the screen appears in My Saved Screens without manual refresh.
- Dependencies: —

**TC-SCR-029 — Result → research → back (critical flow 4)**
- Traces: UXR-SCR-007, UXR-G-021 · Level: L4
- Expected: from results, open a stock page and return → criteria and results still present.
- Dependencies: —

**TC-SCR-030 — My Saved Screens management (critical flows 1–4 of SCR-004)**
- Traces: FR-SCR-008..011, UXR-SCR-012..018, UC-SCR-003 · Level: L4 · Design: EP/decision table — list/re-run/rename-collision/delete
- Expected: signed-in list by name; re-run presents fresh results in the screener context with current as-of (never stored results); rename updates in place; rename to a duplicate → inline error, original retained; delete behind a focus-trapping confirmation stating consequences → row removed, gone from SCR-003 references; anonymous direct access → sign-in path; empty account → guidance state.
- Dependencies: —

**TC-SCR-031 — Server-side persistence across devices**
- Traces: NFR-SCR-005, SC-003 · Level: L4 · Design: EP — second-device class
- Expected: a screen saved in browser context A appears in an independent context B after sign-in (server truth, not device state).
- Dependencies: —

**TC-SCR-032 — Unverified gate unblocks on in-session verification**
- Traces: UXR-SCR-019 second half, UXR-G-030, architecture M-9 · Level: L4 · Design: state transition — blocked → verified (same session)
- Expected: signed-in unverified user's save is blocked; after verifying in another tab, the open session's next save attempt succeeds without re-authentication.
- Dependencies: —

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** criteria — valid single/multi, malformed (5 classes, TC-008), empty (TC-009); metrics — visible/hidden/unknown (TC-001/007/020); savers — anonymous/unverified/verified (TC-014, 024, 028, 032); names — fresh/duplicate intra-account/duplicate inter-account (TC-013/015); targets — owner/non-owner/missing (TC-017); results — populated/zero (TC-002/006/025/027).
- **Boundary value analysis:** bound inclusivity at exactly min/max (TC-003: ALFA pe 10.0 at the min edge, DELTA 20.0 at the max edge, REST 0.04 at div bound in TC-005); criteria count 0/1 (TC-009); window values 3/5/10 vs 7 (TC-004/008).
- **Decision tables:** saver state × save outcome (TC-014/024); malformed-criterion classes (TC-008); delete/rename target ownership (TC-017); NULL-data → exclude-and-count vs fail-bound (TC-002/005 — BR-SCR-009's table).
- **State transition testing:** saved screen → metric hidden → dropped criterion reported → un-hidden → restored (TC-018/020); data day advances → re-run recomputes (TC-011/019); anonymous → registered → verified → save completes (TC-028/032); draft → navigate → return → preserved (TC-023/029).
- **Golden values:** TC-002/003/004/005 pin the run endpoint to FU-derived numbers — the same canonical values the stock pages serve (cross-surface equality, UXR-G-011, asserted sweep-wide in XC-008).

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** run goldens match FU values exactly (6 sig. digits); CRUD behaviors return the exact `03` §7 codes; UI states render per the mutation-matrix lifecycle; e2e flows complete with deterministic MailPit-driven verification. **Fail:** any mismatch; flaky = fail.

**Suspension:** Testcontainers/compose unavailable >1 day; metric-visibility or criteria-model change invalidating goldens (re-derive FU first); auth-flow blocker suspending TC-028/032 (owned by ACC — resume when ACC's blocking defect is fixed). **Resumption:** clean triaged run of the affected group.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-SCR-001 | main | TC-001, 002, 004, 005 | planned |
| FR-SCR-002 | main | TC-002, 009 | planned |
| FR-SCR-003 | main | TC-003, 008 | planned |
| FR-SCR-004 | main | TC-002, 025, 026, 029 | planned |
| FR-SCR-005 | main | TC-002, 010, 025 | planned |
| FR-SCR-006 | main | TC-006, 027 | planned |
| FR-SCR-007 | main | TC-012, 013, 014 | planned |
| FR-SCR-008 | main | TC-012, 030 | planned |
| FR-SCR-009 | main | TC-011, 018, 019, 030 | planned |
| FR-SCR-010 | main | TC-015, 030 | planned |
| FR-SCR-011 | main | TC-017, 030 | planned |
| FR-SCR-012 | main | TC-024, 028 | planned |
| FR-SCR-013 | main | TC-023, 028 | planned |
| FR-SCR-014 | main | TC-002, 005, 025 | planned |
| FR-SCR-015 | main | TC-004, 012, 022 | planned |
| FR-SCR-016 | main | SCR-003/004 disclaimer — XC-006 sweep + TC-026 render | planned |
| FR-SCR-017 | main | TC-020, 021 | planned |
| UC-SCR-001 | main; alt a; alt b; alt c | TC-026; TC-027; TC-002/005; TC-010 | planned |
| UC-SCR-002 | main; alt a | TC-012; TC-028 | planned |
| UC-SCR-003 | main; alt a | TC-030, 019; TC-018 | planned |
| UC-SCR-004 | main; alt a (un-hide) | TC-020, 021; TC-020 (toggle back) | planned |
| NFR-SCR-001 | bilingual | XC parity; labels asserted TC-022 | planned |
| NFR-SCR-002 | plain-language criteria | TC-022 (labels/units from catalog); review ledger | planned |
| NFR-SCR-003 | cross-surface consistency | TC-002 goldens feed XC-008 sweep | planned |
| NFR-SCR-004 | freshness honesty | TC-010, 019 | planned |
| NFR-SCR-005 | server-side persistence | TC-031 | planned |
| UXR-SCR-001 | builder | TC-022, 026 | planned |
| UXR-SCR-002 | window selector | TC-004, 022 | planned |
| UXR-SCR-003 | remove/clear | TC-022 | planned |
| UXR-SCR-004 | run + results | TC-025, 026 | planned |
| UXR-SCR-005 | exclusion count | TC-002, 005, 025 | planned |
| UXR-SCR-006 | zero state | TC-006, 027 | planned |
| UXR-SCR-007 | row links | TC-025, 026, 029 | planned |
| UXR-SCR-008 | save + duplicate | TC-012, 013, 024 | planned |
| UXR-SCR-009 | anonymous prompt | TC-024, 028 | planned |
| UXR-SCR-010 | preservation | TC-023, 028 | planned |
| UXR-SCR-011 | hidden metrics | TC-001, 007, 020 | planned |
| UXR-SCR-019 | unverified gate | TC-014, 024, 028, 032 | planned |
| UXR-SCR-012 | signed-in list/anonymous path | TC-030 | planned |
| UXR-SCR-013 | list by name | TC-030 | planned |
| UXR-SCR-014 | re-run fresh | TC-019, 030 | planned |
| UXR-SCR-015 | dropped criterion | TC-018 | planned |
| UXR-SCR-016 | rename + collision | TC-015, 030 | planned |
| UXR-SCR-017 | delete + confirmation | TC-017, 030 | planned |
| UXR-SCR-018 | empty state | TC-030 | planned |
| UXR-MDF-023 | visibility panel | TC-020, 021 | planned |
| UXR-MDF-024 | consequence confirmation | TC-021 | planned |

## 6. Flags and recorded interpretations (for final review)

- **I-SCR-1 (plan-specified recommendation):** `criteria: []` → `400 VALIDATION_FAILED` (a screen requires ≥1 criterion); the UI disables Run while no criteria exist (UXR-G-019 pattern). The architecture is silent; veto here changes TC-009.
- **I-SCR-2 (plan-specified):** bounds are inclusive (`min ≤ v ≤ max`) — evidenced by TC-SCR-003 edges; DELTA's pe 20.0 included at max 20.
- **I-SCR-3 (interpretation):** sector identity in result rows is asserted as *identity* (matches the stock's sector per FU) rather than a specific serialization (code vs. name(s)) — the API example shows a Turkish name while the codes-not-words principle suggests otherwise; left to implementation, both satisfy the test. Rendered label language is asserted in e2e (active language).
- **I-SCR-4 (interpretation):** window on a non-growth metric → `400 VALIDATION_FAILED` (field `window`) — the criterion model ties windows to growth metrics only (BR-SCR-005).

---
*Change record: v1.0 2026-10-06 — initial SCR test plan, batch mode. Approved at the builder's final review 2026-10-07 — §6 interpretations I-SCR-1..4 upheld.*
