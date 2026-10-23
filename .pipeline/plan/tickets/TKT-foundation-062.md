---
scope:
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-062: Scheduler retry-delay starvation — a retry scheduled for T+60 fired ~11h49m late and starved the morning chain (diagnosis-first)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/126 (branch `feature/TKT-foundation-062`, base `dev`)
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

## Diagnosis (recorded verbatim BEFORE the fix — fake-clock reproduction first)

**Hypothesis confirmed: 3 — "morning-chain tick skipped while a retry is pending", in its
precise engine form: *a due instant that falls while any stream's attempt is in flight is not
evaluated until that attempt ends, and the overdue work then runs at the release instant, not
at its due instant.*** Hypotheses 1 and 2 are falsified (below).

### Mechanism (src/Ingestion/Scheduling/IngestionScheduler.cs, pre-fix)

`TickAsync` collects the due streams under `_gate` and then executes them **sequentially,
awaiting each attempt to its end**:

```csharp
foreach (var (stream, run) in due)
{
    await RunAttemptAsync(stream, run, now, cancellationToken).ConfigureAwait(false);
}
```

The production loop (`IngestionSchedulerHostedService`) calls `NextWake` → `Task.Delay` →
`TickAsync` and is a **single logical loop**: while it is inside `await RunAttemptAsync(...)`
it cannot compute `NextWake`, cannot delay, and cannot tick. Consequences, exactly:

1. **Retry punctuality (acceptance ii).** A retry armed for `T` on stream B is only ever
   noticed by the `now >= active.NextAttemptAt` check in the *next* `TickAsync`. While stream
   A's attempt holds the tick, that check does not run, so retry `T` is late by **the whole
   remaining duration of A's attempt** — there is no bound on it other than A's own watchdog
   bound (`MaxRunDuration`, 90 min by default), and none at all for the documented
   synchronous-block class (§10.9.17 "Scope of the guarantee").
2. **Morning-chain independence (acceptance iii).** The morning stream's cron occurrence is
   consumed by the same `TickAsync` loop; when 05:30 UTC arrives inside A's attempt, nothing
   observes it. On release the occurrence is consumed with `nextAttemptAt: now` — i.e. the
   20:30/05:30 occurrence fires **at the release instant**, which is precisely the live trace:
   the morning chain started at `12:11:24Z`, the instant the EOD attempt's `RUN_HUNG`
   watchdog released the loop, ~6h41m after its 05:30Z due time; and the armed EOD retry
   (`Retry 4 scheduled at 12:16:24Z`) then queued behind it.
3. **Ordering inversion.** Because the released tick starts *both* the starved morning chain
   and (on the following tick) the armed retry, the overnight retry ladder and the morning
   safety net stop being independent lanes and become one serial queue behind whichever
   attempt happened to be in flight.

### Fake-clock reproduction (red, before any code change)

Two doubles-only cases added to `tests/Api.IntegrationTests/SchedulerTests.cs` (`BuildStarvationHost`,
the real engine + `FakeTimeProvider` + the TKT-foundation-059 `RecordingJobRunner` double):

- `TC_MDF_081_Retry_fires_at_its_scheduled_instant_while_another_stream_holds_the_tick` — EOD
  attempt 1 fails at 20:30 (retry 1 armed for 20:35); a per-job stream's attempt starts at
  20:31 and does not end on its own. Advance the fake clock to 20:35: the retry is **not**
  evaluated. `dotnet test --filter TC_MDF_081` → `System.TimeoutException` at the
  "retry entered" wait (30 s liveness bound, fake clock at 20:35). The retry was not late in
  a timing sense at all — it was **never re-evaluated** while the other stream held the tick.
- `TC_MDF_081_Morning_chain_fires_on_schedule_while_a_retry_attempt_holds_the_tick` — EOD
  attempt 1 fails at 20:30, retry 1 starts at 20:35 and its step hangs (the 2026-10-22 live
  shape); the morning stream's 08:30 Europe/Istanbul (05:30Z) occurrence falls while that
  retry attempt is in flight. Advance the fake clock to 05:30Z: **nothing starts**. Same
  `System.TimeoutException` at the morning-chain wait.

Verbatim runner evidence (red, pre-fix, `git checkout --` of `IngestionScheduler.cs` with the two
new tests in place):

```
[xUnit.net]     SchedulerTests.TC_MDF_081_Retry_fires_at_its_scheduled_instant_while_another_stream_holds_the_tick [FAIL]
[xUnit.net]       System.TimeoutException : The operation has timed out.
     at ...SchedulerTests.TC_MDF_081_Retry_fires_at_its_scheduled_instant_while_another_stream_holds_the_tick() in tests/Api.IntegrationTests/SchedulerTests.cs:line 392
  Failed ...TC_MDF_081_Retry_fires_at_its_scheduled_instant_while_another_stream_holds_the_tick [32 s]
[xUnit.net]     SchedulerTests.TC_MDF_081_Morning_chain_fires_on_schedule_while_a_retry_attempt_holds_the_tick [FAIL]
[xUnit.net]       System.TimeoutException : The operation has timed out.
     at ...TC_MDF_081_Morning_chain_fires_on_schedule_while_a_retry_attempt_holds_the_tick() in tests/Api.IntegrationTests/SchedulerTests.cs:line 453
  Failed ...TC_MDF_081_Morning_chain_fires_on_schedule_while_a_retry_attempt_holds_the_tick [30 s]
Total tests: 2 / Failed: 2
```

(line 392 is the "retry attempt reached its step" wait with the fake clock parked at 20:35, i.e.
the armed instant; line 453 is the "morning chain reached its last step" wait with the fake
clock parked at 2026-10-06 05:30Z. Both are `TimeoutException` at the 30 s real-time liveness
bound — the fake clock had already been advanced to the due instant, so this is a
never-evaluated due instant, not a slow one.)

### Hypotheses falsified

- **(1) retry-delay clock/cancellation interaction:** falsified. The delay arithmetic is
  correct and clock-independent — the fake-clock repro arms the retry at exactly 20:35
  (`_clock.GetUtcNow() + RetryDelays[0]`, and the live `Retry 3 … scheduled at
  2026-10-22T22:52:29+00:00` is 21:52:29 + 60 min). Nothing about the token or the delay
  affects it: the engine is never *asked* whether the retry is due.
- **(2) single-threaded loop awaiting a stale delay:** falsified as stated — the awaited delay
  is never stale (`NextWake` recomputes the minimum over retry instants and cron occurrences
  each iteration). The starvation is one layer up: the loop is not awaiting a delay at all,
  it is awaiting an *attempt*.

### Honest limit of this diagnosis (flagged, not fixed by hypothesis)

The mechanism above bounds the lateness of a starved due instant by the duration of the
attempt holding the tick. The recorded night window (21:52:29Z → 10:41:24Z, ~11h49m, "zero
log lines") is longer than one 90-minute watchdog bound, so the *duration* of that specific
night's hold is not fully explained by this ticket's mechanism alone: it requires the holding
attempt to have exceeded its own bound. §10.9.17's "Scope of the guarantee" already records
the one class that can do that — a **synchronous** block inside a chain step, which an
in-process timer cannot pre-empt — and names it as unbounded. This ticket repairs the
*starvation interaction* (a due instant must never be postponed by an unrelated in-flight
attempt beyond that attempt's own bound) and pins it on the fake clock; it does **not** claim
to bound a synchronous block, and no follow-up is invented here. The shape of the live trace
(morning chain starting at the exact instant of loop release) is what this ticket's repro
reproduces deterministically.
