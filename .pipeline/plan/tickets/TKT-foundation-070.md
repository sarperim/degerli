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

- Status: todo
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
