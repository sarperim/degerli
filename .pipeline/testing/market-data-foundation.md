# Market Data Foundation Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** v1.6 — §9 morning-cadence chain cases (TC-MDF-077/078) added 2026-10-20 (TKT-foundation-040, architecture v1.5.15 §10.9.16; delivery-planner-applied, test-planner confirmation pending) · v1.5 — §8 real-source parsing-fidelity & live-activation cases added 2026-10-17 (builder-directed via the delivery planner; architecture v1.5 §10.9 authority; applied by the test-planner — see change record) · v1.4 §7 post-board correctness cases 2026-10-15 (delivery-planner-applied; test-planner confirmation pending) · v1.3 §14 change-propagation 2026-10-13 (TC-MDF-047/048/049 re-leveled L4→L3, builder-approved; confirmation pending) · v1.2 approved (Gate 2 + builder final review 2026-10-07)
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
| FR-MDF-001 | main | TC-001, 002, 064, 068, 069, 070, 072, 073, 074, 075 | planned |
| FR-MDF-002 | main | TC-003, 065, 067, 071 | planned |
| FR-MDF-003 | main | TC-004 | planned |
| FR-MDF-004 | main | TC-005 | planned |
| FR-MDF-005 | main | TC-006 | planned |
| FR-MDF-006 | main | TC-007, 038, 075 | planned |
| FR-MDF-007 | main | TC-037, 075, 076 | planned |
| FR-MDF-008 | main | TC-008, 009, 063 | planned |
| FR-MDF-009 | main | TC-010, 011 | planned |
| FR-MDF-010 | main | TC-015, 016, 043, 068 | planned |
| FR-MDF-011 | main | TC-017, 018, 076 (public side → RES plan) | planned |
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
- Traces: FR-MDF-001, FR-MDF-010/011 (coverage posture), NFR-MDF-001, FR-MOV-001..006/021, NFR-MOV-001, `01` v1.5 §10.9.13 + §10.9.16 · Level: L2 · Design: decision table — stored coverage ∈ {complete, none} × gate outcome; end-to-end serving check
- Preconditions: FU L2 seed; the morning cron and EOD cron both configured. (a) a fully succeeded night: the EOD chain runs at 20:30 for 2026-10-05, so the day is fully covered by stored prices and already snapshotted. (b) a failed-night world: the stored prices for 2026-10-05 are absent and no EOD run happened.
- Steps: advance the fake clock to 08:30 TRT on Tuesday 2026-10-06; read the WireMock request log (the İş Yatırım price route), the run ledger, `daily_prices`, `derived_metrics`, `market_snapshots`, `sector_metric_medians`, `quarantined_facts`; in (b) also `GET /api/v1/market/overview` through the in-process API host.
- Expected: (a) the prices job is **not invoked** — **zero** requests on the price route, its ledger row `succeeded` with `gate: "prices-coverage"`, `gateOutcome: "skipped"`, `constituents` = `covered` = 16, `written = 0` — while `statements`, `metrics-recompute`, `sector-medians` run (`succeeded`) and `market-snapshot` **no-ops** (still exactly one row for 2026-10-05, `computed_at` unchanged); **zero** `quarantined_facts` rows (no `CONFLICTING_VALUE` noise). (b) the uncovered day runs the prices job (one request per current constituent (harness pacing bound to 0), `written = 16`, 16 rows at 2026-10-05), `metrics-recompute` + `market-snapshot` + `sector-medians` write the day, and `GET /api/v1/market/overview` returns 200 with `state: "available"`, `asOf` = 2026-10-05 and `stale: true` (Tuesday morning honestly serves Monday's completed session — never a newer price).
- Dependencies: —

**Phase 2 (executable only after TKT-foundation-034 / PR #106 merges; re-dispatched with this ticket's phase-2 lane):** the morning composition includes `dividends → corporate-actions → disclosures` between statements and metrics-recompute (pinned in `MorningIngestChain.Phase2JobCodes`), cursor-incremental (double call count ≈ per-company list calls, never the full-universe pass), paced ≥ 12 s, and a failed event-family job fail-stops the chain exactly as the EOD chain does.

*Change record: v1.0 2026-10-06 — initial MDF test plan + shared fixture universe (`01-fixture-universe.md`), submitted at Gate 2. v1.1 2026-10-06 — builder decisions applied (Q1 quarantine rule → TC-MDF-052; Q2 formula locked; Q3/Q6 deferred; Q4 confirmed; Q5 noted); coverage matrix updated. v1.2 2026-10-07 — builder final review: Q3 rights-issue factor defined → **TC-MDF-053** (FU §2/§11.6); Q5 storage settled (window-suffixed codes; plain (instrument, metric, as_of_date) key → TC-034); upstream `02` mirror-update gaps flagged; recorded interpretation for degenerate rights inputs; coverage matrix updated. v1.3 2026-10-13 — **§14 change-propagation (builder-approved via the TKT-mdf-012 review round-1 escalation, routing item 1, option A): TC-MDF-047, TC-MDF-048, TC-MDF-049 re-leveled L4 → L3 (Vitest + MSW).** Reason — recorded harness limitation (review-lead-verified): compose.ci seeds no open quarantined fact; the admin trigger endpoint is synchronous, so no non-terminal ingest run is observable in the browser; no slow source is wired into the compose stack. Steps, expected results and traces unchanged; only the level and the per-case justification were added. Affected ticket recorded for re-validation: **TKT-mdf-012** (its C1/C2 findings resolve against this record; the L3 tests on PR #55 become the canonical level). Strategy §5.4 lists admin critical flows 1–8 under e2e coverage — flows 2/3/4 (these three TCs) are now covered at L3; flagged for the test-planner's next strategy pass. **This v1.3 record was applied by the delivery planner on the builder's explicit instruction and requires test-planner confirmation** (strategy §13/§14 — the test-planner owns this document and its change-propagation log).* **v1.4 2026-10-15 — post-board correctness cases (§7) added: TC-MDF-054..062, tracing the builder's verified feedback (statement versioning, unit/currency normalization, financial-profile posture, adjusted per-share CAGRs, `OCF`-based FCF, TMS 29 regime flag, price-source provenance, constituents seed) against `02` v1.3 / `03` v1.4 / FU v1.4. Affected tickets: TKT-mdf-013..022, TKT-mov-008/009, TKT-val-010, TKT-fdf-003, TKT-foundation-015. Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending.* **v1.5 2026-10-17 — real-source parsing-fidelity & live-activation cases (§8) added, mirroring architecture v1.5 §10.9 (AD-14; builder instruction 2026-10-17 via the delivery planner — closes the PR #82 / TKT-foundation-015 round-1 escalation findings DR-01..03: product-path assertion rule, honest catalog labeling, duplicate-concept quarantine overturning latest-wins). TC-MDF-063..068 re-authored (IDs kept — they were burned in test source on the closed branch; content now asserts raw captured bytes through the WireMock harness into the production `src/Ingestion` adapters); TC-MDF-069..076 new (envelope detection, transport-failure class, duplicate-concept rule, per-ticker loop, failure isolation, pacing, universe bound, XU030 launch-seed). TC-MDF-061 amended (FLG-09: Yahoo-contingency volume NULL). §1 scope, §5 matrix, §6 volume-semantics entry updated. FU mirrors: §1 exception, §2/§4 ₺ reinterpretation, §10b real-source catalog lane (FU v1.5); MOV mirror: TC-MOV-028 (MOV plan §8, FU §10b lane). Goldens untouched — no re-derivation (§10.9.7). Affected tickets: the new wave **TKT-foundation-015..019** (TC mapping in §8); TC-MDF-061's amendment flags the v1.4 price-source ticket among TKT-mdf-013..022 for planner re-validation. Applied by the test-planner at the delivery planner's request, builder-directed (architecture v1.5 §10.9 authority); the v1.3/v1.4 pending-confirmation notes above are unchanged by this entry.** **v1.6 2026-10-20 — §9 morning-cadence chain cases added (TC-MDF-077/078)**, mirroring architecture v1.5.15 §10.9.16 / TKT-foundation-040: the morning stream's scheduling + previous-trading-day run date + unchanged retry ladder (077), and the stored-prices repair gate's two branches — fully covered day skipped at zero İş Yatırım cost with the snapshot no-op, failed night repaired end-to-end and served by `GET /market/overview` (078). Implemented as `tests/Api.IntegrationTests/MorningChainTests.cs` (red→green recorded on the ticket). Phase-2 event-family criteria are recorded but not executable until TKT-foundation-034 merges. Applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending.**
