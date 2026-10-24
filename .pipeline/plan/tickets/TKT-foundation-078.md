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
- **Tests:** TC-MDF-099 (a/b), TC-MDF-100 (a/b), TC-MDF-101 (a/b/c/d) in
  `tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs` — L2 doubles only, no live gateway.
- **Red→green:** pre-078 from-zero start restored → **3 failed / 5 passed** (`TC_MDF_099_a`
  `Expected: 2, Actual: 0` — the from-zero walk exhausts its 24-page budget inside the dense archive
  before the recent window; `TC_MDF_099_b` window ignored; `TC_MDF_100_b` reset target `0`);
  **8/8 green** after. Neighbouring suites **113/113** green, `dotnet build` 0 warnings.
- **One pass fits a step bound:** 24 list pages × 12 s = 288 s + 200 details × 12 s = 2,400 s ≈
  **44.8 min**, inside the scheduler's 90-minute `MaxRunDuration` attempt bound (~2× headroom).
- **Live criterion** (orchestrator post-merge) is unchanged and remains open.

## Dependencies
None. 063 (statements isolation) and 069 (cursor/fileType family honesty) are merged/queued and independent.

## Parallel group
None — single coder.
