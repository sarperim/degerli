---
scope:
  - src/Ingestion/Funds/
  - src/Ingestion/Jobs/Jobs.cs
  - tests/Api.IntegrationTests/FundIngestionTests.cs
  - tests/Api.IntegrationTests/FundSchedulingCoverageTests.cs
  - .pipeline/testing/fund-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-044: fund jobs failing instantly all evening with `symbols: []` and an EMPTY error field — root-cause the source fault and make failed ledger rows carry their reason

- Status: done — merged 2026-10-24 (PR #132, in `dev`; flipped by the planner's 2026-10-25 board regeneration from the merge history — live criterion per the ticket's own record)
- PR: https://github.com/sarperim/degerli/pull/132
- Size: S–M
- **Priority: P2 — dispatch-ready, investigation-first** (separate lane from the market chain, per the routing).
- Verified evidence (dev stack ledger, 2026-10-20): `fund-nav` / `fund-performance` / `fund-holdings` failed repeatedly from ~16:15 UTC through at least 17:25 UTC (attempts 2-5 of maxAttempts 7), each failure **instant (~4-30 ms)**, `stats_json: {"mode": "incremental", "symbols": [], "written": 0, ...}` — the jobs resolved **zero fund symbols** before failing — and, compounding the diagnosis problem, the `error` column on every failed row is **EMPTY**: the ledger records the failure but not its reason. This is a source-level or universe-resolution fault, not pacing (4 ms is no network call) and not the scheduler (the streams fired and retried correctly).
- Planner's reading (hypotheses for the investigation, not claims): (a) the fund-universe resolution returned empty (`symbols: []` — TEFAS fund list empty/unreachable → the job fails fast on an empty bound, the `empty bound → failure` posture 034 pinned for its lanes); (b) a TEFAS source-level fault (endpoint changed/down for the list call); (c) a config/env regression in the dev stack. The **empty `error` field is itself a defect** regardless of root cause: a failed run with no recorded reason is a mild silent hole in a ledger whose contract is honesty (FR-MDF-009 — the runner "records failed run before propagating"; the message is evidently being lost on this path).
- Scope: **(a)** root-cause the instant failures (investigation-first; doubles-only reproduction — a double that mimics the failing list call must reproduce `symbols: []` + fast failure); **(b)** fix per root cause (source adapter, universe resolution, or config — scope amendment per the 034 Option-A precedent if the fix lands outside `src/Ingestion/Funds/`); **(c)** **failed-run reason honesty:** the run ledger's `error` field carries the failure reason on every failed path (runner-level fix in `Jobs.cs` and/or job-level — whichever the root cause indicates; an operator must never need the console to know why a run failed); **(d)** docs: `01` §13.1 **v1.5.25**; test plan `fund-data-foundation.md` gains the failure-reason criteria (delivery-planner-applied, test-planner confirmation pending).
- Traces to: FR-FDF-001/002 (fund ingest), FR-MDF-009 (run-ledger completeness — the error field is part of the honest record), FR-MDF-012 (failure isolation), NFR-MDF-001; foundation pseudo-domain convention (post-merge defect — explicit criteria; the 030 precedent).
- Acceptance: **explicit criteria; L2 doubles only.** **(i) Reproduction:** a double reproducing the fault (empty/unreachable fund list) yields the observed signature — and the fix changes the outcome per the root cause (either an honest, REASON-CARRYING failure — some faults are the source's, not ours — or a recovered successful run); **(ii) red→green reason honesty:** every failed `ingest_runs` row written by the fund lane (and, where the fix is runner-level, generally) carries a non-empty `error` (red today: empty on the observed rows); **(iii) golden stability:** the existing `FundIngestionTests` / `FundSchedulingCoverageTests` / `FundHoldingsGapTests` re-run green (pre-listed deltas only); **(iv) CI green.** **(v) Fold-in from TKT-foundation-042 (USER DECISION 2026-10-22):** the runner-level stats seam (`IngestResult` / `RecordAsync` in `Jobs.cs`) gains the distinct `rewritten` count for same-day snapshot repairs — a repaired same-day `market-snapshot` run reports `rewritten: 1` in `ingest_runs.stats_json` (red today: repairs count only as `written: 1` with the store-level `Rewritten` counter that 042 shipped). **Live criterion (orchestrator post-merge, honest, never fabricated):** the next 19:00-19:10 Istanbul fund-job window on the dev stack — a succeeded run with real TEFAS rows, or a failed run whose ledger row names the real reason (e.g. the source still down) — recorded verbatim on the ticket; if the fault was environment-specific and does not reproduce, that is recorded as the result with the reason-honesty fix still verified via (ii).
- Architecture refs: `01-system-architecture.md` §13.1 v1.5.25 (row lands with the fix); `02-data-model.md` unchanged (`ingest_runs.error` column already exists — the defect is that it is left empty); test plan: `.pipeline/testing/fund-data-foundation.md` (failure-reason criteria).
- UX refs: none.
- Dependencies: none hard (Funds lane + `Jobs.cs`). **Serialize notes:** `src/Ingestion/Jobs/Jobs.cs` is ALSO a plausible 041 file (chain-step ledger context) — if 041's root cause lands there, this ticket's runner-level edit serializes behind it (rebase; the edits are small and different); `01` §13.1 v1.5.25 behind v1.5.15-19 — single-row append, last-lander rebases.
- Parallel group: none.

---
## Implementation record (coder, 2026-10-23)

### Root-cause finding — the source was never wired, and the reason never reached the ledger

Diagnosed **with doubles only** (no live TEFAS call was made); the dev-stack evidence was read
read-only from the running container and its ledger.

**Hypothesis verdicts (planner's list):**

- **(a) fund-universe resolution returned empty → REFUTED.** `symbols: []` is **structural** for
  the fund lane: `FundJobBase.RunAsync` never populates `IngestResult.SymbolStats` (the fund jobs
  have no per-symbol loop — one fetch carries the whole universe), so `stats_json.symbols` is `[]`
  on *every* fund run, successful or failed. The job never reaches universe resolution at all in
  the failing world (no payload is ever fetched), and there is no `empty bound → failure` check
  in this lane (034's posture applies to the prices/statements lanes). The empty array is a red
  herring, not the fault.
- **(b) a TEFAS source-level fault (endpoint changed/down) → REFUTED.** No endpoint was ever
  configured, so there is nothing that could have gone down.
- **(c) a config/env fault in the dev stack → CONFIRMED**, more precisely **never wired, not
  regressed.** `Ingestion:Funds` is configured **nowhere** in the repo or the stack:
  `src/Api/appsettings.json` has no `Funds` section at all; `compose.dev.yml`/`.env.example`
  pass through no `Ingestion__Funds__*` variable; the running dev container has no such env var,
  `grep -c Funds appsettings.json` → `0`, and `curl http://localhost/funds/universe` **inside the
  api container** → `Failed to connect to localhost port 80 after 0 ms`.

**Mechanism, end to end:** `TefasSourceOptions.BaseUrl` is empty → the keyed TEFAS
`ISourceClient` falls back to the placeholder `http://localhost` → the universe `GET
/funds/universe` is refused in ~0.4 ms (the observed instant failure; **not** pacing, not the
scheduler) → the `HttpRequestException` escapes `TefasSourceAdapter` → the fund job's
`catch (SourcePayloadException)` does **not** catch it → it propagates to
`IngestionJobRunner.RunCoreAsync`'s catch-all → the runner wrote a `failed` row with `Error`
left `NULL` (the default). **That is the complete dev signature: instant failure, `symbols: []`,
empty error.** Reproduced in `TC-FDF-014 (a)` (a loopback refusal) and `(c)` (no base address).

**Honest status: BLOCKED UPSTREAM, ships as honest-failing — not "fixed".** The fund lane cannot
succeed until a *verified* TEFAS programmatic endpoint exists and is configured. **OQ-FDF-002
("Does TEFAS offer a practical programmatic interface?") is still unverified** (analysis
`fund-data-foundation.md` §OQ-FDF-002; it was always a builder V0 validation item), and wiring a
fabricated `/funds/universe` endpoint plus a `src/Api/appsettings.json` section is **outside this
ticket's audited scope** (`src/Ingestion/Funds/`, `Jobs.cs`, the two fund test files, the fund
test plan, `01`). No endpoint was invented, no fund list was fabricated, and no retry storm was
added. What the ticket *does* deliver for the lane is that its ledger rows now say exactly this:
`SOURCE_NOT_CONFIGURED: … 'Ingestion:Funds:BaseUrl' …`.

### Implementation

1. **`src/Ingestion/Jobs/Jobs.cs` — the reason survives every failed run (the durable half).**
   - `IngestResult` gains `FailureReason` (reason code + bounded detail) and `Rewritten` (the
     042 fold-in, below).
   - New `IngestFailureReason` vocabulary + classifier: `SOURCE_FETCH_FAILED`,
     `SOURCE_NOT_CONFIGURED`, `UNPARSEABLE_PAYLOAD`, `FAILED_UNSPECIFIED`. `Describe(exception)`
     prefers an exception that names itself (`IReasonCodedFailure`), then maps
     `SourcePayloadException` → `UNPARSEABLE_PAYLOAD`,
     `SourceFetchException`/`HttpRequestException` → `SOURCE_FETCH_FAILED`, else
     `FAILED_UNSPECIFIED`. Detail is whitespace-collapsed, single-lined and bounded
     (`MaxLength` = 300, `…`-truncated). Redaction is unchanged: it stays with the adapters,
     which redact their own credentials *before* throwing (never echoing a raw payload).
   - `RecordAsync` now always writes `ingest_runs.error` for a `failed` row: the thrown reason,
     else the job-reported `FailureReason`, else a defensive `FAILED_UNSPECIFIED` **and a
     warning** — a failed row can no longer be written with an empty reason. Non-failed rows
     keep `error` NULL.
   - **042 fold-in (acceptance v):** `stats_json` gains `rewritten` (`IngestResult.Rewritten`),
     distinct from `written`, so a repaired same-day `market-snapshot` row reports `rewritten: 1`.
2. **`src/Ingestion/Funds/TefasSourceAdapter.cs` — an unconfigured source names itself.** When
   `Ingestion:Funds:BaseUrl` is unset the adapter throws `TefasSourceNotConfiguredException`
   (`SOURCE_NOT_CONFIGURED`, naming the config key) **before any request reaches the wire**,
   replacing the anonymous connection refusal.
3. **`src/Ingestion/Funds/FundJobs.cs` — the job-reported failure paths carry their reason too:**
   the unparseable-payload path (`UNPARSEABLE_PAYLOAD`, quarantine unchanged) and the
   missing-provenance path (`MISSING_PROVENANCE`, quarantine unchanged) now set `FailureReason`.
4. **Docs:** `01` §13.1 **v1.5.25** (root cause, mechanism, the honest-limit record, tests);
   `.pipeline/testing/fund-data-foundation.md` **v1.1** §8 with **TC-FDF-014** and **TC-FDF-015**
   (delivery-planner-applied, test-planner confirmation pending — the 030/v1.4 precedent).

### Tests (doubles only; no live source touched) and evidence

- `tests/Api.IntegrationTests/FundIngestionTests.cs` — **TC-FDF-014 (a)–(e)**: the loopback-refusal
  world reproduces the dev signature exactly (fast failure, `symbols: []`, `written: 0`) and the
  row carries `SOURCE_FETCH_FAILED … 127.0.0.1:1`; the HTTP-500 world carries
  `SOURCE_FETCH_FAILED … HTTP 500 … /canned-sources/tefas-universe`; the **unconfigured** world
  carries `SOURCE_NOT_CONFIGURED … 'Ingestion:Funds:BaseUrl'` with **zero** requests on the wire;
  the unparseable and missing-provenance worlds carry their codes with the quarantine unchanged;
  the thrown reason's detail is bounded, single-lined and `…`-truncated.
- **TC-FDF-015** in the same file: a job carrying `Rewritten: 1` reports `rewritten: 1` beside
  `written: 0` / `unchanged: 2`, and a `succeeded` row carries no `error`.
- `tests/Api.IntegrationTests/FundSchedulingCoverageTests.cs` — **TC-FDF-014 (f)** on the
  scheduler path: after the full 5/15/60 ladder, all four `fund-nav` rows carry
  `SOURCE_FETCH_FAILED … HTTP 500`.
- **Red → green:** with `src/` reverted to the ticket base and the new tests in place, **5 failed /
  0 passed** — every failure is `Error = null` on the failed rows (the exact dev defect) —
  **7/7 green** after. The `(e)` bounded-detail and `(f)` scheduler cases are included in the
  red run.
- **Golden stability:** `FundIngestionTests` + `FundSchedulingCoverageTests` + `FundHoldingsGapTests`
  **20/20** green with no assertion weakened and no pre-listed delta. Full backend integration
  suite: **520 passed / 3 failed**, the three being `StatementVersioningTests` failing to reach
  their Testcontainers port (`Connection refused` — a container-reuse flake); run in isolation the
  class is **3/3 green**. `dotnet build Degerli.sln -c Release` → **0 warnings, 0 errors**.

### Flagged items (not fixed here — out of scope)

1. **OQ-FDF-002 is unresolved and is the real blocker for the fund lane succeeding.** Wiring a
   verified TEFAS endpoint + an `Ingestion:Funds` section (`src/Api/appsettings.json`,
   `compose.dev.yml`) is outside this ticket's `scope:`. Until then the lane's honest ledger
   state is `SOURCE_NOT_CONFIGURED`, and its jobs fail fast with that reason — by design, not by
   silence. **Recommendation: a follow-up ticket for OQ-FDF-002 live discovery + config wiring**
   (the TKT-foundation-021 precedent, which did exactly this for the İş Yatırım universe).
2. The `symbols: []` reading in the original evidence was a false signal (the fund lane never
   fills per-symbol stats). Not a defect, but it misled the diagnosis — worth knowing before the
   next fund investigation.
3. **Live criterion (orchestrator post-merge, honest, verbatim on this ticket):** the next
   19:00–19:10 Istanbul fund-job window should write rows that **name their reason**
   (`SOURCE_NOT_CONFIGURED` while the source stays unwired) — a row with an empty `error` is the
   criterion failing.
