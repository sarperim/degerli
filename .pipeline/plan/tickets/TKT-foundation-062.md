---
scope:
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-062: Scheduler retry-delay starvation — a retry scheduled for T+60 fired ~11h49m late and starved the morning chain (diagnosis-first)

- Status: todo
- Size: M
- **Priority: P1 — GATED on TKT-foundation-059 merging** (same files: `IngestionScheduler.cs` + `SchedulerTests.cs`; 059 is in flight). Dispatch 059 first, then this.
- **Verified evidence (orchestrator, Thursday night → Friday morning, 2026-10-22/23):** after the EOD chain's attempt 3 failed at **21:52:29 UTC**, the scheduler logged `Retry 3 for 2026-10-22 scheduled at 2026-10-22T22:52:29+00:00` (`IngestionScheduler.cs:453`) — then emitted **ZERO log lines until 10:41:24 UTC Friday**, when it started attempt 4 (chain step 1/9, Date 2026-10-22) unbidden. Meanwhile the **Friday 08:30 Istanbul morning chain (05:30 UTC) never fired**. So: a retry scheduled for +60 min fired **~11h49m late**, and the morning chain was **starved** while the retry was pending. New behavior, post-041 (the retry ladder itself is 041's; the starvation interaction is not).
- **Pinned approach — diagnosis-first, never fix-by-hypothesis:** reproduce on the fake clock (`SchedulerTests`' clock harness) before touching code. Planner hypotheses to test (in order of suspicion, all falsifiable on the fake clock):
  1. **Retry-delay task clock/cancellation interaction:** the retry's delay computation or its cancellation registration mis-handles the clock or a disposed token, so the delayed tick never observes due-ness until an unrelated wake-up (attempt 4 starting at 10:41 "unbidden" suggests something else woke the loop).
  2. **Single-threaded loop awaiting a stale delay:** the loop awaits a retry delay whose scheduled-at state a later tick (or the morning-chain tick) invalidated/overwrote — the loop then waits on a delay that will never be re-evaluated.
  3. **Morning-chain tick skipped while a retry is pending:** the morning stream's due-check is gated on "no retry pending" (or shares the loop's single await), so a pending retry silently suppresses the morning chain's 05:30 UTC fire.
  The diagnosis (which hypothesis, with the fake-clock repro) is recorded verbatim on this ticket BEFORE the fix; the fix is then pinned to the diagnosed mechanism with red→green tests. **Coordination with 059 (do not duplicate):** 059's DR-1..DR-5 stay as-is (the `RunAttemptAsync` edge repairs); if the diagnosed mechanism lives in `RunAttemptAsync`/the retry scheduler's interaction with it, this ticket pins the interaction with its own tests and references 059's landed shapes — no rework of 059's DRs.
- Traces to: FR-MDF-016 (pipeline health — schedules fire when due), §10.9.17 (the scheduler/retry/watchdog doctrine 041 landed — this ticket repairs its starvation edge), §10.9.16 (the morning chain that was starved); TKT-foundation-041 heritage (the retry ladder), TKT-foundation-059 (in flight — same files, serialized), TKT-foundation-061 (Thursday's chain failure that exercised the retry ladder — the trigger, not the cause).
- Acceptance: **explicit criteria; L2 doubles only (fake clock).** **(i) Diagnosis recorded** on the ticket: the fake-clock reproduction (which hypothesis, the mechanism, verbatim evidence) BEFORE any fix. **(ii) Red→green retry punctuality:** a retry scheduled for T fires within its ladder tolerance on the fake clock (red: fires ~12 h late / never re-evaluated). **(iii) Red→green morning-chain independence:** the morning chain fires on schedule while a retry is pending (red: starved). **(iv) 059's tests re-run green unchanged** (no duplication, no regression of the edge repairs). **(v) Existing scheduler tests re-run green.** **(vi) CI green.** **Live criteria (orchestrator, honest, recorded verbatim):** on the dev stack, a scheduled retry fires within its ladder tolerance of its scheduled-at time, and the next morning chain fires on schedule even with a retry pending (observable in the ledger/log timeline over the next chain cycle).
- Architecture refs: `01-system-architecture.md` §10.9.17 (the scheduler/retry doctrine — the starvation mechanism and its repair recorded), §13.1 **v1.5.41**; test plan: the scheduler test lane (`SchedulerTests` fake-clock harness; TC-MDF numbering follows the plan's next free ID — delivery-planner-applied, test-planner confirmation pending).
- UX refs: none.
- Dependencies: **TKT-foundation-059** (same files — serialize). Coordination: §13.1 v1.5.41 above the pending v1.5.39 (#122) and 061's v1.5.40 — last-lander rebases.
- Parallel group: none
