---
scope:
  - src/Core/Metrics/CanonicalMetrics.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Ingestion/Metrics/SectorMedianEngine.cs
  - src/Ingestion/Metrics/DerivedMetricStore.cs
  - src/Ingestion/Metrics/SectorMedianStore.cs
  - src/Ingestion/Jobs/Jobs.cs
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/Entities/ResearchEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/02-data-model.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-070: Metric formula versioning — recompute provenance across formula changes

- Status: in-review — **PR https://github.com/sarperim/degerli/pull/138** (branch `feature/TKT-foundation-070`, head **`0ca7009`** — the rebased implementation commit, with this rebase/ticket-note commit on top; **rebased onto `origin/dev@aa2d0b8`**, TKT-foundation-064, 078 and 048 merged)
- **PR:** https://github.com/sarperim/degerli/pull/138
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 6 unparked).**
- **Evidence (gathered 2026-10-24):**
  - `DerivedMetric` stores `Value`, `WindowYears`, `IsAdjusted`, `IsRested`, `RegimeBreak`, `ComputedAt` (`MarketDataEntities.cs:224-248`). `SectorMetricMedian` stores `MedianValue`, `PeerCount`, `ExcludedCount`, `IsAdjusted`, `ComputedAt` (`ResearchEntities.cs:35-48`). **No formula/definition version exists** anywhere.
  - `CanonicalMetrics` is the single definition of every metric (`BR-MDF-009`, one definition per number). A change to it silently changes every row produced by the next recompute.
  - TKT-foundation-050's input-aware recompute compares **values** and repairs changed rows in place, counting them `rewritten`. A formula change is therefore indistinguishable from a fact restatement, and `computed_at` only moves when a row is written — it is not a definition stamp.
  - Consequence: after a formula change, no row can answer "which definition produced this value?", historical rows are silently reinterpreted on the next recompute, and the review lane cannot prove a golden change was intentional.
  - Test strategy §10 already requires: "an intentional formula change requires updating the affected test cases **and** recording the change per §14".
- **Pinned approach (decision recorded):**
  - Add an explicit definition version, e.g. `CanonicalMetrics.Version` (a constant, bumped on any formula change) and a **per-row `formula_version`** on `derived_metrics` / `sector_metric_medians` (nullable for pre-existing rows), written on every insert/repair.
  - **Options recorded:** (a) per-row version column — durable, queryable, survives partial recomputes (**recommended**); (b) run-level stamp in `ingest_runs.stats_json` + a lookup — smaller, but cannot answer "which rows came from v1" after a partial recompute; (c) append-only formula-versioned facts — strongest, largest.
  - Recompute rule under (a): a stored row whose `formula_version` differs from the current version is **rewritten even when the value is identical**, and counted/reported distinctly (a `formulaVersion` reason), so provenance is truthful rather than "unchanged".
  - Migration is **additive and forward-only** (`02` §8); old rows stay nullable and are back-filled only by a recompute, never guessed.
- **Acceptance (explicit criteria):**
  - **(i)** A **test-only** version bump (no real formula change) makes a same-day recompute rewrite rows carrying the old version, stamp the new version, and report it distinctly; a second run under the same version is `unchanged` (no rewrite).
  - **(ii)** Historical rows keep their original version unless recomputed; no row is silently reinterpreted.
  - **(iii)** The migration applies cleanly on a fresh container and on the dev DB; it is additive and forward-only.
  - **(iv)** Goldens are unchanged for the test-only bump (the real formula is untouched); the test plan records the provenance case.
  - **(v)** `02` §3.1, `01` §10.9.13 and test plan §14 are updated; a §13.1 row is appended (**soft reservation v1.5.48**; re-pin at merge).
  - **(vi)** If an API/audit surface is chosen to expose the version, it is explicitly recorded; if DB-only, that is recorded honestly.
- Traces to: FR-MDF-013/014 (recompute mechanics + honest accounting), FR-MDF-015/NFR-MDF-002 (append-only, historical immutability), BR-MDF-009 (one definition per number), `01` §10.9.13.
- Architecture refs: `01-system-architecture.md` §10.9.13 (recompute doctrine), §13.1; `02-data-model.md` §3.1 (`derived_metrics` / `sector_metric_medians`), §8 (forward-only migrations); test plan `market-data-foundation.md` §14.
- UX refs: none.
- Dependencies: none. **SCOPE AMENDED 2026-10-24 (orchestrator, from the coder's blocked report; USER GRANT "dispatch whatever can run in parallel").** The original scope was structurally insufficient and the parenthetical banning `Jobs.cs` was wrong — corrected by evidence:
  1. **The engines never write rows.** `MetricsEngine.PlanWriteAsync` and `SectorMedianEngine.PlanWriteAsync` only classify and return results; every INSERT of `derived_metrics` / `sector_metric_medians` happens in `DerivedMetricStore.cs:64` and `SectorMedianStore.cs:52` (verified by grep: `new DerivedMetric` / `new SectorMetricMedian` appear nowhere else). A `FormulaVersion` on the engine result would never reach the database → a permanently-NULL column, i.e. exactly the 058 ambiguity this ticket exists to prevent. **Both store files are now in scope** (stamp on insert *and* on the repair path).
  2. **The trigger cannot reach the engine.** `IngestionRunContext` is built by the scheduler (`IngestionScheduler.cs:718`) and omitted by the admin path (`AdminOpsEndpoints.cs:384`), but is consumed only by the runner; `IIngestJob.RunAsync(IngestionRequest)` carries `Date` + `BackfillFrom` only. So **`src/Ingestion/Jobs/Jobs.cs` is now in scope** (option B1: extend `IngestionRequest`/context with the trigger, runner stamps it). The original ban referenced 044 as an active conflict — **044 is merged** (PR #132, 2026-10-24T01:09Z), so `Jobs.cs` is free.
  3. Chosen approach is **A + B1** (2 store files + `Jobs.cs`), *not* B2's four-file DI trigger service — it reuses the mechanism the runner already owns. No other widening is sanctioned.
  4. Test design (for whoever implements): no test-only hook is needed — set stored rows' `formula_version` to an older value directly in the DB (as `MetricsRecomputeIdempotencyTests` already does for facts), then assert the recompute rewrites, stamps the current version, and reports the distinct count; the migration/backfill case is the same DB-level exercise. `CanonicalMetrics.Version` can stay a plain `const`.
- Parallel group: none (touches `01`/`02`).

## Implementation record (070 lane, 2026-10-24)

Implemented on `feature/TKT-foundation-070` (PR #138) against the amended scope (A + B1), exactly as
pinned — **no further widening was needed or taken**.

- **Version**: `CanonicalMetrics.Version` (`const int` = 1, `/src/Core`) — the single source of truth;
  bumped in the same change as any formula / input-assembly / not-meaningful-rule edit.
- **Stamp**: `formula_version` (`int NULL`, CHECK `IS NULL OR >= 1`) on `derived_metrics` and
  `sector_metric_medians`, written by `DerivedMetricStore.UpsertAsync` and
  `SectorMedianStore.UpsertAsync` on the **insert and repair** paths (the only two `new DerivedMetric` /
  `new SectorMetricMedian` sites — the engines classify, they never insert).
- **Classification**: `MetricsEngine.PlanWriteAsync` / `SectorMedianEngine.PlanWriteAsync` treat
  `formula_version != CanonicalMetrics.Version` as rewrite-worthy even with identical values and count
  it as `FormulaVersionRewritten` (a subset of `Rewritten`); same values **and** same version stay
  `unchanged` (050's idempotency untouched).
- **Migration**: `20261024024109_MetricFormulaVersion` — additive, nullable, forward-only, **no data
  write** (an honest empty backfill: existing rows were produced by the then-current definition, but no
  per-row record of it exists, so a stamp would be invented; `NULL` = "produced before versioning
  existed"). The next recompute of a day stamps it; historical days keep `NULL` until recomputed.
- **Trigger**: `IngestionTrigger` + optional `IngestionRequest.Trigger` / `IngestionRunContext.Trigger`;
  the runner records `stats_json.trigger` (`scheduled` for the scheduler's context, `admin` for the bare
  admin-trigger overload, an explicit request value wins). Additive and optional — no job, test or 044
  reason-code behaviour changes (neighbour suites re-run green).
- **Exposure (vi)**: DB-only; no API/audit surface serves the version, `03` payloads unchanged —
  recorded in `01` §10.9.13.
- **Docs**: `02` header/§1/§3.1/§8 (v1.4), `01` §10.9.13 (new "Definition versioning" subsection) and
  §13.1 **v1.5.48**, test plan **§19** + coverage matrix (FR-MDF-013/014, NFR-MDF-001) + change record
  v1.22 *(renumbered twice by the 2026-10-24 rebases — see rebase records 1 and 2)*.
- **Tests**: TC-MDF-102, 5 cases in `tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs`
  (no test-only hook; the stale-stamp state is a direct `UPDATE`, per the test-design note).
- **Evidence**: red = 5 failed / 4 passed (`42703 column "formula_version" does not exist` on both
  tables; ledger `trigger` absent); green = lane 9/9, backend integration 528/528, core unit 54/54,
  content pipeline 2/2, `dotnet build` 0 warnings.
- **Flagged**: (1) the distinct `FormulaVersionRewritten` count is plan-level, not a `stats_json` key —
  the two metric **job** classes were outside the amended scope, so the ledger's `written` carries the
  total repairs as 044 established (recorded in `01` §10.9.13 as a follow-up); the demanded *trigger*
  distinction **is** in the ledger. (2) The ticket pointed this case at test plan "§14", which is the
  §14 share-count section (TKT-foundation-058); the case is authored as a new §17 instead — recorded in
  the plan's change record, test-planner confirmation pending. (3) The migration applies no backfill, by
  design (above). (4) Live criterion for the orchestrator post-merge: after deploy the first
  `metrics-recompute` stamps that day's rows, a second same-day run reports `unchanged`, and the ledger
  row reads `trigger: scheduled|admin` — recorded verbatim, never fabricated.
- The shared dev DB was **not** migrated from this lane; the migration is exercised by every
  Testcontainers fixture in the suite.

## Rebase record (2026-10-24, rebase only — no redesign, no re-scope, no re-review)

PR #138 went `CONFLICTING` because `dev` advanced from `60a01cf` to **`f12c1b5`** (TKT-foundation-064,
squash-merged as #133). Rebased `feature/TKT-foundation-070` onto `origin/dev@f12c1b5`.

- **Overlap was exactly the two predicted doc files** — `.pipeline/architecture/01-system-architecture.md`
  and `.pipeline/testing/market-data-foundation.md`. No source-file conflict, and **no conflict at all in
  `src/Ingestion/Scheduling/`** (064's per-step bound), which this ticket does not touch.
- **`01`**: 064 landed §13.1 **v1.5.43** and its §10.9.17 "The bound's scope, corrected" prose; both are
  **kept** and this ticket's row stays **v1.5.48** (free on the new `dev` — 43 is the landed maximum, so no
  renumber was required). 070's "Definition versioning" subsection in §10.9.13 (v1.5.48) is intact.
- **Test plan**: 064 landed TC-MDF-098 (§16) and its change record as **v1.17**; 070's §17 / TC-MDF-099
  entry carried the same **v1.17** label, so it is **renumbered to v1.18** and kept. Both cases, both
  sections and both change-record entries are present.
- **Migration**: re-verified on the new base — `dotnet ef migrations has-pending-model-changes` reports
  *"No changes have been made to the model since the last migration"* (**no snapshot drift**), the
  migration list still ends at `20261024024109_MetricFormulaVersion`, and the freshly-migrated
  Testcontainers fixture applies it cleanly. 064 added no migration.
- **Re-verification after the rebase** (`dotnet build Degerli.sln` **0 warnings / 0 errors**):
  `MetricsRecomputeIdempotencyTests` **9/9**, `SchedulerTests` + `EodStallWatchdogTests` **23/23** —
  both exactly as before the rebase, so the rebase disturbed nothing.

## Rebase record 2 (2026-10-24, rebase only — no redesign, no re-scope, no re-review)

PR #138 went `CONFLICTING` a second time because `dev` advanced from `f12c1b5` to **`aa2d0b8`**:
**#139 / TKT-foundation-078** (disclosures, which took `01` **v1.5.52** and test-plan **§17 / v1.18–v1.20**)
and **#140 / TKT-foundation-048** (holiday calendar, which took `01` **v1.5.44** and test-plan **§18 / v1.21**).
Rebased `feature/TKT-foundation-070` onto `origin/dev@aa2d0b8`; the rebased implementation commit is
**`0ca7009`** and this note commit sits on top.

- **Overlap was again exactly the two predicted doc files**, and again **no conflict at all in
  `src/Ingestion/Scheduling/`** (064's per-step bound, then 048's holiday gate) — this ticket does not touch it.
- **`01`**: 070's §13.1 row stays **v1.5.48**. `aa2d0b8` carries 43 (064), 44 (048) and 52 (078); **48 is
  still free**, so no renumber was required. 048's `v1.5.44` and 078's `v1.5.52` rows are kept verbatim,
  and 070's "Definition versioning" subsection in §10.9.13 is intact.
- **Test plan**: 078 claimed §17 **and** TC-MDF-099/100/101 **and** v1.18–v1.20; 048 then claimed §18 and
  v1.21. The earlier rebase's "§17 / v1.18" placement recorded above is therefore **superseded**: 070's
  block moves to **§19** and its change record to **v1.22** (the next free section and version),
  leaving **every** 078 and 048 section, case and change-record entry untouched.
- **Case-ID collision resolved (new finding, not in the original brief):** 078 landed **TC-MDF-099**
  (`StatementsAndEventsIngestionTests.cs`, the bootstrap-window case) while this ticket's case was also
  **TC-MDF-099** — a duplicate TC ID in both the test plan and the test source, which would have made
  TC coverage attribution ambiguous. 078 merged first, so it keeps the ID; 070's case is renumbered to
  **TC-MDF-102** (the next free ID) in §19, in its change record, in the §5 coverage-matrix rows
  (`FR-MDF-013`, `FR-MDF-014`, `NFR-MDF-001`), and in `MetricsRecomputeIdempotencyTests.cs` (six
  occurrences: method names and doc comments **only** — reversing the rename reproduces the file
  byte-for-byte, so no assertion was added, removed, skipped or weakened).
- **Migration**: re-verified on the new base — `dotnet ef migrations has-pending-model-changes` reports
  *"No changes have been made to the model since the last migration"* (**no snapshot drift**); the
  migration list still ends at `20261024024109_MetricFormulaVersion`; the freshly-migrated Testcontainers
  fixture applies it cleanly. Neither 078 nor 048 added a migration.
- **Re-verification after the rebase** (`dotnet build Degerli.sln` **0 warnings / 0 errors**):
  `MetricsRecomputeIdempotencyTests` **9/9**; `SchedulerTests` **11/11** + `EodStallWatchdogTests`
  **13/13** = **24/24** — one higher than the pre-rebase 23/23, which is exactly 048's new per-job
  holiday-gate case landing in `SchedulerTests` (whose amended `TC_MDF_011_Holiday_calendar_is_config`,
  green, now asserts the `holiday-skip` ledger row and "no job ran"). Nothing was dropped or weakened.

