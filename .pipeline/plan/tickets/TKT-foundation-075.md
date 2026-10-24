---
scope:
  - docs/live-vs-ci.md
  - .pipeline/testing/00-test-strategy.md
---

# TKT-foundation-075: Live-vs-CI verification doc — state what CI proves and what only live criteria prove

- Status: done — **merged 2026-10-24** as PR **#135** (squash, `dev@4feb236`), head `11afd06`.
  Review loop ran **3 rounds** and returned MERGEABLE with no disputes, no upstream defects, no
  waiver: `.pipeline/plan/reviews/TKT-foundation-075-round-{1,2,3}.md`. Round 1 was
  **not-mergeable** on three evidence-discipline blockers (`DRI-1`..`DRI-3`) — the doc credited CI
  with a "secrets scan" and "dependency audit" that `ci.yml` does not run; §1's thesis was false
  about its own §5.5 example; and four thin-day figures (`21:53`, `₺8.6 B`, `runs 245–248`,
  `27/72/1 ₺148.8 B`) existed in no artifact. All 11 findings closed by round 3.
  **Process lesson (recorded at the review-lead's request):** the facet gate returned
  `presentation_only=true`, which normally means **zero reviewers** — but this doc's subject *is*
  what CI proves, so prose review was commissioned anyway and was the only thing that caught all
  three blockers. For docs that assert verification coverage, "no executable logic" is not a
  low-risk signal. Delivered: `docs/live-vs-ci.md` (new) + `00-test-strategy.md` §9.5.
  **No live criterion applies** — the deliverable is normative documentation, not runtime behaviour.
- PR: https://github.com/sarperim/degerli/pull/135
- Size: S
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 12 unparked).**
- **Evidence (gathered 2026-10-24 — this week is the worked example):**
  - **CI was green while the live chain fail-stopped at `statements` six times:** runs 227/233/236/244 all reported `failed` on the ER005 stale-cursor world, and the 14:06/14:17 UTC zero-write runs (run 212: written 0 / 24 failed / 6 quarantined) fail-stopped the EOD chain **before** `metrics-recompute` / `market-snapshot` / `medians` (TKT-foundation-063 ticket). CI's doubles answered the requested indices normally; the real gateway answered **HTTP 400 `ER005`**.
  - **The `RUN_HUNG` 4 h bound is a config workaround:** `Ingestion__Scheduler__MaxRunDuration=04:00:00` was set operationally because the shipped 90-minute bound made a healthy 100–120-minute chain impossible (TKT-foundation-064, `run-report.md:177`). The fake-clock tests could not see it.
  - Other same-week examples: 047's İş `value: []` for the current session (CI blind), 032's `/api/vyk` prefix bug (CI doubles bind at the root, so the bug was invisible), 058/065's live-only unit question (64/556 sub-unit rows).
  - The existing test strategy already separates manual/operational items (§5.6, §9.4) but has no doc that states the **boundary** as a usable checklist.
- **Pinned approach:**
  - Write `docs/live-vs-ci.md` with three explicit columns:
    1. **What CI proves** — deterministic L1–L4 against doubles/fixtures; schema/migration/contract/behaviour; no real source, no real credentials, no real network.
    2. **What only live proves** — real source shapes and status semantics, paced chain completion, per-source unit cross-validation, RTO/RPO, SC-001..SC-008, uptime.
    3. **What neither proves** — future source changes, load beyond the tested universe, unrecorded operational drift.
  - Add a one-line classification rule to `00-test-strategy.md` §9.4/§10: every ticket's Acceptance must classify each criterion as **CI / live / manual**, and live criteria must be recorded verbatim post-merge (the existing convention, now explicit).
  - Use this week's incidents as worked examples; cite the actual runs/tickets, never paraphrase into invented evidence.
  - No CI change; doc + convention only.
- **Acceptance (explicit criteria):**
  - **(i)** The doc exists with the three-column boundary and the worked examples above.
  - **(ii)** A one-line classification rule is added to the test strategy; every future ticket can be classified with it.
  - **(iii)** The examples cite the real runs/tickets; no evidence is invented.
  - **(iv)** No requirement is left methodless — the doc explicitly references the coverage matrix's Method column.
- Traces to: NFR-MDF-005/NFR-MOV-003 (operational verification), SC-001..SC-008; test strategy §5.6/§9.4.
- Architecture refs: none.
- UX refs: none.
- Dependencies: none.
- Parallel group: **P-1** (scopes disjoint from 068/074/076).
