---
scope:
  - src/Ingestion/Scheduling/IngestionScheduler.cs
  - src/Ingestion/Scheduling/IngestionSchedulerOptions.cs
  - tests/Api.IntegrationTests/SchedulerTests.cs
  - tests/Api.IntegrationTests/EodStallWatchdogTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-064: Scheduler watchdog — bound each chain step, keep a whole-chain ceiling

- Status: todo
- Size: M
- **Priority: P2 — correctness of the watchdog (USER GRANT 2026-10-23 afternoon).** The current bound is per **attempt**, not per **step**, so a healthy chain can never finish.
- **Verified evidence (dev stack, 2026-10-23):**
  - `IngestionSchedulerOptions.MaxRunDuration` (**90 min**) bounds the **whole chain attempt** (one deadline raced against `ExecuteChainAsync`).
  - A healthy paced chain legitimately needs **~100–120 min**: prices ~5 min @3 s, statements ~13 min @12 s, dividends ~16 min, corporate-actions **58–74 min** at ≥12 s/item, then disclosures + metrics-recompute + market-snapshot + medians. **A healthy chain therefore can never complete under the shipped bound.**
  - Thursday's "corporate-actions hung" and today's "disclosures hung" were both the **whole-attempt bound expiring mid-chain**, not a hung step — `RUN_HUNG` rows **203/208** name whichever step happened to be in flight. 041/059's watchdog is working as designed; the **scope** of its bound is wrong.
  - Operationally raised to **4 h** today via `Ingestion__Scheduler__MaxRunDuration=04:00:00` — a workaround, not the design.
- **Pinned fix (planner):**
  1. **Per-step bound.** Each chain step (`runner.RunAsync(jobCode, …)` in `ExecuteChainAsync`) is awaited under its **own** deadline on the injected clock (`Ingestion:Scheduler:MaxStepDuration`, code default **120 minutes** ≈ 1.6× the observed healthy 58–74 min corporate-actions walk, ~10× the 12 s pace). A step that exceeds **its own** bound is genuinely hung: it is cancelled/snapshotted from its own progress, the attempt fails, and the existing retry ladder takes over. The default is a constant in `IngestionSchedulerOptions`, overridable by configuration.
  2. **Whole-chain ceiling.** `MaxRunDuration` remains the outer attempt-level race and its code default becomes **4 hours** (the operational value proven today; ≈ 2× the healthy 100–120 min chain) — a sane ceiling that still bounds a pathological sequence of individually-healthy steps before the morning safety net. Zero/negative still disables each bound independently (documented).
  3. **One honest terminal report per attempt (041/059 semantics preserved).** A step-bound expiry and a chain-bound expiry each produce **exactly one** `RUN_HUNG` run-ledger row + one alert naming the in-flight step; the report states **which bound fired** (`step` vs `chain`) in the log/alert text and additively in `stats_json` (`maxStepDurationSeconds` / `maxRunDurationSeconds`, `bound: "step" | "chain"`). No double row when the step finishes inside the cancel window; the snapshot is taken before any cancellation; a host stop unwinds with no row/alert; the retry is measured from the attempt's actual end; a cancellation that is not ours still enters the ladder. **No change to `TickAsync`, stream independence (062), or the retry ladder.**
  4. **Config/diagnostic honesty.** `maxRunDurationSeconds` keeps its existing meaning (chain ceiling); `maxStepDurationSeconds` is added. A mis-typed bound still takes the 059 DR-1 failed-attempt path (never a hot refire).
- Traces to: FR-MDF-009 (scheduler/retry machinery), FR-MDF-016 (pipeline health — schedules complete; the watchdog's honesty), NFR-MDF-001 (no silent ledger hole); §10.9.17 (watchdog scope corrected: step bound + chain ceiling; 041/059/062 semantics cited and preserved); §10.9.16 (the morning safety net the ceiling stays ahead of).
- Acceptance: **explicit criteria; L2 doubles only, fake clock.**
  - **(i) TC-MDF-098(a) — "slow but healthy step is not hung":** a chain whose preceding steps consume ~40 min and whose current step takes ~60 min (total ~100 min) completes `succeeded` with one ledger row and **zero** `RUN_HUNG` under the shipped per-step 120 min / chain 4 h defaults (red today: the 90 min whole-attempt bound fires mid-step). The step's own duration is asserted against the step bound.
  - **(ii) TC-MDF-098(b) — "hung step is cancelled and named correctly":** a doubled step that never completes is bounded at its **step** bound (configured small on the fake clock); exactly one `RUN_HUNG` row names that step, `bound: "step"`, the alert names it, the retry ladder starts from the attempt's end, and the next attempt's healthy step lands `succeeded` (no second `RUN_HUNG`).
  - **(iii) TC-MDF-098(c) — chain ceiling still bites:** individually-healthy steps whose total exceeds a configured small `MaxRunDuration` produce exactly one `RUN_HUNG` naming the in-flight step with `bound: "chain"` (the ceiling is not lost).
  - **(iv) 041/059/062 unchanged:** `SchedulerTests` (incl. TC-MDF-079 with `MaxRunDuration = 10 min`, 059's DR-1/2/3/5, TC-MDF-011/012, TC-MDF-081) and `EodStallWatchdogTests` re-run green with no assertion weakened; `EodChainTests`, `MorningChainTests`, `EodStaleServingTests`, `FundSchedulingCoverageTests`, `MacroIngestionTests`, `BackfillAndCoverageTests` green; `dotnet build` clean.
  - **(v) CI green.**
  - **Live criterion (orchestrator post-merge, honest, recorded verbatim):** with the 4 h environment override removed, a scheduled EOD chain completes end-to-end with **no `RUN_HUNG`** and per-step durations recorded; the shipped defaults (120 min step / 4 h chain) are confirmed in effect; the log/ledger shows which bound would fire (none). The ticket records the observed per-step durations.
- Architecture refs: `01-system-architecture.md` §10.9.17 (watchdog: per-step bound + whole-chain ceiling; 041/059/062 boundaries preserved), §10.9.16 (cited), §13.1 **v1.5.43 (reserved above landed max v1.5.41; the row lands with the implementation)**; test plan: `.pipeline/testing/market-data-foundation.md` §16 TC-MDF-098.
- UX refs: none (scheduler correctness; user-visible effect is that healthy nights stop producing false "hung" and retries).
- Dependencies: none hard — 062 landed (PR #126, §13.1 v1.5.41) and 059 is merged, so the scheduler files are free. **Coordination:** scheduler files only; the two doc files are shared with 063/065 — doc edits serialize by last-lander rebase.
- Parallel group: none (doc-file overlap with 063/065)

## Evidence appendix (verbatim, 2026-10-23)

- `IngestionSchedulerOptions.MaxRunDuration` default: `TimeSpan.FromMinutes(90)`; enforced as one deadline around the whole `ExecuteChainAsync` attempt.
- Healthy-chain budget recorded today: prices ~5 min @3 s; statements ~13 min @12 s; dividends ~16 min; corporate-actions 58–74 min at ≥12 s/item; disclosures + metrics + snapshot + medians after.
- Operational workaround in effect today (to be removed by the live criterion): `Ingestion__Scheduler__MaxRunDuration=04:00:00`.
- `RUN_HUNG` rows **203/208** (2026-10-23) named the step in flight when the 90-min attempt bound expired — corporate-actions on Thursday, disclosures today — neither step individually hung.
