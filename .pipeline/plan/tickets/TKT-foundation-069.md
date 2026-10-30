---
scope:
  - src/Ingestion/Dividends/DividendsSourceAdapter.cs
  - src/Ingestion/Dividends/DividendsJob.cs
  - src/Ingestion/CorporateActions/CorporateActionsSourceAdapter.cs
  - src/Ingestion/CorporateActions/CorporateActionsJob.cs
  - src/Ingestion/Disclosures/DisclosuresSourceAdapter.cs
  - src/Ingestion/Disclosures/DisclosuresJob.cs
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-069: VYK cursor/fact transaction window — a store failure must not advance the cursor past unwritten facts

- Status: done — **PR #167 squash-merged 2026-10-30 (`7bcb9f3` on dev), round-1 MERGEABLE** (`.pipeline/plan/reviews/TKT-foundation-069-round-1.md` — zero blockers, no fixer round; the cursor/fact window verified closed for all three families, store failure injected with a real PostgreSQL trigger). **Non-blocking findings recorded (DR-1..DR-5):** §10.9.15 stale two-passage walk-persistence wording; §10.9.10 rationale no longer load-bearing; `DividendDetailBodyAt` fixture chain self-defeating; disclosures market-wide/reset-clear paths untested; `is ConcreteAdapter` seam reach. **Routing item:** `Jobs.cs` `RecordAsync` ledger gap (failed runs of the store-failure class write no `ingest_runs` row) — pre-existing, in 044's scope, candidate 044 follow-up. 084's serialization gate is now released.
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 4 unparked).**
- **Evidence (gathered 2026-10-24):**
  - `01` §10.9.15 (lines ~1328-1332) states the residual verbatim: "**Cursors are persisted at the end of the adapter loop, before the job's store write.** A failure inside the loop therefore never advances a cursor, but a store failure *after* the adapter returns can skip the indexes the cursor already covers (accepted residual, recorded on the ticket): the statements precedent persists per-company *after* its write, which the adapter cannot see. The next run's stale-cursor reset (below) is the recovery path, not a claim that this window is closed."
  - TKT-foundation-063 **closed the window for statements only**: `StatementsJob` persists per-company after the write (`StatementsJob.cs:186-212`, `_cursorStore.SetAsync`/`ClearAsync` after the store call). The three **event families** still own their company loop in the adapter and persist the cursor at loop end, before the job writes facts (`DividendsSourceAdapter`, `CorporateActionsSourceAdapter`, `DisclosuresSourceAdapter`).
  - **Interaction with 063's reset path:** the reset is triggered **only** by HTTP 400 `ER005` on the first request from a persisted cursor. A cursor advanced past facts that were never stored produces **no ER005** — the next run simply starts after them. The recovery path therefore does not cover this class, and the loss is a silent gap, which FR-MDF-010/011 forbid.
  - The failure mode is concrete: a DB error, a quarantine-store failure, or a process stop between the adapter's cursor write and the job's fact write loses those disclosures for that instrument until a later ER005 reset happens to fire.
- **Pinned approach (decision recorded, not invented):**
  - **(a) Return-and-persist-after-write** — the adapter returns `facts + nextCursor`; the job writes facts first, then persists the cursor (exactly the 063/statements shape). Minimum honest fix, proven in the statements lane.
  - **(b) One transaction** spanning fact rows + the cursor row — no window at all, but couples the adapter/job boundary to the `coverage_metadata` write and needs a unit-of-work refactor.
  - **(c) Two-phase cursor** — write a "pending" marker before the store, promote it after; recovery re-walks pending indexes. Stronger than (a), more moving parts.
  - **Recommendation recorded:** (a) as the default; (b)/(c) only if the user wants the stronger guarantee. Whichever is chosen, the ER005 reset remains the **ER005** path only, and the new recovery is "re-walk indexes not durably stored", relying on the existing idempotent stores to report `unchanged`.
- **Acceptance (explicit criteria):**
  - **(i)** Red→green: a store failure injected **after** the walk emits facts leaves the cursor **not advanced past the unstored facts**; the next run re-walks and writes them; no gap; the run is honestly `failed`/`partial` per the existing posture.
  - **(ii)** The 063 ER005 reset path still passes unchanged (a stale cursor past the tail resets once and persists/clears as today).
  - **(iii)** The guarantee is applied to **all three** families (dividends / corporate-actions / disclosures), one case each or a shared theory.
  - **(iv)** The replay does not duplicate rows (idempotent store reports `unchanged` for already-written facts).
  - **(v)** `01` §10.9.15's residual text is corrected to state the window is closed (or precisely bounded by the chosen option), and a §13.1 row is appended (**soft reservation v1.5.47**; re-pin at merge); test plan `market-data-foundation.md` gains the case.
- Traces to: FR-MDF-003/004/005 (the three family lanes), FR-MDF-010/011 (honest achieved depth — no silent gaps), FR-MDF-016 (pipeline health); §10.9.11 (cursors), §10.9.15 (the residual this closes).
- Architecture refs: `01-system-architecture.md` §10.9.11 (cursor storage), §10.9.15 (accepted residual, corrected), §13.1; test plan `market-data-foundation.md` §15.
- UX refs: none.
- Dependencies: none (063 landed; this extends its statements shape to the families).
- Parallel group: none (touches `01` + shared ingestion test file).

## Notes

- Do **not** touch `src/Ingestion/Jobs/Jobs.cs` — it is TKT-foundation-044's active scope. The run-stat fold-in, if needed, goes through the existing adapter/job channels, not `Jobs.cs`.
- `StatementsJob`/`KapDisclosureCursorStore` are already correct (063); this ticket is the three families only.

## Implementation record (2026-10-30, branch `TKT-foundation-069`, PR #167)

- **Option chosen: (a), return-and-persist-after-write** — the ticket's recorded default. Each adapter's walk now only collects its cursor work (`HasPendingCursors` / `PersistCursorsAsync`: per-instrument advances for all three families, the `disclosures` market-wide row, and the clears of a reset whose re-walk was honestly empty); each job persists it **after** its fact write **and** its skip/unknown-symbol evidence, the 063/statements order. No `Jobs.cs` edit (044's scope), no schema, migration, cursor data type, config key, job code or request path.
- **Acceptance evidence:** TC-MDF-130 (test plan §15, v1.57) — 4 cases: the failure injected by a PostgreSQL trigger on the fact table's `INSERT` (a genuine store failure after the walk emitted its facts), a theory over all three families ((i), (iii)) plus the incremental half ((i) with a persisted cursor), and a cursor-rewind replay proving the idempotent `unchanged` reporting ((iv)). The 063 ER005 reset re-runs green unchanged ((ii)). Red measured, not remembered: **4 failed / 0 passed** with only the six product files reverted (cursor row present — `Expected: 0, Actual: 1` — in all three families; incremental cursor `Expected: "1138986", Actual: "1138987"`), **4/4 green** with the fix. Non-regression: `StatementsAndEventsIngestionTests` 64/64, `StockFundamentalsEndpointsTests` 18/18, `EodChainTests | MorningChainTests | VykCompanyDirectoryTests` 13/13; build 0 warnings / 0 errors.
- **Docs:** `01` §10.9.15's residual paragraph replaced (window **closed**, ER005 reset restated as the ER005 path only), §10.9.10's fail-fast rationale re-worded to the new shape (it reasoned from the closed one), §13.1 **v1.5.84** appended.
- **Flagged item (recorded, not fixed — out of scope):** a failed run of this class surfaces as a thrown exception but writes **no `ingest_runs` row**, because `IngestionJobRunner`'s own record write re-attempts the inserts EF still tracks from the failed save and fails identically. That is `Jobs.cs` (TKT-foundation-044's active scope); TC-MDF-130 records the observation in prose and pins nothing about the ledger, so a later fix there cannot turn the case red. Routed to the orchestrator as a candidate follow-up.
