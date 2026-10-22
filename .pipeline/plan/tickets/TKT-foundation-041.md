---
scope:
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - src/Ingestion/Scheduling/IngestionSchedulerHostedService.cs
  - src/Ingestion/Scheduling/IngestionSchedulerOptions.cs
  - src/Ingestion/Scheduling/EodIngestChain.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - tests/Api.IntegrationTests/EodChainTests.cs
  - tests/Api.IntegrationTests/EodStallWatchdogTests.cs
  - tests/Api.IntegrationTests/EodStaleServingTests.cs
  - tests/Api.IntegrationTests/MorningChainTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/03-api-design.md
---

# TKT-foundation-041: EOD chain silently stalls after `prices` (second consecutive night) — root-cause the inter-job stall, add a hung-run watchdog, and close the universe-sync nightly-chain gap

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/114
- Size: M
- **Priority: P1 — dispatch as soon as 040 (PR #110) merges** (shared scheduling files; see Dependencies). The nightly chain has now failed to deliver the overview row TWO consecutive nights without a single ledger row, log line, or alert — the front page exists only because the orchestrator manually repaired it each time (Monday run 101, Tuesday runs 118-124).
- Verified evidence (dev stack, 2026-10-20 ~20:30-23:50 Istanbul; planner-corroborated against the scheduler source):
  - **Tuesday:** the 20:30 Istanbul EOD chain fired; `prices` (ledger run 117) started 17:30:00 UTC, finished 17:37:03 — `succeeded, written 77/100, failed 0`. Then **NOTHING**: no `statements` ledger row, no metrics-recompute, no market-snapshot, no error/exception in the API logs, process alive and serving throughout. Monday: identical signature (recorded as out-of-scope evidence on TKT-foundation-040).
  - The stall is **between prices completing and the next chain step even starting** — not a job failure (a failed job writes a ledger row and enters the retry ladder; neither happened) and not the 20:12:53 UTC SIGABRT (finding F — the stall began at 17:37, the abort was 2.5 h later).
- Planner's code reading (candidate classes for the investigation — NOT a root-cause claim): the production loop (`IngestionSchedulerHostedService.ExecuteAsync`) **awaits one `TickAsync` to completion**, and `TickAsync` runs each due stream's `RunAttemptAsync` sequentially, which runs the chain's jobs sequentially — so **a single await that never completes inside the next job's execution (an HTTP call without timeout, connection-pool starvation, a pacer/semaphore wait) stalls the ENTIRE scheduler silently**: no further wakes, no retries on any stream, no exhausted-alert, process healthy. Corroborating detail: fund-job retry entries were being scheduled in the same window ("Retry 3/4/5 for 2026-10-20 scheduled at 17:30/17:40") — determine whether those retries ever fired AFTER 17:37 (if they did, the loop was alive and the stall is elsewhere, e.g., a swallowed exception between jobs or a state-machine path that neither completes nor retries `stream.Active`); if they did not, the hung-await class is confirmed. Note the ledger writes rows at job completion — a hang inside a job before completion leaves no row, exactly the observed signature.
- **Universe-sync question — ANSWERED from the scheduler source: `universe-sync` is NOT in the nightly chain.** `EodIngestChain.JobCodes` = prices → statements → dividends → corporate-actions → disclosures → metrics-recompute → market-snapshot → sector-medians (8 jobs), and `JobSchedules` carries only macro/fund jobs — **universe-sync has NO schedule at all** (admin trigger / cold-start only). Nightly index-level refresh is therefore a **design gap**, not a swallowed step: Tuesday's exact official closes (XU100 11,797.70 / XU030 14,676.53, run 122) existed only because the orchestrator ran universe-sync manually. This ticket closes the gap: **`universe-sync` is prepended to the EOD chain** (before prices — membership changes and the day's index levels then feed prices' constituent resolution and the snapshot's index moves; the job is idempotent, provenance-marked, ~3 cheap İş Yatırım calls, launch-seed once-only logic unaffected).
- Scope: **(a) Root cause the inter-job stall** (investigation-first, doubles-only reproduction: a fake-clock/double world where the chain reproduces the silent stall — e.g., a job whose await never completes or whose exception is swallowed between steps — then fix it; if the root cause lands in a file outside this ticket's scope list (e.g., a specific job's HTTP timeout), amend scope per the TKT-foundation-034 Option-A precedent rather than widening silently). **(b) Hung-run watchdog + chain-step observability** so this class can never recur invisibly: per-job chain-step progress (log + ideally ledger-visible attempt context), a max-duration guard on a stream's `ActiveRun` (configurable; on expiry: ledger row + `IngestionAlert` with a named reason code, e.g. `RUN_HUNG` — the alerting machinery already exists), and every exception path between chain steps either propagates to the retry ladder or records honestly. **(c) Universe-sync joins the EOD chain** (above). **(d) Docs:** `01` new §10.9.17 (chain-orchestration reliability: watchdog, the single-loop stall class, universe-sync membership) + §13.1 **v1.5.17**; `03` §9 EOD-chain row updated (universe-sync prepended); test plan TC-MDF-079/080 (delivery-planner-applied, test-planner confirmation pending).
- Traces to: FR-MDF-009 (scheduler/retry — the machinery that failed to engage), FR-MDF-012 (failure isolation — a silent stall is the isolation worst case), NFR-MDF-001 (run-ledger budget/completeness — the ledger must never have a silent hole), FR-MDF-006 (index levels — the universe-sync gap), FR-MOV-001..006/021 + NFR-MOV-001 (the overview row existing at all); UC-MDF-001 alternates (failed-run recovery); foundation pseudo-domain convention (post-merge defect + design-gap fix — explicit criteria below; the 030/032 precedent).
- Acceptance: **explicit criteria; L2 doubles only, never live sources; the scheduler's fake-clock determinism is the seam.** **(i) Red→green stall reproduction (TC-MDF-079):** a double world where the chain's post-prices step never completes — under the old code the scheduler stalls silently forever (no ledger row, no alert, no further wakes — assert the fund-stream retry due later never fires); under the fix the watchdog fires within the configured max duration: a ledger row + `RUN_HUNG` alert + the stream recovers (next wake proceeds). **(ii) Exception-path honesty:** a job that throws between steps either enters the existing retry ladder (ledger row + retry log) or is fixed to do so — no silent swallow (test both a throwing job and a hanging job). **(iii) Universe-sync in chain (TC-MDF-080):** the EOD chain runs universe-sync first (ledger row, idempotent re-run no-op), and a world where index levels for the day exist produces a snapshot with real levels (no dependency on manual runs); existing `SchedulerTests`/`EodChainTests` re-run green with the composition delta pre-listed (the chain-order golden gains one leading job). **(iv) Config:** watchdog max-duration ships a sensible default; no behavior change with the scheduler disabled. **(v) CI green.** **Live criterion (orchestrator post-merge, the only honest proof):** the next scheduled 20:30 Istanbul EOD chain completes end-to-end on its own — universe-sync → prices → … → market-snapshot rows in the ledger, the overview serving the day's row with real index levels by ~21:00, ZERO manual repair; if the stall recurs, the watchdog's `RUN_HUNG` alert and ledger row fire (recorded verbatim either way — never fabricated).
- Architecture refs: `01-system-architecture.md` §10.9.13 (chain/selection doctrine — consumer), new §10.9.17 + §13.1 v1.5.17; `03-api-design.md` §9 (EOD chain row); test plan: `.pipeline/testing/market-data-foundation.md` TC-MDF-079/080.
- UX refs: none (no screen change; the user-visible effect is SCR-001's overview row existing nightly — the point of the chain).
- Dependencies: **TKT-foundation-040 (PR #110, in review) MUST merge first** — it owns `IngestionScheduler.cs`/`IngestionSchedulerOptions.cs` (the morning chain); this ticket builds on the same files (serialize-rebase behind #110; do not dispatch concurrently). **Complementary, not duplicate:** 040's morning chain is the SAFETY NET (08:30 repair of a stalled night); this ticket removes the root cause and adds the watchdog — both are needed, neither replaces the other. Phase-2 note: once 041 lands, 040's morning chain inherits the universe-sync question (its composition may want universe-sync before its repair gate — a one-line chain-composition decision recorded on 040's phase-2 lane, not a new ticket).
- Parallel group: none.

## Implementation record

- **Root cause (established by reproduction + the live log, not guessed).** The hosted loop awaits one
  `TickAsync`; a tick runs a stream's attempt, and the attempt its chain's jobs, **sequentially**; the run
  ledger is written by the job runner **at completion**. Any await that never completes inside a chain step
  therefore freezes the whole scheduler — no further wakes, no retries on any stream, no exhausted alert, no
  ledger row, process healthy. Live confirmation from the dev log (read-only): `prices` finished 17:37:03Z
  `succeeded` on 2026-10-20 while the fund retry **already due at 17:30:00.026Z** never fired and the API kept
  serving; the in-flight step was still running when the process was restarted at 20:15Z, so it never wrote a
  row. No duration bound existed anywhere between the cron and the ledger.
- **Fix.** Attempt-level bound `IngestionSchedulerOptions.MaxRunDuration` (default 90 min, `<= 0` disables),
  raced on the injected `TimeProvider`; on expiry: cancel, defer the abandoned task's scope/CTS to a
  continuation (exception observed), one `ingest_runs` row named for the hung step with
  `stats_json.reasonCode = RUN_HUNG` (+`hungJob`/`step`/`stepCount`/`runDate`/`attempt`/`retries`/
  `maxRunDurationSeconds`), one `RUN_HUNG` `IngestionAlert` through the existing seam, then the unchanged
  retry ladder measured from the instant the attempt **ended**. Per-job `Chain step {Step}/{Total}` progress
  logging. A cancellation that is not the host's stopping token is a failed attempt (previously escaped
  `TickAsync`). `universe-sync` prepended to `EodIngestChain`; morning chain untouched.
- **Red→green (doubles only, `FakeTimeProvider`, Testcontainers + WireMock).** Red with the fix stashed and the
  final tests in place: `EodStallWatchdogTests` + `EodChainTests` → 1 passed / **4 failed** (hung tick never
  returns; self-cancelling step escapes `TickAsync`; chain-order golden 8 vs 9). Green post-fix and post-rebase:
  `SchedulerTests · EodChainTests · EodStallWatchdogTests · MorningChainTests · EodStaleServingTests ·
  MacroIngestionTests · UniverseSyncTests · FundSchedulingCoverageTests` → **45/45**, build 0 warnings.
- **Scope amendment (recorded, reviewer-approved — 034 Option-A precedent):** the review lead approved
  this amendment in review round 1 (artifact
  `reviews/TKT-foundation-041-round-1.md`, G-1/G-2) per the TKT-foundation-034 Option-A precedent;
  `tests/Api.IntegrationTests/EodStaleServingTests.cs` and `tests/Api.IntegrationTests/MorningChainTests.cs`
  are declared in this ticket's `scope`. Both files drive the production **default** chain, so the prepended
  `universe-sync` step required the universe/index-levels source stubs and the +1 night ledger row there
  (MorningChain additionally uses the FU snapshot with every instrument classified, because it pins a *silent*
  night). No assertion weakened; 4–6 added lines per file. The planner's pre-listed delta covered only
  `EodChainTests` — closed here as a declared-scope amendment, not a silent widening.
- **Version renumber at rebase:** §13.1 **v1.5.17** was consumed by TKT-foundation-039 (PR #111, v1.5.16 +
  v1.5.17) while this lane was in flight; this PR's row landed as **v1.5.18**. Exact-note per the ticket's
  "last-lander rebases" rule.
- **Not fixed here (out of scope, flagged):** `src/Ingestion/Jobs/Jobs.cs` excludes `OperationCanceledException`
  from its failed-row write, so a self-cancelling job still leaves no per-job `ingest_runs` row (the scheduler
  now enters the ladder honestly). Belongs to the 044 ledger-error-honesty lane.
- **Live criterion** (orchestrator post-merge, never fabricated): the next 20:30 Istanbul chain completes
  end-to-end on its own (`universe-sync → … → market-snapshot` rows, overview serving the day with real index
  levels, zero manual repair), or `RUN_HUNG` fires verbatim.
