# TKT-int-004: Security, caching, performance & infrastructure contracts

- Status: done — merged to `dev` 2026-10-15 (PR https://github.com/sarperim/degerli/pull/66, merge commit `f7a8f33`). The 2026-10-15 planner reconciliation stood (TC-XC-009/018 recorded dependency-owned by TKT-foundation-004 and removed from the `Acceptance:` line). Round-3 re-gate escalated on **G-1/G-2 `missing_tc=TC-XC-009/018`**, which the builder **waived as a facet-gate tooling false positive** (see waiver note below). CI green on the reviewed head `f994100` (run 38657008933; all three jobs SUCCESS). Review artifacts: `.pipeline/plan/reviews/TKT-int-004-round-1.md`, `-round-2.md`, `-round-3.md`.
- Size: L
- Scope: the cross-cutting contract specs: i18n parity script as a formal CI gate (TC-XC-002 — the script ships with TKT-foundation-002; this ticket makes it a recorded, failing-case-verified gate); advice-vocabulary grep (TC-XC-007, term list maintained beside the script); security headers; CSRF on unsafe methods across endpoint classes; rate-limit edges (5/600/60, `retryAfter`, window reset) + PBKDF2 config assertion; performance budget smoke (p95 ≤ 2× targets, ≥30 calls each; Playwright ≤ 5 min gate); caching-policy decision table per surface; reproducibility compose-up smoke; OpenAPI document served with no fund routes. Test specs + middleware configuration fixes only (middleware itself ships with TKT-foundation-004).
- Traces to: `01` §10 (security posture), §9 (performance targets); `03` §2, §7, §12, §13; NFR-MOV-002/006; NFR-ACC-002; NFR-MDF-005; strategy decisions A/D
- Acceptance: TC-XC-002, TC-XC-007, TC-XC-010, TC-XC-011, TC-XC-014, TC-XC-015, TC-XC-016 — TDD: specs first, then green; specs in `.pipeline/testing/cross-cutting.md` (I-XC-1/I-XC-2 interpretations binding). Reconciled 2026-10-15: two formerly pinned cases are dependency-owned by TKT-foundation-004 (see Status).
- Architecture refs: `01-system-architecture.md` §6.1, §9, §10.1–§10.5; `03-api-design.md` §2, §7, §12, §13
- UX refs: —
- Dependencies: TKT-foundation-003, TKT-foundation-004, TKT-mov-004, TKT-scr-002, TKT-scr-004, TKT-res-002, TKT-res-003, TKT-res-004, TKT-val-003, TKT-val-004, TKT-acc-002, TKT-acc-003, TKT-mdf-010
- Parallel group: P-18


## Waiver note — builder, 2026-10-15

**Finding waived:** facet-gate `missing_tc` — `TC-XC-009`, `TC-XC-018`
**Decision:** waived as a **facet-gate tooling false positive**; the PR was merged on that basis.
**Reason:** `review-facets.sh` derives a ticket's pinned acceptance set from *every* `TC-` token anywhere in the ticket file, instead of from the ticket's `Acceptance:` line. Any `TC-…` string appearing in Status/notes **prose** is therefore mis-read as a pinned case; because it is not referenced by the PR's changed test files, the gate raises a blocking `missing_tc` finding even though:
1. the ticket's *real* pinned TCs are all present in the changed specs;
2. the flagged case is genuinely delivered elsewhere — `TC-RES-034` by the merged TKT-res-011, `TC-XC-009`/`TC-XC-018` by the dependency TKT-foundation-004; and
3. CI is green on the reviewed head.
The finding is a gate artifact, not a coverage gap, so it was waived rather than worked around — duplicating a dependency's test or pasting a bare token would be gaming the classifier.
**Follow-up (recommended):** fix the tool to source the pinned set from the `Acceptance:` line only, so prose can never spoof the TC gate. Also logged in `.pipeline/plan/run-report.md`.
