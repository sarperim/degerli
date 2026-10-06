# Market Overview & Macro Indicators Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved v1.1 (builder final review 2026-10-07 — F-MOV-1 accepted, stays Could/contract-only; §6 interpretations upheld)
**Derives from:** `analysis/market-overview.md` (FR-MOV-001..021, UC-MOV-001..005, NFR-MOV-001..006) · `ux/market-overview.md` (SCR-001, UXR-MOV-001..012) · `architecture/` v1.2 (`02` §3.2/§6.3, `03` §3, `01` §8–§9) · strategy (approved) · fixture contract `01-fixture-universe.md` (FU).
**TC ID convention:** `TC-MOV-NNN`, permanent. Levels: L1 unit · L2 integration · L3 web component · L4 e2e.

## 1. Scope

**Tested here:** FR-MOV-001..016, 020, 021 (19 non-Won't); UC-MOV-001..005 including alternates; NFR-MOV-001..006; UXR-MOV-001..012 (SCR-001). MOV owns the macro ingestion jobs (`macro-daily`, `macro-cpi`, per-release repo rate — `03` §9) and the market-snapshot requirement (BR-MOV-009; computed by the shared Metrics Engine — goldens asserted here).

**Not tested here:** FR-MOV-017/018 (period selector is Could — case specified but blocked-on-formula, §6 flag F-MOV-1; historical charts Won't); FR-MOV-019 (10Y yield — permanent exclusion); caching headers, CSRF, rate limits, i18n parity, disclaimer sweep, a11y (→ XC plan); the 99.0% availability figure (operational ledger); cost NFR (manual ledger).

## 2. Test Case Specification

### Group A — Snapshot computation (Metrics Engine output, MOV-owned requirement)

**TC-MOV-001 — Market snapshot golden values**
- Traces: FR-MOV-001..006, 021, BR-MOV-009/010/011, UC-MOV-001 step 2 · Level: L2 · Design: golden values (EP — canonical snapshot class)
- Preconditions: seeded fixtures; EOD chain run for T (as TC-MDF-010).
- Steps: read the computed `market_snapshots` row for T (via `GET /api/v1/market/overview`).
- Expected (FU §11.3, exact): xu100_level 10200.00, xu100_change_pct +2.00; xu30_level 30600.00, xu30_change_pct +2.00; breadth 10/5/1; volume_total 174,500,000; market_pe 28.1218; market_pe_excluded_count 2; market_div_yield 0.011225; gainers_json = 9 entries in order ETA +10.0000, NEWP +4.0000, ZETA +3.0000, KAPPA +2.5000, EPSL +1.9888, PART +1.5022, REST +1.2151, ALFA +1.0101, DELTA +0.5025, each with volume; losers_json = 5 entries THETA, IOTA, BETA, GAMA, UNSEC; sector_perf_json daily per the locked formula: A +0.2530%, B +2.2409%, C +4.1726%.
- Dependencies: —

**TC-MOV-002 — Movers eligibility excludes ineligible volume**
- Traces: FR-MOV-004, BR-MOV-011 · Level: L2 · Design: EP — below-threshold class
- Expected: LAMDA (+25.0000%, volume 500,000 < 1,000,000 threshold) absent from gainers_json despite the largest change; every listed entry carries its volume; entries are current-universe stocks only.
- Dependencies: covers TC-MOV-001 state.

**TC-MOV-003 — Top-10 cap and exact-threshold eligibility**
- Traces: FR-MOV-004, BR-MOV-011 · Level: L2 · Design: BVA — threshold edge (volume = threshold) and cap edge (12 eligible candidates)
- Preconditions: variant price payload `prices-variant-threshold.json` (12 eligible gainers, one at exactly 1,000,000 volume).
- Expected: the exactly-at-threshold stock **is** eligible (≥); gainers list caps at 10 entries ranked by % change desc; the 11th/12th candidates excluded.
- Dependencies: —

**TC-MOV-004 — Market P/E exclusion rule and degenerate case**
- Traces: FR-MOV-006, BR-MOV-010, D-05 · Level: L2 · Design: decision table — loss-makers excluded + counted; all-loss-makers degenerate class
- Steps: (a) assert TC-MOV-001's exclusion values; (b) variant fixture where every constituent has NI ≤ 0.
- Expected: (a) excluded_count 2 (BETA, GAMA) disclosed; (b) market_pe is NULL (not 0, not negative) with excluded_count = universe size — honest unavailable aggregate *(plan-specified expectation; see §6 interpretation I-MOV-2)*.
- Dependencies: —

**TC-MOV-005 — One snapshot per trading day; latest completed served**
- Traces: NFR-MOV-001, BR-MOV-009, `02` §3.2 · Level: L2 · Design: EP — repeated computation; recency class
- Expected: re-running the snapshot job for T produces a single row (idempotent by snapshot_date); GET overview always serves the latest completed snapshot — after a T+1 run, payload reflects T+1 with its own asOf.
- Dependencies: —

### Group B — Macro ingestion (UC-MOV-002)

**TC-MOV-006 — Daily macro series ingest**
- Traces: FR-MOV-014, UC-MOV-002 main, BR-MOV-008 · Level: L2 · Design: EP — valid daily class
- Expected: `macro-daily` job stores USD_TRY 47.10, EUR_TRY 51.40, GOLD 5200.00 for T with value_date, unit, source_ref, recorded_at; one canonical value per series per date; idempotent re-run.
- Dependencies: —

**TC-MOV-007 — CPI ingest with revision; canonical = latest**
- Traces: FR-MOV-014, UC-MOV-002 alternate b, BR-MOV-008 · Level: L2 · Design: state transition — value → revision appended
- Expected: TUIK_CPI 2026-08-01 has two rows (44.80 @ recorded 2026-09-05; 45.00 @ recorded 2026-09-20); canonical served value = 45.00; both rows retained (nothing overwritten).
- Dependencies: —

**TC-MOV-008 — Per-release series cadence**
- Traces: FR-MOV-014 · Level: L2 · Design: EP — per_release class
- Expected: CBRT_REPO 42.50 stored with its decision date 2026-09-15; a new decision appends a new dated value (previous retained).
- Dependencies: —

**TC-MOV-009 — Macro source failure: last-known + stale + alert**
- Traces: FR-MOV-015, FR-MOV-016, UC-MOV-005 main, UC-MOV-001 alternate a · Level: L2 · Design: state transition — fresh → stale (served)
- Preconditions: GOLD already stale in seed (last success T−10); switch a fresh series' source stub to failure and exhaust retries.
- Expected: series continues serving its last-known value with its as-of date and `stale: true`; builder alerted (mail double + Serilog event) naming the series; recovery on next successful run clears stale (fresh→stale→fresh cycle asserted).
- Dependencies: —

**TC-MOV-010 — Macro freshness cadence targets**
- Traces: NFR-MDF-001 macro rows, NFR-MOV-001 · Level: L2 · Design: BVA — schedule edges (by 09:00 next day; CPI within 24h of release)
- Expected: with the fake clock, the daily macro job completes before 09:00 on the day after the trading day; the CPI job runs within 24h of the canned release timestamp; ledger rows record the timings.
- Dependencies: —

**TC-MOV-011 — Macro alert content**
- Traces: FR-MOV-015 · Level: L2 · Design: EP — alert class
- Expected: alert e-mail and log event identify the series, the failure class (unreachable / invalid / late), and the run.
- Dependencies: —

### Group C — Public endpoints (`03` §3)

**TC-MOV-012 — GET /market/overview payload**
- Traces: FR-MOV-001..006, 010, 021, UC-MOV-001 · Level: L2 · Design: EP — anonymous happy class
- Expected: anonymous 200 (BR-MOV-007); payload = the TC-MOV-001 values under the honest-data envelope (`asOf` = T, `stale` = false); response shape serves all equity blocks in one payload.
- Dependencies: —

**TC-MOV-013 — GET /market/macro payload**
- Traces: FR-MOV-007..009, 016, UC-MOV-003, UXR-MOV-009 · Level: L2 · Design: decision table — series × {fresh, stale, absent}
- Expected: six entries each `{code, value, unit, source, asOf, stale, state}`; inflation pair **always both entries** (BR-MOV-002); GOLD carries `stale: true` + its T−10 asOf; in the degraded profile (INDEP_CPI absent) the independent entry is present with `state: "unavailable"` and no value — never silently dropped.
- Dependencies: —

**TC-MOV-014 — Equity and macro failure domains are separate**
- Traces: UC-MOV-002 vs UC-MDF-001, `03` §3 note, SCR-001 §5 · Level: L2 · Design: decision table — equity × macro health (4 combinations)
- Expected: prices source down + macro fresh → `/market/overview` serves last-known with `stale: true` while `/market/macro` serves fresh values 200; equity fresh + macro degraded → the inverse; both fresh → both clean; both down → both degrade independently (no cross-blanking).
- Dependencies: —

**TC-MOV-015 — Sector performance period parameter (Could)**
- Traces: FR-MOV-017, UXR-MOV-012 · Level: L2 · Design: EP — period classes
- Status: **specified, blocked-on-formula** (§6 flag F-MOV-1): the 1W/1M/YTD aggregate formula is undefined in the architecture. When the feature is promoted: recommended formula = cap-weighted total return over the period (mirroring the locked daily formula); the fixture doc gains a period-history table and goldens at that time. The API contract itself is testable now: `period=1w|1m|ytd` returns the precomputed aggregate for that period with the same envelope; invalid period → 400 VALIDATION_FAILED.
- Dependencies: —

### Group D — SCR-001 rendering (L3/L4)

**TC-MOV-016 — Dashboard renders anonymously, TR default, all blocks**
- Traces: UXR-MOV-001..005, 007, 010, FR-MOV-020, UC-MOV-001 critical flow 1, NFR-MOV-004 · Level: L4 · Design: EP — populated class
- Expected: anonymous visit to root renders all blocks with values; every macro value shows unit, source, and as-of date (NFR-MOV-004 proxy); index/sector/mover change directions conveyed non-color-only; disclaimer visible (FR-MOV-020); initial language Turkish (TR default); language toggle present; equity as-of date displayed.
- Dependencies: —

**TC-MOV-017 — Inflation pair displayed side by side**
- Traces: UXR-MOV-008, FR-MOV-008, BR-MOV-002, UC-MOV-003 · Level: L4 · Design: EP — pair class
- Expected: TÜİK CPI and independent measure both visible, each with source label and as-of date; no editorial commentary; pair survives narrow viewport (both visible, possibly stacked).
- Dependencies: —

**TC-MOV-018 — Degraded macro: unavailable state component**
- Traces: UXR-MOV-008/009, UC-MOV-003 alternate a · Level: L3 · Design: EP — unavailable payload class (MSW handler serving `state: unavailable`)
- Expected: official CPI renders alone with an explicit note that the independent measure is unavailable; the unavailable indicator's label is retained (no value) — never hidden, never blank-as-zero.
- Dependencies: —

**TC-MOV-019 — Dashboard navigation click-throughs**
- Traces: FR-MOV-011/012/013, UXR-MOV-002/004/011, UC-MOV-004 critical flows 2–3 · Level: L4 · Design: EP — link classes
- Expected: gainer entry → that stock's page; sector entry → Stock List pre-filtered to the sector; screener and stock list reachable from navigation.
- Dependencies: —

**TC-MOV-020 — Volume shown per mover entry**
- Traces: FR-MOV-021, UXR-MOV-004 · Level: L4 · Design: EP — mover-entry class
- Expected: each gainer/loser entry displays its traded volume alongside symbol and change.
- Dependencies: —

**TC-MOV-021 — Loss-maker exclusion disclosure rendered**
- Traces: UXR-MOV-006, BR-MOV-010 · Level: L4 · Design: EP — disclosure class
- Expected: the market valuation block shows the plain-language disclosure that loss-making companies are excluded, with the excluded count (2).
- Dependencies: —

**TC-MOV-022 — No real-time implication**
- Traces: UXR-G-012, BR-MOV-001 · Level: L4 · Design: negative probe — absence of polling/live language
- Expected: over a 3-second quiet window after load, no periodic requests to `/market/*` (assert via network tracking); no countdown/auto-refresh UI; freshness framed as EOD with as-of date.
- Dependencies: —

**TC-MOV-023 — Language switch preserves context**
- Traces: UXR-G-013, NFR-MOV-002, SCR-001 critical flow 6 · Level: L4 · Design: EP — toggle class
- Expected: TR→EN toggle re-renders all blocks in English; same data, same scroll position; and EN→TR back.
- Dependencies: —

**TC-MOV-024 — Stale marker component**
- Traces: FR-MOV-010, UXR-MOV-010, UXR-G-008 · Level: L3 · Design: decision table — payload stale true/false
- Expected: `stale: true` payload → visible stale badge + as-of date rendered; `stale: false` → as-of only, no badge. *(The e2e stale assertion rides on the always-seeded stale GOLD macro block — same marker component; equity-side payload correctness is TC-MOV-012/014.)*
- Dependencies: —

**TC-MOV-025 — Blocks render and fail independently**
- Traces: SCR-001 §5, UXR-G-003, architecture M-5 · Level: L3 · Design: EP — single-block failure class (MSW rejects one block's query)
- Expected: the failing block shows a plain-language error with retry for that block; all other blocks render; retry re-invokes and recovers when the handler is restored.
- Dependencies: —

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** payload/source classes — valid daily, per-release, revised, failing, absent (TC-006..009, 013); callers — anonymous (TC-012/013/016); mover candidates — eligible / below-threshold / at-threshold (TC-002/003); macro series states — fresh/stale/unavailable (TC-013/018); block health — ok/failing (TC-025).
- **Boundary value analysis:** volume threshold at exactly 1,000,000 and just below (TC-003, via LAMDA at 500,000 in TC-002); top-10 cap with 12 candidates (TC-003); macro schedule edges — 09:00 next day, 24h after release (TC-010).
- **Decision tables:** equity × macro health (4 cells — TC-014); series × state {fresh, stale, absent} (TC-013/018); payload stale flag × marker rendering (TC-024); loss-maker exclusion with degenerate all-loss column (TC-004).
- **State transition testing:** macro value → revision appended → canonical latest (TC-007); fresh → stale → recovered (TC-009); snapshot per trading day → latest served (TC-005).
- **Golden values:** TC-MOV-001 is the snapshot contract — every number hand-derived in FU §11.3 from the locked formulas (cap-weighted market P/E, mover eligibility, cap-weighted sector performance).

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** snapshot/block values match FU §11.3 to 6 significant digits; HTTP shapes per `03` §3; envelope flags correct per state; doubles (mail/log) receive specified events; e2e assertions deterministic (network-quiet windows, fixed viewport/locale). **Fail:** any mismatch; flaky = fail.

**Suspension:** Testcontainers/compose unavailable >1 day; fixture universe invalidated by an approved change; the locked sector-performance formula or market-aggregate definitions change (re-derive FU §11.3 first). **Resumption:** blocker fixed + clean full run of the affected group triaged.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-MOV-001 | main | TC-001, 012, 016 | planned |
| FR-MOV-002 | main | TC-001, 012, 016, 019 | planned |
| FR-MOV-003 | main | TC-001, 012, 016 | planned |
| FR-MOV-004 | main | TC-001, 002, 003, 012, 016, 019, 020 | planned |
| FR-MOV-005 | main | TC-001, 012, 016 | planned |
| FR-MOV-006 | main | TC-001, 004, 012, 016, 021 | planned |
| FR-MOV-007 | main | TC-013, 016, 017 | planned |
| FR-MOV-008 | main | TC-013, 017, 018 | planned |
| FR-MOV-009 | main | TC-013, 016, 017 | planned |
| FR-MOV-010 | main | TC-012, 013, 016, 024 | planned |
| FR-MOV-011 | main | TC-019 | planned |
| FR-MOV-012 | main | TC-019 | planned |
| FR-MOV-013 | main | TC-019 | planned |
| FR-MOV-014 | main | TC-006, 007, 008 | planned |
| FR-MOV-015 | main | TC-009, 011 | planned |
| FR-MOV-016 | main | TC-009, 013, 024 | planned |
| FR-MOV-017 | Could | TC-015 (blocked-on-formula F-MOV-1) | flagged — accepted (2026-10-07) |
| FR-MOV-020 | main | TC-016 (+ XC sweep) | planned |
| FR-MOV-021 | main | TC-001, 020 | planned |
| UC-MOV-001 | main; alt a (macro stale); alt b (equity stale) | TC-016; TC-009, 013, 018; TC-014, 024 | planned |
| UC-MOV-002 | main; alt a (indep unreachable); alt b (revision) | TC-006, 007, 008; TC-009; TC-007 | planned |
| UC-MOV-003 | main; alt a (independent unavailable) | TC-017; TC-018, 013 | planned |
| UC-MOV-004 | main; alt a (no stock page) | TC-019; covered by construction — movers are computed over the current universe only (TC-001/002 assert entries are universe stocks) | planned |
| UC-MOV-005 | main; alt a (permanent source death — builder decision) | TC-009, 011, 014; manual ledger | planned |
| NFR-MOV-001 | freshness | TC-001, 005, 009, 010 | planned |
| NFR-MOV-002 | bilingual | TC-023; XC i18n parity + TR default | planned |
| NFR-MOV-003 | public availability | TC-012, 013, 016 (anonymous); 99.0% figure → operational ledger | planned |
| NFR-MOV-004 | plain language | TC-016, 017 (units/asOf presence); builder review ledger | planned |
| NFR-MOV-005 | cost $0 | manual ledger | manual |
| NFR-MOV-006 | regulatory | TC-016; XC disclaimer sweep + advice grep | planned |
| UXR-MOV-001..005 | blocks | TC-016 (+ 001/012 payload) | planned |
| UXR-MOV-006 | exclusion disclosure | TC-021 | planned |
| UXR-MOV-007 | macro strip | TC-016, 017 | planned |
| UXR-MOV-008 | inflation pair | TC-017, 018 | planned |
| UXR-MOV-009 | unavailable state | TC-013, 018 | planned |
| UXR-MOV-010 | as-of + stale | TC-012, 013, 016, 024 | planned |
| UXR-MOV-011 | navigation | TC-019 | planned |
| UXR-MOV-012 | period selector (Could) | TC-015 | flagged — accepted (2026-10-07) |

## 6. Flags and recorded interpretations (for final review)

- **F-MOV-1 (flagged, blocks TC-015 golden):** the 1W/1M/YTD period-aggregate formula is undefined anywhere in the architecture (only "precomputed" is stated). **Recommendation:** cap-weighted total return over the period, mirroring the locked daily formula (Q2 decision); fixture history table + goldens added when the Could feature is promoted. The endpoint contract (param, envelope, 400 on invalid period) is testable now. **ACCEPTED by builder (2026-10-07):** the flag stands — FR-MOV-017 remains Could; TC-MOV-015 stays contract-only (contract tested now, golden deferred to promotion); the recommended formula above is what the plan will adopt when the feature is promoted.
- **I-MOV-1 (interpretation):** equity-stale *rendering* is proven at L3 (marker component, TC-024) + L2 (payload flags, TC-012/014); the e2e stale assertion uses the always-seeded stale GOLD macro block, which exercises the same marker mechanism deterministically.
- **I-MOV-2 (plan-specified expectation):** when every constituent is a loss-maker, market_pe is NULL with excluded_count = universe size (honest unavailable aggregate) — the architecture specifies exclusion and disclosure but not this degenerate; the plan fixes the behavior (TC-004b).
- **I-MOV-3 (interpretation):** UC-MOV-004 alternate a (a linked stock with no stock page) is impossible by construction — movers are computed over the covered universe; asserted as an invariant (entries are universe stocks) rather than a fallback path.

---
*Change record: v1.0 2026-10-06 — initial MOV test plan, batch mode. v1.1 2026-10-07 — builder final review: F-MOV-1 accepted (stays Could; TC-MOV-015 contract-only; golden deferred to promotion); interpretations I-MOV-1..3 upheld; plan approved.*
