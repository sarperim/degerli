---
scope:
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-059: Watchdog edge-fix — the four `RunAttemptAsync` boundary defects from 041's round-3 late report

- Status: done — merged 2026-10-23 (PR #124; §13.1 v1.5.37). No live criterion of its own (edge repairs); the watchdog fired correctly on live hangs twice (runs 203, 208).
- PR: https://github.com/sarperim/degerli/pull/124 (base `dev`, head `feature/TKT-foundation-059`)
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

## Implementation record (2026-10-23)

**PR #124** (`feature/TKT-foundation-059`, base `dev`, rebased onto `cdc773c`). Scope: exactly the three declared files + this ticket file. No deviation from the design; §10.9.17 gained a **repair note** (edge semantics, mechanism unchanged), §13.1 the single-row append **v1.5.37** (v1.5.35 = 057 on `dev`; v1.5.36 held by the in-flight 058 branch — no collision).

### What was repaired (`IngestionScheduler.RunAttemptAsync`)

- **DR-1 — prologue into the `try`.** The attempt's owned resources (DI scope, both linked cancellation sources, the `Task.Delay` deadline) are created *inside* the failure-classifying `try`, and disposed (null-guarded) in its `finally`. A prologue exception is now a failed attempt on the ordinary 5/15/60 ladder instead of an escape that left `Attempts` bumped with `NextAttemptAt` still past.
- **DR-2 — post-cancel `IsCompletedSuccessfully` re-check.** After `attemptCts.Cancel()` + the awaited deadline-source cancel, a successfully completed attempt takes its own outcome: one row, no `RUN_HUNG` row, no alert, no retry. `hung` is cleared so the scope/CTS pair is disposed normally.
- **DR-3 — snapshot hoisted above the cancel.** The hung-step snapshot is taken the moment the entry completion re-check fails (before the shutdown check and before any cancellation), so a cancellation-swallowing step that returns `Partial` and lets the chain `Begin` the next step cannot rename the step in `job_code`/`hungJob`/the alert.
- **DR-5 — both round-2 branches pinned** (completes against the armed bound → one completed attempt; host stopping at the bound → no row, no alert, no `Hung run detected` line), with the hung-step snapshot/no-op semantics asserted rather than assumed.

Nothing else in the watchdog changed: the primary scenario (a chain step awaiting forever → bounded attempt, exactly one `RUN_HUNG` row + one alert naming the step, ladder resuming from the attempt end) is byte-identical in behaviour and still green.

### Test evidence (`tests/Api.IntegrationTests/SchedulerTests.cs`, doubles only, fake clock)

| Test | Before the fix (red, verbatim) | After (green) |
|---|---|---|
| `TC_MDF_079_DR_1_Mistyped_MaxRunDuration_is_classified_and_never_refires_hot` | `System.ArgumentOutOfRangeException : … less than or equal to the maximum allowed timer duration. (Parameter 'delay')` at `IngestionScheduler.cs:325` → escaped `TickAsync` | 4 attempts at 20:30/20:35/20:50/21:50, one alert `failed after 4 attempts`, **0** ledger rows, next wake = 2026-10-06 20:30 (still 4 attempts and 0 rows after sweeping to 23:59) |
| `TC_MDF_079_DR_2_Attempt_finishing_in_the_cancel_window_writes_one_true_row` | `Assert.Single() Failure: The collection contained 2 items` — `swallow-step` succeeded row **+** `RUN_HUNG` row, same attempt | exactly one row: `succeeded`, `attempt: 1`, no reason code; no `RUN_HUNG` alert; no retry after 20:47 |
| `TC_MDF_079_DR_3_RunHung_row_names_the_pre_cancel_step` | `Assert.Equal() Failure: Strings differ` — `hung.JobCode`/`hungJob` named `uninterruptible-step` (step 2) instead of `swallow-step` (step 1) | row + alert name `swallow-step`, `step: 1`, `stepCount: 2` |
| `TC_MDF_079_DR_5a_Attempt_completing_against_the_armed_bound_is_completed_once` | green on the merged head **by construction** (the branch existed; only its coverage was missing) — red proven by mutation, below | one completed row (`succeeded`, attempt 1), no `RUN_HUNG`, no alert, no `Hung run detected` line |
| `TC_MDF_079_DR_5b_Host_shutdown_at_the_bound_raises_no_RunHung_row_or_alert` | green on the merged head by construction — red proven by mutation, below | `OperationCanceledException` unwinds the tick, 0 rows, 0 alerts, no `Hung run detected` line |

**DR-5 red evidence is mutation-based and reported as such** (a pre-fix "red" is impossible: these two branches already existed, their defect was the absence of tests). With the completion re-check family disabled (`ReferenceEquals(winner, work) || work.IsCompleted` and the post-cancel check both neutralised): `DR-5a` → `Assert.Single() Failure: The collection contained 2 items`, `DR-2` → same. With the shutdown branch disabled (`cancellationToken.IsCancellationRequested` neutralised): `DR-5b` → `Assert.Throws() Failure: No exception was thrown`. Both mutations were reverted before commit.

**Deterministic-window note (DR-2).** The success row is committed by a doubled job runner with `CancellationToken.None`, pinning the "terminal result already recorded when the watchdog cancelled" interleaving; the production runner's write races that cancellation on the thread pool (a token-cancelled EF write throws), so without the double the window is a race and the test could not be deterministic. DR-3 needs the same double for the same reason. The scheduler engine, retry ladder, watchdog, alert seam and ledger table are all production paths.

**Caveat recorded honestly:** the `work.IsCompleted` half of the *entry* re-check (as opposed to `ReferenceEquals(winner, work)`) is reachable only in a genuine thread race — `winner == deadline` while `work` completed between the `WhenAny` completion and the check, with no await in between — so it cannot be pinned deterministically; DR-5(a) pins the branch's observable contract, and DR-2's post-cancel re-check covers the deterministic cancel-window completion.

### Suite evidence (post-rebase, focused scheduler/watchdog/chain suites)

```
SchedulerTests 8 · EodStallWatchdogTests 4 · EodChainTests 2 · MorningChainTests 7
EodStaleServingTests 2 · FundSchedulingCoverageTests 6 · MacroIngestionTests 12
Passed! - Failed: 0, Passed: 41, Skipped: 0, Total: 41
```

No existing assertion was weakened, skipped or re-interpreted. CI verdict is the PR's.

### CI evidence (run 39945081372, head `257afcf`)

```
Backend (restore · build · test)                  pass  2m49s
Web (lint · typecheck · test · i18n · build)      pass  1m17s
E2E (compose · Playwright · MailPit)              fail  3m43s
```

The E2E failure is the **standing-waived class**: the single red case is `global-ux.spec.ts:97` **TC-XC-004 — language switch preserves context › SCR-002 keeps the sector filter, search term, scroll and data cache** (the scroll-harness assertion), 75/76 green; it fails identically on `dev` and is the class TKT-foundation-060 was sliced to repair. This PR touches no Web/E2E file (its diff is `src/Ingestion/Scheduling/IngestionScheduler.cs`, `tests/Api.IntegrationTests/SchedulerTests.cs`, `01-system-architecture.md`, this ticket). Recorded, not waived silently.

### Live symptom seen on 2026-10-22 → investigation note (out of scope, routed)

Thursday's chain gives two facts: the watchdog **fired correctly** (`RUN_HUNG` ledger rows 185 and 190 carrying `hungJob`/`step`/`stepCount`, naming the `corporate-actions` step 5 — 041 working as designed), and then — after attempt 3 failed at 21:52 UTC and `Retry 3 … scheduled at 2026-10-22T22:52:29` was logged — the scheduler emitted **zero** lines for ~12.8 h and the retry ran at 10:41 UTC Friday.

What my reading of `RunAttemptAsync` + the hosted loop can and cannot say (offered as candidates for the diagnosis ticket, **not** a diagnosis):

- The ladder arithmetic cannot produce 22:52 → 10:41: the rung is end-relative `+60 min` for attempt 3→4 and the logged instant follows from it exactly.
- A tick that arrives with `now < NextAttemptAt` writes no line at all (it just recomputes `NextWake`), and the loop would then poll on a sub-millisecond delay — that is a silent *busy poll*, not a 12.8 h sleep, so it does not match either.
- A *synchronous* block inside the retry attempt is not silent: `Chain step … starting` is written before the step is awaited — the report says zero lines, which contradicts this class (unless the lines were lost downstream).
- A DR-1-class prologue escape is also excluded for the same reason: the hosted loop logs `Ingestion scheduler tick failed; will retry on the next wake` at Error, and then would refire hot rather than go quiet.
- Remaining candidates my read cannot exclude and that the diagnosis ticket should test first: the loop's own `Task.Delay(delay, _clock, stoppingToken)` wake-up not arriving (host/container paused, `BackgroundService` timer lost), a process restart between 22:52 and 10:41 with only the post-restart segment retained, or the run state being dropped (`stream.Active`) so the next wake is a cron instant rather than the retry. The scheduler holds retry state **in memory only** — a restart silently loses an armed retry until the next cron, which is a plausible silent-wait shape and worth naming in that ticket.

No implementation change was made for this symptom here (explicitly out of scope).

