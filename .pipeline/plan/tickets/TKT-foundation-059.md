---
scope:
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-059: Watchdog edge-fix — the four `RunAttemptAsync` boundary defects from 041's round-3 late report

- Status: todo
- Size: S
- **Priority: P1 — dispatch-ready immediately (#114/041 is MERGED; the scheduler files are free). RECOMMENDED BEFORE FRIDAY'S 20:30 Istanbul EOD chain:** the watchdog is tonight's/tomorrow's chain-evidence instrument — its edge cases must be correct before the chain relies on it.**
- Source: the #114 review's late-report routing, round artifact `.pipeline/plan/reviews/TKT-foundation-041-round-3.md` (the late report's DR numbering). Target: `IngestionScheduler.RunAttemptAsync` (src/Ingestion/Scheduling/IngestionScheduler.cs:302) — a ~15-line edge-case repair, no redesign:
  - **DR-1 — prologue-into-try:** the pre-await prologue (ledger row creation, state transition) sits outside the try; a mis-typed `MaxRunDuration` (a value beyond `Task.Delay`'s accepted range) throws before the watchdog arm and can refire **unbounded and hot**. Move the prologue into the try so every failure path lands in the watchdog's accounting.
  - **DR-2 — post-cancel `IsCompletedSuccessfully` re-check:** after the watchdog's cancellation fires, the code can still observe the task completed successfully — writing BOTH a success row and a RUN_HUNG row for one attempt. Re-check and prefer the terminal truth (one row, never both).
  - **DR-3 — snapshot hoist before cancel:** the run-state snapshot used for the RUN_HUNG row is taken after cancellation, naming the wrong step. Hoist the snapshot before the cancel so the row names the state the run was actually in when it hung.
  - **DR-5 — the two untested boundary branches from the round-2 fixer:** the boundary branches the round-2 fix introduced carry no tests. Pin both with red→green coverage (the exact branches per the round-3 artifact).
- Traces to: FR-MDF-016 (pipeline health — the watchdog's honesty), the hung-run-watchdog doctrine 041 landed (§10.9.x scheduling section); TKT-foundation-041 heritage (merged, PR #114 — round-3 late report); TKT-foundation-040's chain (the 20:30 EOD chain the watchdog guards).
- Acceptance: **explicit criteria; L2 doubles only.** **(i) Red→green per defect:** SchedulerTests gains one test per DR (DR-1: a mis-typed MaxRunDuration fails into the watchdog's accounting — bounded, single refire semantics, no hot loop; DR-2: a run completing at the cancellation boundary produces exactly ONE ledger row, the true terminal one; DR-3: the RUN_HUNG row names the pre-cancel step; DR-5: both round-2 boundary branches covered). **(ii) Existing scheduler/watchdog tests re-run green** (the 041 suite — semantics preserved, edges repaired). **(iii) CI green.** **Live criterion (orchestrator, honest):** the next EOD chain runs under the repaired watchdog with no spurious RUN_HUNG/success double rows and no hot-refire signatures in the ledger; observed behavior recorded verbatim on this ticket.
- Architecture refs: `01-system-architecture.md` — the scheduling/watchdog section 041 added (edge semantics corrected; a correction note, not a redesign), §13.1 **v1.5.37**.
- UX refs: none.
- Dependencies: none — #114 merged; the files are free. Coordination: §13.1 v1.5.37 above the in-review v1.5.35 (057/#120) — last-lander rebases; dispatch order urgency: before Friday 20:30 Istanbul.
- Parallel group: none
