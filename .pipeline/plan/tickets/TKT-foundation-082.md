---
scope:
  - src/Ingestion/Statements/StatementsSourceAdapter.cs
  - src/Ingestion/Statements/StatementsJob.cs
  - src/Ingestion/Statements/StatementsModels.cs
  - src/Ingestion/KapPublic/
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-082: Statements lane public-site mode — config-selected source, the bounded byCriteria walk, own cursors, per-company isolation, and the two-source coexistence rule

- Status: in-review — PR https://github.com/sarperim/degerli/pull/147 (branch `feature/TKT-foundation-082`). **Implemented 2026-10-26** against TC-MDF-109 (i)-(v) and TC-MDF-110 **as amended** (decision B): `Ingestion:Statements:Source` selects `vyk` (shipped default) or `kap-public` with an unrecognised value failing by name (`INVALID_SOURCE_SELECTION`, zero requests); a date-windowed byCriteria walk over 080's stored oid with the site's own `disclosureCategory: FR` selection, newest-first; one `KapPublicWalkBudget` per **pass** shared across companies; own cursor (`kap-public-statements-cursor`) and achieved-depth provenance (`kap-public-statements-depth`); per-company and per-report isolation with the honest statuses; and the coexistence write rule — a period the other source covers is not written, the incumbent row is retained as-is and as-of queryable, and the overlap is a named, counted `SOURCE_OVERLAP_RETAINED` finding (counted example recorded in the PR: 3 withheld + 3 comparatives written). Tests: `tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs`, 16 cases (12 at implementation; +3 in review round 1 — per-pass ceiling across two companies, mid-walk list outage, cross-source period-type disagreement — plus the wall-clock case rewritten as a walk-level deadline case; +1 in review round 2 — the pass-order case that proves the fair ordering), red-1 `CS0117` (seam absent) / red-2 10 failed-2 passed (lane inert) → **12/12 green** at implementation; the round-1 additions red on `CS0117` at `83cb557` and, with only their remedy reverted, on their own assertions (mid-walk `Expected: 6, Actual: 0`; period-type `Expected: 5, Actual: 6`) → **15/15**; the round-2 pass-order case red against the bare constituent order (`Expected: 1, Actual: 0`) → **16/16 green**; targeted non-regression at implementation 177/177 + 15/15; build 0 warnings. Docs: test plan **v1.29**, `01` §13.1 **v1.5.56**. **Zero live kap.org.tr requests.** Residual: the live criteria below are the orchestrator's post-merge step. Prior re-dispatch record (for reference): **RE-DISPATCHED 2026-10-26 ~02:15 UTC after user decision B unblocked the ticket (amendment landed on dev at `506fca6`, test plan v1.28).** Implement TC-MDF-110 **as amended**: the overlap-read clause is split out — for a period both sources cover, the public-site fact is **not written** and the overlap surfaces as a named, counted `SOURCE_OVERLAP_RETAINED` finding (VYK row retained as-is and as-of queryable; nothing overwritten, nothing swallowed, no false restatement flag). The deferred read preference is TC-MDF-111's overlap-read sub-clause in 083. Everything else from the original work order stands. Prior block record below for reference. Block evidence and the three rejected workarounds: `## Blocker` section on this branch (commits `42d256e`/`e7f5a14`). Store evidence (dev@1b8925a): `FinancialStatementStore.cs:283,157-163` keys facts by `(instrument, periodType, periodEndDate, statementType)` — source NOT in the key; DB `UNIQUE(…,version)` (`DegerliDbContext.cs:197`, InitialSchema:905-908); `version` is frozen as the restatement sequence (`02` §91-93); served payload has no source preference (`StockFundamentalsEndpoints.cs:583-587`). A public FY2025 over an existing VYK FY2025 row can only be: swallowed as `unchanged` (public fact absent), cross-source in-place overwrite (forbidden), or a false `restated` declaration corrupting `is_restated` — none honest, none in scope. **Remedies (user decision pending):** A — widen 082 (add FinancialStatementStore, Persistence entities/DbContext/one forward-only migration putting `source_ref` in the natural key, StockFundamentalsEndpoints `Pick` preference; pin mechanism in TC-MDF-110); B — split the clause: 082 lands coexistence + provenance + no cross-source overwrite + named/counted `SOURCE_OVERLAP_RETAINED` skip, overlap read-preference moves to 083 (store + mixed-world read verification + `src/Api/Stocks/` scope) with a TC-MDF-110 amendment / TC-MDF-111 sub-case. Full evidence + remedies in the branch's `## Blocker` section (`42d256e`/`e7f5a14`). **TC-MDF-109 (the walk) is NOT blocked — fully implementable in scope.** Fixture constraint recorded: only committed report body is the 6-month interim `1643141`; annual `1561039` has no body (live criterion) — annual column sets must never be faked. Zero live requests.
- Size: M
- **Priority: P1 — lane ticket 4/6 (the statements lane actually ingesting current data).**
- **What to implement:**
  1. **Source selection by configuration:** `Ingestion:Statements:Source` = `vyk` (the incumbent, stays the shipped default — flipping the dev stack to `kap-public` is the orchestrator's recorded post-merge live step, never a silent default change) | `kap-public`. The VYK adapter stays wired and green — switching back must be a config change, not a rewrite.
  2. **The walk (the 078 `WalkBudget` pattern re-applied):** per bound company (via 080's stored mapping), a byCriteria `disclosureClass="FR"` walk over date windows (`fromDate`/`toDate` — the public list is **date-windowed, not index-paged**; the cursor is a date watermark plus the seen public `disclosureIndex` set within the source's own space); per-company isolation (one unfetchable detail = quarantine with the verbatim body + counted skip, run `partial`; an all-isolated zero-write run is `partial`, never `failed`; a genuine source-down is `failed` — the 063 doctrine); bounded in work and wall clock (page caps + walk deadline < `MaxStepDuration`; truncation persists the last completed unit and resumes exactly there); backfill lookback recorded as achieved-depth provenance (`bootstrapped`-style), never as "no statements exist" for what was not walked.
  3. **Own cursors, never mixed:** the public-site lane's cursor rows are **own** coverage-metadata entries (distinct data-type names from the VYK `kap-disclosure-cursor` rows); a public `disclosureIndex` never appears in a VYK cursor row nor a VYK index in a public-site row — asserted by test (the 1643141 vs 1231017 spaces).
  4. **Decode wiring:** the walk feeds 081's decoder; written statements carry `SourceRef = kap-public` with `SourceUnitScale`/`SourceCurrency` from the header decode; versioning/idempotency per the existing statement-store rules.
  5. **The two-source coexistence rule (landed in `01` §10.9.19 by this ticket):** both sources' facts coexist — no deletion, no cross-source in-place overwrites; for a period both sources cover, the **pinned preference rule** decides the read (public-site current wins for the periods it covers; VYK rows retained and as-of queryable); every row answers its evidence class (`kap` vs `kap-public`); a recompute over the mixed world reports per-source counts honestly.
- Traces to: FR-MDF-002 (statements ingest), FR-MDF-008 (provenance), FR-MDF-010 (backfill/achieved depth), FR-MDF-011 (coverage metadata), FR-MDF-012 (isolation), NFR-MDF-001; `01` §10.9.11 (the VYK lane this runs beside), §10.9.15/§10.9.16 (walk-budget heritage).
- Acceptance: **TC-MDF-109, TC-MDF-110** (test plan §23) — L2 doubles only, through the production `statements` job runner; red→green recorded. Non-regression: `VykStatementsSourceTests`, `StatementsAndEventsIngestionTests`, `StatementsLoopTests`, `EodChainTests`, `MorningChainTests` re-run green (the incumbent source's behaviour is unchanged when unselected). **Live criteria (orchestrator, post-merge):** (i) one paced run with `Source = kap-public` over the bound universe — current statements written for the mapped companies (FY2025/2026 periods visible in the store), honest skips named and counted, the run completes inside its step bound; (ii) a second run is incremental from the watermark; (iii) the served stock financials show the current periods. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.19 (walk + coexistence subsections — landed by this ticket) + §13.1 **v1.5.56** (soft reservation; re-pin at merge); test plan §23 (TC-MDF-109/110).
- UX refs: none (ingest lane; the served presentation already exists).
- Dependencies: TKT-foundation-080, TKT-foundation-081.
- Parallel group: none.

## Amendment (user decision B, 2026-10-26)

The overlap-read clause of TC-MDF-110 is split out per the recorded remedies above (option B, chosen by the user 2026-10-26 ~01:50 UTC; test plan v1.28). This ticket implements **TC-MDF-110 as amended**: for a period both sources cover, the public-site fact is **not written** and the overlap is surfaced as a named, counted `SOURCE_OVERLAP_RETAINED` finding — the VYK row retained as-is, as-of queryable, nothing overwritten, nothing swallowed silently, no false restatement flag; the coexistence, per-row provenance and no-cross-source-overwrite assertions are unchanged. The deferred read-preference rule (public-site current wins for the periods it covers; VYK retained and as-of queryable; the served payload selects by the pinned preference, via `source_ref` in the store's natural key) is **TC-MDF-111's overlap-read sub-clause (test plan §24), assigned to TKT-foundation-083** — see its coordination note.

## Blocker (implementation lane, 2026-10-26) — the period-overlap preference rule needs the store's key and the serving read path

**What is blocked.** TC-MDF-110's expected result contains two different claims:

1. *Coexistence + provenance + no cross-source in-place overwrite* — implementable inside this
   ticket (the public lane writes its own `SourceRef = kap-public` rows through the existing
   store; no VYK row is deleted or re-stamped).
2. *"for a period both sources cover, the pinned preference rule decides the read (public-site
   current wins for the periods it covers; VYK rows retained and as-of queryable)"* — **not
   implementable inside this ticket's scope**, because there is no way for two sources to hold
   the same period simultaneously and for the reader to prefer one.

**Evidence (all read on `dev@1b8925a`, no code changed).**

- `src/Ingestion/Statements/FinancialStatementStore.cs:283` — the store keys a statement by
  `(instrumentId, periodType, periodEndDate, statementType)`; **the source is not part of the
  key**. An `as_reported` fact (`FinancialStatementStore.cs:157-163`) whose key already holds
  version `0` is counted `unchanged` and **not written**, regardless of which source produced it.
  So a public-site FY2025 filing over an existing VYK FY2025 row is silently swallowed as
  "unchanged" — neither coexistence nor a preference, and a silent non-write disguised as
  idempotency.
- `src/Persistence/DegerliDbContext.cs:197` and the landed migration
  `src/Persistence/Migrations/20261007113309_InitialSchema.cs:905-908` — the DB enforces
  `UNIQUE (instrument_id, period_type, period_end_date, statement_type, version)`. Two rows for
  the same period from two sources are impossible unless the *version* differs.
- `.pipeline/architecture/02-data-model.md:91-93` (frozen) — `version` is the **restatement
  sequence** (`0` = as-reported, `1, 2, …` = successive restatements of the same source), and
  the serving rule is `ORDER BY version DESC LIMIT 1` with `is_restated = version > 0`.
- `src/Api/Stocks/StockFundamentalsEndpoints.cs:583-587` — the served financials payload picks
  the highest `version` per `(periodType, periodEndDate, statementType)` and has no source
  preference at all; with two equal-version rows it would be order-nondeterministic, and with
  one row it can only serve whoever wrote first.

**Why the three in-scope workarounds are all dishonest (recorded, not attempted).**

- *Let the public payload be swallowed as `unchanged`* → the "public-site current wins" clause
  is false and the public fact is silently absent.
- *Overwrite / delete the VYK row in place* → explicitly forbidden ("no deletion, no
  cross-source in-place overwrites; VYK rows retained and as-of queryable").
- *Declare the public fact `restated` so the store assigns a higher version* → this makes
  `Pick` return the public row and "wins", but it records an **as-reported** public filing as a
  restatement of a VYK filing: it sets `restated: true` / `is_rested` on a first-time fact,
  corrupts `02` §3.1's frozen version semantics (and `derived_metrics.is_rested` downstream),
  and is a silent latest-write dressed up as a restatement — the exact thing the TC forbids.

**What this needs (proposed remedies — planner/user decision).**

- **Option A (make 082 land the rule):** amend the frontmatter scope to add
  `src/Ingestion/Statements/FinancialStatementStore.cs`, `src/Persistence/Entities/MarketDataEntities.cs`,
  `src/Persistence/DegerliDbContext.cs`, `src/Persistence/Migrations/` (one forward-only
  migration adding `source_ref` to the natural key) and
  `src/Api/Stocks/StockFundamentalsEndpoints.cs` (source preference in `Pick`), and amend
  TC-MDF-110 to state the mechanism precisely (two rows coexist, the `kap-public` row is served
  for the periods it covers, the `kap` row stays as-of queryable). This is the only option that
  makes the pinned expectation true as written.
- **Option B (split the clause):** keep TC-MDF-110 in 082 as *coexistence + provenance + no
  cross-source overwrite + named/counted `SOURCE_OVERLAP_RETAINED` when the public lane meets a
  foreign-source row*, and move the *overlap read preference* to TKT-foundation-083 (which
  already owns `FinancialStatementStore.cs` and the mixed-world read verification), adding
  `src/Persistence/` and `src/Api/Stocks/` to 083's scope and a TC-MDF-111 sub-case. Requires a
  test-plan amendment by the test-planner (TC-MDF-110's expected result is not mine to rewrite).

**What is *not* blocked.** TC-MDF-109 is implementable end-to-end inside this ticket's scope
(config-selected source, the date-windowed byCriteria walk over 080's stored oid, the paced
client inherited from 079, own `kap-public-*` cursor rows, per-company isolation, bounded
walk + truncation/resume + achieved-depth provenance, decode through 081, store with
`SourceRef = kap-public`), with the VYK lane still the shipped default. One honest constraint
for the walk cases: **the only report *body* double is `kap-public-report-1643141` (the 6-month
interim)**; the annual `Yıllık` report (`1561039`) appears in the committed byCriteria list but
has **no committed body** (081's annual golden is its own live-criterion item). The selection
rule itself is still assertable without a second body (assert the *requested* indices from the
WireMock journal, and give the un-held annual index its honest per-detail skip); decoding an
annual column set is not part of TC-MDF-109 and must not be faked from the interim body.

## Review round 1 (fix dispatch 1 of 2, 2026-10-26)

PR #147 reviewed at `83cb557`; artifact `.pipeline/plan/reviews/TKT-foundation-082-round-1.md`.
Triage: D1–D7, D9–D12 **fixed** in code/tests/docs; D8 **routed to the test-planner** (test-plan
defect: TC-MDF-110's "metrics recompute over the mixed world reports per-source counts honestly"
has no implementation and its machinery is outside this ticket's scope — not fixed here, not
weakened); no disputes, no escalations. What changed:

- **D1** — a list source-down in a **later** window no longer unwinds the walk: the walker returns
  `KapPublicStatementWalkResult.Failure` (`KapPublicStatementFailure`) alongside the pass's real
  counts, the job fails the company with the rows it wrote, and `failed` status now requires
  `written == 0` (partial writes → `partial`). New case: mid-walk outage keeps the first window's
  rows and the persisted cursor/depth.
- **D2** — the universe is walked **least-recently-walked first** via the new
  `KapPublicStatementCursorStore.GetWatermarksAsync` (never-walked first), and a company the spent
  budget never started is named under its own `WALK_NOT_STARTED` reason, distinct from the
  truncation codes.
- **D3** — new two-company / detail-ceiling-1 case asserts **one** report request for the whole pass
  and `WALK_NOT_STARTED` on the company that was never started (a per-company budget would issue 2).
- **D4** — the budget-unit drive is replaced by a walk-level deadline case: the job's
  `MaxWalkDuration` drives the per-pass budget, the injected pacer advances the fake clock, and the
  walk truncates with `WALK_DEADLINE`, persisting the completed report and a truncated depth row.
- **D5** — coverage is now asked on the exact key **and** the calendar period
  (`ForeignStatementCoverage`/`ForeignStatementPeriod`): a same-period end + statement type under a
  different period type is withheld as `SOURCE_PERIOD_TYPE_DISAGREEMENT` with its own evidence,
  never written as a duplicate row. New FY/Q case.
- **D6** — `01` §10.9.19 gains the statements-lane subsections (selection, windowed walk + per-pass
  budget, fair ordering, cursors/depth, isolation + statuses, coexistence write rule incl. the
  period-type disagreement and the deferred read preference), not just the changelog row.
- **D7/D9/D10/D11/D12** — window-scoped truncation wording; overlap evidence no longer promises a
  later pass (needs an explicit cursor reset); `IsFinancialReport` filters on `disclosureCategory`
  alone; the seen-set doc states it grows for the lane's lifetime; a resumed pass preserves the
  bootstrap's `requestedFrom`.

Evidence: `dotnet build tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj` → 0 warnings,
0 errors; targeted `KapPublicStatementsIngestTests` → **15/15 passed**. Docs: test plan v1.29 case
count/clarifications and `01` v1.5.56 row amended in place (no version bump). Zero live requests.

## Review round 2 (fix dispatch 2 of 2, 2026-10-26)

PR #147 reviewed at `98e1d14`; artifact `.pipeline/plan/reviews/TKT-foundation-082-round-2.md`.
Triage: D14, D15, D16 **fixed**; D13 left as recorded residue (explicitly out of scope); D8 stays
routed to the test-planner. What changed:

- **D14** — a new case proves the fair-ordering remedy in the **starvation direction**: pass 1's
  one-detail ceiling is spent by the head company, the second is `WALK_NOT_STARTED` with no cursor
  row at all; pass 2 (same ceiling) walks the **never-walked** company first — proven from
  observable state (the list request carries that company's own stored oid, its cursor row appears
  and its facts are stored, while the head company's cursor watermark and seen set are unchanged and
  it is the one named not-started). Red against the bare constituent order: `Expected: 1, Actual:
  0` on `ListRequestsFor(ThyaoPublicOid)`; green with the least-recently-walked order restored.
- **D15** — the public-run `catch (SourceFetchException)` is **removed** (it was unreachable: 079's
  reader converts every transport failure/timeout into its own `SourceDown` result, which becomes
  `KapPublicStatementFailure` for a list or a per-report skip for a detail — nothing in the public
  path throws it), and its stale comment with it; a comment where it stood records why no guard is
  needed and that the old branch could have re-created D1's false zero. No behaviour change.
- **D16** — test plan v1.29's red→green paragraph and case count made accurate (12/12 →
  15/15 → 16/16, with the round-1 compile red, the two round-1 remedy-revert assertion reds and
  round 2's pass-order red recorded), and this ticket's status line updated to match. No TC text, no
  version bump, no TC-MDF-110 Expected edit.

Evidence: `dotnet build` → 0 warnings, 0 errors; targeted `KapPublicStatementsIngestTests` →
**16/16 passed**; the D14 remedy-revert red and the round-1 remedy-revert reds recorded above and in
the v1.29 row. Zero live requests; no file outside the ticket's declared scope changed.
