---
scope:
  - src/Ingestion/Metrics/
  - src/Ingestion/Coverage/
  - src/Ingestion/Statements/FinancialStatementStore.cs
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - tests/Api.IntegrationTests/KapPublicRecomputeTests.cs
  - tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs
  - tests/Api.IntegrationTests/ProfileMetricServingTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-083: Coverage and downstream recompute on current data — the mixed-source world's metrics, medians, market P/E and served financials, stamped by the formula version

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/148
- Size: M
- **Priority: P2 — lane ticket 5/6 (the acceptance lane for the user's goal: current fundamentals served).**
- **Posture — investigation-first, not a new ingest mechanism (the 043/044 convention):** the recompute/medians/snapshot jobs and the served surfaces already exist; this ticket verifies what the mixed-source world (VYK FY2023 `kap` + public-site FY2025/2026 `kap-public`) actually does to them, pins the observed gaps honestly, and repairs only what is broken. **Pinned non-goals:** no new metric formulas; no share-count consumption (see below); no UI work.
- **Pinned honesty rules for this ticket:**
  - **Market P/E / per-share metrics stay honestly null where share counts are unresolved** — the 065 Option-A posture stands (`currentCapital` is paid-in capital, never divided into a count; `BR-MOV-010` P/E-exclusion doctrine). If 081 recorded evidenced share-count lines in the report, that finding is routed to 065 and its outcome decides any later change — **never this ticket**.
  - 070's `formula_version` machinery stamps every written/rewritten row of the refresh (this ticket's **hard dependency on TKT-foundation-070 being merged** — the recompute provenance for the first current-data refresh must be answerable from day one).
  - Coverage honesty: per-source achieved depth (which periods, which source, how deep) is reported through the coverage-metadata/admin surfaces — a company with only 2023 VYK facts reads as such, never as "current".
- **Likely work (to be confirmed by the investigation half, in scope where the effect happens):** period-overlap reads through the coexistence rule on the metrics/serving paths (if the preference rule is enforced only in the statement store, the consumers' read paths are where it must actually hold — the 048/070/043 lesson); coverage-reporting seams in `src/Ingestion/Coverage/`; any recompute-repair the mixed world exposes in `src/Ingestion/Metrics/` / `FinancialStatementStore`. **Scope amendment precedent:** if the investigation shows the fix lands outside this list, amend scope per 034 Option A — never widen silently.
- **Scope amendments declared at implementation (034 Option A — declared, never widened silently; ratification pending with the user/planner):**
  1. `src/Ingestion/Statements/StatementSourcePreference.cs` **(new file; the scope list names only `FinancialStatementStore.cs` inside that directory)** — the pinned cross-source preference must have exactly one definition, and four consumers need it (`StockFundamentalsEndpoints.Pick`, `MetricsEngine`, `MarketSnapshotEngine`, `DcfBaselineGenerationJob`); duplicating the rule per consumer is the order-nondeterminism this ticket exists to remove.
  2. `src/Ingestion/Valuation/DcfBaselineGenerationJob.cs` — **the migration's direct exposure, not new work:** with two sources able to hold the same period at the same `version`, its local `OrderByDescending(version).First()` pick became order-dependent on a same-version tie. It now selects through the one preference. No DCF formula, threshold or output changes.
  3. `tests/Api.IntegrationTests/KapPublicRecomputeTests.cs` (new) — this fills the `KapPublicRecomputeTests.cs` entry the scope already lists.
- **Evidence note (honest, environment-bound):** the implementation environment has **no Docker/Testcontainers** (dangling WSL `docker` link, no `/var/run/docker.sock`, no local PostgreSQL), so `dotnet test` cannot start the container and no local red→green count exists — every failure is the `Docker.DotNet` ping, i.e. environment, not red. Red→green is therefore recorded from **CI** (ubuntu-latest has Docker) on the PR; the predicted pre-migration red is documented in the test plan's v1.31 entry (the source-less unique key makes the mixed-world seed unwritable — `23505` — and the store-level case fails as the silent `version 0` swallow, `Expected: 1, Actual: 0`). `dotnet build Degerli.sln -c Release` is 0 warnings / 0 errors locally.
- **Recorded residue (not fixed here, honestly stated):** `KapPublicStatementTargets.GetForeignPeriodsAsync`'s doc comment still describes the pre-migration "at most one provenance answers per key" shape (behaviour is unaffected — the overlap question is asked of *foreign* sources and the first match is named); and `GET /admin/coverage` renders only `CoverageDataTypes.InstrumentScoped`, so the new per-source depth is visible in `coverage_metadata` but not yet on that admin surface (an admin-API change, outside this scope).
- **Coordination note (user decision B, 2026-10-26):** the overlap read-preference clause split out of TC-MDF-110 (082's recorded blocker, remedy B — test plan v1.28) lands **here**, as TC-MDF-111's overlap-read sub-clause (test plan §24): this ticket lands `source_ref` in the statement store's natural key (one forward-only migration — `FinancialStatementStore`, the persistence entities/`DbContext`, `src/Persistence/Migrations/`), the pinned read preference in `StockFundamentalsEndpoints.Pick` (`src/Api/Stocks/`), and the mixed-world read verification (both sources present for a period → the public-site current fact is served; public absent → VYK served; an as-of query reaches both). 082 ships the interim honest posture — the named, counted `SOURCE_OVERLAP_RETAINED` skip — which this ticket's migration supersedes for overlapped periods (two rows coexist as distinct facts; nothing already written is rewritten); per the test plan's v1.28 change record, this lane re-pins TC-MDF-110's overlap-write behaviour against the migrated store.
- Traces to: FR-MDF-013/014 (recompute mechanics + honest accounting), FR-MDF-015/NFR-MDF-002 (append-only, historical immutability), FR-MDF-011 (coverage), FR-MOV-006/BR-MOV-010 (market P/E honest-null), FR-RES-005..018 (served financials supply), FR-SCR metric supply.
- Acceptance: **TC-MDF-111** (test plan §24) — L2 doubles only over the production jobs on the seeded mixed world; red→green recorded (red = the observed pre-ticket behaviour, honestly documented even if it is "mostly works, gaps X/Y"). Non-regression: `MetricsRecomputeIdempotencyTests`, `MarketSnapshotTests`, `MetricsEngineTests`, `ProfileMetricServingTests` re-run green (pre-listed additive deltas only). **Live criteria (orchestrator, post-merge):** on the dev stack after a real public-site statements pass — `metrics-recompute`/`medians`/`market-snapshot` complete with current-period metrics for covered companies, every written row carrying the current `formula_version`, per-source coverage visible, market P/E honestly null (or real only if 065 has landed a count decision — recorded either way); the stock pages show current-period financials. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.13 (recompute doctrine) + §10.9.19 (coexistence consumer note — landed by this ticket) + §13.1 **v1.5.57** (soft reservation; re-pin at merge); test plan §24 (TC-MDF-111).
- UX refs: none (job/serving lane; the presented surfaces already exist).
- Dependencies: TKT-foundation-082; **TKT-foundation-070 merged** (formula_version — hard for the acceptance, not for starting the investigation half).
- Parallel group: none.
