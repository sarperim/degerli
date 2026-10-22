---
scope:
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/plan/tickets/TKT-foundation-046.md
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
---

# TKT-foundation-054: 046 post-merge reconciliation — TC-MDF-083 expected-result wording (DR-1) and the ticket-vs-§10.9.15 partial-rule reconcile (DR-3)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/119
- Size: S
- **Priority: P3 — dispatch-ready, docs-and-assertion alignment only; no functional change.** Non-blocking routing items from `reviews/TKT-foundation-046-round-1.md` (046 merged as PR #113, v1.5.22).
- **DR-1 (test-plan defect):** TC-MDF-083's pinned expected result says "exactly one log event names GLRMK with COMPANY_IDENTITY_UNRESOLVED", but the shipped code emits **two** honest events (the adapter's per-symbol skip + the job's summary) and the shipped test asserts `Contains` (weaker than the pinned expectation). Fix: amend TC-MDF-083's expected result to pin the two-event shape (per-symbol + summary, both named) — the test may then be tightened to assert both. Test-planner confirmation lane (the v1.2/v1.3 precedent: delivery-planner/test-planner-applied amendments recorded in the plan's change record).
- **DR-3 (wording reconcile):** the 046 ticket says the isolated run terminates "per the statements lane's rule (failed when all constituents fail)" while amended **§10.9.15 pins always-`partial`** for the family lanes. §10.9.15 is the permanent record and wins; reconcile 046's ticket wording to it (a dated correction note on the ticket file — history is amended transparently, never silently rewritten).
- Traces to: TKT-foundation-046 (merged) and its review round 1; TC-MDF-083; §10.9.15.
- Acceptance: **explicit criteria.** (i) TC-MDF-083's expected result names both events (per-symbol skip + job summary) and the shipped test asserts both (if the assertion tighten requires a code-adjacent test edit, it stays within the pinned scope of this ticket's two files plus the test file already listed under 046's lane — the test file itself is `StatementsAndEventsIngestionTests.cs`; a scope note: if the test assertion must change, add that file to this ticket's scope in the PR description and flag it for the review gate). (ii) 046's ticket carries the dated correction aligning to §10.9.15's always-`partial`. (iii) The test plan's change record gains the v-line for both amendments. **No live criteria** (docs + assertion alignment; CI green is the gate).
- Architecture refs: `01-system-architecture.md` §10.9.15 (the authority — unchanged by this ticket); no §13.1 row needed if no architecture doc changes — if the repo convention requires one for test-plan amendments, take **v1.5.34** (the pinned allocation).
- UX refs: none.
- Dependencies: none — dispatch-ready any time (046 is merged; no open PR touches these files).
- Parallel group: none
