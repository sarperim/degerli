# Fund Data Foundation Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved (builder final review 2026-10-07 — §6 interpretations upheld; v1.0 unchanged)
**Derives from:** `analysis/fund-data-foundation.md` (FR-FDF-001..009, UC-FDF-001..003, NFR-FDF-001..004) · `ux/market-data-foundation.md` (UXR-MDF-012 — fund coverage scope) · `architecture/` v1.2 (`02` §3.7, `03` §8–§9) · strategy · fixture contract FU §8.
**TC ID convention:** `TC-FDF-NNN`, permanent. Levels: L1 · L2 · L4. TEFAS is always doubled (WireMock canned payloads); time is faked for cadence tests.

## 1. Scope

**Tested here:** FR-FDF-001..006 (6 non-Won't); UC-FDF-001..003 including alternates; NFR-FDF-001..004; UXR-MDF-012 (fund scope of the admin coverage report).

**Not tested here:** FR-FDF-007..009 (fund UI/look-through — Won't, BR-FDF-001); real TEFAS access method and behavior (OQ-FDF-002 — builder V0 validation; the adapter contract against canned payloads is tested); reproducibility beyond the shared compose stack (XC).

## 2. Test Case Specification

**TC-FDF-001 — NAV history ingest, dated and idempotent**
- Traces: FR-FDF-001, UC-FDF-001 step 2, BR-FDF-003 · Level: L2 · Design: EP — valid NAV class
- Steps: run the `fund-nav` job with the TEFAS fixture payloads (FU §8).
- Expected: `fund_navs` rows for the 3 funds dated per publication day (TEF0001: 10.00, 10.10, 10.05, 10.20, 10.25), `source_ref`/`recorded_at` populated; re-run → no duplicates (one row per fund per day); append-only (no updates).
- Dependencies: —

**TC-FDF-002 — Performance ingest**
- Traces: FR-FDF-002 · Level: L2 · Design: EP — valid performance class
- Expected: `fund_performances` rows as published (TEF0001: 1M 2.5%, 3M 6.0%, 1Y 18.0% as of T; TEF0002's rows; TEF0003 none — absence is a recorded gap, not an error).
- Dependencies: —

**TC-FDF-003 — Holdings ingest with partial coverage and symbol matching**
- Traces: FR-FDF-003, BR-FDF-004, UC-FDF-001 step 2 · Level: L2 · Design: EP — full/partial/unmatched holding classes
- Expected: TEF0001's snapshot: ALFA and EPSL lines carry `instrument_id` matched to MDF instruments (the future look-through link), the "Yabancı Hisse X" line stores `name_raw` with `instrument_id` NULL — never dropped; TEF0003's single line with NULL weight/units stored honestly; TEF0002 has no holdings rows (gap recorded per TC-FDF-005).
- Dependencies: —

**TC-FDF-004 — Fund universe bound: equity and equity-heavy mixed only**
- Traces: BR-FDF-006, OQ-FDF-001 decision · Level: L2 · Design: decision table — fund type × ingest
- Steps: run ingest with a canned payload containing a bond fund and a money-market fund alongside TEF0001–3.
- Expected: bond/money-market funds are **not** ingested (no `funds` rows) — the bound is enforced; the skip is counted in run stats (visible in the ledger's `stats_json`); equity and equity-heavy mixed funds ingest *(plan-specified skip-and-count behavior — flag I-FDF-1)*.
- Dependencies: —

**TC-FDF-005 — Per-fund coverage metadata with holdings gaps**
- Traces: FR-FDF-004, NFR-FDF-001, UC-FDF-003 main, UXR-MDF-012 · Level: L2 · Design: EP — full/NAV-only/partial classes
- Expected: `coverage_metadata` (scope fund) records per fund: TEF0001 — NAV, performance, holdings from 2026-09-28; TEF0002 — NAV + performance, **holdings absent recorded as a gap** (never silently ignored); TEF0003 — NAV + partial holdings; the admin coverage report `GET /admin/coverage?scope=fund` serves these rows with from-when dates and holdings availability.
- Dependencies: —

**TC-FDF-006 — Daily refresh unattended**
- Traces: FR-FDF-006, UC-FDF-002 main, BR-FDF-005, SC-005 · Level: L2 · Design: state transition — scheduled idle → running → succeeded
- Expected: with the fake clock at the TEFAS cadence, the fund jobs trigger without manual intervention; run ledger rows per job (`fund-nav`, `fund-holdings`, `fund-performance`) with statuses and stats; no manual step anywhere in the cycle.
- Dependencies: —

**TC-FDF-007 — Fund backfill records achieved depth**
- Traces: UC-FDF-001 main + alternate a, FR-FDF-001..003 (historical) · Level: L2 · Design: EP — backfill classes (full / source-limited)
- Steps: admin-trigger the fund jobs in backfill mode with `{backfillFrom}`; variant with a source-limited canned range.
- Expected: historical NAV/performance/holdings ingested for the defined universe; achieved depth recorded in coverage metadata; source limitation recorded as the actual depth (incremental progress accepted — the limitation is recorded, not fabricated).
- Dependencies: —

**TC-FDF-008 — Source failure: last-known retained, anomaly alerted**
- Traces: FR-FDF-005, UC-FDF-002 alternate a · Level: L2 · Design: state transition — fresh → failing → alerted
- Expected: TEFAS stub failure → run failed after retries; builder alerted (mail double + Serilog event) naming the fund job; stored NAV rows retained (last-known); staleness recorded via the freshness view (fund data types appear stale in the admin summary).
- Dependencies: —

**TC-FDF-009 — Append-only retention**
- Traces: NFR-FDF-002, BR-FDF-003 · Level: L2 · Design: EP — correction/re-ingest classes
- Expected: re-publishing an identical NAV → idempotent no-op; no deletion or TTL artifacts on fund tables (schema backstop TC-MDF-009 family); a *conflicting* NAV value follows the Q1 quarantine rule (cross-ref TC-MDF-052's mechanism — same policy class).
- Dependencies: —

**TC-FDF-010 — No fund UI surface exists in V1**
- Traces: BR-FDF-001, FR-FDF-007/008/009 (Won't boundary) · Level: L2 · Design: negative probe — API surface
- Expected: the served OpenAPI document (`GET /api/v1/openapi.json`) contains **no** fund-facing public endpoints (no `/funds` routes); fund data is reachable only through the admin coverage report (builder-gated) — the data-only boundary is mechanically enforced.
- Dependencies: —

**TC-FDF-011 — Holdings→instrument link integrity**
- Traces: FR-FDF-003 (the look-through link), `02` §3.7 · Level: L2 · Design: EP — matched/unmatched classes
- Expected: every non-NULL `fund_holdings.instrument_id` resolves to an existing MDF instrument; matched lines' symbols correspond (ALFA/EPSL); unmatched lines have NULL ids with `name_raw` retained — referential integrity holds both ways.
- Dependencies: —

**TC-FDF-012 — Coverage report fund scope renders (admin UI)**
- Traces: UXR-MDF-012/013 (fund side) · Level: L4 · Design: EP — fund-scope render classes
- Expected: the SCR-012 coverage report switched to fund scope lists the three funds with their data types, from-when dates, and holdings availability; TEF0002's holdings gap is shown explicitly; empty conditions (e.g., no funds matching a filter, if any) render honestly.
- Dependencies: —

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** fund payloads — equity / equity-heavy mixed / out-of-scope types (TC-004); holdings — matched / unmatched / partial-NULL / absent (TC-003/005/011); NAV re-publication — identical / conflicting (TC-009); sources — reachable / failing (TC-008).
- **Boundary value analysis:** backfill depth — full range vs source-limited (TC-007); cadence edges via fake clock (TC-006).
- **Decision tables:** fund type × ingest decision (TC-004); coverage per fund × data type with gap recording (TC-005).
- **State transition testing:** scheduled idle → running → succeeded/failed with alert (TC-006/008); fresh → stale (freshness view, TC-008); backfill depth unknown → recorded (TC-007).
- **Negative probes:** no public fund surface (TC-010) — the Won't boundary as a mechanical check.

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** rows match FU §8 exactly; coverage gaps recorded per fund (never silent); alerts dispatched; universe bound enforced; no fund UI surface. **Fail:** any silent gap, any out-of-scope fund ingested, any fund fact overwritten. Flaky = fail.

**Suspension:** Testcontainers unavailable >1 day; TEFAS adapter contract change (canned payloads re-derived first); a quarantine-policy change affecting TC-009. **Resumption:** clean triaged run of the affected group.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-FDF-001 | main | TC-001, 007 | planned |
| FR-FDF-002 | main | TC-002, 007 | planned |
| FR-FDF-003 | main | TC-003, 007, 011 | planned |
| FR-FDF-004 | main | TC-005, 012 | planned |
| FR-FDF-005 | main | TC-008 | planned |
| FR-FDF-006 | main | TC-006 | planned |
| UC-FDF-001 | main; alt a (source limits) | TC-001..003, 007; TC-007 (limited variant) | planned |
| UC-FDF-002 | main; alt a (source unavailable) | TC-006; TC-008 | planned |
| UC-FDF-003 | main; alt a (low holdings coverage) | TC-005, 012; TC-005 (TEF0002/0003 gaps) | planned |
| NFR-FDF-001 | coverage transparency | TC-005, 012 | planned |
| NFR-FDF-002 | retention | TC-001, 009 | planned |
| NFR-FDF-003 | cost $0 | manual ledger | manual |
| NFR-FDF-004 | reproducibility | shared compose stack (XC-017); adapter + payloads in repo | planned |
| UXR-MDF-012 | fund coverage report | TC-005, 012 | planned |

## 6. Flags and recorded interpretations (for final review)

- **I-FDF-1 (plan-specified):** out-of-scope fund types (bond, money-market, per BR-FDF-006) encountered in a payload are **skipped and counted in run stats** (visible in the ledger), not quarantined — they are a scope bound, not a data-quality failure. Veto here changes TC-004.
- **I-FDF-2 (interpretation):** conflicting NAV re-publication follows the equity-side Q1 rule (quarantine `CONFLICTING_VALUE`) — one policy for all fact tables; asserted via the shared mechanism rather than a fund-specific case.
- **I-FDF-3 (interpretation):** TEF0003's performance absence is a recorded coverage gap (row-less data type with a note), consistent with BR-FDF-004's partial-coverage posture.

---
*Change record: v1.0 2026-10-06 — initial FDF test plan, batch mode. Approved at the builder's final review 2026-10-07 — §6 interpretations I-FDF-1..3 upheld.*
