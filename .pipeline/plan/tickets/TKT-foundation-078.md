---
scope:
  - src/Ingestion/Disclosures/DisclosuresSourceAdapter.cs
  - src/Ingestion/Disclosures/DisclosuresJob.cs
  - src/Ingestion/Disclosures/DisclosuresModels.cs
  - src/Ingestion/Kap/VykDisclosureWalk.cs
  - src/Ingestion/Kap/VykCompanyDirectory.cs
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-078: disclosures lane never completes — the per-company walk starts at `EarlyDisclosureIndex=0` and re-reads ~140k index positions per company, so it is structurally guaranteed to time out

- Status: in-review
- Size: M
- **Priority: P0 — USER DIRECTIVE 2026-10-24 ("This takes most priority so plan and run it").** It is the gate on *recent KAP data* and on cash-flow statements (051), and therefore on sector performance, market P/E and the DCF calculator.

## Verified evidence (orchestrator, live probes 2026-10-24 02:30–02:50 UTC, from inside the api container)

**The gateway is fully reachable and already credentialed** — this is NOT an access problem:
- `GET /api/vyk/members` → **1,037 companies** with ids (ASELS = `866`).
- `GET /api/vyk/lastDisclosureIndex` → **`1231017`** (current platform head).
- `GET /api/vyk/disclosures?companyId=866&disclosureIndex=0` → **39 real rows**, index range **1092199 → 1142913**.
- `GET /api/vyk/disclosures?companyId=866&disclosureIndex=1142913` → **47 more rows**, 1142913 → 1188739 (paging confirmed).
- `GET /api/vyk/disclosures?disclosureIndex=1230970` (market-wide, no `companyId`) → **48 real rows**, newest **1231017** == `lastDisclosureIndex`, classes `ODA 24 / DG 21 / DUY 3`.
- `GET /api/vyk/disclosureDetail/1225002?fileType=data` → **7,892 bytes** of real content.
- Auth is Basic `ApiKey:ApiSecret` (`Ingestion__Kap__ApiKey` / `__ApiSecret` are already set in the container).

**Why `kap_disclosures` is still 0 rows — a throughput defect:**
- `DisclosuresSourceAdapter.FetchVykAsync` loops the bound index's constituents and, per company, calls `GetCursorAsync` → an empty cursor falls back to `EarlyDisclosureIndex` = **`"0"`** (`DisclosuresSourceAdapter.cs:51`). It therefore re-walks each company from the beginning of platform history.
- ASELS alone spans **1092199 → 1231017 ≈ 138,818 index positions ≈ 39+ pages** at ~39–47 rows/page.
- At the mandated ≥12 s pacing that is ≈**8 minutes per company**; ×30 XU030 constituents ≈ **4 hours** for one pass.
- Ledger proof: run **208** = `10:41:24 → 13:41:24` — **exactly 3 h 00 m**, `written=0`, killed by the watchdog. The lane has never once reached the end of a pass.
- `ER005 "Bildirim bulunamadı"` is a *red herring* in this diagnosis: it is returned when an index is **above** a company's latest disclosure (I reproduced it at `companyId=866&disclosureIndex=1231017`, and got clean rows at index `0` and mid-range indices). It is the end-of-list signal, not the cause.

## What to implement

**(a) Bootstrap the cursor from the platform head instead of `0`.** Seed a company's starting cursor from a bounded window below `lastDisclosureIndex` (already fetched by `VykCompanyDirectory`) rather than the absolute beginning of history. The window must be **configuration** (`Ingestion:Disclosures:BootstrapLookback` or equivalent, with a documented default — propose a sane value; ~5,000 index positions ≈ 100 pages ≈ minutes at 12 s) and must be an honest, recorded decision, not a magic number.
  - A company that has **no** stored cursor must not be treated as "has never disclosed anything" if that is not true — record the bootstrap as provenance on the run (`bootstrapped` count), never as "no disclosures exist".
  - An existing persisted cursor keeps being honoured (incremental behaviour unchanged).

**(b) Ingest the market-wide list for "recent KAP data".** The gateway pages the whole market from a single cursor (`?disclosureIndex=N`, no `companyId`). Use it so a normal run covers *recent disclosures across the market* rather than re-deriving per-company history. Keep per-company walking for backfill/first-seed only if it is still needed — and if you keep it, it must be bounded and recorded, not unbounded.
  - Class filtering (`DisclosureType`) and the existing honesty rules stay exactly as they are: an honest empty is a skip with a reason, never a fabricated disclosure; a genuine source-down is still `failed`.

**(c) The run must be able to finish.** Under the 12 s pacing and the configured budgets, one pass over the bound universe must complete well inside a step bound, and the job must report an honest terminal status with per-symbol/aggregate counts. If the very first bootstrap run cannot finish even with `(a)`+`(b)`, say so with numbers rather than shipping something that still times out.

**Pinned honesty rules (unchanged, restated because they are load-bearing):**
- Never fabricate a disclosure, a subject, or a body.
- Never silently drop a company: unmapped identity = named + counted skip; unfetchable detail = quarantine with the gateway body verbatim.
- `ER005` at the top of a company's range is an **honest end-of-list**, not a failure (this is the 063 precedent for statements) — and must not trigger an infinite or repeated reset.

## Tests (doubles only — **no live gateway calls**; the probes above are the evidence)
- A double whose company spans a huge index range must show the run **completes** within the configured budget and pages only the bootstrap window, not 39 pages from zero.
- Second run after a persisted cursor must be a single incremental page (no re-walk), and must not reset again.
- Market-wide mode: a paged cursor walk collects recent disclosures and advances the stored cursor.
- Honest-empty and source-down paths keep their current semantics (regression: 063's isolation family and the existing `StatementsAndEventsIngestionTests` stay green).
- Red→green: the "from zero" world must fail the budget assertion before the fix.

Docs: `01` §10.9.15 extension + §13.1 row (allocate above the landed max), test plan additions.

## Live criterion (orchestrator, post-merge)
Trigger `disclosures` on the merged dev stack: `kap_disclosures` > 0 rows, newest stored index within ~50 of `lastDisclosureIndex`, run completes inside its step bound, and a second run is incremental with no re-walk.

## Implementation record

- **PR:** https://github.com/sarperim/degerli/pull/139 — branch `feature/TKT-foundation-078`.
- **§13.1:** v1.5.52 (allocated above the landed max v1.5.42 and the reservations v1.5.43–51).
- **Tests:** TC-MDF-099 (a/b), TC-MDF-100 (a–d), TC-MDF-101 (a–g) in
  `tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs` — L2 doubles only, no live gateway.
  (101 f/g are the round-2 wall-clock deadline cases; the shipped-config guard lives in
  `TKT_foundation_034_config_wiring_ships_vyk_event_routes`.)
- **Red→green:** pre-078 from-zero start restored → **3 failed / 5 passed** (`TC_MDF_099_a`
  `Expected: 2, Actual: 0` — the from-zero walk exhausts its 24-page budget inside the dense archive
  before the recent window; `TC_MDF_099_b` window ignored; `TC_MDF_100_b` reset target `0`).
  Review round 1 at the pre-round head `fe890ae` → **7 failed / 4 passed** (the three above, plus
  the reset-clears-cursor case leaving the stale row, the anchor-down incremental case throwing,
  the 30-row-then-more short-page case writing 30 of 50 rows, and the market walk's row-count
  stop). **11/11 green** after; neighbouring suites green; `dotnet build` 0 warnings.
- **The pass is bounded in work *and* wall clock:** the work budget is **per walk** —
  `MaxListPages = 24` and `MaxDetailFetches = 200`. The pacing actually in force is
  `Ingestion:Statements:RequestDelayMs` = **10 s** (`Ingestion:Kap` carries no delay), so one
  `market` walk is nominally 24 × 10 s + 200 × 10 s ≈ **37.3 min**; the shipped `market` feed is
  one walk per run. The detail cap bounds the *number* of details, not their time: a stall is
  bounded per item at **180 s** (`Ingestion:Kap:DetailFetchTimeoutMs`), so a work-only bound could
  let a degraded gateway drive 200 × 180 s ≈ **10 h** (+ ≤ 12 min of list calls at the 30 s client
  timeout) out of one walk — past the **120-min per-step bound** (§10.9.17, v1.5.43; the 90-min
  whole-attempt bound no longer exists). The walk therefore also carries a **wall-clock deadline**:
  `Ingestion:Disclosures:MaxWalkDuration` = **90 min** (strictly below the 120-min `MaxStepDuration`,
  leaving ≈30 min for the job's store write). One clock is started per pass in `FetchVykAsync` and
  shared by the walk(s) — the market feed is one walk, the company feed shares it across the
  constituent loop — and the walker checks it before every list and detail request on the injected
  `TimeProvider`. On expiry the walk self-truncates: it records `truncated`, persists the last index
  it **completed** (and every already-completed company's cursor), and the next run resumes exactly
  there. A deadline stop is an honest bounded pass (`succeeded`/`partial` with counts), never
  `failed` and never a watchdog `RUN_HUNG` that loses the whole pass (the run-208 signature). The
  worst case is `min(work bound, MaxWalkDuration)`; the scheduler watchdog is now only the backstop
  for a single request that outlives its own per-item timeout. `MaxWalkDuration ≤ 0` disables the
  deadline (the pre-078 behaviour). In the shipped market feed the detail term cannot even bind
  (~36 bound rows over 24 × 50 rows), so the pass is bounded by its pages in practice.
  `Feed = company` builds one walk per constituent but shares the pass deadline, so one run is
  bounded as a whole (the pre-deadline nominal was ≈ 37.3 min × 30 ≈ **18.7 h**): it is now a
  recorded, resumable multi-run backfill, never one chain step.
- **Review round 2 (DR-1) — the walk enforces its own wall clock.** `MaxWalkDuration` (code default
  and shipped config: `01:30:00`), one deadline per pass, checked before every list/detail request
  via the injected `TimeProvider`. Truncation sets `truncated` and persists the last **completed**
  index (and every completed company's cursor); a deadline stop is `succeeded`/`partial` with
  counts. If the deadline expires before any row is handled, no cursor is advanced. Tests:
  `TC_MDF_101_f` (a fake clock that crosses a 10-min deadline truncates after four details,
  persists `1230020`, and the next run resumes there without re-fetching the boundary row) and
  `TC_MDF_101_g` (a non-positive bound disables the deadline; the same clock writes all six rows).
  The shipped-config guard asserts `DefaultMaxWalkDuration < IngestionSchedulerOptions.DefaultMaxStepDuration`
  and config ↔ code equality.
- **Live criterion** (orchestrator post-merge) is unchanged and remains open.

## Dependencies
None. 063 (statements isolation) and 069 (cursor/fileType family honesty) are merged/queued and independent.

## Parallel group
None — single coder.
