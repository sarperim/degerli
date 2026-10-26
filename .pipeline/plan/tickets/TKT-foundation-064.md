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

- Status: done — **PR #133 merged earlier this run; live criterion OBSERVED 2026-10-26 05:45 UTC (window: 14 h, 23 runs).** Verbatim ledger (`ingest_runs`): `total=23, hung=0, failed=21` — **zero RUN_HUNG across the window; the criterion is met.** Today's scheduled chain fired as designed: `macro-daily 05:00:00 → 05:00:05 succeeded`; `prices 05:30:00 → 05:30:00 succeeded`. The 21 failures are all three funds jobs (`fund-nav`/`fund-holdings`/`fund-performance`, 7 runs each, yesterday 16:00–18:50), every one `SOURCE_NOT_CONFIGURED: The TEFAS fund source has no base address: 'Ingestion:Funds:BaseUrl…'` — the known pre-existing funds state (120/120 historical runs of those jobs failed identically), honest named degradation, unrelated to the scheduler race this ticket fixed. **Deploy pin LIFTED 05:45 UTC** — the dev box takes the accumulated batch (079/080/081/085/082; 086 pending user D5 ratification).
  — pre-merge record: - Status: in-review — **PR https://github.com/sarperim/degerli/pull/133** (branch `feature/TKT-foundation-064`, head **`ec8951e`**, rebased onto **`origin/dev@60a01cf`**). Doc slice landed: `01` §10.9.17 "The bound's scope, corrected" + §13.1 **v1.5.43**; test plan §16 TC-MDF-098 implemented + v1.17 record.
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

## Implementation record (2026-10-24, coder lane — PR #133)

**What shipped.** Two independent watchdog bounds in `IngestionScheduler`/`IngestionSchedulerOptions`:

- `MaxStepDuration` — new, code default **120 min** (`IngestionSchedulerOptions.DefaultMaxStepDuration`). Each chain step (`runner.RunAsync(jobCode, …)` inside `ExecuteChainAsync`) is awaited under its **own** clock-driven deadline, started immediately before the step and armed only for that step's lifetime. On expiry the step's progress is snapshotted **before** any cancellation and handed to the attempt-level watchdog, which cancels the attempt, writes the single row + alert, and lets the ladder take over. Because it is a `TimeProvider` timer, it is deterministic on the fake clock and cannot fire after the step has ended (it is disposed with the step's linked CTS).
- `MaxRunDuration` — remains the outer race around the whole attempt; code default **90 min → 4 h** (`DefaultMaxRunDuration`). `<= 0` disables each bound **independently**.
- **Honesty (one report, two classes).** Exactly one `RUN_HUNG` row + one alert per event, naming the in-flight step and stating which bound fired: `stats_json.bound: "step" | "chain"`, both bounds additively (`maxStepDurationSeconds` / `maxRunDurationSeconds`), and `per-step bound <d>` / `whole-chain ceiling <d>` in the row's `error`, the alert subject & body and the `Hung run detected` log line. The startup line now names both configured bounds.
- **041/059/062 preserved.** Pre-cancel snapshot (DR-3), successful finish inside the cancel window is the terminal truth (DR-2 — a chain that merely *ends* on our cancellation is not a success), host stop writes nothing (DR-5b), retry measured from the attempt's actual end, unexpected cancellation still enters the ladder, `TickAsync`/stream independence untouched, mis-typed bound → DR-1 failed attempt (never a hot refire). All five 059/081 boundary tests pass unmodified.

**Red → green (TC-MDF-098, `tests/Api.IntegrationTests/EodStallWatchdogTests.cs`, doubles only, fake clock, no real `Task.Delay`).**

| Case | Red (pre-fix) | Green |
|---|---|---|
| constant: shipped 120 min step / 4 h chain, both keys bind from config | `Expected: 04:00:00, Actual: 01:30:00` | pass |
| (a) 40 min + 60 min healthy chain completes, zero `RUN_HUNG`, 60-min step asserted inside the 120-min bound | `RUN_HUNG: chain step 'slow-step' exceeded the 01:30:00 bound` — the healthy chain declared hung mid-step | pass |
| (b) hung step cancelled at its own 10-min bound: one row, `bound:"step"`, `maxStepDurationSeconds`=600, `per-step` alert, retry from attempt end, next attempt `succeeded`, no second `RUN_HUNG` | the tick never returned (`Assert.Same` in `AwaitTickAsync`) — no per-step bound existed | pass |
| (c) individually-healthy steps over a 10-min ceiling: one row naming the in-flight step, `bound:"chain"`, `maxRunDurationSeconds`=600, `whole-chain` alert | `Ledger stats lack 'bound'` | pass |
| supporting: mis-typed `MaxStepDuration` (60 days) → 4 ladder attempts + `RETRIES_EXHAUSTED`, zero rows, no hot refire | (DR-1 path pinned by this case) | pass |

Red run: **4 failed / 0 passed** (`dotnet test --filter FullyQualifiedName~TC_MDF_098`). Green run: **5/5**.

**Non-regression (targeted suite + full local runs).**
- `SchedulerTests` + `EodStallWatchdogTests` → **19/19** (the watchdog/059/081 subset re-run 5× consecutively, 16/16 every time).
- `EodChainTests`, `MorningChainTests`, `EodStaleServingTests`, `FundSchedulingCoverageTests`, `MacroIngestionTests`, `BackfillAndCoverageTests`, `MetricsRecomputeIdempotencyTests`, `MarketSnapshotTests` → **49/49**.
- Full `tests/Api.IntegrationTests` → **515/515** (4 m 7 s); `tests/Core.UnitTests` → **54/54**; `dotnet build Degerli.sln` → **0 warnings, 0 errors**.

**Flagged items.**
1. **One harness-only delta, no assertion weakened:** `SchedulerTests.TC_MDF_081(b)` now also sets `Ingestion:Scheduler:MaxStepDuration = 12:00:00` alongside its existing 12 h `MaxRunDuration`. The test's premise is "the retry attempt is still in flight at 05:30Z"; with the new per-step bound that attempt would (correctly) be declared hung at 22:35. This is the same premise-preserving adjustment the test already made for the attempt bound — no expected value, assertion, or TC id changed.
2. **Scope:** no file touched outside this ticket's `scope:` list. Doc edits rebased onto `origin/dev@817de1b`, then again onto `origin/dev@60a01cf` (see the rebase record below); no conflict with 063/065.
3. **Live criterion (orchestrator post-merge, not fabricated here):** with `Ingestion__Scheduler__MaxRunDuration=04:00:00` removed from `/tmp/opencode/dev-overrides.yml`, a scheduled EOD chain completes end-to-end with **no `RUN_HUNG`** and per-step durations recorded; the shipped 120 min / 4 h defaults are confirmed in effect; the observed per-step durations are to be recorded verbatim below when measured.

## Rebase record (2026-10-24, coder lane — PR #133)

**Why.** Review closed reviewer-clean; the PR was held by two mechanical gates only: G4 (no CI run had ever been created for head `fb7cc88`, a stale-head artefact — no run, not a red run) and G5 (PR `CONFLICTING` after `dev` moved `a490394` → `60a01cf`, 045/PR #131 merging mid-review).

**What.** Rebased `feature/TKT-foundation-064` onto `origin/dev@60a01cf`; old head `fb7cc88` → **new head `ec8951e`**, force-pushed (`--force-with-lease`). Exactly one conflict, in `.pipeline/architecture/01-system-architecture.md` §13.1 — the anticipated "doc edits serialize by last-lander rebase" case. `dev` had added the **v1.5.25** (044) and **v1.5.26** (045) rows at the table's append point; resolved by **keeping both landed rows and our v1.5.43 row**, appended below them in the table's existing append order.

**Renumbering: none — v1.5.43 kept.** The landed maximum after the merge is **v1.5.42** (TKT-foundation-063), which is below 064's reserved v1.5.43; the reservation is still the next free number, so no row was renumbered and no landed row was dropped.

**Local verification after the rebase (worktree `/tmp/opencode/worktrees/TKT-foundation-064`).**
- `dotnet build Degerli.sln` → **0 warnings / 0 errors**.
- `SchedulerTests` + `EodStallWatchdogTests` → **23/23 passed**, including the load-bearing **TC_MDF_098_Slow_but_healthy_chain_completes_under_the_shipped_bounds** (the ~200-minute two-step healthy chain: 40 min + 60 min under the shipped 120 min step / 4 h chain defaults) alongside the step-bound, chain-ceiling, per-step-arming, responsive-step, zero-bound and mis-typed-bound cases, and the 059/062 boundary tests (TC-MDF-079, TC-MDF-081b) — no assertion changed by the rebase.
- `EodChainTests`, `MorningChainTests`, `EodStaleServingTests`, `FundSchedulingCoverageTests`, `MacroIngestionTests`, `BackfillAndCoverageTests` → **32/32 passed**.

**CI for the new head (G4).** Run exists for `ec8951e`: **https://github.com/sarperim/degerli/actions/runs/40057126324** (workflow "CI", jobs `Backend (restore · build · test)` and `Web (lint · typecheck · test · i18n · build)`), confirmed via runs-by-SHA and check-runs for the head commit — not inferred from the push. PR #133 now reports `mergeable: MERGEABLE` (G5 closed).

