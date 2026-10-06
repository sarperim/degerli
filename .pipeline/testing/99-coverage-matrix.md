# Consolidated Coverage Matrix

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** v1.1 — approved (builder final review 2026-10-07) — the closing document of the test-planning package
**Consolidates:** every FR, UC flow, NFR, UXR, and SC from the approved brief / analysis / UX / architecture (v1.2) package against the test cases of the eight domain plans.
**Test case universe:** 238 cases — TC-MDF (53) · TC-MOV (25) · TC-SCR (32) · TC-RES (36) · TC-VAL (27) · TC-ACC (32) · TC-FDF (12) · TC-XC (21). Methods: `unit | integration | component | e2e | script | manual | operational` (strategy §9.4).

**How to read:** each row points to test case IDs; full case specifications live in the domain plans. `cross-ref` = owned by another plan's case; `manual`/`operational` = recorded verification ledger entry (owner: builder). Detailed per-domain flow breakdowns are in each plan's §5.

---

## 1. Requirements inventory vs. coverage

| Category | Total | Covered by TCs | By recorded method | Orphans |
|---|---|---|---|---|
| FRs (non-Won't) | 104 | 103 | 0 | **1 flagged (accepted)**: FR-MOV-017 (Could) — contract tested (TC-MOV-015), golden deferred to promotion on undefined formula (F-MOV-1, accepted 2026-10-07) |
| FRs (Won't — out of scope by BA decision) | 16 | — | — | none (listed in §2) |
| UCs (main flows) | 30 | 30 | 0 | none |
| UC alternate/error flows | 38 | 35 | 3 | 3 by recorded method: UC-MOV-005a (source retirement — builder decision), UC-RES-004b (re-draft trigger policy — content cycle), UC-MOV-004a (impossible by construction — asserted as invariant) |
| NFRs | 36 | 30 | 6 | 6 manual/operational: NFR-MDF-006, MOV-005, FDF-003 (cost); NFR-RES-002 (plain-language quality); NFR-MOV-003 (99.0% figure — operational; anonymous-access precondition tested); NFR-VAL-002 (comprehension — validated in the SC-006 session) |
| UXRs | 156 | 156 | 0 | none |
| SCs | 8 | 5 | 3 | SC-001 (date), SC-006 (builder session), SC-008 (external users — measured via TC-MDF-044) |

## 2. FR coverage (104 non-Won't; Won't items listed once at the end)

### MDF (18)

| FR | TCs | | FR | TCs |
|---|---|---|---|---|
| FR-MDF-001 | MDF-001, 002 | | FR-MDF-011 | MDF-017, 018; public side RES-015/016 |
| FR-MDF-002 | MDF-003 | | FR-MDF-012 | MDF-019, 020, 021, 025, 052 |
| FR-MDF-003 | MDF-004 | | FR-MDF-013 | MDF-026, 031, 034 |
| FR-MDF-004 | MDF-005 | | FR-MDF-014 | MDF-027, 033 |
| FR-MDF-005 | MDF-006 | | FR-MDF-015 | MDF-035, 052 |
| FR-MDF-006 | MDF-007, 038 | | FR-MDF-016 | MDF-013, 014, 040, 046 |
| FR-MDF-007 | MDF-037 | | FR-MDF-018 | MDF-029, 030, 053 |
| FR-MDF-008 | MDF-008, 009 | | FR-MDF-019 | MDF-032 |
| FR-MDF-009 | MDF-010, 011 | | FR-MDF-010 | MDF-015, 016, 043 |

### MOV (19)

| FR | TCs | | FR | TCs |
|---|---|---|---|---|
| FR-MOV-001 | MOV-001, 012, 016 | | FR-MOV-011 | MOV-019 |
| FR-MOV-002 | MOV-001, 012, 016, 019 | | FR-MOV-012 | MOV-019 |
| FR-MOV-003 | MOV-001, 012, 016 | | FR-MOV-013 | MOV-019 |
| FR-MOV-004 | MOV-001, 002, 003, 012, 016, 019, 020 | | FR-MOV-014 | MOV-006, 007, 008 |
| FR-MOV-005 | MOV-001, 012, 016 | | FR-MOV-015 | MOV-009, 011 |
| FR-MOV-006 | MOV-001, 004, 012, 016, 021 | | FR-MOV-016 | MOV-009, 013, 024 |
| FR-MOV-007 | MOV-013, 016, 017 | | FR-MOV-017 | MOV-015 (golden flagged — F-MOV-1) |
| FR-MOV-008 | MOV-013, 017, 018 | | FR-MOV-020 | MOV-016, XC-006 |
| FR-MOV-009 | MOV-013, 016, 017 | | FR-MOV-021 | MOV-001, 020 |
| FR-MOV-010 | MOV-012, 013, 016, 024 | | | |

### SCR (17)

| FR | TCs | | FR | TCs |
|---|---|---|---|---|
| FR-SCR-001 | SCR-001, 002, 004, 005 | | FR-SCR-010 | SCR-015, 030 |
| FR-SCR-002 | SCR-002, 009 | | FR-SCR-011 | SCR-017, 030 |
| FR-SCR-003 | SCR-003, 008 | | FR-SCR-012 | SCR-024, 028 |
| FR-SCR-004 | SCR-002, 025, 026, 029 | | FR-SCR-013 | SCR-023, 028 |
| FR-SCR-005 | SCR-002, 010, 025 | | FR-SCR-014 | SCR-002, 005, 025 |
| FR-SCR-006 | SCR-006, 027 | | FR-SCR-015 | SCR-004, 012, 022 |
| FR-SCR-007 | SCR-012, 013, 014 | | FR-SCR-016 | XC-006, SCR-026 |
| FR-SCR-008 | SCR-012, 030 | | FR-SCR-017 | SCR-020, 021 |
| FR-SCR-009 | SCR-011, 018, 019, 030 | | | |

### RES (24)

| FR | TCs | | FR | TCs |
|---|---|---|---|---|
| FR-RES-001 | RES-001, 022 | | FR-RES-014 | RES-009, 024, 030 |
| FR-RES-002 | RES-002, 022 | | FR-RES-015 | RES-015, 024 |
| FR-RES-003 | RES-003, 022 | | FR-RES-016 | RES-011, 013, 015, 031 |
| FR-RES-004 | RES-004, 035 | | FR-RES-017 | XC-006, RES-028 |
| FR-RES-005 | RES-005, 026 | | FR-RES-018 | RES-028 |
| FR-RES-006 | RES-007 | | FR-RES-019 | RES-016 |
| FR-RES-007 | RES-007, 008, 025 | | FR-RES-020 | RES-017, 020, 034 |
| FR-RES-008 | RES-009 | | FR-RES-021 | RES-019, 034 |
| FR-RES-009 | RES-010 | | FR-RES-022 | RES-005, 021, 026 |
| FR-RES-010 | RES-011 | | FR-RES-026 | RES-006, 026, 029 |
| FR-RES-011 | RES-012 | | FR-RES-027 | RES-012 |
| FR-RES-012 | RES-013 | | FR-RES-013 | RES-024, 032 |
| | | | | |

### VAL (10)

| FR | TCs | | FR | TCs |
|---|---|---|---|---|
| FR-VAL-001 | VAL-005, 006, 023 | | FR-VAL-006 | VAL-001, 019, 023 |
| FR-VAL-002 | VAL-005, 012, 017 | | FR-VAL-007 | VAL-002, 020 |
| FR-VAL-003 | VAL-001, 005, 017 | | FR-VAL-008 | VAL-019 |
| FR-VAL-004 | VAL-017 | | FR-VAL-009 | VAL-013, 021, 024 |
| FR-VAL-005 | VAL-009, 018, 023 | | FR-VAL-010 | VAL-014, 021, 025, 027 |

### ACC (10)

| FR | TCs | | FR | TCs |
|---|---|---|---|---|
| FR-ACC-001 | ACC-001, 004, 005, 022 | | FR-ACC-006 | ACC-021, 032 |
| FR-ACC-002 | ACC-006, 007, 008 | | FR-ACC-007 | ACC-020 |
| FR-ACC-003 | ACC-010, 031 | | FR-ACC-008 | ACC-001, 011, 012, 013, 014, 024, 027 |
| FR-ACC-004 | ACC-006, 019, 028, 029 | | FR-ACC-009 | ACC-015, 016, 017, 018, 030 |
| FR-ACC-005 | ACC-025 | | FR-ACC-010 | ACC-028; SCR-028; VAL-024 |

### FDF (6)

| FR | TCs |
|---|---|
| FR-FDF-001 | FDF-001, 007 |
| FR-FDF-002 | FDF-002, 007 |
| FR-FDF-003 | FDF-003, 007, 011 |
| FR-FDF-004 | FDF-005, 012 |
| FR-FDF-005 | FDF-008 |
| FR-FDF-006 | FDF-006 |

**Won't FRs (16 — out of scope by BA decision, not defects):** FR-MDF-017 · FR-MOV-018/019 · FR-SCR-018/019 · FR-RES-023/024/025 · FR-VAL-011/012/013 · FR-ACC-011/012 · FR-FDF-007/008/009. Negative/boundary tests exist only where the architecture defines rejection behavior (unknown metric → METRIC_NOT_AVAILABLE; no public fund routes → FDF-010).

## 3. UC coverage (30 UCs, main + alternate/error flows)

| UC | Flows → TCs |
|---|---|
| UC-MDF-001 | main MDF-010 · alt a (unreachable/retry/stale) MDF-012, 013, 014 · alt b (quarantine) MDF-019, 020, 024, 052 |
| UC-MDF-002 | main MDF-015, 043 · alt a (source-limited) MDF-016 |
| UC-MDF-003 | main MDF-037, 038 · alt a (unclassified) MDF-025 |
| UC-MDF-004 | report MDF-017, 018, 040, 051 · go/no-go decisions — manual ledger |
| UC-MDF-005 | main MDF-026, 036 · short-window MDF-033 · alt a (no data + boundary) MDF-036, RES-015/016 |
| UC-MOV-001 | main MOV-016 · alt a (macro stale) MOV-009, 013, 018 · alt b (equity stale) MOV-014, 024 |
| UC-MOV-002 | main MOV-006, 007, 008 · alt a (indep unreachable) MOV-009 · alt b (revision) MOV-007 |
| UC-MOV-003 | main MOV-017 · alt a (independent unavailable) MOV-013, 018 |
| UC-MOV-004 | main MOV-019 · alt a — impossible by construction (invariant asserted MOV-001/002) |
| UC-MOV-005 | main MOV-009, 011, 014 · alt a (permanent source death) — manual ledger |
| UC-SCR-001 | main SCR-026 · alt a (zero-match) SCR-006, 027 · alt b (exclusions) SCR-002, 005 · alt c (stale) SCR-010 |
| UC-SCR-002 | main SCR-012 · alt a (anonymous → hop) SCR-028 |
| UC-SCR-003 | main SCR-030, 019 · alt a (dropped criterion) SCR-018 |
| UC-SCR-004 | main SCR-020, 021 · alt a (un-hide) SCR-020 (toggle back) |
| UC-RES-001 | main RES-001..004, 022, 035 · alt a (no-match) RES-003, 022 |
| UC-RES-002 | main RES-005, 026 · alt a (preparing) RES-006, 029 |
| UC-RES-003 | main RES-007..015, 028 · alt a (stale) RES-015, 024 · alt b (short history) RES-011, 031 |
| UC-RES-004 | main RES-016, 018..021, 034 · alt a (re-draft) RES-016 variant · alt b (new-filings cycle) — manual ledger |
| UC-RES-005 | main RES-028 |
| UC-VAL-001 | main VAL-023 · alt a (missing inputs) VAL-008, 026 · alt b (restated) VAL-007, 017 · alt c (stale price) VAL-017 |
| UC-VAL-002 | main VAL-013, 014, 027 · alt a (anonymous) VAL-024 |
| UC-VAL-003 | main VAL-015, 016 · alt a (new stock) VAL-015 (per-stock active assertion) |
| UC-ACC-001 | main ACC-001, 022, 023, 028 · alt a (taken) ACC-002 · alt b (weak password) ACC-003, 022 |
| UC-ACC-002 | main ACC-006, 028, 031 · alt a (wrong creds) ACC-007, 008 · alt b (forgotten) ACC-030 |
| UC-ACC-003 | main ACC-019, 020, 025 · alt a (deletion confirm) ACC-021, 026, 032 |
| UC-ACC-004 | main ACC-015, 016, 030 · alt a (expired) ACC-017, 030 |
| UC-ACC-005 | manual ledger (data surface tested: ACC-005, 021) |
| UC-FDF-001 | main FDF-001..003, 007 · alt a (source limits) FDF-007 |
| UC-FDF-002 | main FDF-006 · alt a (unavailable) FDF-008 |
| UC-FDF-003 | main FDF-005, 012 · alt a (low holdings coverage) FDF-005 (TEF0002/0003 gaps) |

## 4. NFR coverage (36)

| NFR | Method → TCs / ledger |
|---|---|
| NFR-MDF-001 | integration MDF-010, 012, 013; MOV-010 (macro cadence); operational (real-scale 23:59 completion — run-ledger monitoring) |
| NFR-MDF-002 | integration MDF-009, 032, 034, 035 |
| NFR-MDF-003 | integration MDF-017, 018 |
| NFR-MDF-004 | integration MDF-008, 009 |
| NFR-MDF-005 | e2e XC-016 (compose-up smoke); manual (README ≤10-commands quickstart) |
| NFR-MDF-006 | manual (cost $0) |
| NFR-MOV-001 | integration MOV-001, 005, 009, 010 |
| NFR-MOV-002 | script XC-002; e2e XC-003, MOV-023 |
| NFR-MOV-003 | integration/e2e MOV-012, 013, 016 (anonymous); operational (99.0% — UptimeRobot) |
| NFR-MOV-004 | e2e MOV-016, 017 (units/as-of presence); manual (copy review) |
| NFR-MOV-005 | manual (cost $0) |
| NFR-MOV-006 | e2e XC-006, MOV-016; script XC-007 |
| NFR-SCR-001 | script XC-002; component SCR-022 |
| NFR-SCR-002 | component SCR-022; manual (review) |
| NFR-SCR-003 | e2e XC-008 (goldens: SCR-002 = RES-007/010/011) |
| NFR-SCR-004 | integration SCR-010, 019 |
| NFR-SCR-005 | e2e SCR-031 |
| NFR-RES-001 | script XC-002; integration RES-020 (publish gate) |
| NFR-RES-002 | manual (builder review gate) |
| NFR-RES-003 | e2e XC-008 |
| NFR-RES-004 | integration RES-005 (lastReviewedAt), RES-015 (asOf) |
| NFR-RES-005 | e2e RES-036 |
| NFR-RES-006 | integration RES-016, 017 |
| NFR-VAL-001 | script XC-002; component VAL-019 |
| NFR-VAL-002 | component VAL-019 (rendering); manual (SC-006 session comprehension) |
| NFR-VAL-003 | integration VAL-012; e2e XC-008 |
| NFR-VAL-004 | unit VAL-004; component VAL-017 |
| NFR-ACC-001 | integration ACC-004, 005, 021, 022; manual (pre-launch review) |
| NFR-ACC-002 | integration ACC-003, 007, 008, 015; config assertion XC-011 (PBKDF2 ≥100k) |
| NFR-ACC-003 | script XC-002; component ACC-022/025 |
| NFR-ACC-004 | integration ACC-021 |
| NFR-ACC-005 | integration SCR-014, VAL-013 (+ anonymous flows throughout) |
| NFR-FDF-001 | integration FDF-005, 012 |
| NFR-FDF-002 | integration FDF-001, 009 |
| NFR-FDF-003 | manual (cost $0) |
| NFR-FDF-004 | e2e XC-016; repo containment of adapter + payloads |
| *Architecture perf targets (`01` §9)* | integration XC-014 (budget smoke, ×2 tolerance); manual (VPS spot-check); CI policy (Playwright ≤ 5 min) |

## 5. UXR coverage (156)

**Global rules UXR-G-001..030** — owner map in XC §5 (every rule → TCs). Highlights: G-011 → XC-008; G-012 → XC-021/MOV-022 (+ recorded admin exception MDF-048); G-013/014 → XC-003/004/005; G-016 → XC-006; G-017 → XC-007; G-029 → SCR-013/015, VAL-013; G-030 → SCR-014/024/028/032, VAL-013/021/024, ACC-011/027; per-screen instantiation of G-001..G-006, G-018..G-022, G-025..G-028 in the domain plans (see each plan's §5).

**Per-screen UXRs (98):**

| Group | UXRs | TCs (domain plan §5 has the row-by-row map) |
|---|---|---|
| UXR-MOV-001..012 | 12 | MOV-001..025 (MOV plan §5) |
| UXR-SCR-001..019 | 19 | SCR-001..032 (SCR plan §5) |
| UXR-RES-001..025 | 25 | RES-001..036 (RES plan §5) |
| UXR-VAL-001..021 | 21 | VAL-001..027 (VAL plan §5) |
| UXR-ACC-001..021 | 21 | ACC-001..032 (ACC plan §5) |

**Admin UXRs (28, SCR-012):**

| Group | TCs |
|---|---|
| UXR-MDF-001..011, 013..017, 026..028 (19 — shared admin surface) | MDF-039..051 (MDF plan §5) |
| UXR-MDF-012 (fund coverage) | FDF-005, 012 |
| UXR-MDF-018..022 (descriptions) | RES-018..021, 034 |
| UXR-MDF-023/024 (metric visibility) | SCR-020, 021 |
| UXR-MDF-025 (baselines) | VAL-015, 016 |

## 6. Success criteria coverage

| SC | Method |
|---|---|
| SC-001 (bilingual V1 live by Dec 2026) | process — not testable behavior; bilingual-ness itself: XC-002/003, deployment: deploy smoke-check + `/health` (XC-016) |
| SC-002 (core loop end-to-end) | **TC-XC-001** (+ SCR-026, RES-028, VAL-023 legs) |
| SC-003 (saved screens, server-side, 5 families) | SCR-012..019, 030, 031; catalog SCR-001; families in goldens SCR-002/004/005 |
| SC-004 (adjustable DCF per stock, price vs fair value) | VAL-001..027 (esp. 006, 017, 023) |
| SC-005 (daily refresh unattended) | MDF-010..014; FDF-006 |
| SC-006 (builder research session) | manual ledger (the platform capability it exercises is fully tested) |
| SC-007 (public repo + README) | XC-016 (executable reproducibility); README quality — manual |
| SC-008 (10 external users) | operational; measurement endpoint tested (MDF-044) |

## 7. Orphan check (both directions)

- **Requirement → test:** complete — §1's inventory shows every item lands in ≥1 TC or a recorded method; the single flagged golden (FR-MOV-017, Could) has its contract tested and the formula gap accepted for deferral to promotion (F-MOV-1, 2026-10-07).
- **Test → requirement:** complete by construction — every TC in every plan carries a `Traces` line to FR/UC/NFR/UXR/SC IDs; no TC exists without a requirement (the plans contain no intuition-only cases; §3 of each plan names each case's technique).
- **Known deferred/blocked items (explicit, not silent):** F-MOV-1 (period-aggregate formula — TC-MOV-015 golden deferred to promotion, accepted by the builder 2026-10-07); Q6 (membership-removal mechanism — confirmed deferred 2026-10-07; TC-MDF-037 asserts the observable contract). **Resolved at the builder's final review (2026-10-07):** F-VAL-1 (total-debt reading, goldens verified, corner corrected); Q3 (rights factor defined in FU §2/§11.6 → TC-MDF-053); Q5 (window-suffixed metric codes, plain upsert key → TC-MDF-034). **Upstream-doc gaps flagged (not silent, 2026-10-07):** `02` §6.4's parameter name should read `debt`/`total_debt`; `02` corporate-action and §6 metric-storage sections should mirror the Q3 factor and the Q5 code shape — the architect's dispatch repeatedly timed out, so the fixture contract (FU) and domain plans are authoritative for implementation until those patches land.

## 8. Decisions ledger (all flags across the package, for the final review)

**Resolved at the builder's final review (2026-10-07):**

- **F-VAL-1 → ruling (a) adopted:** the `02` §6.4 DCF debt parameter carries **total debt (ST+LT)**, formula as written (`EquityValue = PV_explicit + TV/(1+r)^N + Cash − Debt`); goldens independently re-verified under exact arithmetic — FVPS 13.1969, MoS −0.5155, corner 24.0000 all confirmed; literal net-debt reading (15.1969) rejected. **One golden corrected during verification:** sensitivity corner (0.14, 0.01) 8.9952 → **8.99623** (TC-VAL-002, FU §11.5). Affects VAL-001/002/009 + FU §11.5; `02` §6.4 param-name reconciliation flagged upstream.
- **F-MOV-1 → accepted:** FR-MOV-017 stays Could; TC-MOV-015 is contract-only (contract tested now); formula + goldens at promotion (recommended: cap-weighted total return over the period).
- **Q1 (2026-10-06)** conflicting-price quarantine `CONFLICTING_VALUE` → TC-MDF-052 · **Q2 (2026-10-06)** sector performance cap-weighted, locked (FU §11.3) · **Q4 (2026-10-06)** CAGR compute-from-available confirmed.
- **Q3 → resolved:** rights-issue factor defined in FU §2/§11.6 (TERP-based `(P_C + q·P_S)/((1+q)·P_C)`) → **TC-MDF-053**.
- **Q5 → resolved:** window-suffixed metric codes (e.g. `rev_cagr_3y`); upsert key `(instrument, metric, as_of_date)` → TC-MDF-034.
- **Q6 → confirmed deferred (2026-10-07):** TC-MDF-037 asserts the observable history contract.

**Plan-specified expectations / interpretations — all validated by the builder (2026-10-07; veto-able only through the change gate):** I-MOV-1..3 · I-SCR-1..4 · I-RES-1..4 · I-VAL-1..3 · I-ACC-1..3 · I-FDF-1..3 · I-XC-1..3 — each recorded in its plan's §6 with rationale; plus the new I-MDF degenerate-rights-input interpretation (TC-MDF-053(e)). Summary of the load-bearing ones: empty criteria → 400 (SCR-009); inclusive bounds (SCR-003); publish rejection = VALIDATION_FAILED + missing field (RES-020); PATCH descriptions strict against provenance writes (RES-019); grid cells with r ≤ g_t → NULL (VAL-020); horizon out of range → 422 not 400 (VAL-003/011); wrong current password on change → 400 field error (ACC-020); out-of-scope fund types skipped-and-counted, not quarantined (FDF-004).

**Manual/operational ledger — acknowledged by the builder (2026-10-07):** SC-006 research session · UC-ACC-005 KVKK review · NFR-VAL-002 comprehension (SC-006 session) · UC-MDF-004 go/no-go · UC-MOV-005 alt a (source death) · UC-RES-004 alt b (re-draft policy) · NFR-MOV-003 99.0% availability (UptimeRobot) · NFR-MDF-005 README ≤10-commands · backup restore drill (RTO 4h / RPO 24h) · cost NFRs ($0) · real-domain email deliverability post-D-04 · HSTS pre-launch checklist.

## 9. Package deliverables and the contract

`.pipeline/testing/`: `00-test-strategy.md` (approved) · `01-fixture-universe.md` (v1.3) · eight domain plans — MDF v1.2, MOV v1.1, SCR v1.0, RES v1.0, VAL v1.1, ACC v1.0, FDF v1.0, XC v1.0 — all approved at the builder's final review 2026-10-07 · this matrix v1.1.

**The contract:** the coder agent implements against these plans; a unit of work is done when its tests pass. Once approved, test cases may not be weakened to make an implementation pass — only the builder can change a test case, via the change-propagation rule (strategy §14): every TC change lists the affected requirement IDs and tickets for re-validation. Expected values derive from the approved documents and the fixture universe — never from an implementation.

---
*Change record: v1.0 2026-10-06 — initial consolidated coverage matrix, submitted for final review. v1.1 2026-10-07 — builder final review applied: F-VAL-1 ruled (reading (a), goldens verified, corner corrected to 8.99623); F-MOV-1 accepted (golden deferred to promotion); Q3 resolved → TC-MDF-053 (+1 case, total 238, FR-MDF-018 row); Q5 resolved (window-suffixed codes, plain upsert key); Q6 confirmed deferred; all §6 interpretations and the manual ledger acknowledged; upstream `02` mirror-update gaps flagged; modified TCs: TC-VAL-002 (golden), TC-MDF-034 (key made definitive, wording only) — none implemented yet, re-validation by construction.*
