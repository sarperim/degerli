# Market Data Foundation Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** v1.14 — §13 implemented in TKT-foundation-061's lane (red→green 6 failed / 1 passed → 7/7) and one superseded 034 detail-404 expectation amended 2026-10-23 · **Status:** v1.13 — §14 share-count store cases (TC-MDF-092/093) added 2026-10-22 (TKT-foundation-058, architecture v1.5.36 §10.9.11; delivery-planner-applied, test-planner confirmation pending) · v1.12 — §13 event-family per-disclosure resilience cases (TC-MDF-094/095) added 2026-10-23 (TKT-foundation-061, architecture v1.5.40 §10.9.15; delivery-planner-applied, test-planner confirmation pending) · **Status:** v1.11 — §12 intraday refresh lane cases (TC-MDF-084/085/086) added 2026-10-22 (TKT-foundation-047, architecture v1.5.27 §10.9.18; delivery-planner-applied, test-planner confirmation pending) · v1.10 — §11 derived-fact-lane case (TC-MDF-088) added 2026-10-22 (TKT-foundation-050, architecture v1.5.34 §10.9.13; delivery-planner-applied, test-planner confirmation pending) · v1.9 — TC-MDF-083's expected result amended to the two-event log shape 2026-10-22 (TKT-foundation-054 / 046 review DR-1; confirmation pending) · v1.8 — §9 chain-orchestration reliability cases (TC-MDF-079/080) added 2026-10-20 (TKT-foundation-041, architecture v1.5.23 §10.9.17; delivery-planner-applied, test-planner confirmation pending) · v1.6 — §9 morning-cadence chain cases (TC-MDF-077/078) added 2026-10-20 (TKT-foundation-040, architecture v1.5.15 §10.9.16; delivery-planner-applied, test-planner confirmation pending) · v1.5 — §8 real-source parsing-fidelity & live-activation cases added 2026-10-17 (builder-directed via the delivery planner; architecture v1.5 §10.9 authority; applied by the test-planner — see change record) · v1.4 §7 post-board correctness cases 2026-10-15 (delivery-planner-applied; test-planner confirmation pending) · v1.3 §14 change-propagation 2026-10-13 (TC-MDF-047/048/049 re-leveled L4→L3, builder-approved; confirmation pending) · v1.2 approved (Gate 2 + builder final review 2026-10-07)
**Derives from:** `analysis/market-data-foundation.md` (FR-MDF-001..019, UC-MDF-001..005, NFR-MDF-001..006) · `ux/market-data-foundation.md` (SCR-012, UXR-MDF-001..028 — shared admin surface only) · `architecture/` v1.2 (`01` C3/C5, `02` §3.1/§6, `03` §8–§9) · `01` **v1.5 §10.9** (real-source envelope contracts → §8) · strategy `00-test-strategy.md` (approved) · fixture contract `01-fixture-universe.md` (submitted with this plan).
**TC ID convention:** `TC-MDF-NNN`, permanent. Levels: L1 unit · L2 integration (Testcontainers Postgres + WireMock sources + fake TimeProvider + mail/Serilog doubles) · L3 web component (Vitest + Testing Library + MSW) · L4 e2e (compose stack + MailPit + Playwright). All fixtures referenced as `FU §n`.

## 1. Scope

**Tested here:** FR-MDF-001..016, 018, 019 (18 non-Won't); UC-MDF-001..005 including every alternate/error flow; NFR-MDF-001..006 (005 partially via the XC reproducibility case, 006 via the manual ledger); SCR-012 shared admin infrastructure: UXR-MDF-001..011, 013..017, 026..028 (19 of the 28 admin UXRs).

**Not tested here (and why):** FR-MDF-017 (backtesting — Won't); the admin description queue/editor/publish UXRs (018..022 → RES plan), metric-visibility UXRs (023/024 → SCR plan), baseline regeneration (025 → VAL plan), fund coverage rows (012 → FDF plan); **live network pulls against production source endpoints** (operational — the real envelopes' *parsing* IS tested here, via captured raw bytes served through the WireMock doubles: §8; the live-pull observations are explicit ticket criteria, §8 preamble), cost NFRs, and the 23:59 real-data completion budget (operational — §4); reproducibility quickstart (TC-XC, cross-referenced).

## 2. Test Case Specification

### Group A — Ingestion & fact storage (FR-MDF-001..008)

**TC-MDF-001 — EOD price ingest stores dated, provenanced rows**
- Traces: FR-MDF-001, UC-MDF-001 step 1 · Level: L2 · Design: EP — valid payload class
- Preconditions: migrated container, seed applied; WireMock serves `prices-ok.json` for day T.
- Steps: run the `prices` job for T.
- Inputs/Expected: 16 `daily_prices` rows matching FU §4 exactly (close, volume per instrument); `source_ref` + `recorded_at` NOT NULL and populated; one row per instrument (PK instrument+date).
- Dependencies: —

**TC-MDF-002 — Price ingest is idempotent**
- Traces: FR-MDF-001, `01` §7 mechanism · Level: L2 · Design: EP — repeated identical input class
- Preconditions: TC-MDF-001 state (or re-seeded equivalent — each test re-creates state; no inter-test reads).
- Steps: run the `prices` job for T a second time with the identical payload.
- Expected: row count unchanged (16); values identical; no duplicates, no ledger anomaly.
- Dependencies: —

**TC-MDF-003 — Statement ingest with canonical mapping and versioned uniqueness**
- Traces: FR-MDF-002, BR-MDF-004 · Level: L2 · Design: EP — valid statement class
- Steps: run `statements` with `statements-ok-alfa-fy2025.json` (FY + 4 quarters).
- Expected: `financial_statements` rows for FY2025 + Q1..Q4 (period_type Q/FY, statement_type IS/BS/CF, version `as_reported`); `fin_line_items` mapped to canonical codes (§6.1) incl. `GROSS_PROFIT` derived when not reported; UNIQUE (instrument, period_type, period_end_date, statement_type, version) respected on re-run (no duplicates); `OTHER_*` passthrough items stored and ignored by metrics.
- Dependencies: —

**TC-MDF-004 — Dividend ingest**
- Traces: FR-MDF-003 · Level: L2 · Design: EP — valid dividend class
- Steps: run `dividends` with `dividends-ok.json`.
- Expected: ALFA's 4 rows (ex_date, pay_date, amount_per_share 0.25, currency TRY) + other stocks' rows per FU §6; LAMDA has zero rows.
- Dependencies: —

**TC-MDF-005 — Corporate action ingest**
- Traces: FR-MDF-004 · Level: L2 · Design: EP — valid action class
- Steps: run `corporate-actions` with `corporate-actions-ok.json`.
- Expected: EPSL rows: split (action_type split, action_date 2025-06-02, terms_json ratio 1:5) and bonus (2026-03-02, ratio 1:10); provenance populated.
- Dependencies: —

**TC-MDF-006 — KAP disclosure ingest**
- Traces: FR-MDF-005 · Level: L2 · Design: EP — valid disclosure class
- Steps: run `disclosures` with `disclosures-ok.json`.
- Expected: `kap_disclosures` rows with disclosure_type, publish_date, title, source_url, document_path (file exists on the fixture volume); metadata only — documents outside DB.
- Dependencies: —

**TC-MDF-007 — Index levels and sector sync**
- Traces: FR-MDF-006 · Level: L2 · Design: EP — valid class
- Steps: run `universe-sync` + index adapter with `index-levels-ok.json`.
- Expected: `index_levels` rows XU100 10200.00 / XU30 30600.00 for T (FU §4); `sectors` rows A/B/C with `name_tr`/`name_en` bilingual labels; instruments linked per FU §3.
- Dependencies: —

**TC-MDF-008 — Facts without provenance are refused**
- Traces: FR-MDF-008, NFR-MDF-004, UC-MDF-001 step 3 · Level: L2 · Design: EP — invalid class (missing provenance)
- Steps: run `prices` with `prices-missing-provenance.json`.
- Expected: zero rows written; `quarantined_facts` row with reason_code `MISSING_PROVENANCE`, payload_json retained, job_code `prices`.
- Dependencies: —

**TC-MDF-009 — Schema enforces the data-model invariants**
- Traces: FR-MDF-008, NFR-MDF-002/004, `02` §3 constraints · Level: L2 · Design: state/structure conformance (backstop; primary tests are behavioral)
- Steps: inspect migrated schema.
- Expected: `source_ref`/`recorded_at` NOT NULL on every fact table; UNIQUEs per `02` (`financial_statements` 5-col, `daily_prices` PK, `saved_screens` (user,name), `dcf_scenarios` (user,instrument,name)); `business_descriptions` CHECK status='published' ⇒ text_tr AND text_en NOT NULL; no DELETE/TTL artifacts on fact tables.
- Dependencies: —

### Group B — Scheduler & freshness (FR-MDF-009, FR-MDF-016, NFR-MDF-001, UC-MDF-001)

**TC-MDF-010 — EOD chain triggers on schedule and runs in order**
- Traces: FR-MDF-009, UC-MDF-001 main, NFR-MDF-001, `03` §9 chain · Level: L2 · Design: state transition — scheduled-idle → running → succeeded
- Preconditions: fake TimeProvider at R 20:29 TRT (Monday 2026-10-05).
- Steps: advance clock to 20:30 TRT.
- Expected: chain runs in order prices → statements/dividends/corporate-actions/disclosures → metrics-recompute → snapshot → medians; `ingest_runs` rows per job with status `succeeded`, `started_at`/`finished_at`, `stats_json` counts; all artifacts exist for T (prices, metrics, snapshot, medians).
- Dependencies: —

**TC-MDF-011 — No trigger on non-trading days**
- Traces: FR-MDF-009 · Level: L2 · Design: BVA — schedule boundary (Sat/Sun outside trading-day set; holiday calendar is config, recorded interpretation)
- Steps: advance fake clock across Saturday and Sunday.
- Expected: zero new `ingest_runs` rows; Monday 20:30 triggers normally.
- Dependencies: —

**TC-MDF-012 — Source failure retries per the 5/15/60 ladder**
- Traces: UC-MDF-001 alternate a, NFR-MDF-001 · Level: L2 · Design: BVA — retry timing edges (+5, +15, +60)
- Preconditions: WireMock `prices-source-down` (500 forever).
- Steps: trigger at 20:30; advance clock; let retry 2 succeed (switch stub mid-test).
- Expected: retry attempts at +5 min and +15 min recorded; on retry-2 success run status `succeeded` with attempt count in ledger; no duplicate rows from partial attempts.
- Dependencies: —

**TC-MDF-013 — Exhausted retries: alert, last-known-good, stale marking**
- Traces: UC-MDF-001 alternate a, FR-MDF-012, FR-MDF-016, NFR-MDF-001 · Level: L2 · Design: state transition — running → failed → stale-served
- Preconditions: `prices-source-down` for all 3 retries.
- Steps: exhaust retries; then GET `/api/v1/market/overview`.
- Expected: run status `failed`; alert e-mail dispatched to the builder address (mail double) + Serilog alert event (test sink); T−1 data still served; payload `stale: true` with `asOf` = T−1 (FR-MDF-016 mechanism, observed on a public endpoint); `v_data_freshness`/admin summary show old last-success for `prices`.
- Dependencies: —

**TC-MDF-014 — Recovery clears staleness**
- Traces: FR-MDF-016 · Level: L2 · Design: state transition — stale → fresh
- Steps: after TC-MDF-013's state is re-created, restore the source stub and run the next scheduled cycle.
- Expected: T data served; `stale: false`; freshness view shows new last-success.
- Dependencies: —

### Group C — Backfill & coverage (FR-MDF-010, FR-MDF-011, UC-MDF-002, UC-MDF-004, NFR-MDF-003)

**TC-MDF-015 — Admin-triggered backfill ingests a historical range and records depth**
- Traces: FR-MDF-010, UC-MDF-002 main · Level: L2 · Design: EP — valid backfill request
- Steps: POST `/api/v1/admin/ingest/prices/run` `{backfillFrom: "2016-01-01"}` as builder.
- Expected: run ledger row for the backfill; historical rows ingested per canned range; `coverage_metadata` updated — `available_from` = earliest ingested date per instrument.
- Dependencies: —

**TC-MDF-016 — Source-limited history is recorded, not fabricated**
- Traces: UC-MDF-002 alternate a, BR-MDF-006 · Level: L2 · Design: EP — source-limited class
- Preconditions: stub serves history only from 2021-01-01.
- Steps: backfill with target 10Y.
- Expected: achieved depth recorded as 2021-01-01 (the actual limit); no rows fabricated before it; coverage note records the limitation.
- Dependencies: —

**TC-MDF-017 — Coverage report endpoint exposes per-instrument coverage and gaps**
- Traces: FR-MDF-011, UC-MDF-004, UXR-MDF-011 · Level: L2 · Design: EP — covered / gap / partial classes
- Steps: GET `/api/v1/admin/coverage?scope=instrument` as builder.
- Expected: per-instrument data types with `availableFrom`, listing/IPO date per FU §3; PART shows the missing CF statement as an explicit gap; LAMDA shows no-dividend; every universe instrument present (gaps shown, never omitted).
- Dependencies: —

**TC-MDF-018 — No silent gaps invariant**
- Traces: NFR-MDF-003 · Level: L2 · Design: decision table — data present XOR gap recorded, over all instruments × data types
- Steps: consistency query over the seeded universe.
- Expected: the set (instrument × data_type) lacking both data and an explicit coverage/gap record is **empty**.
- Dependencies: —

### Group D — Validation & quarantine (FR-MDF-012, UC-MDF-001 alt b, UC-MDF-003 alt a)

**TC-MDF-019 — Invalid facts are quarantined, never written**
- Traces: FR-MDF-012, UC-MDF-001 alternate b, `02` §3.1 `quarantined_facts` · Level: L2 · Design: decision table — invalid classes × (quarantine, write, drop)
- Steps: run `prices` with `prices-invalid-negative-close.json` (ALFA close −5), `prices-unparseable.json`, and a statement payload with a non-numeric value.
- Expected: `quarantined_facts` rows with reason codes `NEGATIVE_PRICE`, `UNPARSEABLE_PAYLOAD`, `SCHEMA_MISMATCH` respectively; payload_json retained per item; **zero** rows written to the fact tables for those items; valid items in the same payload ingest normally (partial-batch behavior).
- Dependencies: —

**TC-MDF-020 — Quarantine alerts the builder**
- Traces: FR-MDF-012 · Level: L2 · Design: EP — alert-on-quarantine class
- Expected after TC-MDF-019's setup: alert e-mail (mail double) + Serilog alert event naming the job and reason.
- Dependencies: —

**TC-MDF-021 — Quarantine queue payload serves review fields**
- Traces: UXR-MDF-007, FR-MDF-012 · Level: L2 · Design: EP — open-queue class
- Steps: GET `/api/v1/admin/quarantine?status=open` as builder.
- Expected: items with job_code, source_ref, reason_code, quarantined_at, payload_json (inspectable).
- Dependencies: —

**TC-MDF-022 — Dismissal requires a note and records the accepted gap**
- Traces: UXR-MDF-008, BR-MDF-007, UC-MDF-001 alternate b · Level: L2 · Design: decision table — note empty/non-empty × accept/reject
- Steps: POST `/api/v1/admin/quarantine/{id}/dismiss` `{note: "source error accepted"}`; then with `{note: ""}`.
- Expected: valid note → status `dismissed`, `resolution_note` stored; empty note → `400 VALIDATION_FAILED` (field detail `note`); nothing deleted either way.
- Dependencies: —

**TC-MDF-023 — Dismissed items remain reviewable**
- Traces: UXR-MDF-009, BR-MDF-007 · Level: L2 · Design: state transition — open → dismissed (terminal, retained)
- Steps: GET `/api/v1/admin/quarantine?status=dismissed`.
- Expected: dismissed items with their resolution notes and original payloads.
- Dependencies: —

**TC-MDF-024 — Quarantine → run ledger → re-ingestion path**
- Traces: UXR-MDF-010, UC-MDF-001 alternate b (re-ingestion per `03` §8) · Level: L2 · Design: state transition — fixed-source → re-trigger → new run
- Steps: GET `/api/v1/admin/ingest-runs?job=prices` (filtered view for the quarantined item's job); re-trigger the job after restoring the stub.
- Expected: filtered ledger returns the failed runs; re-trigger creates a new run that succeeds; quarantine item remains until dismissed (re-ingestion does not auto-dismiss).
- Dependencies: —

**TC-MDF-025 — Missing sector classification is flagged, not dropped**
- Traces: UC-MDF-003 alternate a, FR-MDF-012 · Level: L2 · Design: EP — missing-classification class
- Steps: run `universe-sync` with `universe-missing-sector.json`.
- Expected: instrument stored with `sector_id` NULL; anomaly flagged — alert (mail double + log) with reason `MISSING_CLASSIFICATION`; instrument still served (stock list shows unclassified — asserted in the RES plan).
- Dependencies: —

**TC-MDF-052 — Conflicting price re-publication is quarantined, never overwritten**
- Traces: FR-MDF-015, FR-MDF-012, BR-MDF-004, UC-MDF-001 alternate b (conflicting-data class) · Level: L2 · Design: EP — conflicting-valid-value class (decision Q1, 2026-10-06)
- Preconditions: T prices already ingested (ALFA close 20.00 stored).
- Steps: re-run the `prices` job for T with `prices-conflicting-value.json` (ALFA close 21.00 — a *valid* value conflicting with the stored fact).
- Expected: stored row unchanged (ALFA T close stays 20.00); `quarantined_facts` row with reason_code `CONFLICTING_VALUE` and the payload retained; alert raised (mail double + log); other instruments' identical values in the same payload remain idempotent no-ops (no quarantine, no change).
- Dependencies: —

**TC-MDF-053 — Rights-issue adjustment factor (v1.2; Q3 resolution 2026-10-07)**
- Traces: FR-MDF-018, BR-MDF-010 (adjustment-factor system), FU §2/§11.6 · Level: L1 · Design: golden values + BVA — rights class, composition, P_S = P_C edge, P_S = 0 (bonus-reduction) edge, degenerate-input column
- Preconditions: the pure adjustment function over an action record `{type: rights, terms: {q, subscriptionPrice}}` and a cum price P_C.
- Steps/Expected: (a) clean class — q = 0.25, P_S = 10.00, P_C = 20.00 → factor **0.90** (TERP 18.00 / P_C 20.00); (b) composition — rights 0.90 after a split 1:5 (0.2) → **0.180** (multiplicative rule unchanged); (c) edge — P_S = P_C → factor **1** (no adjustment, price at TERP); (d) edge — P_S = 0 → **0.80**, equal to the bonus factor 1/(1+q); (e) degenerate — q ≤ 0 or P_C ≤ 0 → the action is **invalid** (rejected by action validation, never a factor; no division by zero).
- Dependencies: —



### Group E — Metrics engine (FR-MDF-013, 014, 018, 019; UC-MDF-005; NFR-MDF-002)

**TC-MDF-026 — 18 canonical metric golden values on ALFA**
- Traces: FR-MDF-013, BR-MDF-009, UC-MDF-005 main · Level: L2 · Design: golden values (EP — canonical valid class; the single-definition contract)
- Preconditions: seeded ALFA facts (FU §5) + prices for T; metrics-recompute run.
- Expected: `derived_metrics` current values for ALFA equal FU §11.1 exactly (each of the 18 concepts; growth concepts per window) to 6 significant digits; `as_of_date` = T; one row per (instrument, metric, date).
- Dependencies: —

**TC-MDF-027 — CAGR function boundaries (unit)**
- Traces: FR-MDF-014 math · Level: L1 · Design: BVA — series length edges (0, 1, 2 points; single interval; multi-decade) + negative-result class
- Expected on synthetic series: 2 points → (v2/v1)−1 over 1 interval; n points → (last/first)^(1/(n−1))−1; 0–1 points → NULL; declining series → negative CAGR (not NULL); precision 1e-9 relative.
- Dependencies: —

**TC-MDF-028 — Effective tax rate fallback**
- Traces: `02` §6.2 roic note · Level: L1 · Design: decision table — PRETAX > 0 / ≤ 0
- Expected: t_eff = TAX/PRETAX when PRETAX > 0; fallback 0.25 when PRETAX ≤ 0 or TAX missing.
- Dependencies: —

**TC-MDF-029 — Adjustment factors (split, bonus, composition)**
- Traces: FR-MDF-018 basis, BR-MDF-010 · Level: L1 · Design: EP — action classes × factor rules
- Expected: split 1:5 → 0.2; bonus 1:10 → 10/11 = 0.909091; factors compose multiplicatively (both → 0.2 × 10/11); no actions → factor 1. *(Rights-issue factor resolved 2026-10-07 — see TC-MDF-053 and FU §2/§11.6.)*
- Dependencies: —

**TC-MDF-030 — Raw vs adjusted series diverge correctly around actions**
- Traces: FR-MDF-018, BR-MDF-010 · Level: L2 · Design: BVA — price rows before/between/after action dates
- Steps: request EPSL's historical price series (via the valuation endpoint contract — `adjusted` flag).
- Expected: FU §4 EPSL rows exactly — 2025-05-30 raw 100.00 / adjusted 18.1818; 2026-02-27 raw 22.00 / adjusted 20.0000; 2026-06-02 and T raw = adjusted; historical payload carries `adjusted: true`; current display uses `close_raw` (20.00).
- Dependencies: —

**TC-MDF-031 — Not-meaningful rules produce NULL, never zero**
- Traces: FR-MDF-013 (`02` §6.2 rules), UC-MDF-005 · Level: L2 · Design: decision table — one column per archetype fixture (FU §11.2)
- Expected: per FU §11.2 — BETA/GAMA: pe, payout NULL; DELTA: pb, roe, roic NULL; IOTA: ev_ebitda, net_debt_ebitda NULL; KAPPA: ev_fcf, fcf_yield, fcf_cagr NULL; PART: FCF metrics NULL; LAMDA: div metrics NULL; NEWP: all CAGRs NULL; NU: payout = 1.5 (honest >1); ZETA: computable with window_years 1. No NULL is stored or served as 0; screener exclusions of NULL stocks are counted (cross-tested in SCR plan).
- Dependencies: —

**TC-MDF-032 — Restatement: latest restated serves, marked, as-reported retained**
- Traces: FR-MDF-019, BR-MDF-011, `02` §5.7 · Level: L2 · Design: state transition — as_reported-only → restated-arrives (both retained)
- Preconditions: REST with `statements-restated-rest.json` ingested.
- Expected: current metrics computed from restated NI 75 → pe = 10.0 (not 12.5); `derived_metrics.is_rested = true` for those rows; public payload figures carry `restated: true`; both statement versions remain stored and as-of queryable (`version` filter); restate date 2026-06-20 recorded.
- Dependencies: —

**TC-MDF-033 — CAGR from available history with actual window**
- Traces: FR-MDF-014, UC-MDF-005 (short-window flow) · Level: L2 · Design: BVA — FY-count edges (1, 2, 3, 11 FYs)
- Expected: ZETA (2 FYs): rev_cagr any window = 0.25, `window_years = 1` (available-history rule per FU §2); NEWP (1 FY): all CAGRs NULL + no-data state with coverage boundary naming listing date 2025-08-01; ALFA (11 FYs): full windows 3/5/10 with window_years 3/5/10. *(Reading confirmed as flag Q4: compute from available history whenever ≥2 FYs; the UX "no-data/shorter-history state" is the actual-window indication, not a refusal to compute.)*
- Dependencies: —

**TC-MDF-034 — Metrics recompute is idempotent and append-only**
- Traces: FR-MDF-013 mechanics, FR-MDF-015, NFR-MDF-002 · Level: L2 · Design: EP — repeated compute; retention
- Steps: run `metrics-recompute` for T twice.
- Expected: no duplicate rows — idempotent by `(instrument, metric, as_of_date)`; metric codes are window-suffixed (Q5 resolved 2026-10-07), so the plain key already distinguishes windows; row count = universe × metric codes × trading days seeded; prior dates' rows untouched.
- Dependencies: —

**TC-MDF-035 — Facts are never overwritten; as-of retrieval works**
- Traces: FR-MDF-015, BR-MDF-004, `02` §5.6/5.7 · Level: L2 · Design: state transition — fact exists → correction/revision arrives
- Steps: (a) ingest TUIK_CPI revision rows per FU §7; (b) request the canonical value for 2026-08-01; (c) attempt as-of retrieval of the earlier recorded_at row.
- Expected: canonical = latest `recorded_at` (45.00, not 44.80); both rows retained; statement as-reported rows unchanged after restatement (also TC-032); UPDATE/DELETE on fact rows is not exposed by any platform path (schema backstop TC-009).
- Dependencies: —

**TC-MDF-036 — Downstream serving: canonical values or honest no-data**
- Traces: UC-MDF-005 main + alternate a · Level: L2 · Design: decision table — data present / short history / absent
- Expected: request for ALFA metrics → canonical values with asOf (TC-026); NEWP growth → NULL + explicit no-data with coverage boundary (never fabricated, never blank-as-zero — the 200-with-`state` envelope per `03` §1.2).
- Dependencies: —

### Group F — Universe & membership (FR-MDF-006, FR-MDF-007, UC-MDF-003)

**TC-MDF-037 — Constituent changes are effective-dated and history-preserving**
- Traces: FR-MDF-007, UC-MDF-003 main, BR-MDF-005 · Level: L2 · Design: state transition — member(active) → removed → re-added
- Steps: run `universe-sync` with `universe-add-remove.json` (NEWP removed effective T+1; a new instrument "SIGMA" added effective T+1); then re-add NEWP at T+5.
- Expected: `v_current_universe` excludes NEWP and includes SIGMA after T+1; querying membership as of T (past) still includes NEWP; no membership row is deleted; the re-add creates a **new** row with a new effective_from (the closed interval is never reopened). *(Mechanism for closing effective_to is the implementation's choice — the observable history contract is what this asserts; see flag Q6.)*
- Dependencies: —

**TC-MDF-038 — Sector classification stays current per instrument**
- Traces: FR-MDF-006, UC-MDF-003 step 3 · Level: L2 · Design: EP — reclassification class
- Expected: a canned reclassification moves the instrument's sector link; bilingual labels intact; stock list and medians reflect it on next compute (cross-refs RES).
- Dependencies: —

### Group G — SCR-012 shared admin surface (UXR-MDF-001..011, 013..017, 026..028)

**TC-MDF-039 — Admin role gating (API)**
- Traces: UXR-MDF-001/002 (API side), `03` §8, `01` §10.1 · Level: L2 · Design: decision table — anonymous / user / builder × admin endpoints
- Steps: call `GET /api/v1/admin/summary` (and one representative unsafe admin call) as anonymous, as user-a, as builder.
- Expected: anonymous → 401 `UNAUTHENTICATED`; user-a → 403 `FORBIDDEN`; builder → 200. Unsafe methods without CSRF token → 400 (cross-tested in XC).
- Dependencies: —

**TC-MDF-040 — Ops summary payload**
- Traces: UXR-MDF-003/004/005/006, FR-MDF-016 display side, `03` §8 sketch · Level: L2 · Design: EP — summary classes (jobs/freshness/coverage/quarantine)
- Steps: GET `/api/v1/admin/summary` as builder with the seeded state incl. GOLD staleness.
- Expected: shape per `03` §8 sketch — `jobs[].lastRun.{status,finishedAt}`; `freshness[]` with GOLD `stale: true` and its T−10 last-success; `contentCoverage` counts match seed (descriptionsPublished/draft/universe per RES fixtures); `openQuarantineCount` matches seeded quarantine rows.
- Dependencies: —

**TC-MDF-041 — Run ledger is filterable**
- Traces: UXR-MDF-014 · Level: L2 · Design: decision table — job filter × status filter
- Steps: GET `/api/v1/admin/ingest-runs?job=prices`, `?status=failed`, `?job=prices&status=succeeded`.
- Expected: rows carry job, status, timing; filters apply conjunctively; unfiltered returns all.
- Dependencies: —

**TC-MDF-042 — Job trigger (valid and unknown codes)**
- Traces: UXR-MDF-016 (API side), UC-MDF-002 · Level: L2 · Design: EP — known/unknown job code
- Steps: POST `/api/v1/admin/ingest/metrics-recompute/run` (no body) as builder; POST `/api/v1/admin/ingest/not-a-job/run`.
- Expected: valid code → run created and visible in the ledger (`202`/`200` with run reference); unknown code → `404 NOT_FOUND` *(recorded interpretation — job code is a path id; per the `03` §7 catalog)*.
- Dependencies: —

**TC-MDF-043 — Backfill trigger carries the start date**
- Traces: UXR-MDF-017, UC-MDF-002 · Level: L2 · Design: EP — backfill body present/absent
- Expected: POST with `{backfillFrom}` → the run's ledger entry records the backfill mode + start date; without it → normal incremental run.
- Dependencies: —

**TC-MDF-044 — Aggregate-only stats**
- Traces: UXR-MDF-026, SC-008/OBJ-005 measurement, BR-ACC-002/008 posture · Level: L2 · Design: EP — aggregate shape; negative: no per-user surface
- Steps: GET `/api/v1/admin/stats` as builder.
- Expected: exactly `{accounts: {registered, verified}, savedScreens, dcfScenarios}` with values matching seed; no per-user/per-account fields anywhere in the payload.
- Dependencies: —

**TC-MDF-045 — Admin entry visibility and access-denied state**
- Traces: UXR-MDF-001/002 · Level: L4 · Design: decision table — builder/user/anonymous × nav entry × direct URL
- Steps: as builder — admin entry visible in global nav; as user-a — entry absent, direct `/admin` URL → access-denied state; as anonymous — same denied state.
- Expected: builder-only nav entry; denied state is plain-language with a path back to platform content (never blank, never a raw error).
- Dependencies: —

**TC-MDF-046 — Ops summary renders with staleness**
- Traces: UXR-MDF-003/004/006 · Level: L4 · Design: EP — populated + stale classes
- Expected: per-job last run rendered with status and completion time; GOLD data type shows its last-success date with a visible stale indication; open-quarantine count links into the queue.
- Dependencies: —

**TC-MDF-047 — Quarantine review flow (browser)**
- Traces: UXR-MDF-007/008/009/010 · Level: L3 (re-leveled from L4 — §14 change record v1.3) · Design: state transition — open → (invalid dismiss) → open → (valid dismiss) → dismissed; nav to ledger
- Steps: open queue → inspect an item's payload → attempt dismiss with empty note (inline validation blocks) → dismiss with a note → view dismissed filter → navigate from an item to its job's run-ledger view.
- Expected: empty note blocked inline with visible rule; dismissal moves the item and updates the summary's open count without manual reload; dismissed item reviewable with note; ledger view arrives filtered to that job.
- Re-level justification (v1.3, builder-approved 2026-10-13): compose.ci seeds no open quarantined fact — `fixtures/` seeds none (only a catalog doc comment in `CannedSourceCatalog.cs`) — so the L4 queue cannot be exercised against the seeded stack. Covered at L3 (Vitest + MSW) per the TKT-mdf-012 review C1 escalation, option A.
- Dependencies: —

**TC-MDF-048 — Run-ledger auto-refresh during an in-progress run**
- Traces: UXR-MDF-015/028 (flag ruling 4), mutation matrix row 12 · Level: L3 (re-leveled from L4 — §14 change record v1.3) · Design: state transition — idle → running (slow source) → updates visible
- Preconditions: WireMock serves `prices-source-slow.json` (5 s delay) to the compose stack.
- Steps: trigger the `prices` job from the admin UI (confirmation names the job); observe the open ledger view.
- Expected: the triggered run appears in the ledger without manual refresh; while non-terminal the view updates periodically with a visible periodic-update indication; the manual refresh control is present and works; on completion the summary reflects it on next load (not auto-refreshed — ops summary deliberately doesn't).
- Re-level justification (v1.3, builder-approved 2026-10-13): the recorded harness limitation makes L4 infeasible — no slow source (`prices-source-slow`) is wired into `compose.ci.yml` (it exists only in plan/fixture docs), and the admin trigger endpoint is synchronous (`AdminOpsEndpoints.cs` `TriggerAsync` awaits `runner.RunAsync(...)` and returns 200), so a run is never observable as non-terminal in the browser. The L4 precondition above is therefore unsatisfiable in the current harness; the in-progress window is exercised at L3 (Vitest + MSW) instead, per the TKT-mdf-012 review C1 escalation, option A.
- Dependencies: —

**TC-MDF-049 — Job trigger and backfill confirmations**
- Traces: UXR-MDF-016/017, UXR-G-022 pattern · Level: L3 (re-leveled from L4 — §14 change record v1.3) · Design: decision table — confirm/cancel × backfill on/off
- Expected: trigger requires an explicit confirmation naming the job; backfill mode confirmation names the job **and** the start date; cancel path aborts with no run created; pending lock on the trigger (UXR-G-002/020).
- Re-level justification (v1.3, builder-approved 2026-10-13): re-leveled together with TC-MDF-047/048 as one §14 decision — the run a confirmed trigger creates is only observable at L3 in this harness (the trigger endpoint is synchronous; no non-terminal run, no slow source in compose.ci), so keeping 049 alone at L4 would split one flow across two levels for no incremental coverage. Covered at L3 (Vitest + MSW) per the TKT-mdf-012 review C1 escalation, option A (which also subsumes the C2 non-disclosure finding).
- Dependencies: —

**TC-MDF-050 — Admin empty states**
- Traces: UXR-MDF-027 · Level: L4 · Design: EP — legitimate-empty classes
- Expected: no open quarantined facts (healthy steady state) → explicit empty state naming the condition; run-ledger filter matching nothing → empty state; each names its condition (never blank).
- Dependencies: —

**TC-MDF-051 — Coverage report and scope switch**
- Traces: UXR-MDF-011/013 · Level: L4 · Design: EP — instrument scope / fund scope
- Expected: instrument scope shows per-instrument data types, from-when, listing dates, explicit gaps (PART's CF gap visible); switching to fund scope shows fund rows (content asserted in the FDF plan); scope choice preserved across in-session navigation (UXR-G-021).
- Dependencies: —

### Group H — Cross-references (no local TCs)

- NFR-MDF-005 reproducibility → **TC-XC** (compose-up smoke) in the cross-cutting plan.
- NFR-MDF-006 ($0 cost) → manual/operational ledger entry (strategy §9.4).
- UC-MDF-004 go/no-go decisions → manual record (coverage endpoints tested in TC-017/018).
- NFR-MDF-001's 23:59 real-data completion → operational (run-ledger monitoring); fixture-scale completion asserted in TC-010.

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** source payloads split into valid / invalid-type / missing-provenance / unparseable / source-down / source-slow classes (TC-001..008, 019, TC-012/013/048); admin callers split into anonymous / user / builder (TC-039, 045); job codes into known/unknown (TC-042); backfill bodies present/absent (TC-043).
- **Boundary value analysis:** schedule edges — 20:29 vs 20:30 trigger, weekend non-trigger (TC-010/011); retry ladder +5/+15/+60 (TC-012); CAGR series length 0/1/2/n points and window edges 1/2/3 intervals (TC-027/033); action-date boundaries — price rows immediately before/between/after corporate actions (TC-030); rights edges — P_S = P_C and P_S = 0, plus the degenerate q ≤ 0 / P_C ≤ 0 column (TC-053).
- **Decision tables:** (1) the 18-metric not-meaningful table (`02` §6.2) — conditions NI≤0, equity≤0, EBITDA≤0, FCF≤0, IC≤0, FIN_EXP≤0, <2 FYs, FCF sign change, never-paid → one case per column, realized as the nine archetype fixtures (TC-031, FU §11.2); (2) dismissal note empty/non-empty × accept/reject (TC-022); (3) admin role × endpoint class (TC-039/045); (4) ledger job×status filters (TC-041); (5) t_eff PRETAX>0/≤0 (TC-028); (6) coverage data-present/gap-recorded (TC-018).
- **State transition testing:** run lifecycle idle→running→succeeded/failed with retries (TC-010/012/013); freshness fresh→stale→fresh (TC-013/014); quarantine open→(rejected)→open→dismissed-retained (TC-022/023/047); statement as_reported-only→restated-added, both retained (TC-032); macro revision append + canonical selection (TC-035); membership active→removed→re-added with history preserved (TC-037); ledger idle→running→auto-refreshing→complete (TC-048).

**Golden values** (TC-026, 030, 031, 032, 033, 053) are the determinism core: every expected number is hand-derived in `01-fixture-universe.md` §11 from the `02` §6 formulas.

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** every expected result observed on a clean run — DB row values to the stated precision (6 significant digits for computed metrics), HTTP codes per the `03` §7 catalog, e-mail/log doubles receiving the specified events. **Fail:** any mismatch; flaky counts as fail (fix at source, no retry-masking). Golden-value mismatches after an *intentional* formula change require a §14 change record and re-approval before the plan is edited.

**Suspension (this domain):** Docker/Testcontainers unavailable >1 day; migrations fail (blocks all L2+); the fixture universe is invalidated by an approved upstream change and re-derivation is pending; CI minutes exhausted. **Resumption:** blocker fixed + one full clean run of the affected group triaged to zero unexplained failures.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-MDF-001 | main | TC-001, 002, 064, 068, 069, 070, 072, 073, 074, 075, 084, 086 | planned |
| FR-MDF-002 | main | TC-003, 065, 067, 071, 092 | planned |
| FR-MDF-003 | main | TC-004, 092 | planned |
| FR-MDF-004 | main | TC-005 | planned |
| FR-MDF-005 | main | TC-006 | planned |
| FR-MDF-006 | main | TC-007, 038, 075 | planned |
| FR-MDF-007 | main | TC-037, 075, 076 | planned |
| FR-MDF-008 | main | TC-008, 009, 063 | planned |
| FR-MDF-009 | main | TC-010, 011 | planned |
| FR-MDF-010 | main | TC-015, 016, 043, 068 | planned |
| FR-MDF-011 | main | TC-017, 018, 076, 093 (public side → RES plan) | planned |
| FR-MDF-012 | main | TC-019, 020, 021, 025, 052, 071, 073 | planned |
| FR-MDF-013 | main | TC-026, 031, 034 | planned |
| FR-MDF-014 | main, short-history | TC-027, 033 | planned |
| FR-MDF-015 | main | TC-035, 052 (backstop 009, 034) | planned |
| FR-MDF-016 | main | TC-013, 014, 040, 046 | planned |
| FR-MDF-018 | main | TC-029, 030, 053 | planned |
| FR-MDF-019 | main | TC-032 | planned |
| UC-MDF-001 | main; alt a; alt b | TC-010; TC-012, 013, 070; TC-019, 020, 024, 052, 071, 073 | planned |
| UC-MDF-002 | main; alt a | TC-015, 043, 068; TC-016 | planned |
| UC-MDF-003 | main; alt a | TC-037, 038, 076; TC-025 | planned |
| UC-MDF-004 | main (report); decisions manual | TC-017, 018, 040, 051; ledger entry | planned |
| UC-MDF-005 | main; short-window; alt a | TC-026, 036; TC-033; TC-036 | planned |
| NFR-MDF-001 | fixture-scale timing; real-scale operational | TC-010, 012, 013; ledger entry | planned |
| NFR-MDF-002 | retention/append-only | TC-009, 032, 034, 035 | planned |
| NFR-MDF-003 | coverage transparency | TC-017, 018 | planned |
| NFR-MDF-004 | provenance completeness | TC-008, 009 | planned |
| NFR-MDF-005 | reproducibility | TC-XC (cross-cutting plan) | cross-ref |
| NFR-MDF-006 | cost $0 | manual ledger entry | manual |
| UXR-MDF-001/002 | role gating | TC-039, 045 | planned |
| UXR-MDF-003/004/005/006 | summary | TC-040, 046 | planned |
| UXR-MDF-007/008/009/010 | quarantine queue | TC-021, 022, 023, 024, 047 | planned |
| UXR-MDF-011/013 | coverage report | TC-017, 051 | planned |
| UXR-MDF-012 | fund scope rows | FDF plan | cross-ref |
| UXR-MDF-014/015/028 | run ledger | TC-041, 048 | planned |
| UXR-MDF-016/017 | job triggers | TC-042, 043, 049 | planned |
| UXR-MDF-018..022 | descriptions | RES plan | cross-ref |
| UXR-MDF-023/024 | metric visibility | SCR plan | cross-ref |
| UXR-MDF-025 | baselines | VAL plan | cross-ref |
| UXR-MDF-026 | stats | TC-044 | planned |
| UXR-MDF-027 | admin empty states | TC-050 | planned |
| FR-MOV-001/002/003, FR-RES-005..018 (price-state contract) | overview + stock page | TC-085 (MOV/RES plans cross-ref) | planned |

## 6. Decisions record (builder rulings, 2026-10-06) and deferred items

- **Q1 — Conflicting price re-publication → RESOLVED (adopted recommendation):** a valid value conflicting with an already-stored (instrument, date) fact is quarantined with reason `CONFLICTING_VALUE` + alert; identical re-sends remain idempotent no-ops. Test case: **TC-MDF-052**.
- **Q2 — Sector daily performance formula → SET (test-planner, per builder):** cap-weighted average of members' daily % changes. Formula and goldens locked in `01-fixture-universe.md` §2/§11.3; asserted in the MOV plan.
- **Q3 — Rights-issue adjustment factor → RESOLVED (recorded 2026-10-07 on the builder's final review):** TERP-based factor `(P_C + q·P_S)/((1+q)·P_C)` — P_C = last raw close on/before the action's effective date, P_S = subscription price, q = new shares per 1 held; reduces to the bonus factor `1/(1+q)` at P_S = 0 and to `1` at P_S = P_C. Golden + composition in FU §2/§11.6; test case **TC-MDF-053**. The architect was asked to define it but its dispatch repeatedly timed out; the formula is contract-defined here for implementation and flagged for the `02` corporate-action section to mirror (upstream-doc gap, 2026-10-07).
- **Q4 — CAGR short-history reading → CONFIRMED:** compute from available history whenever ≥2 FYs, `window_years` = intervals actually used; the UX "no-data/shorter-history state" is the actual-window indication. TC-033 final.
- **Q5 — Windowed CAGR storage → RESOLVED (recorded 2026-10-07):** **window-suffixed metric codes** (e.g. `rev_cagr_3y`, matching FU §11.1's code names) — the metric model stays flat; the idempotency/upsert key is plainly `(instrument, metric, as_of_date)` (TC-034; no window dimension). Flagged for the `02` §6 metric-storage section to mirror (upstream-doc gap, 2026-10-07).
- **Q6 — Membership removal mechanism → DEFERRED (builder, 2026-10-06; confirmed 2026-10-07):** TC-037 asserts the observable history/current-universe contract; `effective_to`-close vs. new-row remains the implementation's choice.
- **Volume semantics (FLG-09) — pinned by builder 2026-10-17 (architecture v1.5 §10.9.1):** the stored `volume` semantic is **TRY turnover (₺)** — İş Yatırım rows store `HGDG_HACIM` as-is; Yahoo-contingency rows store **NULL** (share-count volume is a different unit; deriving ₺ from shares × close rejected as computing); FU volumes reinterpret as ₺ **with unchanged numeric values — no golden re-derivation** (LAMDA stays movers-excluded under the 1,000,000 test threshold; FU §11.3 goldens stand). The movers threshold (`Ingestion:MarketSnapshot:VolumeEligibilityThreshold`, BR-MOV-011) is recalibrated from the observed BIST 30 distribution during live activation — an explicit TKT-foundation-018 criterion, not a TC. Mirrored in FU §2/§4/§10b; pinned by TC-MDF-061 (amended v1.5), 064, 069.
- **Recorded interpretations (no decision needed):** unknown job code → 404 (TC-042); Mon–Fri trading days in fixtures, holiday calendar config-only; quarantine reason-code catalog defined by FU §2; median even-count = mean of middles; TTM = FY with ALFA's ΣQ consistency fixture; a rights action with q ≤ 0 or P_C ≤ 0 is invalid (rejected by validation, never a factor) — TC-053 column (e), plan-specified.

---
## 7. Post-board correctness cases — v1.4 (builder-approved 2026-10-15 via the delivery planner; test-planner confirmation pending)

These cases pin the fixes from the builder's post-board feedback (`02` v1.3, `03` v1.4, FU v1.4). Levels per the convention above; fixtures per FU v1.4.

**TC-MDF-054 — Multiple restatements of one period are sequence-versioned**
- Preconditions: REST2 fixture (FU §3b) — one period published as v0 (NI 60), restated once (NI 75), restated a second time (NI 80).
- Steps: ingest all three versions in publication order; then run metrics-recompute and read the served statement.
- Expected: three `financial_statements` rows with `version` = 0, 1, 2 (no unique-constraint violation on the second restatement); serving selects **v2 (NI 80)**; `derived_metrics.is_rested` = true; v0/v1 retained. Re-ingesting an identical payload is an idempotent no-op.
- Traces: FR-MDF-002, FR-MDF-019, BR-MDF-011, `02` §3.1/§5.7 · Level: L2 · Design: state transition — as_reported → restated → restated-again

**TC-MDF-055 — Statement unit-scale & currency normalization (with FX)**
- Preconditions: USDX fixture (FU §3b) declaring `unitScale: "millions"`, `currency: "USD"`; `USD_TRY` 2025-12-31 = 42.00 in `macro_values` (FU §7).
- Steps: ingest USDX FY2025; read `fin_line_items` + statement provenance.
- Expected: stored values are **raw single-unit TRY** — `NI` = 10 × 10⁶ × 42.00 = **420,000,000**; `SHARES_DILUTED` = 20,000,000; `source_unit_scale` = `millions`, `source_currency` = `USD` recorded. A payload omitting the scale/currency declaration quarantines `SCHEMA_MISMATCH`; a USD payload with no period-end FX rate quarantines **`MISSING_FX_RATE`** (never guessed).
- Traces: FR-MDF-002, FR-MDF-008, `02` §1.8/§3.1/§5.9 · Level: L2 · Design: decision table — scale × currency × FX-present

**TC-MDF-056 — Bank/insurance statements ingest without quarantine; profile recorded**
- Preconditions: BANKA fixture (bank-shaped, no `REV`/`COGS`/`CUR_ASSETS`) and SIGORTA (insurance).
- Steps: ingest both; read `instruments.accounting_profile` and the stored lines; run the universe sync afterwards.
- Expected: statements stored (no quarantine for absent industrial lines); `accounting_profile` = `bank`/`insurance`; a subsequent universe sync does **not** overwrite the profile (classification, not a source fact). Default profile for the seeded 16 remains `industrial`.
- Traces: FR-MDF-002, FR-MDF-012, `02` §3.1 · Level: L2 · Design: decision table — profile × line absence

**TC-MDF-057 — Financial-profile metric NULL matrix**
- Preconditions: BANKA (incl. a spurious `REV` 500) and SIGORTA ingested; price seeded; metrics-recompute.
- Steps: read all metric values for both.
- Expected (BANKA, FU §3b): `pe` = 30.0, `pb` = 3.0, `roe` = 0.10 compute; **NULL**: `ev_ebitda`, `ev_fcf`, `fcf_yield`, `roic`, `gross_margin` (despite the spurious `REV`), `operating_margin`, `fcf_cagr_*`, `net_debt_ebitda`, `interest_coverage`, `current_ratio`. SIGORTA: `pe` 12.0, `pb` 1.2, `roe` 0.10; same ten NULL. No NULL is served as 0.
- Traces: FR-MDF-013, `02` §6.2 (profile gating) · Level: L1+L2 · Design: decision table — profile × metric

**TC-MDF-058 — Corporate-action-adjusted per-share CAGRs**
- Preconditions: EPSL deep history (FU §5.3); actions fixture (split 0.2 + bonus 10/11).
- Steps: metrics-recompute; read `eps_cagr_3y`, `div_cagr_3y`, `eps_cagr_5y`, `div_cagr_5y`.
- Expected: `eps_cagr_3y` = **0.062659**, `div_cagr_3y` = **0.062659** (adjusted series); `eps_cagr_5y`/`div_cagr_5y` = NULL (< 5 intervals); both computed CAGRs carry `is_adjusted` = true. Guard: the unadjusted (0.75/3.125)^(1/3)−1 = −0.378553 must **not** appear.
- Traces: FR-MDF-013, FR-MDF-018, `02` §6.2 · Level: L1+L2 · Design: golden values + negative guard

**TC-MDF-059 — `OCF`-based FCF and the KAP sign convention**
- Preconditions: ALFA FY2025 statements with `OCF` 250 (§5) and the sign-convention payload (`CAPEX −150`, `OCF` 250).
- Steps: metrics-recompute on both.
- Expected: `FCF` = `OCF − |CAPEX|` = 250 − 150 = **100** in both; the negative-CAPEX case is *subtracted*, not added; EV/FCF-dependent goldens unchanged (FU §11.1); a CF statement missing `OCF` → FCF metrics NULL (PART unchanged).
- Traces: FR-MDF-013, `02` §6.1/§6.2 · Level: L1+L2 · Design: golden values + sign-trap boundary

**TC-MDF-060 — TMS 29 regime-break flag**
- Preconditions: ALFA (windows span 2023-12-31) and ZETA (FY2024–FY2025, entirely after).
- Steps: metrics-recompute; read `regime_break` for each CAGR.
- Expected: ALFA `rev_cagr_*`/`eps_cagr_*`/`fcf_cagr_*`/`div_cagr_*` = **TRUE** (min FY period-end < 2023-12-31 ≤ max FY period-end); ZETA CAGRs = **FALSE**; NEWP CAGRs NULL with no flag. The flag is served in the metric-payload flags object (`03` §4).
- Traces: FR-MDF-013/014, `02` §3.1/§6.2 · Level: L2 · Design: decision table — window × boundary

**TC-MDF-061 — Price source contract (İş Yatırım) and per-row provenance**
- Preconditions: İş Yatırım-shaped price payload (`03` §1.2) + the Yahoo contingency payload (FU §10).
- Steps: run the prices job against each; read `daily_prices.source_ref`.
- Expected: rows stored with `source_ref` = `isyatirim` (primary) / `yahoo` (contingency) respectively — each row records its concrete source; asserted close/volume outcomes unchanged (FU §4); `index_levels` rows likewise provenance-stamped; backfill depth recorded. *(v1.5 amendment, 2026-10-17 — FLG-09 volume pin, architecture §10.9.1): the İş Yatırım/canonical legs are unchanged — asserted close/volume outcomes per FU §4, volumes ₺-reinterpreted with unchanged values; the Yahoo-contingency leg now expects **volume NULL** (share-count volume is a different unit; deriving ₺ from shares × close rejected as computing) and may carry `open`. Cross-pinned by TC-MDF-069.*
- Traces: FR-MDF-001, FR-MDF-008, FR-MDF-018 · Level: L2 · Design: contract + provenance assertion

**TC-MDF-062 — Constituents launch-seed & pre-launch coverage gap**
- Preconditions: the existing `universe-add-remove.json` (FU §10) plus a seeded current-membership base.
- Steps: run the launch seed; apply `universe-add-remove.json`; read `index_constituents` + `coverage_metadata`.
- Expected: seed writes current membership (`effective_to` NULL) append-only; the change set adds new rows without mutating existing ones; `coverage_metadata` records a `universe`-scope gap for pre-launch constituent history (`available_from` = launch date) — an explicit accepted gap, not a silent absence.
- Traces: FR-MDF-007, FR-MDF-011, BR-MDF-005/007 · Level: L2 · Design: state transition + recorded-gap check

## 8. Real-source parsing-fidelity & live-activation cases — v1.5 (builder-directed via the delivery planner, 2026-10-17; architecture v1.5 §10.9 authority)

**Authority and posture.** Architecture v1.5 §10.9 (AD-14; builder instruction 2026-10-17 — "main functionality requires real data"): the production adapters parse the real source envelopes — İş Yatırım EOD `HGDG_*` prices (§10.9.1), KAP XBRL fact statements (§10.9.5), TCMB EVDS macro (§10.9.6) — alongside the canonical source-double envelope. This section is the test-side mirror, closing the PR #82 escalation (`.pipeline/plan/reviews/TKT-foundation-015-round-1.md`, DR-01..03):

- **Product-path rule (DR-01 overturned):** every parsing-fidelity case serves the catalog's **raw captured bytes** through the WireMock harness with the correct `Content-Type; charset=` header and decodes them with the **production adapters in `src/Ingestion`** — never with fixture-owned decoders. The decoders, the XBRL concept map and the code-pages provider registration are lifted from the fixtures project into `src/` (AD-14); the real-source catalog itself stays test-side (checked-in fixtures module, FU §10b) and ships no decoders of its own.
- **Goldens untouched (§10.9.7):** the FU canonical envelope stays the L2 golden contract. FU volumes **reinterpret as TRY turnover (₺) with unchanged numeric values** (FLG-09) — **no golden re-derivation anywhere in this section**; LAMDA stays movers-excluded under the 1,000,000 test threshold; §11.3 goldens stand. Real-envelope cases assert **parse shape/fidelity** — capture-recorded pins, i.e. values present in the captured body flow through unchanged — and **never derived numbers**: no metric, snapshot or median golden is ever computed from a real capture. If a capture is refreshed, its pins update via §14 change record (they always equal the captured body's values).
- **Honest labeling (DR-03):** every catalog entry carries a captured-vs-constructed provenance label; a hand-constructed body is never labeled "captured verbatim" — enforced mechanically by TC-MDF-063.
- **Duplicate concepts (DR-05 overturned by §10.9.5):** equal values collapse; unequal values quarantine `CONFLICTING_VALUE` with both values retained — never silent latest-wins (TC-MDF-071).
- **L2 harness seam:** the doubles keep serving the canonical envelope through the symbol-scoped seam (§10.9.7); the L2 test config binds `Ingestion:Prices:UniverseIndex` to the seeded FU **XU100** so all existing price-path goldens are unchanged (16 per-ticker fetches → 16 rows, TC-MDF-001/002/015/061 canonical legs untouched). The production default `XU100` is asserted as a config value (TC-MDF-075); the bound switch is exercised against the FU XU30 subset.
- **CI-verifiable vs live-pull (TKT-foundation-018):** loop mechanics, universe bound, pacing, launch-seed and config are CI-verifiable against doubles (TC-MDF-072..076). The **volume-threshold recalibration** (BR-MOV-011, from the observed BIST 30 distribution), the **bounded live capture** (replacing constructed catalog bodies where honest) and the **real-world correctness of the XU030 membership list** are live-pull observations — explicit ticket acceptance criteria recorded in the consolidated matrix's v1.5 block, not TCs.

**Ticket mapping (this wave):** TKT-foundation-015 → TC-MDF-061 (amended), 063, 064, 068, 069, 070 · TKT-foundation-016 → TC-MDF-066 (063 supplies the bytes) · TKT-foundation-017 → TC-MDF-065, 067, 071 · TKT-foundation-018 → TC-MDF-072..076 + the explicit live-pull criteria above · TKT-foundation-019 → **TC-MOV-028** (MOV plan §8 — macro is MOV-owned, FR-MOV-014; the lane itself is shared via FU §10b).

**TC-MDF-063 — Real-source catalog contract: honest labels, charset-faithful bytes, harness serving** (re-authored v1.5; was "catalog loads through the same helper seam" — now asserts the product path per DR-01)
- Preconditions: the real-source catalog checked in under the fixtures module (FU §10b); WireMock harness; production `ISourceClient`.
- Steps: (a) load the catalog through the same `Names`/`Build`/`Get`/`TryGet` seam as the FU §10 catalog and inspect every entry; (b) serve each entry's **raw bytes** at its default route with its declared `Content-Type; charset=` header and fetch through the production `ISourceClient`; (c) look up a non-existent name.
- Expected: catalog count equals `Names` count; every entry carries non-empty raw bytes, a declared charset, and a provenance label (`captured` or `constructed`); every entry labeled `captured` decodes under its declared charset with no U+FFFD and its label asserts verbatim capture; **no entry whose body is hand-constructed is labeled captured-verbatim** (the DR-03 rule, mechanically enforced); redistribution terms recorded with the catalog (RISK-MDF-001 — bounded, non-commercial, parsing-fidelity only); each harness-served entry decodes through the product client to the entry's canonical decoded body; unknown name → `TryGet` false, `Get` throws.
- Traces: FR-MDF-008 (provenance discipline, test-data mirror), `01` v1.5 §10.9.7, FU §10b, RISK-MDF-001 · Level: L2 · Design: EP — catalog entry classes (captured/constructed) × label honesty; the labeling rule as an invariant
- Dependencies: —

**TC-MDF-064 — Real İş Yatırım HGDG envelope decodes through the production prices adapter** (re-authored v1.5; was fixture-decoder assertions)
- Preconditions: WireMock serves each captured İş Yatırım EOD body's **raw bytes** at its per-ticker route with `Content-Type: application/json; charset=utf-8`; production `PricesSourceAdapter` + `ISourceClient` from `src/Ingestion` (HGDG decoder lifted per AD-14).
- Steps: fetch each ticker's bounded window through the symbol-scoped adapter fetch; separately fetch a catalog variant body (honestly labeled `constructed`) that uses the alternate `HG_*` field spellings.
- Expected (fidelity pins — capture record 2026-10-16, window 2026-10-06..2026-10-08):

| ticker | date | close | high | low | volume (₺ turnover, bigint) |
|---|---|---|---|---|---|
| ASELS | 2026-10-08 | 348.75 | 354.00 | 340.25 | 12,941,100,644 |
| THYAO | 2026-10-08 | 284.25 | 287.50 | 282.00 | 5,829,586,658 |
| GARAN | 2026-10-08 | 128.50 | 129.80 | 127.00 | 2,001,849,137 |
| FROTO | 2026-10-08 | 74.40 | 74.70 | 73.70 | 837,500,049 |
| EBEBK | 2026-10-08 | 80.0176 | 80.56323 | 74.95755 | 39,335,497 |

  Field map per §10.9.1: `HGDG_HS_KODU` → symbol as-is; `HGDG_TARIH` (`dd-MM-yyyy`) → ISO date; `HGDG_KAPANIS`/`HGDG_MAX`/`HGDG_HACIM` primary with `HG_KAPANIS`/`HG_MAX`/`HG_HACIM` fallback (primary asserted on the captured bodies; the fallback spellings asserted on the constructed variant); scientific-notation volume (E-notation JSON numbers) parses to exact bigint, round half-away-from-zero; **`open` NULL on every İş Yatırım row** (honest gap — never derived from `HGDG_AOF`); `source_ref` = `isyatirim`. No metric or golden is derived from these rows.
- Traces: FR-MDF-001, FR-MDF-010, `01` v1.5 §10.9.1 · Level: L2 · Design: EP — valid real-envelope class over the five captured tickers + BVA edges (dd-MM-yyyy date, E-notation volume, NULL open, `HG_*` fallback spellings)
- Dependencies: TC-MDF-063 (catalog contract).

**TC-MDF-065 — Turkish-locale KAP numbers parse culture-free through the production statement decoder** (re-authored v1.5; was `ParseTurkishNumber` fixture assertions)
- Preconditions: WireMock serves the KAP XBRL fact bodies (shape-faithful; current entries honestly labeled `constructed` per DR-03 — a bounded live capture may replace them via TKT-foundation-018, pins then update by change record) plus one constructed variant body carrying the tolerance edges; production statement decoder in `src/Ingestion/Statements/`.
- Steps: decode the five tickers' fact bodies and the tolerance-edge variant through the production decoder.
- Expected: capture-pinned revenues — ASELS FY2025 `REV` = 1,234,567.89; THYAO 2,345,678,901.23; GARAN 3,456,789,012.34; FROTO 4,567,890,123.45; EBEBK 5,678,901,234.56. The parser is culture-free (host runs under `InvariantGlobalization`; `.` thousands / `,` decimal regardless of host culture): `-1.234,50` and `–1.234,50` (en-dash) → −1234.50; `(1.234,50)` (accounting parentheses) → −1234.50; `456.000.000` → 456,000,000; trailing `%` tolerated (numeric part parsed); NBSP thousand-groupings parse. Exact decimal equality — no tolerance.
- Traces: FR-MDF-002, `01` v1.5 §10.9.5, `02` §1.8 · Level: L2 · Design: EP — TR-locale number classes + BVA tolerance edges (en-dash minus, accounting parentheses, trailing %, NBSP grouping)
- Dependencies: TC-MDF-063.

**TC-MDF-066 — `ISourceClient` honors the declared response charset** (re-authored v1.5; was fixture-side `DecodeRawText` assertions — TKT-foundation-016)
- Preconditions: WireMock serves the legacy-artifact raw bytes (constructed but genuinely windows-1254-encoded, honestly labeled) under four headers — `charset=windows-1254`, `charset=iso-8859-9`, no charset parameter, `charset=x-unknown` — plus a UTF-8 body with and without a charset declaration; production `ISourceClient` with the code-pages provider registered at product startup (AD-14).
- Steps: fetch each stub through the production `ISourceClient`.
- Expected: windows-1254 → Turkish text decodes without mojibake ("ığüşöçİĞÜŞÖÇ" present, no U+FFFD); ISO-8859-9 → identical decoded text (the two code pages agree on the Turkish letters); utf-8 / no declaration / unknown charset → the UTF-8 default, the current behavior (for the 1254 bytes served without a charset the decode contains replacement characters — the honored header is the fix; charset sniffing is deliberately not added, §10.9.4); a successful windows-1254 decode also proves the code-pages provider registration at product startup (without it the encoding is unsupported and the fetch fails).
- Traces: FR-MDF-001, FR-MDF-002 (every adapter reads through the client), `01` v1.5 §10.9.4 · Level: L2 · Design: decision table — declared charset ∈ {windows-1254, ISO-8859-9, utf-8, absent, unknown} × decode outcome
- Dependencies: TC-MDF-063.

**TC-MDF-067 — KAP XBRL concept map, fact grouping, and `OTHER_*` passthrough through the production decoder** (re-authored v1.5; was `XbrlConceptMap` fixture assertions)
- Preconditions: as TC-MDF-065 (KAP fact bodies via WireMock; production decoder in `src/Ingestion/Statements/` — the concept map lifted per AD-14).
- Steps: decode; read the grouped statements and line items.
- Expected: `ifrs-full:Revenue` (FY2025) and `tr:Hasilat` (FY2024) collapse onto the same canonical item code `REV` (capture pins: ASELS FY2025 1,234,567.89 / FY2024 1,100,000.00); `kap:NetSatisTutari` likewise → `REV`; `ifrs-full:CostOfSales` → `COGS`; `GROSS_PROFIT` derived when not reported (pinned ASELS FY2025: REV − COGS = 246,913.57); facts group by (periodType, periodEndDate, statementType, version) with the grouping coordinates intact for the store's integer version sequencing (TC-MDF-054 mechanics downstream); unmapped concepts (e.g. `kap:OzkaynakDegisimTablosu` 42,000.00) pass through as `OTHER_*` lines — recorded, ignored by metrics (honest gap, `02` §6.1); envelope-level `unitScale`/`currency` coordinates are carried through decode so the v1.4 normalization path (TC-MDF-055) consumes them; `source_ref` = `kap`.
- Traces: FR-MDF-002, `02` §6.1, `01` v1.5 §10.9.5 · Level: L2 · Design: EP — tag-variance classes (IFRS spelling / TR spelling / KAP spelling / unmapped) × canonical outcome
- Dependencies: TC-MDF-065.

**TC-MDF-068 — One real captured HGDG payload end-to-end through the adapter backfill path** (re-authored v1.5 — the DR-01 fix in miniature: raw bytes served, never a re-serialized canonical envelope)
- Preconditions: WireMock serves the captured ASELS **raw bytes** at the per-ticker backfill route with the correct charset header; production adapter + client.
- Steps: call the symbol-scoped backfill fetch for a historical range covering the capture window.
- Expected: canonical payload with `source_ref` `isyatirim`; exactly the 3 captured rows (2026-10-06..2026-10-08), the ASELS 2026-10-08 row pinned per TC-MDF-064; `open` NULL; exactly one HTTP request to the per-ticker route (WireMock request log); rows outside the source-returned window are absent — the achieved depth is what the source returned, never fabricated (BR-MDF-006; job-side depth recording cross-referenced by TC-MDF-015/016).
- Traces: FR-MDF-010, FR-MDF-001, BR-MDF-006, `01` v1.5 §10.9.1 · Level: L2 · Design: EP — real-envelope backfill class + negative guard (the served body is the raw capture, not a canonical re-serialization)
- Dependencies: TC-MDF-064.

**TC-MDF-069 — Prices adapter envelope detection: real, Yahoo, canonical-double** (new v1.5)
- Preconditions: three per-ticker stubs: the captured real HGDG body (TC-MDF-064), `prices-yahoo-contingency.json` (FU §10), and the canonical double envelope `prices-ok.json` (FU §10) served through the symbol-scoped seam.
- Steps: fetch each through the same production adapter.
- Expected: real → `source_ref` `isyatirim`, HGDG mapping, `open` NULL (pins per TC-MDF-064); Yahoo → `source_ref` `yahoo`, rows may carry `open` (a mixed NULL/open history across sources is accepted and honest), **volume NULL** (FLG-09(b): Yahoo's share-count volume is a different unit; deriving ₺ from shares × close rejected as computing); canonical double → the L2 golden shape, rows equal the FU §4 outcomes with volume stored (₺-reinterpreted, values unchanged); all three parse through the same production adapter with no envelope rejection and no cross-attribution of rows to the wrong source.
- Traces: FR-MDF-001, `03` §1.2, `01` v1.5 §10.9.1 · Level: L2 · Design: decision table — envelope shape ∈ {real HGDG, Yahoo chart, canonical double} × parse outcome / `source_ref`
- Dependencies: TC-MDF-064.

**TC-MDF-070 — Real-envelope transport failure is a source failure, not a parse event** (new v1.5)
- Preconditions: WireMock per-ticker stubs returning a real-shaped body with `ok:false`, and one with a non-null `errorCode`.
- Steps: fetch each through the production adapter; observe job-side handling.
- Expected: the adapter surfaces a source failure (the fetch-exception class TC-MDF-012's retry ladder consumes); **zero** rows emitted; **zero** `quarantined_facts` rows (not an `UNPARSEABLE_PAYLOAD` event); job-side, the symbol is marked failed per TC-MDF-073's isolation semantics and the retry ladder applies.
- Traces: FR-MDF-001, UC-MDF-001 alternate a, NFR-MDF-001, `01` v1.5 §10.9.1 (transport fields) · Level: L2 · Design: EP — invalid-transport class (`ok:false` / `errorCode` non-null) vs valid-payload class
- Dependencies: —

**TC-MDF-071 — KAP duplicate-concept rule: equal collapses, unequal quarantines** (new v1.5 — DR-05 overturned by §10.9.5)
- Preconditions: two constructed KAP fact bodies (honestly labeled): one statement group carrying the same canonical item code twice with **equal** values; one with **unequal** values.
- Steps: run the statements job against each through the harness.
- Expected: equal → collapses to a single line item (re-ingesting the identical payload is an idempotent no-op; no quarantine); unequal → `quarantined_facts` row with reason `CONFLICTING_VALUE` and **both values retained** in the payload; no silent latest-wins in either case; each group's other facts ingest normally (partial-batch behavior per TC-MDF-019).
- Traces: FR-MDF-002, FR-MDF-012, UC-MDF-001 alternate b, `01` v1.5 §10.9.5 · Level: L2 · Design: decision table — duplicate canonical concept × equal/unequal value → collapse / quarantine
- Dependencies: TC-MDF-067.

**TC-MDF-072 — The per-ticker loop resolves the configured index's current constituents** (new v1.5)
- Preconditions: migrated container, seeded FU universe; `Ingestion:Prices:UniverseIndex` bound to the FU `XU30` (5 members); WireMock serves the canonical double envelope through the symbol-scoped seam (the L2 golden seam — real-envelope decode is separately pinned by TC-MDF-064; the two compose via TC-MDF-069's detection).
- Steps: run the prices job for T; read the WireMock request log, `daily_prices`, and the run ledger.
- Expected: exactly one fetch per current XU30 member (5 requests at symbol-scoped routes); `daily_prices` rows for exactly those 5 instruments at T; the other 11 seeded instruments receive no rows from this run and no requests; requests are strictly sequential (no overlapping in-flight requests); the run-ledger row carries per-symbol stats; a constituent with a closed `effective_to` (per the FU `universe-add-remove.json` state) is not fetched.
- Traces: FR-MDF-001, FR-MDF-006, FR-MDF-007, `01` v1.5 §10.9.2 · Level: L2 · Design: EP — loop bound = current constituents (`effective_to IS NULL`); boundary — closed-interval member excluded
- Dependencies: —

**TC-MDF-073 — Per-symbol failure isolation within the loop** (new v1.5)
- Preconditions: as TC-MDF-072, with one member's per-ticker route returning 500 for the whole run.
- Steps: run the prices job for T.
- Expected: the failed symbol's rows absent; an alert is raised (mail double + Serilog test sink) naming the symbol; the run does **not** abort — the other 4 members' rows ingest; the run ledger reaches a terminal status with per-symbol stats recording the failure; no quarantine rows for a transport-level failure (the TC-MDF-070 class) — quarantine applies to parse/validation events, not source failures.
- Traces: FR-MDF-001, FR-MDF-012, UC-MDF-001 alternates a/b, `01` v1.5 §10.9.2 (per-symbol failure isolation) · Level: L2 · Design: EP — single-symbol failure class inside a multi-symbol loop
- Dependencies: TC-MDF-072.

**TC-MDF-074 — Sequential pacing honors `RequestDelayMs`** (new v1.5)
- Preconditions: as TC-MDF-072; `Ingestion:Prices:RequestDelayMs` = 40 ms (test value).
- Steps: run the prices job; read consecutive WireMock request timestamps; repeat with `RequestDelayMs` = 0.
- Expected: every consecutive inter-request gap ≥ `RequestDelayMs`; total elapsed ≥ (N−1) × `RequestDelayMs` (N = 5 → ≥ 160 ms); zero overlapping in-flight requests. With `RequestDelayMs` = 0 the run completes with no enforced wait (no gap assertion). Assertions are lower bounds only — deterministic by construction (a slower machine can only increase gaps); the explicit tolerance is "gap ≥ configured delay" with no upper bound.
- Traces: FR-MDF-001, `01` §10.4 / v1.5 §10.9.2 (politeness posture — sequential, no parallelism) · Level: L2 · Design: BVA — delay boundary (0 = no wait required; configured delay = enforced lower bound)
- Dependencies: TC-MDF-072.

**TC-MDF-075 — The fetch bound follows the configured index (config-only flip)** (new v1.5)
- Preconditions: seeded FU universe; two runs with `Ingestion:Prices:UniverseIndex` = `XU30`, then `XU100`.
- Steps: run the prices job under each binding; separately assert the options default with no override configured.
- Expected: XU30 → 5 per-ticker fetches, 5 instruments' rows; XU100 → 16 fetches, 16 rows (this is the L2 harness binding that keeps the existing price-path goldens intact — §8 preamble); with no override the bound resolves to the production default **`XU100`** (config assertion, the PBKDF2-config precedent TC-XC-011); flipping the bound is observable as the request-set difference with no code-path change. *(v1.5.4 amendment, 2026-10-18 — TKT-foundation-025 block: the production default is flipped `XU030` → `XU100` in the shipped `src/Api/appsettings.json` now that the XU100 launch-seed makes the flip valid — architecture §10.9.2 "XU100 later = a config flip"; the code-level template default stays `XU030` and the assertion reads the shipped config value. The L2 harness binding to the FU `XU100`/`XU30` subsets and all golden values are untouched. Test-planner confirmation pending.)*
- Traces: FR-MDF-006, FR-MDF-007, `01` §10.4 / v1.5 §10.9.2 · Level: L2 · Design: decision table — configured index × constituent set; config-default assertion
- Dependencies: TC-MDF-072.

**TC-MDF-076 — XU030 membership launch-seed** (new v1.5; reuses the TC-MDF-062 machinery)
- Preconditions: fresh migrated container; the checked-in XU030 launch-seed (real membership observed at launch — the seed content is live-observed data; CI asserts structure, not real-world list correctness).
- Steps: apply the launch seed; re-apply it; resolve the loop bound with `UniverseIndex` = `XU030`.
- Expected: `index_constituents` rows for the XU030 members — append-only, effective-dated, `effective_to` NULL for current members, `source_ref` recorded; re-application is idempotent (no duplicate rows, no mutations — the TC-MDF-062 posture); the per-ticker loop resolves exactly the seeded current members (composes with TC-MDF-072); pre-launch constituent history remains an explicit recorded coverage gap (`available_from` = launch date, TC-MDF-062). *Real-world correctness of the checked-in membership list (matching BIST's actual XU030 at launch) is a live-pull observation — an explicit TKT-foundation-018 criterion, not a TC.*
- Traces: FR-MDF-007, FR-MDF-011, BR-MDF-005/007, `01` v1.5 §10.9.3 · Level: L2 · Design: state transition — empty → seeded → re-seeded (idempotent) + recorded-gap check
- Dependencies: TC-MDF-072.

## 9. Morning-cadence chain — EOD repair + incremental KAP freshness (TKT-foundation-040)

**Lane posture.** The morning chain is a second stream on the same C3c engine
(architecture §10.9.16): `Ingestion:Scheduler:MorningCron` = `0 30 8 * * *` Europe/Istanbul,
trading-day gated, running the pinned `MorningIngestChain` for the **previous trading day**.
The scheduler's fake-clock determinism is the test seam (the TKT-mdf-005 harness shape): the
real engine is driven by a `FakeTimeProvider`, every source is the FU §10 WireMock double
(never a live source), and pacing is bound to `0` in the harness.

**TC-MDF-077 — The morning stream schedules like the EOD stream: 08:30 on trading days, for the previous trading day** (new v1.6)
- Traces: FR-MDF-009, FR-MDF-012, NFR-MDF-001, `01` v1.5 §10.9.16 · Level: L2 · Design: EP — occurrence eligibility classes × run-date resolution; state transition over the retry ladder
- Preconditions: migrated container, FU L2 seed; `Ingestion:Scheduler:MorningCron` = `0 30 8 * * *`, `TimeZone` = `Europe/Istanbul`, EOD `Cron` unchanged; both chain sources stubbed at their canned routes; the target day's stored prices deleted (an uncovered/failed session).
- Steps: (i) advance the fake clock to 08:30 TRT on a trading day (Tuesday 2026-10-06). (i-b) advance to a 08:30 occurrence on a Saturday. (iii) with the prices source returning HTTP 500, advance through 08:30/08:35/08:50/09:50 and then past the ladder.
- Expected: (i) exactly one run of the pinned chain — `prices → statements → metrics-recompute → market-snapshot → medians`, every ledger row `succeeded` — and **every artifact dated the previous trading day (2026-10-05)**: 16 `daily_prices` rows for that day (the prices row's stats record `written = 16`), `derived_metrics` / `market_snapshots` / `sector_metric_medians` at 2026-10-05 and **none** at the 08:30 occurrence's own date. (i-b) a non-trading occurrence is skipped silently: **zero** ledger rows (the EOD stream's `TradingDaysOnly` posture mirrored). (iii) the morning stream retries on the unchanged 5/15/60 ladder (attempts at 08:30, 08:35, 08:50, 09:50, each a `failed` ledger row with `attempt` 1..4; a fail-stopped chain records no later job), raises exactly one `RETRIES_EXHAUSTED` alert naming the run date after `MaxAttempts`, and fires no further attempt.
- Dependencies: —

**TC-MDF-078 — The stored-prices repair gate: a covered day is never re-fetched, a failed night is repaired** (new v1.6)
- Traces: FR-MDF-001, FR-MDF-010/011 (coverage posture), NFR-MDF-001, FR-MOV-001..006/021, NFR-MOV-001, `01` v1.5 §10.9.13 + §10.9.16 · Level: L2 · Design: decision table — stored coverage ∈ {complete, partial, none} × gate outcome; end-to-end serving check
- Preconditions: FU L2 seed — note the anchor T = 2026-10-06 leaves the occurrence day's *own* prices (2026-10-06) seeded as T−1 of the repair target, so every morning-path case must delete them to model a Tuesday morning whose session does not exist yet. The morning cron and EOD cron both configured. (a) a fully succeeded night: the stored prices for 2026-10-05 are cleared before the EOD chain runs at 20:30, so the night itself writes the day (16 rows) and it is already snapshotted. (a′) a partially covered day: after that night one current constituent's 2026-10-05 row is removed (15 of 16 stored). (b) a failed-night world: the stored prices for 2026-10-05 **and** for 2026-10-06 are absent and no EOD run happened.
- Steps: advance the fake clock to 08:30 TRT on Tuesday 2026-10-06; read the WireMock request log (the İş Yatırım price route), the run ledger, `daily_prices`, `derived_metrics`, `market_snapshots`, `sector_metric_medians`, `quarantined_facts`; in (b) also `GET /api/v1/market/overview` through the in-process API host.
- Expected: (a) the prices job is **not invoked** — **zero** requests on the price route, its ledger row `succeeded` with `gate: "prices-coverage"`, `gateOutcome: "skipped"`, `constituents` = `covered` = 16, `written = 0` — while `statements`, `metrics-recompute`, `sector-medians` run (`succeeded`) and `market-snapshot` **no-ops** (still exactly one row for 2026-10-05, `computed_at` unchanged); **zero** `quarantined_facts` rows (no `CONFLICTING_VALUE` noise). (a′) partial coverage is **not** "covered": the gate does not skip — the prices job runs (its ledger row is the job runner's, with no `gateOutcome`) and the day ends complete (16 rows at 2026-10-05); the exact request count is not pinned (the current implementation re-fetches the whole bound and writes only the missing name). (b) the uncovered day runs the prices job (one request per current constituent (harness pacing bound to 0), `written = 16`, 16 rows at 2026-10-05), `metrics-recompute` + `market-snapshot` + `sector-medians` write the day, and `GET /api/v1/market/overview` returns 200 with `state: "available"`, `asOf` = 2026-10-05 and **`stale: false`** — on a Tuesday morning Monday is the latest *stored* trading day, so the served row is fresh (§10.9.13/v1.5.13; `stale: true` would mean a newer stored session the row lacks).
- Dependencies: —

**TC-MDF-079 — A hung chain step cannot stall the scheduler silently: the watchdog bounds the attempt, records and alerts, and the loop recovers** (new v1.8)
- Traces: FR-MDF-009, FR-MDF-012, NFR-MDF-001, `01` v1.5 §10.9.17 · Level: L2 · Design: fault-injection class — a chain step that never completes / throws / self-cancels, against the liveness of an independent stream
- Preconditions: migrated container, FU L2 seed; `Ingestion:Scheduler:MaxRunDuration` = `00:10:00`; the EOD stream's `JobCodes` bound to `[prices, <doubled stall step>]` (the doubled step hangs on its first attempt, then succeeds); an independent per-job stream (`JobSchedules`) due **after** the stall instant; prices stubbed at its canned route; `FakeTimeProvider` drives the clock and the watchdog deadline.
- Steps: (a) set the clock to 20:29 TRT (Monday 2026-10-05), start `TickAsync(20:30)` on a task, wait for the doubled step to be entered, advance the clock past the bound, then await the tick; (b) read `ingest_runs`, the mail double and the captured log events; (c) advance to the independent stream's 20:42 occurrence; (d) advance to the retry instant.
- Expected: (a) the tick **returns** once the bound expires (pre-fix it never returns — the reproduction); (b) exactly one row with `stats_json.reasonCode = "RUN_HUNG"`, `job_code` = the hung step, `status = failed`, `hungJob` = the step, `attempt = 1`, `maxRunDurationSeconds = 600`, `started_at`/`finished_at` set; exactly one `RUN_HUNG` alert whose subject names the step; a `Chain step 2/2 '<step>' starting` progress log exists; (c) the independent stream's ledger row **exists** (the loop is alive — pre-fix nothing after `prices` ever ran again); (d) the retry (5 min after the hung attempt **ended**, not after it started) re-runs the chain and the now-healthy step lands `succeeded` with `attempt = 2`, `retries = 1`, and exactly one `RUN_HUNG` row overall. Supporting cases: a step that throws between steps keeps its runner `failed` row and enters the ladder (no `RUN_HUNG`); a step that throws an `OperationCanceledException` from its **own** token (not the host's) also enters the ladder instead of escaping the tick.
- Dependencies: —

**TC-MDF-080 — `universe-sync` leads the EOD chain, so the day's index levels need no manual run** (new v1.8)
- Traces: FR-MDF-006, FR-MDF-009, FR-MOV-001..006/021, NFR-MOV-001, `01` v1.5 §10.9.17 · Level: L2 · Design: composition + idempotency + end-to-end artifact check
- Preconditions: FU L2 seed; **all rows deleted from `index_levels`** (the live gap's world); the universe + index-levels source routes stubbed (the levels body carrying the run day's own closes — XU100 11,797.70 / XU030 14,676.53); the EOD stream on the production default `EodIngestChain`; `FakeTimeProvider`.
- Steps: run the 20:30 TRT chain for Monday 2026-10-05; read the run ledger, `index_levels`, `market_snapshots` and `index_constituents`; then run the next night's 20:30 occurrence over the same world and read them again.
- Expected: (i) the ledger's job codes equal the pinned chain **with `universe-sync` first** (`universe-sync → prices → statements → dividends → corporate-actions → disclosures → metrics-recompute → market-snapshot → sector-medians`), and its row finishes before `prices` starts; (ii) `index_levels` holds the run day's XU100 11,797.70 **written by this run** (the table started empty) and `market_snapshots` for that day serves `xu100_level` 11,797.70 / `xu30_level` 14,676.53 — no manual `universe-sync` invocation; (iii) the second night is an idempotent no-op: one more `universe-sync` ledger row, **no** new `index_levels` rows and unchanged `index_constituents` membership. On the canonical FU snapshot (one unclassified instrument) the step lands `partial` — a non-fatal status that never stops the chain.
- Dependencies: —

**Phase 2 (executable only after TKT-foundation-034 / PR #106 merges; re-dispatched with this ticket's phase-2 lane):** the morning composition includes `dividends → corporate-actions → disclosures` between statements and metrics-recompute (pinned in `MorningIngestChain.Phase2JobCodes`), cursor-incremental (double call count ≈ per-company list calls, never the full-universe pass), paced ≥ 12 s, and a failed event-family job fail-stops the chain exactly as the EOD chain does.

## 10. VYK company-identity resolution (TKT-foundation-046)

**Lane posture.** Both cases are **L2** and **doubles-only, never live sources**: the `/members`
directory is exercised through the production `VykCompanyDirectory` over a **committed
real-shape double derived from the live 2026-10-20 dev-gateway capture** (kept with the tests —
the `/tmp` snapshot is ephemeral and is never referenced), and the family walk uses the existing
WireMock verified-route doubles (architecture §10.9.15, §10.9.11).

**TC-MDF-082 — The `/members` directory resolves every ticker of a comma-joined `stockCode` (and is deterministic on duplicates)** (new v1.7)
- Traces: FR-MDF-008, `01` v1.5 §10.9.15 (company mapping, v1.5.22) · Level: L2 · Design: decision table — member `stockCode` class ∈ {single ticker, comma-joined multi-ticker, codeless auditor-type, literal duplicate, same-code/different-id conflict} × map outcome / log
- Preconditions: the committed real-shape `/members` double (comma codes incl. the exact `"GARAN, TGB"`, `"ALBRK, ALK"`, `"A1CAP, ACP"`; a literal duplicate `"GARAN, TGB"` entry with the same id; two `AKBNK` entries with different ids; codeless `YK`/`FK` members — one empty, one whitespace-only — and a member with no `stockCode` property); `LastDisclosureIndexPath` stubbed; `RequestDelayMs` = 0.
- Steps: (i) build the directory snapshot through the production `VykCompanyDirectory`. (ii) look up each ticker, the joined string, and a lowercased ticker. (iii) inspect the `/members` map keys. (iv) repeat (i) with a recording logger. (v) compose end-to-end: a bound carrying `GARAN` with a `"GARAN, TGB"` member row walks the verified `corporate-actions` list/detail routes.
- Expected: (i) the snapshot is not `Failed`. (ii) `GARAN`/`TGB` → the member's `companyId`, likewise `ALBRK`/`ALK` and `A1CAP`/`ACP`; the joined strings `"GARAN, TGB"`/`"ALBRK, ALK"` are **not** keys; single-ticker members still resolve; the lookup stays `OrdinalIgnoreCase` (`garan` → same id). (iii) the resolved key set is exactly the ticker keys — no empty/whitespace key, no fabricated mapping for the codeless members. (iv) the same-code/different-id conflict is **last-wins** with exactly one logged warning naming the code and both ids; the identical duplicate logs nothing. (v) the comma-coded constituent resolves to its `companyId`, the walk requests that company's list at the early index, one fact is stored with `provenance=kap` and nothing is quarantined. *(Red before the fix: only the joined-string keys existed, so (ii)/(v) missed and (iv) logged nothing.)*
- Dependencies: —

**TC-MDF-083 — An unmapped bound constituent is isolated per constituent; the family run completes** (new v1.7; expected result amended v1.9 — the two-event log shape, TKT-foundation-054/DR-1)
- Traces: FR-MDF-008, FR-MDF-012, FR-MDF-003/004/005, FR-MDF-010/011 (skips counted, never hidden), `01` v1.5 §10.9.15 (per-constituent isolation, v1.5.22) + §10.9.10/§10.9.11 (doctrine copied) · Level: L2 · Design: decision table — bound symbol ∈ {directory-mapped, directory-unmapped} × run outcome; the same case run for each of the three families
- Preconditions: migrated container, FU L2 seed; the fetch bound carries one mapped constituent (`ALFA`, in the `/members` double) and one the archive does not list (`GLRMK` — the dev frozen archive's genuinely-absent class); the family's verified list/detail routes stubbed for the mapped company only; a Serilog test sink captures the run's log events; `RequestDelayMs` = 0.
- Steps: run `dividends`, `corporate-actions` and `disclosures` in turn against that bound; read the fact tables, `coverage_metadata`, `quarantined_facts`, the run ledger (`ingest_runs`) and the WireMock request log.
- Expected: the loop **completes** past the unmapped symbol (no exception, no run 500); the mapped constituent's facts are stored with `provenance=kap` and its per-instrument cursor (`kap-dividends-cursor` / `kap-corporate-actions-cursor` / `kap-disclosures-cursor`) advances to the walked index; the unmapped symbol has **no** fact row and **no** cursor (never written under a fabricated identity); **zero** quarantine rows (identity isolation is not a parse event); the run ledger row records the skip in its per-symbol stats (`symbols[GLRMK].failed = true`, `written = 0`) and terminates **`partial`**, never `failed`; **exactly two** log events name `GLRMK` together with `COMPANY_IDENTITY_UNRESOLVED` — the adapter's per-symbol skip event (`… company GLRMK could not be mapped …`) and the job's single run-summary event (`… 1 bound symbol(s) isolated …: GLRMK`) — and no third event names the pair (amended v1.9, DR-1); and the walk made exactly one list request — for the **mapped** company only (the unmapped symbol is never fetched). *(Red before the fix: `CompanyIdentityUnresolvedException` propagated out of the adapter at the first absent symbol, so the run threw and recorded `failed`.)*
- Dependencies: TC-MDF-082.

## 11. Derived-fact lanes — the input-aware same-day recompute (TKT-foundation-050)

**Lane posture.** L2, canonical FU universe, doubles only: both jobs are driven through the
production `IIngestionJobRunner` over the FU §5 facts in a migrated Testcontainers PostgreSQL,
and the run's own counts (plus its `ingest_runs.stats_json` row) are the observable — no live
source, no wall clock (a `FakeTimeProvider` supplies the run instant, and a later instant on the
re-run makes "nothing was written" observable). Mirrors architecture §10.9.13 as extended by
v1.5.34 (the TKT-foundation-042 input-aware-recompute doctrine applied to `derived_metrics` and
`sector_metric_medians`).

**TC-MDF-088 — A same-day re-run of `metrics-recompute` and `medians` is input-aware: identical inputs are honestly `unchanged`, changed inputs repair in place and are counted** (new v1.10)
- Traces: FR-MDF-013/014 (recompute mechanics + honest run accounting), FR-MDF-015/NFR-MDF-002 (append-only, one row per key, historical immutability), FR-MDF-010/011 (honest achieved depth / run accounting), FR-RES-007/SD-002 (sector-median supply), BR-MDF-009, `01` v1.5.34 §10.9.13 (extended), TKT-foundation-042 heritage · Level: L2 · Design: state transition — first compute → identical re-run → changed-input re-run, on two jobs; decision table over stored row ∈ {absent, identical, input-changed} × run counts
- Preconditions: migrated container, FU L2 seed; `derived_metrics` / `sector_metric_medians` / `ingest_runs` cleared; a completed earlier trading day (T−1) computed first in every case so historical immutability is observable; the run clock is a `FakeTimeProvider` at T, and the re-runs advance it (+2 h) so a rewrite is visible in `computed_at`.
- Steps: (a) run `metrics-recompute` for T, then again for T with unchanged facts; (b) with T computed, change an upstream canonical fact (ALFA's serving FY `CAPEX` 150 → 100, so the FCF family leaves the stored values) and re-run `metrics-recompute` for T; (c) run `metrics-recompute` then `medians` for T, then `medians` again unchanged; (d) with T's medians computed, correct one upstream `derived_metrics` row (ALFA `pe` 10 → 20, moving sector A's P/E peer set from [10, 10, 20] to [10, 20, 20]) and re-run `medians` for T. Read the job results, the latest `ingest_runs` stats, and both fact tables.
- Expected: identical re-runs report `written: 0` with `unchanged` = the full row set (416 metric rows / 15 median rows — never the pre-ticket silent `0/0`), every stored row (values, windows, markers **and** `computed_at`) is untouched, exactly one row per `(instrument, metric, as_of_date)` / `(sector, metric, as_of_date)` and the historical day is byte-identical. Changed-input re-runs report the affected rows as **written** (the metrics lane: exactly the rows whose stored values no longer match the recompute — the FCF family for the CAPEX change; the medians lane: exactly one row, sector A P/E, now 20 with unchanged peer/exclusion counts) and the rest as `unchanged`, the repaired values are the current-fact values (EV 2200 / FCF 150, FCF yield 0.075), only the repaired rows carry the later `computed_at`, the run-ledger stats agree with the result, one row per key holds and the historical day is byte-identical. *(Red before the fix: 4/4 cases failed — the identical re-runs rewrote every row and moved `computed_at`, and the changed-input re-runs reported `written: 0` while overwriting the rows.)*
- Dependencies: —

## 12. Intraday refresh lane (TKT-foundation-047) — session sweeps and the intraday/close price-state contract

**Lane posture.** The intraday lane is a **sibling** of the EOD prices lane on the same
ingestion engine (architecture §10.9.18): job code `intraday-quotes`, cadence
`0 15 11,13,15,17 * * 1-5` Europe/Istanbul (11:15 / 13:15 / 15:15 / 17:15 TRT — four sweeps
two hours apart, the last 45 minutes before the 18:00 close), **Mon–Fri cron-gated** — the
`1-5` cron is the only day gate today, because `JobSchedules` entries register as per-job
streams with `TradingDaysOnly = false`; a Turkish weekday holiday therefore still sweeps until
TKT-foundation-048 wires the calendar in. Sequential
and paced (`Ingestion:Intraday:RequestDelayMs` = 500 → ≈400 calls/day at the 100-constituent
launch bound). Storage is the new `intraday_quotes` table — **never** `daily_prices`; one row
per (instrument, session), updated in place.

**Source is probe-gated, not asserted.** Whether İş Yatırım `HisseTekil` serves a *same-day*
row mid-session is not verifiable from code or captures (its envelope is daily-bar shaped;
043's progressive-publication evidence suggests it may not). The lane therefore rides the
existing envelope detection (İş `HGDG_*` **and** the documented Yahoo `.IS` chart contingency)
behind configuration only (`Ingestion:Intraday:BaseUrl`/`Path`, `{symbol}`/`{date}` tokens),
and the cases below pin **both** outcomes plus the honest no-data class. Doubles only — no
case reaches a live source; the mid-session probe and the live sweeps are the ticket's
post-merge **live criteria**, recorded verbatim on the ticket, not TCs.

**World anchor.** The API harness clock starts at 18:00 UTC on the fixture anchor T
(2026-10-06) and `FakeTimeProvider` cannot move backwards, so the mid-session state cases
read a **Wednesday 2026-10-07** session whose sweeps are inserted directly (the sweep itself
is TC-MDF-084's subject). Intraday cases in the sweep harness set their own clock (Monday
2026-10-05 / Tuesday 2026-10-06 sweeps).

**TC-MDF-084 — A paced session sweep stores quotes, counts no-data symbols honestly, and never writes `daily_prices`** (new v1.11)
- Traces: FR-MDF-001 (the intraday ingest mode), NFR-MDF-001 (operational cost), BR-MDF-009 (no derived display values), `01` v1.5 §10.9.18 / §10.9.1 / §10.9.2 · Level: L2 · Design: EP — source payload ∈ {İş same-day row, Yahoo delayed-quote chart, İş envelope without a session row} × run outcome, plus a config-constant assertion
- Preconditions: migrated container, FU L2 seed; the XU100 bound restricted to `ALFA`/`BETA`/`GAMA`; the intraday source retargeted at the WireMock double (`Ingestion:Intraday:*`, `RequestDelayMs` small but non-zero) with one route per symbol: `ALFA` = the real İş `HGDG_*` envelope carrying T−1 **and** T (the adapter must narrow the range body to the requested day), `BETA` = the Yahoo `.IS` chart envelope whose last observation of the session is the delayed quote and whose final grid interval carries a **null close** (an interval with no trade — the hole must not shadow the last available price; its `volume` array is present in the body), `GAMA` = the İş envelope with history but **no** T row; `intraday_quotes`, `daily_prices`, `quarantined_facts` and the ledger cleared; the swept session T = 2026-10-06.
- Steps: run `intraday-quotes` at a Monday-noon fake clock; read `intraday_quotes`, `daily_prices`, the run result, the Serilog test sink and the WireMock request log; time the run.
- Expected: run `succeeded` with `written = 2`, `unchanged = 0`, **`skipped = 1`**; exactly two `intraday_quotes` rows for T — `ALFA` with `last_price` 21.50, `day_volume` 1 234 567, `source_ref` `isyatirim`; `BETA` with 33.75 (the last **non-null** close — the trailing null interval is skipped, never stored as a price), **`day_volume` NULL** (`source_ref` `yahoo`; FLG-09(b): Yahoo's share count is not the stored ₺ semantic — an honest gap, never a mislabelled unit); both rows' `fetched_at` = the sweep's clock instant (UTC) and `session_date` = T; **no** `GAMA` row; **zero** `daily_prices` rows for T (an intraday value is never a close); exactly one log event containing `NO_DATA` and naming `GAMA`; exactly one HTTP request per bound symbol and the run's wall time ≥ 2 × the configured delay (a sequential, paced loop — lower bound only).
- Also asserted (same TC): the shipped `src/Api/appsettings.json` carries `JobSchedules[intraday-quotes].Cron` = `0 15 11,13,15,17 * * 1-5` (the scheduler's six-field form of the ticket's five-field cadence) and the `Ingestion:Intraday` section — İş `BaseUrl`, a `Path` with `{symbol}` and `{date}` (`TC-MDF-071`'s tokenized pattern), `RequestDelayMs` 500, `UniverseIndex` XU100 — so the two-hour interval and the Yahoo repoint are config changes, never code forks.
- Dependencies: —

**TC-MDF-085 — The price-state contract: `intraday` during and just after the session, `close` once the EOD lands, last completed session at the weekend** (new v1.11)
- Traces: FR-MOV-001/002/003 (overview price/index display), FR-MDF-001/016 (freshness language), FR-RES-005–018 (stock-page price header supply), `03` §1.2 (honest-data envelope) / §2/§4 (`changePct` is a decimal fraction), `01` v1.5 §10.9.18 · Level: L2 · Design: state-transition — intraday (mid-session) → intraday (closed, pre-EOD) → close (EOD stored) → close (weekend, last completed session); plus a per-surface check on both payloads
- Preconditions: migrated container, FU L2 seed with the market snapshot at T (2026-10-06); a **Wednesday 2026-10-07** session carrying `intraday_quotes` rows for `ALFA` (21.50) and `BETA` at `fetched_at` 2026-10-07T09:15Z (12:15 TRT); in the close branch additionally the session's `daily_prices` close (ALFA 20.60, `recorded_at` 2026-10-07T17:45Z) for every constituent; `daily_prices` rows after T cleared in the pre-close branches. The harness clock is set per case: 12:30 TRT (mid-session), 19:00 TRT (closed, pre-EOD), 21:00 TRT (post-EOD), Saturday 12:00 TRT.
- Steps: `GET /api/v1/market/overview` and `GET /api/v1/stocks/ALFA/overview` in each clock world; read `priceState`.
- Expected:
  - **(i) mid-session** — state `intraday`, `sessionDate` 2026-10-07, `asOf` = 2026-10-07T09:15Z; on the **stock page** `lastPrice` 21.50, `prevClose` = the previous stored session's close (ALFA at T = 20.00), `changePct` = `(21.50 − 20.00)/20.00` rounded to 6 dp (a decimal fraction, `03` §2/§4); on the **market overview** the three numerics are **NULL** — the lane stores no index-level intraday quote and aggregating one would be a derived display value (BR-MDF-009): honest NULLs, never a 0 reading.
  - **(ii) closed, pre-EOD (19:00 TRT)** — still `intraday`, `asOf` unchanged at the last sweep (the close-in-formation is never relabelled final), `lastPrice` 21.50.
  - **(iii) post-EOD (21:00 TRT)** — state `close` for 2026-10-07; on the stock page `lastPrice` = the stored `daily_prices.close_raw` 20.60, `prevClose` 20.00, `changePct` = `(20.60 − 20.00)/20.00` (6 dp); the overview's `asOf` = the served close row's own `recorded_at` (2026-10-07T17:45Z) — never the intraday sweep's stamp.
  - **(iv) weekend (Saturday)** — `close` of the last completed stored session (2026-10-06): a Saturday clock never fabricates a Saturday session (stored prices are the truth; 048 extends the calendar to holidays).
- Dependencies: —

**TC-MDF-086 — A re-sweep is in place: identical values count `unchanged`, a changed value overwrites and is counted `rewritten`, prior sessions stay immutable** (new v1.11)
- Traces: FR-MDF-001 (the intraday ingest mode), NFR-MDF-002 (append-only history), `01` v1.5 §10.9.18 (in-place per-sweep update — the 042 input-aware-overwrite precedent) · Level: L2 · Design: state transition — first write → identical re-sweep → changed re-sweep, with a prior session observed throughout
- Preconditions: migrated container, FU L2 seed; the bound restricted to `ALFA`; the İş same-day route stubbed per sweep. A prior-session sweep (Monday 2026-10-05, ALFA 19.90, volume 999 000) is run first and its row's `last_price`/`day_volume`/`fetched_at` captured. Then Tuesday sweeps at 11:15 (ALFA 21.50, volume 1 234 567), 13:15 (identical values) and 15:15 (ALFA 22.00, volume 2 000 000).
- Steps: run `intraday-quotes` for each sweep at its own fake clock; read `intraday_quotes` and the run results/stat log after each.
- Expected: first sweep `written = 1`, `unchanged = 0`; identical re-sweep `written = 0`, **`unchanged = 1`** with the stored `last_price` unchanged but `fetched_at` advanced to the second sweep's instant (the row records when the value was last *observed*); changed re-sweep `written = 0`, `unchanged = 0` and a run stat line carrying the `rewritten` counter with `{Rewritten} = 1` (the counter's value is pinned — the template alone is emitted on every sweep, including `rewritten 0`) — the row is **overwritten in place** (still exactly one row for instrument × session, `last_price` 22.00, `day_volume` 2 000 000, `fetched_at` = the third sweep); the Monday row's `last_price`, `day_volume` and `fetched_at` are **byte-identical** afterwards (historical sessions immutable).
- Dependencies: —

### Implementation note for the current lane (recorded with v1.11)

`IngestResult` exposes `written`/`unchanged`/`skipped` only, and `src/Ingestion/Jobs/` is
outside this ticket's scope (043's lane). The `rewritten` count is therefore surfaced as a
run **stat log line** (`rewritten {Rewritten}`) rather than a result field; folding it into
the ledger stats is a later ticket's concern. Likewise the market overview's NULL
`priceState` numerics are a recorded contract choice for this lane (stock page carries the
full numeric block), flagged for the architecture document's §10.9.18 note and for the Web
half (TKT-foundation-049).

## 13. Event-family per-disclosure resilience (TKT-foundation-061, architecture v1.5.40 §10.9.15)

**TC-MDF-094 — Event-family per-item isolation: an unfetchable disclosure detail yields an honest partial, never a failed run** (new v1.12)
- Steps: for each of the three event families (dividends, corporate-actions, disclosures — the 034/046 verified-route doubles), bind a walk set in which ONE disclosure detail returns an HTTP 500 fault envelope from the doubled `/disclosureDetail/{id}` endpoint (the shape of Thursday 2026-10-22's live gateway 500 on index 1185709) while every other detail returns its normal double body. Run the family job through the production runner. Read the run result, the `ingest_runs` ledger stats, the quarantine table, and the family fact tables.
- Expected: the walk completes — the unfetchable item is **quarantined with evidence** (the gateway's error verbatim in the quarantine record) and **counted in the run stats with its named reason** (`DISCLOSURE_DETAIL_UNFETCHABLE`-class, the disclosure index recorded); **all fetchable items' rows/cursors are written**; the run terminates an **honest partial** — never `failed`, never a silent omission. At chain level, a partial family step does not block `metrics-recompute`/`market-snapshot`/`medians` (the composition asserted in the chain test lane). *(Red before the fix: the `SourceFetchException` propagates out of the shared `VykDisclosureWalk` into the adapter and job → run `failed` → the EOD chain fail-stops before the snapshot steps — the 2026-10-22 evidence, runs 185/190/195.)*
- Dependencies: —

**TC-MDF-095 — Per-item hang bounded: a stalled detail fetch is timed out and takes the same honest skip path** (new v1.12)
- Steps: with the per-item fetch timeout configured (the ticket's config constant, a low-minutes default), double ONE disclosure detail endpoint that accepts the connection and never responds (the 90-minute hang shape of runs 185/190). Run the family job; assert the wall-clock bound of the stalled item's fetch, the skip path it takes, and the integrity of the rest of the walk. Separately, run a healthy paced walk (the ~12 s/item pace of run 173) under the same timeout and assert no false timeout.
- Expected: the stalled item's fetch is **bounded by the configured per-item timeout** and then takes the **identical quarantine-and-continue skip path as the 500 case** (named reason, evidence, counted); the run completes honest-partial with every fetchable item's rows written. A healthy paced walk is **never falsely timed out** (the timeout constant vs the pace asserted). *(Red before the fix: the hang propagates to the run level, where only the 041 watchdog bounds it — a `RUN_HUNG` row and a lost whole run, the 2026-10-22 evidence.)*
- Dependencies: TC-MDF-094.

## 14. Share-count store from VYK `/memberSecurities` (TKT-foundation-058; supersedes the TKT-foundation-033 goal)

**Lane posture.** Both cases are **L2** and **doubles-only, never live sources**: the `/memberSecurities`
payload is the committed real-shape double `CannedSourceCatalog.MemberSecuritiesOk`, derived from the
live 2026-10-22 dev-gateway capture (the `/tmp` snapshot is ephemeral and is never referenced). The
double carries the real archive's shapes — multi-tertip members whose lines sum to the real counts
(ASELS A+B = 4.56B, THYAO C+A = 1.38B, ISDMR A+B = 2.9B), a trading-open flag per line, an
ISIN-fallback member, a closed-only member, a codeless/odd member and no-line-at-all members — plus
two **clearly labeled derived anomalies** (a line with no `currentCapital` in a group, and a sole line
with no `currentCapital`), because the capture happened to have zero such lines while the ticket's
pinned contract requires the skip path to be exercised. The store is driven through the production
`IIngestionJobRunner` over a migrated Testcontainers PostgreSQL; the **live unit/aggregation
cross-validation** (ASELS/THYAO/TUPRS/GARAN/AEFES) is the ticket's post-merge orchestrator step, not
this suite's job — the suite pins the decode/aggregation/store contract against the capture's shapes.

**TC-MDF-092 — The `shares` lane decodes `/memberSecurities` into an honest per-instrument store** (new v1.13)
- Traces: FR-MDF-002/003 (VYK gateway ingest — the new endpoint), FR-MDF-008 (provenance), FR-MDF-010/011 (skips counted, never hidden), `01` v1.5.36 §10.9.11 (the endpoint + `shares` lane) · Level: L2 · Design: decision table — security-line class ∈ {single trading-open line, multi-tertip mixed open/closed, multi-tertip all open, closed-only, no line at all, sole line missing `currentCapital`, group with one line missing `currentCapital`} × store outcome; plus state transitions first run → identical re-run → changed-input re-run, and the failure posture
- Preconditions: migrated container, FU L2 seed; the double's instruments bound by symbol (`ASELS`, `THYAO`, `TUPRS`, `GARAN`, `AEFES`, `ISDMR`, `DRVFM`, `OTHER`, `GLRMK`, `CLSDR` — `DRVFM` carrying the fallback line's ISIN, `OTHER` carrying `ASELS`'s ISIN as a decoy); `CannedSourceCatalog.MemberSecuritiesOk` served on `/memberSecurities` through WireMock; `RequestDelayMs` = 0; `FakeTimeProvider` at T, advanced to T+12h for the repair re-run.
- Steps: (i) run `shares`; read `share_counts`, the `ingest_runs` row and `quarantined_facts`. (ii) read the WireMock request log for `/memberSecurities` before/after two runs. (iii) re-run unchanged; then change one tertip (`2138181818.184` → `2138181819.184`, ASELS's B line) and re-run. (iv) stub HTTP 500; then stub a body that is not the memberSecurities shape.
- Expected: (i) the run is `succeeded` with `written: 7` / `skipped: 2`; one row per **matched** instrument with `shares_outstanding` = the sum of that instrument's security lines' `currentCapital` — ASELS 4,560,000,000 (A closed + B open), THYAO 1,380,000,000, TUPRS 1,926,795,598, GARAN 4,200,000,000, AEFES 592,105,263, ISDMR 2,900,000,000 (both tertips open), DRVFM 1,234,567 via the **ISIN fallback**; every row carries `source_ref = kap` and `recorded_at` = the run instant; matching is `borsaKodu`-primary, so `OTHER` (sharing `ASELS`'s ISIN) is **never** a candidate and gets no row; **no row** for `GLRMK` (no line) or `CLSDR` (only closed lines) and **no zero row anywhere**; the two `currentCapital`-less lines are counted as `skipped: 2`, `NOCAP` (sole line) gets no row and `DRVE`'s matched group is **withheld** (a partial tertip sum would be a fabricated count) — never `0`; the skip count rides `ingest_runs.stats_json` and the run stays `succeeded` (the gap is counted, not hidden behind a failed run). (ii) exactly **one** paced `/memberSecurities` call per run — no per-instrument loop, no second request; two runs = two requests; the code default path is `/memberSecurities`. (iii) the identical re-run writes nothing (`written: 0`, `unchanged: 7`, `skipped: 2`) and every `recorded_at` stays put — the served `asOf` does not lie; the changed-input re-run reports `written: 1` / `unchanged: 6`, the repaired row carries the new value (4,560,000,001) and the later instant, the untouched rows keep theirs, and one row per instrument holds. (iv) HTTP 500 → the run row is `failed`, no `share_counts` row and **no** quarantine (transport is not a parse event); a non-memberSecurities body → `UNPARSEABLE_PAYLOAD` quarantine, no rows.
- Dependencies: —

**TC-MDF-093 — The balance-sheet payload serves the stored share count and honest-null when absent** (new v1.13)
- Traces: FR-MDF-011 (public-side honest surface), FR-MOV-006 lane (the downstream market-P/E unblock — seam only), TKT-foundation-031/052 doctrine, `01` v1.5.36 §10.9.11 (serving seam) · Level: L2 · Design: decision table — instrument ∈ {stored count, no stored count} × served field
- Preconditions: migrated container, FU L2 seed, `derived_metrics` recomputed at T; `share_counts` cleared and one row seeded directly for `ALFA` (4,560,000,000 at T 06:00Z, `source_ref = kap`) — the ingest path is TC-MDF-092's observable, this case pins the seam; `LAMDA` (current XU100 constituent with a FY balance sheet) has **no** stored count.
- Steps: `GET /api/v1/stocks/ALFA/balance-sheet`; `GET /api/v1/stocks/LAMDA/balance-sheet`.
- Expected: both are `200` with the public cache headers and the honest envelope unchanged (`asOf` = T). `ALFA.shareCount.sharesOutstanding` = 4,560,000,000 and `ALFA.shareCount.asOf` = T's date. `LAMDA.shareCount.sharesOutstanding` and `.asOf` are JSON `null` — honest absence, never `0` (a dash downstream). The **statement-reported** `lines.sharesOutstanding` / `lines.sharesDiluted` and `bookValuePerShare` are byte-identical to TC-RES-012's goldens (the market count is an additive field, not a replacement) — the market-data lane never rewrites the statement's own figures.
- Dependencies: TC-MDF-092 (the store's shape).

*Change record: v1.0 2026-10-06 — initial MDF test plan + shared fixture universe (`01-fixture-universe.md`), submitted at Gate 2. v1.1 2026-10-06 — builder decisions applied (Q1 quarantine rule → TC-MDF-052; Q2 formula locked; Q3/Q6 deferred; Q4 confirmed; Q5 noted); coverage matrix updated. v1.2 2026-10-07 — builder final review: Q3 rights-issue factor defined → **TC-MDF-053** (FU §2/§11.6); Q5 storage settled (window-suffixed codes; plain (instrument, metric, as_of_date) key → TC-034); upstream `02` mirror-update gaps flagged; recorded interpretation for degenerate rights inputs; coverage matrix updated. v1.3 2026-10-13 — **§14 change-propagation (builder-approved via the TKT-mdf-012 review round-1 escalation, routing item 1, option A): TC-MDF-047, TC-MDF-048, TC-MDF-049 re-leveled L4 → L3 (Vitest + MSW).** Reason — recorded harness limitation (review-lead-verified): compose.ci seeds no open quarantined fact; the admin trigger endpoint is synchronous, so no non-terminal ingest run is observable in the browser; no slow source is wired into the compose stack. Steps, expected results and traces unchanged; only the level and the per-case justification were added. Affected ticket recorded for re-validation: **TKT-mdf-012** (its C1/C2 findings resolve against this record; the L3 tests on PR #55 become the canonical level). Strategy §5.4 lists admin critical flows 1–8 under e2e coverage — flows 2/3/4 (these three TCs) are now covered at L3; flagged for the test-planner's next strategy pass. **This v1.3 record was applied by the delivery planner on the builder's explicit instruction and requires test-planner confirmation** (strategy §13/§14 — the test-planner owns this document and its change-propagation log).* **v1.4 2026-10-15 — post-board correctness cases (§7) added: TC-MDF-054..062, tracing the builder's verified feedback (statement versioning, unit/currency normalization, financial-profile posture, adjusted per-share CAGRs, `OCF`-based FCF, TMS 29 regime flag, price-source provenance, constituents seed) against `02` v1.3 / `03` v1.4 / FU v1.4. Affected tickets: TKT-mdf-013..022, TKT-mov-008/009, TKT-val-010, TKT-fdf-003, TKT-foundation-015. Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending.* **v1.5 2026-10-17 — real-source parsing-fidelity & live-activation cases (§8) added, mirroring architecture v1.5 §10.9 (AD-14; builder instruction 2026-10-17 via the delivery planner — closes the PR #82 / TKT-foundation-015 round-1 escalation findings DR-01..03: product-path assertion rule, honest catalog labeling, duplicate-concept quarantine overturning latest-wins). TC-MDF-063..068 re-authored (IDs kept — they were burned in test source on the closed branch; content now asserts raw captured bytes through the WireMock harness into the production `src/Ingestion` adapters); TC-MDF-069..076 new (envelope detection, transport-failure class, duplicate-concept rule, per-ticker loop, failure isolation, pacing, universe bound, XU030 launch-seed). TC-MDF-061 amended (FLG-09: Yahoo-contingency volume NULL). §1 scope, §5 matrix, §6 volume-semantics entry updated. FU mirrors: §1 exception, §2/§4 ₺ reinterpretation, §10b real-source catalog lane (FU v1.5); MOV mirror: TC-MOV-028 (MOV plan §8, FU §10b lane). Goldens untouched — no re-derivation (§10.9.7). Affected tickets: the new wave **TKT-foundation-015..019** (TC mapping in §8); TC-MDF-061's amendment flags the v1.4 price-source ticket among TKT-mdf-013..022 for planner re-validation. Applied by the test-planner at the delivery planner's request, builder-directed (architecture v1.5 §10.9 authority); the v1.3/v1.4 pending-confirmation notes above are unchanged by this entry.** **v1.6 2026-10-20 — §9 morning-cadence chain cases added (TC-MDF-077/078)**, mirroring architecture v1.5.15 §10.9.16 / TKT-foundation-040: the morning stream's scheduling + previous-trading-day run date + unchanged retry ladder (077), and the stored-prices repair gate's two branches — fully covered day skipped at zero İş Yatırım cost with the snapshot no-op, failed night repaired end-to-end and served by `GET /market/overview` (078). Implemented as `tests/Api.IntegrationTests/MorningChainTests.cs` (red→green evidence is recorded in the PR body — PR #110: 4 failed / 1 passed before the implementation, the four failing on `Actual: []`; the ticket carries no red→green section). Phase-2 event-family criteria are recorded but not executable until TKT-foundation-034 merges. Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending.**
 **v1.7 2026-10-21 — §10 VYK company-identity cases added: TC-MDF-082 (the `/members` comma-split — every ticker of a comma-joined `stockCode` resolves, codeless auditor members are skipped, duplicates are last-wins with a logged same-code/different-id conflict, and a `"GARAN, TGB"` constituent resolves end-to-end through the family walk) and TC-MDF-083 (per-constituent identity isolation — an unmapped bound symbol is skipped, named `COMPANY_IDENTITY_UNRESOLVED`, counted in the run's per-symbol stats and never fetched, while the rest of the bound ingests and the run ends `partial`), mirroring architecture v1.5.22 §10.9.15 / TKT-foundation-046. Doubles only — the `/members` double is committed with the tests and derived from the live 2026-10-20 dev-gateway capture (the ephemeral `/tmp` snapshot is not referenced), and the family walk keeps the 034 verified-route doubles. Implemented as `tests/Api.IntegrationTests/VykCompanyDirectoryTests.cs` (new) plus two additions to `StatementsAndEventsIngestionTests.cs`; red→green evidence is recorded in the ticket's PR body (7 failed / 0 passed on the pre-fix behaviour, 7/7 green after). Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending (the v1.2/v1.3 precedent).** **v1.8 2026-10-20 — §9 chain-orchestration reliability cases added (TC-MDF-079/080)**, mirroring architecture v1.5.23 §10.9.17 / TKT-foundation-041: the hung-run watchdog (079 — a doubled chain step that never completes, throws, or self-cancels; the watchdog's `RUN_HUNG` row + alert naming the step; the independent stream still waking; the retry recovering the chain) and `universe-sync` leading the EOD chain (080 — pinned nine-job order, the day's index levels written by the run itself with no manual trigger, idempotent re-run). Implemented as `tests/Api.IntegrationTests/EodStallWatchdogTests.cs` (079) and `EodChainTests.cs` (080, plus the one-leading-job chain-order golden delta); `SchedulerTests`, `MorningChainTests`, `EodStaleServingTests`, `MacroIngestionTests` and `UniverseSyncTests` re-run green. Red→green evidence is recorded in the PR body (pre-fix: the hung tick never returns and the self-cancelling step escapes `TickAsync`). Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending.*
 **v1.9 2026-10-22 — TC-MDF-083's expected result amended to the shipped two-event log shape (TKT-foundation-054, 046 review round 1 DR-1).** The v1.7 text pinned "exactly one log event names `GLRMK` together with `COMPANY_IDENTITY_UNRESOLVED`"; the shipped, reviewer-verified code deliberately emits **two** honest events — the adapter's per-symbol skip (`… company GLRMK could not be mapped …`) and the job's single run-summary (`… 1 bound symbol(s) isolated …: GLRMK`) — and the shipped assertion only checked "at least one", so the test was weaker than its own pin. The expected result now pins exactly those two events (one of each form, no third), and `StatementsAndEventsIngestionTests`'s `TC-MDF-083` theory asserts the count and both forms. No behaviour, level, precondition or trace changes — the code was correct, the pin was wrong. The one 046 wording correction this ticket also carries (DR-3: 046's ticket text reconciled to §10.9.15's always-`partial`) is a ticket-file amendment, not a test-plan amendment; §10.9.15 is unchanged. Applied in TKT-foundation-054's implementation lane from the planner-sliced post-merge routing of 046's review item DR-1 (the v1.2/v1.3 precedent); test-planner confirmation pending.
 **v1.10 2026-10-22 — §11 derived-fact-lane case added: TC-MDF-088** (the input-aware same-day recompute for `metrics-recompute` and `medians`), mirroring architecture v1.5.34 §10.9.13 (extended by reference — the TKT-foundation-042/v1.5.18 doctrine applied to `derived_metrics` and `sector_metric_medians`) / TKT-foundation-050. Identical same-day re-runs report an honest `unchanged: N` and write nothing (no `computed_at` movement); re-runs after an upstream fact changes repair the affected rows in place and count them as written — never the pre-050 blind overwrite (all rows rewritten, all reported "unchanged") or the silent `written: 0` repair. L2 doubles only through the production job runner; implemented as `tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs` (new). Red→green evidence is recorded in the ticket's PR body (4 failed / 0 passed on the pre-fix behaviour, 4/4 green after; the full backend suites re-run green — 450/450 integration + 54/54 unit). Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending (the v1.2/v1.3 precedent).**
**v1.11 2026-10-22 — §12 intraday refresh lane cases added: TC-MDF-084 (a paced session sweep over the İş same-day row / the Yahoo `.IS` delayed-quote contingency / an envelope without a session row — two rows stored, the no-data symbol counted and logged, `daily_prices` untouched, Yahoo volume honest NULL — plus the shipped cadence and source config constants), TC-MDF-085 (the `priceState` contract on both `GET /market/overview` and `GET /stocks/{symbol}/overview`: `intraday` mid-session with `changePct` vs the previous close, still `intraday` with its last `asOf` between the 18:00 close and the EOD chain, `close` from `daily_prices` once the EOD lands, and the last completed session at the weekend; market-overview numerics honestly NULL — no index-level intraday quote exists in this lane, BR-MDF-009), and TC-MDF-086 (in-place re-sweep: identical → `unchanged` with an advanced `fetched_at`, changed → overwrite + `rewritten` stat, prior sessions immutable), mirroring architecture v1.5.27 §10.9.18 / TKT-foundation-047. Doubles only — the source is **probe-gated** by design, so every case pins both envelope outcomes and the mid-session probe plus the live sweeps remain the ticket's post-merge live criteria (recorded verbatim on the ticket), never TCs. Implemented as `tests/Api.IntegrationTests/IntradayQuotesTests.cs` (new) plus three additions to `MarketOverviewFreshnessTests.cs` and three to `StockOverviewAndValuationEndpointsTests.cs`; red→green evidence is recorded in the ticket's PR body (9 failed / 1 passed before the implementation — the config-constant case passing because the shipped `appsettings.json` guard is the implementation's own artifact — 10/10 green after). The `priceState` shape is the frozen seam shared with TKT-foundation-049 (Web half, disjoint files). Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending.**
 **v1.12 2026-10-23 — §13 event-family per-disclosure resilience cases added: TC-MDF-094/095**, mirroring architecture v1.5.40 §10.9.15 / TKT-foundation-061 (the 2026-10-22 unattended-EOD-chain evidence: a per-disclosure gateway 500 and a per-disclosure hang fail-stopped the chain before the snapshot steps). TC-MDF-094 pins the per-item isolation — an unfetchable detail is quarantined with evidence (the gateway error verbatim), counted with its named reason, and the run ends an honest partial while all fetchable items are written, never `failed` and never silent omission; TC-MDF-095 pins the bounded per-item fetch timeout — a stalled detail is timed out and takes the same skip path, while a healthy paced walk is never falsely timed out. The isolation and the timeout are implemented in the shared walker (`src/Ingestion/Kap/VykDisclosureWalk.cs` — the 061 scope amendment of 2026-10-23; the adapters cannot express per-item isolation). L2 doubles only through the production job runner; implemented in `tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs` (061's scope). This entry repairs a planning defect: 061 originally referenced the cases as applied before they were authored (the plan's highest case was TC-MDF-088) — authored here on the coder's correct block. Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending (the v1.2/v1.3 precedent).**
 **v1.13 2026-10-22 — §14 share-count store cases added: TC-MDF-092** (the `shares` lane's decode + honest store from VYK `/memberSecurities`) **and TC-MDF-093** (the balance-sheet serving seam), mirroring architecture v1.5.36 §10.9.11 / TKT-foundation-058 (which supersedes the TKT-foundation-033 goal — the source was never the XBRL disclosure layer, it is the gateway's `/memberSecurities`). The lane writes one row per **matched** instrument, the value being the sum of that instrument's security lines' `currentCapital` (the tertip-aggregation rule the live ASELS A+B = 4.56B / THYAO C+A = 1.38B / ISDMR 2.9B shapes evidence); matching is `borsaKodu`-primary / `isin`-fallback, keyed on a trading-open line, and an instrument with no line — or only closed lines — gets **no row**, never a zero (the 031/052 doctrine). A line without `currentCapital` is skipped and counted, and its matched group is withheld rather than served as a partial sum. The re-run is input-aware (identical → `unchanged` and no `asOf` movement; changed → repaired in place and counted). The serving seam adds an **additive** `shareCount { sharesOutstanding, asOf }` block to `GET /api/v1/stocks/{symbol}/balance-sheet` — honest-null when absent, never `0` — leaving the statement's own `lines.sharesOutstanding`/`sharesDiluted`/BVPS figures untouched. Doubles only, through the production job runner and the L2 harness; the committed `CannedSourceCatalog.MemberSecuritiesOk` double is derived from the 2026-10-22 dev-gateway capture (the ephemeral `/tmp` snapshot is not referenced) and carries two explicitly labeled **derived anomalies** (no-`currentCapital` lines) because the capture itself had none while the contract requires the skip path exercised. Implemented as `tests/Api.IntegrationTests/MemberSecuritiesIngestTests.cs` (new, 10 cases after the review-round-1 additions — the trading-open flag's string/numeric spellings, the single-object member body, and the withheld-group stale-row removal) plus one case in `StockFundamentalsEndpointsTests.cs`; red→green evidence is recorded in the ticket's PR body (7 failed / 1 passed on the pre-implementation behaviour, 8/8 green after; the golden-stability suites — `StockFundamentalsEndpointsTests` 18/18, `CannedSourceCatalogTests`/`VykCompanyDirectoryTests`/`VykStatementsSourceTests` 27/27 — re-run green). Coverage matrix: TC-MDF-092 → FR-MDF-002/003/008/010/011, TC-MDF-093 → FR-MDF-011. The **live unit/aggregation cross-validation** (ASELS/THYAO/TUPRS/GARAN/AEFES served values vs the known real counts, one paced `/memberSecurities` fetch on the dev stack) is the ticket's post-merge orchestrator step and is recorded on the ticket, not asserted here. Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending (the v1.2/v1.3 precedent).
 **v1.14 2026-10-23 — §13 implemented and one 034 expectation amended (TKT-foundation-061's implementation lane; §14 change-propagation).** TC-MDF-094/095 are implemented in `tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs` against L2 doubles only — the HTTP-500 case is a WireMock detail stub, the hang case a WireMock delayed response, never a live gateway call. The isolation and the bounded per-item fetch live in the shared `VykDisclosureWalker` (`VykDetailSkip` / `DISCLOSURE_DETAIL_UNFETCHABLE`, `IDisclosureIsolationReporter`, `Ingestion:Kap:DetailFetchTimeoutMs` defaulting to `KapSourceOptions.DefaultDetailFetchTimeoutMs` = 180 s). Red→green: 6 failed / 1 passed on the pre-061 walker (the `SourceFetchException` escaped `WalkAsync` and the run recorded `failed` — the 2026-10-22 chain fail-stop), 7/7 green after; `StatementsAndEventsIngestionTests` re-runs 39/39 and the neighbours stay green (`VykCompanyDirectoryTests`, `EodChainTests`, `MorningChainTests`, `SchedulerTests`, `EodStallWatchdogTests`, `MarketSnapshotTests`, `MetricsRecomputeIdempotencyTests`). **One pre-listed delta:** `TKT_foundation_034_detail_404_mid_walk_is_an_honest_source_failure` pinned the superseded posture (a detail-404 mid-walk → failed run, no rows, no quarantine), which architecture v1.5.40 / TC-MDF-094 replaces with per-item isolation (skip quarantined with the gateway's 404 verbatim, the fetchable sibling written, the run `partial`). The amended case keeps the 034 intent — a 404 detail is never an honest empty and nothing is silently dropped — and asserts strictly more (evidence + sibling row + counted skip); its id and tag are unchanged. Applied by the 061 implementation lane under the delivery planner's authored cases; test-planner confirmation pending (the v1.2/v1.3 precedent).
