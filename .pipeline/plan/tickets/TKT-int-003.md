# TKT-int-003: Global UX behavior sweep (language, disclaimers, a11y, responsive, feedback)

- Status: done — merged to `dev` 2026-10-15 (PR https://github.com/sarperim/degerli/pull/71, merge commit `99019c8`). Round-1 **F1 `red-ci` (TC-RES-034) was cleared** by the merged TKT-res-011 TZ fix (`dev` E2E green). Round-2 escalated on **F2 `missing_tc=TC-RES-034`**, which the builder **waived as a facet-gate tooling false positive** (see waiver note below). Code + CI green on the reviewed head (run 38776950436: Backend/Web/E2E all pass; E2E 4m4s). Review artifacts: `.pipeline/plan/reviews/TKT-int-003-round-1.md`, `-round-2.md`.
- Size: L
- Scope: the cross-screen L4/L3 specs for global behaviors: TR default on first visit; language switch preserving context across data-bearing screens; language persistence (device + account); disclaimer sweep across the seven research surfaces; loading/pending feedback spot checks; plain-language error rendering with no technical leakage (5xx); axe-core zero-known-violations gate on key screens; responsive operability at 375px; keyboard-only journeys; no-real-time framing probes on market-data surfaces. Test specs + defect fixes only.
- Traces to: UXR-G-001..014, UXR-G-016, UXR-G-017 (cross-screen form), UXR-G-025..028; NFR-MOV-002/004/006; strategy decision C
- Acceptance: TC-XC-003, TC-XC-004, TC-XC-005, TC-XC-006, TC-XC-012, TC-XC-013, TC-XC-017, TC-XC-019, TC-XC-020, TC-XC-021 — TDD: specs first, then green; specs in `.pipeline/testing/cross-cutting.md`.
- Architecture refs: `01-system-architecture.md` §8.2 (global rules table), §6.1 (e2e job), §5 (a11y via shadcn/Radix); `03-api-design.md` §1 principle 1
- UX refs: UXR-G-001..017, UXR-G-025..028 (as cross-screen sweeps)
- Dependencies: TKT-mov-005, TKT-mov-006, TKT-scr-006, TKT-scr-007, TKT-res-007, TKT-res-008, TKT-res-009, TKT-val-006, TKT-val-007, TKT-val-008, TKT-acc-007, TKT-acc-008, TKT-acc-009, TKT-acc-010, TKT-mdf-012, TKT-foundation-010
- Parallel group: P-18


## Waiver note — builder, 2026-10-15

**Finding waived:** facet-gate `missing_tc` — `TC-RES-034`
**Decision:** waived as a **facet-gate tooling false positive**; the PR was merged on that basis.
**Reason:** `review-facets.sh` derives a ticket's pinned acceptance set from *every* `TC-` token anywhere in the ticket file, instead of from the ticket's `Acceptance:` line. Any `TC-…` string appearing in Status/notes **prose** is therefore mis-read as a pinned case; because it is not referenced by the PR's changed test files, the gate raises a blocking `missing_tc` finding even though:
1. the ticket's *real* pinned TCs are all present in the changed specs;
2. the flagged case is genuinely delivered elsewhere — `TC-RES-034` by the merged TKT-res-011, `TC-XC-009`/`TC-XC-018` by the dependency TKT-foundation-004; and
3. CI is green on the reviewed head.
The finding is a gate artifact, not a coverage gap, so it was waived rather than worked around — duplicating a dependency's test or pasting a bare token would be gaming the classifier.
**Follow-up (recommended):** fix the tool to source the pinned set from the `Acceptance:` line only, so prose can never spoof the TC gate. Also logged in `.pipeline/plan/run-report.md`.
