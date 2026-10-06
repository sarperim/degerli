# Market Data Foundation Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** v1.2 — approved (Gate 2 + builder final review 2026-10-07)
**Derives from:** `analysis/market-data-foundation.md` (FR-MDF-001..019, UC-MDF-001..005, NFR-MDF-001..006) · `ux/market-data-foundation.md` (SCR-012, UXR-MDF-001..028 — shared admin surface only) · `architecture/` v1.2 (`01` C3/C5, `02` §3.1/§6, `03` §8–§9) · strategy `00-test-strategy.md` (approved) · fixture contract `01-fixture-universe.md` (submitted with this plan).
**TC ID convention:** `TC-MDF-NNN`, permanent. Levels: L1 unit · L2 integration (Testcontainers Postgres + WireMock sources + fake TimeProvider + mail/Serilog doubles) · L4 e2e (compose stack + MailPit + Playwright). All fixtures referenced as `FU §n`.

## 1. Scope

**Tested here:** FR-MDF-001..016, 018, 019 (18 non-Won't); UC-MDF-001..005 including every alternate/error flow; NFR-MDF-001..006 (005 partially via the XC reproducibility case, 006 via the manual ledger); SCR-012 shared admin infrastructure: UXR-MDF-001..011, 013..017, 026..028 (19 of the 28 admin UXRs).

**Not tested here (and why):** FR-MDF-017 (backtesting — Won't); the admin description queue/editor/publish UXRs (018..022 → RES plan), metric-visibility UXRs (023/024 → SCR plan), baseline regeneration (025 → VAL plan), fund coverage rows (012 → FDF plan); real-source behavior, cost NFRs, and the 23:59 real-data completion budget (operational — §4); reproducibility quickstart (TC-XC, cross-referenced).

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
- Traces: UXR-MDF-007/008/009/010 · Level: L4 · Design: state transition — open → (invalid dismiss) → open → (valid dismiss) → dismissed; nav to ledger
- Steps: open queue → inspect an item's payload → attempt dismiss with empty note (inline validation blocks) → dismiss with a note → view dismissed filter → navigate from an item to its job's run-ledger view.
- Expected: empty note blocked inline with visible rule; dismissal moves the item and updates the summary's open count without manual reload; dismissed item reviewable with note; ledger view arrives filtered to that job.
- Dependencies: —

**TC-MDF-048 — Run-ledger auto-refresh during an in-progress run**
- Traces: UXR-MDF-015/028 (flag ruling 4), mutation matrix row 12 · Level: L4 · Design: state transition — idle → running (slow source) → updates visible
- Preconditions: WireMock serves `prices-source-slow.json` (5 s delay) to the compose stack.
- Steps: trigger the `prices` job from the admin UI (confirmation names the job); observe the open ledger view.
- Expected: the triggered run appears in the ledger without manual refresh; while non-terminal the view updates periodically with a visible periodic-update indication; the manual refresh control is present and works; on completion the summary reflects it on next load (not auto-refreshed — ops summary deliberately doesn't).
- Dependencies: —

**TC-MDF-049 — Job trigger and backfill confirmations**
- Traces: UXR-MDF-016/017, UXR-G-022 pattern · Level: L4 · Design: decision table — confirm/cancel × backfill on/off
- Expected: trigger requires an explicit confirmation naming the job; backfill mode confirmation names the job **and** the start date; cancel path aborts with no run created; pending lock on the trigger (UXR-G-002/020).
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
| FR-MDF-001 | main | TC-001, 002 | planned |
| FR-MDF-002 | main | TC-003 | planned |
| FR-MDF-003 | main | TC-004 | planned |
| FR-MDF-004 | main | TC-005 | planned |
| FR-MDF-005 | main | TC-006 | planned |
| FR-MDF-006 | main | TC-007, 038 | planned |
| FR-MDF-007 | main | TC-037 | planned |
| FR-MDF-008 | main | TC-008, 009 | planned |
| FR-MDF-009 | main | TC-010, 011 | planned |
| FR-MDF-010 | main | TC-015, 016, 043 | planned |
| FR-MDF-011 | main | TC-017, 018 (public side → RES plan) | planned |
| FR-MDF-012 | main | TC-019, 020, 021, 025, 052 | planned |
| FR-MDF-013 | main | TC-026, 031, 034 | planned |
| FR-MDF-014 | main, short-history | TC-027, 033 | planned |
| FR-MDF-015 | main | TC-035, 052 (backstop 009, 034) | planned |
| FR-MDF-016 | main | TC-013, 014, 040, 046 | planned |
| FR-MDF-018 | main | TC-029, 030, 053 | planned |
| FR-MDF-019 | main | TC-032 | planned |
| UC-MDF-001 | main; alt a; alt b | TC-010; TC-012, 013; TC-019, 020, 024, 052 | planned |
| UC-MDF-002 | main; alt a | TC-015, 043; TC-016 | planned |
| UC-MDF-003 | main; alt a | TC-037, 038; TC-025 | planned |
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

## 6. Decisions record (builder rulings, 2026-10-06) and deferred items

- **Q1 — Conflicting price re-publication → RESOLVED (adopted recommendation):** a valid value conflicting with an already-stored (instrument, date) fact is quarantined with reason `CONFLICTING_VALUE` + alert; identical re-sends remain idempotent no-ops. Test case: **TC-MDF-052**.
- **Q2 — Sector daily performance formula → SET (test-planner, per builder):** cap-weighted average of members' daily % changes. Formula and goldens locked in `01-fixture-universe.md` §2/§11.3; asserted in the MOV plan.
- **Q3 — Rights-issue adjustment factor → RESOLVED (recorded 2026-10-07 on the builder's final review):** TERP-based factor `(P_C + q·P_S)/((1+q)·P_C)` — P_C = last raw close on/before the action's effective date, P_S = subscription price, q = new shares per 1 held; reduces to the bonus factor `1/(1+q)` at P_S = 0 and to `1` at P_S = P_C. Golden + composition in FU §2/§11.6; test case **TC-MDF-053**. The architect was asked to define it but its dispatch repeatedly timed out; the formula is contract-defined here for implementation and flagged for the `02` corporate-action section to mirror (upstream-doc gap, 2026-10-07).
- **Q4 — CAGR short-history reading → CONFIRMED:** compute from available history whenever ≥2 FYs, `window_years` = intervals actually used; the UX "no-data/shorter-history state" is the actual-window indication. TC-033 final.
- **Q5 — Windowed CAGR storage → RESOLVED (recorded 2026-10-07):** **window-suffixed metric codes** (e.g. `rev_cagr_3y`, matching FU §11.1's code names) — the metric model stays flat; the idempotency/upsert key is plainly `(instrument, metric, as_of_date)` (TC-034; no window dimension). Flagged for the `02` §6 metric-storage section to mirror (upstream-doc gap, 2026-10-07).
- **Q6 — Membership removal mechanism → DEFERRED (builder, 2026-10-06; confirmed 2026-10-07):** TC-037 asserts the observable history/current-universe contract; `effective_to`-close vs. new-row remains the implementation's choice.
- **Recorded interpretations (no decision needed):** unknown job code → 404 (TC-042); Mon–Fri trading days in fixtures, holiday calendar config-only; quarantine reason-code catalog defined by FU §2; median even-count = mean of middles; TTM = FY with ALFA's ΣQ consistency fixture; a rights action with q ≤ 0 or P_C ≤ 0 is invalid (rejected by validation, never a factor) — TC-053 column (e), plan-specified.

---
*Change record: v1.0 2026-10-06 — initial MDF test plan + shared fixture universe (`01-fixture-universe.md`), submitted at Gate 2. v1.1 2026-10-06 — builder decisions applied (Q1 quarantine rule → TC-MDF-052; Q2 formula locked; Q3/Q6 deferred; Q4 confirmed; Q5 noted); coverage matrix updated. v1.2 2026-10-07 — builder final review: Q3 rights-issue factor defined → **TC-MDF-053** (FU §2/§11.6); Q5 storage settled (window-suffixed codes; plain (instrument, metric, as_of_date) key → TC-034); upstream `02` mirror-update gaps flagged; recorded interpretation for degenerate rights inputs; coverage matrix updated.*
