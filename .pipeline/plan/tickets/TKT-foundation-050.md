---
scope:
  - src/Ingestion/Metrics/MetricsRecomputeJob.cs
  - src/Ingestion/Metrics/SectorMediansJob.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Ingestion/Metrics/SectorMedianEngine.cs
  - tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs
  - tests/Api.IntegrationTests/MetricsEngineTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-050: F-1 — `metrics-recompute` and `medians` same-day silent 0/0 no-op: extend 042's input-aware recompute to both jobs

- Status: todo
- Size: S
- **Priority: P2 — dispatch-ready, no dependencies.** Honesty defect in run accounting (the 042 sibling): a same-day re-run with identical inputs and identical outputs reports `written: 0, unchanged: 0` — the run is invisible in its own stats, exactly the shape 042 fixed for `market-snapshot`.
- Verified evidence (orchestrator-found during Thursday 2026-10-22's manual repair, corroborated in 042's review): `metrics-recompute` and `medians` ledger runs 164/166 for 2026-10-21 both returned the silent same-day no-op. Root shape: neither job compares its recomputed rows against the stored same-day rows before skipping/deciding — the write path treats "same day, nothing new upstream" as an uncounted vacuum instead of an input-aware `unchanged`/`rewritten` decision.
- **Pinned pattern (copy 042, do not re-design):** on a same-day re-run — **identical inputs + identical computed outputs → `unchanged: N` counted per row; changed outputs → in-place overwrite + a `rewritten` stat; one row per (instrument, metric, as_of_date) preserved; historical days immutable.** The input-awareness lives in the engine/job boundary where 042 put it for `MarketSnapshotEngine` (`src/Ingestion/Metrics/MarketSnapshotEngine.cs` — reference implementation, read-only). Note: 044's acceptance already carries the ledger-stat surface for `rewritten` (the 2026-10-22 user fold-in) — this ticket produces the stat for its two jobs; 044's runner-level ledger work is disjoint and untouched here.
- Traces to: FR-MDF-010/011 (honest achieved depth / run accounting), FR-MOV-005 lane (derived metrics + sector medians supply); §10.9.x recompute doctrine (042's section — extended to the two jobs); TKT-foundation-042 heritage (merged, PR #112).
- Acceptance: **explicit criteria; L2 doubles only.** **(i) TC-MDF-088 — idempotent same-day recompute** (`MetricsRecomputeIdempotencyTests`, canonical fixture universe): first run writes; identical same-day re-run reports `unchanged: N` (red today: `0/0` silent); a same-day re-run after an upstream fact changes reports in-place overwrite + `rewritten` count; historical days immutable; one-row-per-key preserved for both `metrics-recompute` and `medians`. **(ii) `MetricsEngineTests` re-run green** (computation semantics untouched — accounting only). **(iii) CI green.** **Live criterion (orchestrator post-merge, honest):** the next manual/scheduled same-day re-run of both jobs on the dev stack shows counted `unchanged`/`rewritten` stats in the ledger row — never 0/0 silence. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` — 042's input-aware-recompute section (extended by reference to the two jobs), §13.1 **v1.5.30**; test plan: `.pipeline/testing/market-data-foundation.md` TC-MDF-088 (delivery-planner-applied, test-planner confirmation pending).
- UX refs: none (ingestion accounting).
- Dependencies: none — dispatch-ready. Coordination: `src/Ingestion/Metrics/` shared directory with 042's landed files but disjoint files (`MarketSnapshot*` untouched); `.pipeline/testing/market-data-foundation.md` rebase convention (047/048/051/053).
- Parallel group: none
