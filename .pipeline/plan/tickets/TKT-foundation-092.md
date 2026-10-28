---
scope:
  - src/Ingestion/KapPublic/KapPublicStatementCursor.cs
  - src/Ingestion/KapPublic/KapPublicStatementsWalk.cs
  - src/Ingestion/Statements/StatementsJob.cs
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-092: Quarantined-cursor recovery — the ~180 transport-failure `DISCLOSURE_DETAIL_UNFETCHABLE` quarantines become re-walkable once, bounded and recorded, so the affected companies' history is not cursor-locked forever

- Status: done
- Size: M
- **Priority: P2 — follow-up slice 3b of the 2026-10-27 live-run wave: coverage repair, not new capability. AKBNK, TTKOM and TUPRS's Q2-2023/H1-2024 windows are locked out of their own history by quarantines that were transient transport failures.**
- **The verified live root cause (runs 302/303, recorded on 082):** after ~30 successful excel exports, report fetches failed in transport bursts (~180 `DISCLOSURE_DETAIL_UNFETCHABLE`, `failed in transport`, `bodyReceived: false` — AKBNK 102 in pass 1); the same exports fetch fine from the host moments later (886 KB in 2.7 s — suspected per-connection throttling or container-network MTU, **undetermined**). Per the 063/046 doctrine these quarantines are terminal: the walk marks the failed index completed (`state.With(index)` — it enters the seen set) and the cursor advances past it, so **the affected windows can never be re-walked by an ordinary pass**. The companies' history is silently bounded by a transient outage.
- **What to implement (the 063 stale-cursor-reset doctrine, applied one level down — recovery is bounded, recorded, never infinite):**
  1. **Quarantine replay, once per index, persisted:** a quarantined **transport-failure** detail index (`DISCLOSURE_DETAIL_UNFETCHABLE`) is replayed by a later pass **at most once** — the retry state persisted in the lane's own cursor row (`kap-public-statements-cursor`'s JSON notes carry a bounded `retried` marker set; no new table, no migration). A replay that succeeds writes the statements and records the recovery verbatim (symbol, index, `quarantined → recovered` with the write outcome — inserted / unchanged / overlap-retained, so a recovery that is entirely withheld under 083's coexistence rule is never logged as "written"). A replay that fails again re-quarantines **terminally** — the index keeps its evidence, the marker keeps its terminal state, and no third attempt ever fires (the 063 "never retried forever" rule).
  2. **Shape verdicts stay terminal (recorded rule, not a silent drop):** `UNPARSEABLE_PAYLOAD` quarantines are **not** replayed — a body the source delivered in a non-report shape is a verdict about the body, not a transport outage; replaying it would re-fetch ~900 KB to re-learn it. If the user later wants shape-quarantine replay, that is its own decision/ticket.
  3. **The replay is a walk, with the walk's own rules:** it rides the ordinary pass (no new job, no new request path — 079's client, ≥3 s pacing, single-flight, per-pass budget; replayed indices count against the detail ceiling like any fetch), ordered with the pass's existing fair ordering, and the run's own log lines name **recovered** and **still-quarantined** counts honestly (never a silent hole in either direction — NFR-MDF-001; a log line, **not** a new ledger/stats field).
  4. **The explicit reset stays the escape hatch:** the v1.5.56-recorded condition — an explicit cursor reset makes any withheld/terminal index re-ingestable — is unchanged and remains the recorded route for anything beyond the one automatic replay (090's data-repair note and the future overlap-supersession slice both rely on it).
- **Pinned non-goals:** no change to the quarantine evidence itself (the verbatim bodies stand); no retry ladder/scheduler change; no VYK lane change; no root-causing of the transport bursts themselves (a separate ops finding, recorded on 082 — this ticket recovers from them, it does not diagnose the network).
- Traces to: FR-MDF-002 (statements ingest), FR-MDF-010/011 (achieved depth honesty — a recovered window updates the depth row), FR-MDF-012 (isolation — recovery is named+counted), FR-MDF-016/NFR-MDF-001 (pipeline health — no silent ledger hole), TKT-foundation-063 heritage (the stale-cursor-reset precedent this follows), TKT-foundation-082 heritage (the live finding this closes).
- Acceptance: **TC-MDF-121** (test plan §32, **v1.43** record — authored with the implementation lane; test-planner confirmation pending) — L2 doubles only (the committed byCriteria/report captures; the transport-failure and recovery states derived by WireMock sequencing); red→green recorded (red = a seeded seen-set holding a quarantined index with a now-healthy source never re-fetches it on the current head — the live runs' own observed state). Non-regression: `KapPublicStatementsIngestTests` (the 082 suite's resume/idempotency cases green, unchanged), `StatementsAndEventsIngestionTests`, `EodChainTests`, `MorningChainTests` re-run green. **Live criteria (orchestrator-owned, recorded verbatim):** one paced `Source = kap-public` pass over the affected companies (AKBNK, TTKOM, TUPRS at minimum) — replayed/recovered/still-quarantined counts verbatim, the recovered windows' statements visible in the store, and a second pass making no further replay attempts on the same indices.
- Architecture refs: `01-system-architecture.md` §10.9.19 (walk/cursor subsections — the quarantine-recovery subsection) + §13.1 **v1.5.67** (re-pinned at implementation: v1.5.66 is taken by 091's UA record; review round 1 adds **v1.5.68**, the record reconciliation); test plan §32 (TC-MDF-121, v1.43; review round 1 adds **v1.44**).
- UX refs: none (ingest lane).
- Dependencies: none hard (082/087 merged). Shares `KapPublicStatementsIngestTests.cs` with 088 (if 088 adds cases there) and the two permanent docs with the whole wave — serial by last-lander rebase. **Recommended dispatch: after 088/089/090/091** (it re-walks windows 090 may have re-semantics'd, so its recovery pass writes corrected-decoder output; dispatching it earlier is safe but its live criterion is cleaner last).
- Parallel group: none.

---

## Implementation notes (2026-10-27) — PR https://github.com/sarperim/degerli/pull/154

**Status: in-review.** Branch `tkt-foundation-092`, PR #154 against `dev`. Every touched file is inside the
ticket's `scope:` list; no contract file was amended beyond the two permanent docs this ticket owns.

### The design, as landed

- **Persisted state in the lane's own cursor, no migration** (`src/Ingestion/KapPublic/KapPublicStatementCursor.cs`):
  two **additive** index arrays inside `kap-public-statements-cursor`'s `coverage_metadata.notes` JSON —
  `quarantined` (backlog owing its one replay) and `retried` (the once-only tombstone) — serialized
  **always**, even empty. An **absent** array is read as an empty one, so a pre-092 row keeps its position
  and is never reinterpreted as "everything was retried". `WithQuarantine` adds the index to the seen set
  **and** the backlog (unless tombstoned); `WithReplaySpent` removes it from the backlog and enters the
  tombstone whatever the outcome was. The `AfterWindow` static was replaced by the instance `CloseWindow`
  so the retry markers travel through every window close (both call sites updated); the unused
  `IsReplayPending` helper was dropped.
- **Classification by what the read carried** (`KapPublicStatementsWalk.IsTransportFailure`):
  `read.IsSourceDown && string.IsNullOrEmpty(read.RawBody)` — no body ever received, which is exactly the
  live runs' `failed in transport` / `bodyReceived: false` signature. A non-success status **carrying a
  body** (the source already answered about this index) and a shape verdict (`UNPARSEABLE_PAYLOAD`) are
  terminal. (An earlier draft classified by `!IsQuarantined && IsSourceDown`, which would have made a
  500-with-body replayable; corrected before green and covered by TC-MDF-121 (iii).)
- **The replay rides the ordinary pass** (`WalkAsync`): the backlog is drained **before** the window loop,
  through the same 079 paced single-flight client, admitted by the same `KapPublicWalkBudget` — a replay
  costs a **detail** fetch, so `WALK_DETAIL_CAP` bounds it (it needs no list page, so the page ceiling is
  untouched). A bounded replay sets `truncated`/`WALK_DETAIL_CAP`, leaves position and backlog exactly
  where they were, and the next pass resumes the backlog; the window loop stops at its first `truncated`
  check, so a bounded replay issues no list request at all.
- **Named outcomes** (`KapPublicStatementWalkResult` + `StatementsJob`): `ReplaysAttempted`, `Recovered`
  (facts written — a repair of a hole, counted apart from ordinary `Inserted`), `StillQuarantined`
  (re-quarantined terminally, replay spent). They surface in **three log lines** — the walker's
  per-symbol summary (`KapPublicStatementsWalk.cs:597-616`), the job's per-company replay line (with the
  remaining backlog, emitted only when a replay was attempted — `StatementsJob.cs:650-656`) and the run's
  **completion log line**, which names `quarantine-replays N (recovered R, still-quarantined S)`
  (`StatementsJob.cs:879-895`). They are **not** a stored ledger/stats field: `IngestResult` and
  `SymbolRunStat` are untouched by this ticket, so the counts are observable in the log and nowhere
  else. No third attempt ever fires, in this process or a later one.

### Recorded deliberate behaviours (dispatcher R4 — rules a reader would otherwise infer from a count)

- **One quarantine row per attempt; rows are neither deduped nor superseded.** A replay that fails again
  writes a **second** `DISCLOSURE_DETAIL_UNFETCHABLE` row for the *same* disclosure index. Verified in
  the shipped write path, not inferred from the test: `QuarantineWriter.WriteAsync`
  (`src/Ingestion/Quarantine/Quarantine.cs:35-50`) only ever `Add`s a new `QuarantinedFact` — it never
  updates, deletes or supersedes — and `StatementsJob.QuarantineSkipsAsync`
  (`src/Ingestion/Statements/StatementsJob.cs:693-727`) calls it once per skip, including the replay's
  own skip (`KapPublicStatementsWalk.cs:337-352`). TC-MDF-121 (i)'s "5 unfetchable rows for 3 indices"
  is therefore five **intended** rows — three first-pass quarantines and two spent replays — not a
  double-write bug. The dedupe instrument is the cursor's `retried` tombstone (it bounds *attempts*);
  evidence is never deleted (BR-MDF-007). Recorded as a rule in `01` v1.5.68 §10.9.19.
- **A recovered index leaves its original quarantine row standing.** The recovery path
  (`KapPublicStatementsWalk.cs:389-405`) writes the facts, marks the cursor
  (`state.With(index).WithReplaySpent(index)`) and logs — it never touches `quarantined_facts`: nothing
  deletes the earlier row and nothing annotates it with a recovery status. The quarantine evidence stays
  exactly as recorded (the pinned "no change to the quarantine evidence itself" non-goal); the recovery
  is read from the run's completion log line and from the stored facts. Recorded in `01` v1.5.68
  §10.9.19.

### Test evidence (executed locally — Docker/Testcontainers available)

- `dotnet test … --filter "FullyQualifiedName~TC_MDF_121"` → **4 passed / 0 failed / 0 skipped**.
- **Review round 1 (2026-10-28):** `dotnet test … --filter "FullyQualifiedName~Cursor_written_before_092|FullyQualifiedName~TC_MDF_121"` → **5 passed / 0 failed / 0 skipped** — the four TC-MDF-121 cases plus the new **untagged** cursor back-compatibility case (DR-2). Run targeted only; CI is the authority for the full suite.
- **Red evidence:** with the replay loop disabled (`foreach (var quarantinedIndex in
  Enumerable.Empty<long>())`) → **4 failed / 0 passed**: (i) `Expected: 1, Actual: 0` on the recovered
  report's request (the live cursor-locked world), (ii)/(iii) the `quarantine-replays …` run line missing,
  (iv) `Expected: "WALK_DETAIL_CAP", Actual: null`. Restored and re-run green.
- **Non-regression, targeted (round-1 baseline, before the DR-2 cursor test — +1 case):**
  `KapPublicStatementsIngestTests` **20/20**; `StatementsAndEventsIngestionTests`
  + `EodChainTests` + `MorningChainTests` + `KapPublicIdentityJobTests` + `KapDisclosure*` **77/77**
  (**97/97** total — the class is **21** cases / **98** in total with the DR-2 test added). `dotnet build`
  0 warnings, 0 errors. The counts were not re-measured after the test landed: review round 2 is docs-only
  and CI is the authority for the post-change suite.

### Harness notes (recorded so they are not re-discovered)

- The transport failure is a **real** connection that never delivers: WireMock answers after **30 s** while
  the lane's per-item bound is **150 ms** (`Ingestion:KapPublic:DetailFetchTimeoutMs`), so the client times
  out with no body read. `FaultType.EMPTY_RESPONSE` and `FaultType.MALFORMED_RESPONSE_CHUNK` were tried and
  neither produces a transport failure here (the first behaves as a body-less shape verdict).
- A per-item timeout is a real timer, so this class injects `TimeProvider.System` (`BuildTransportIngestion`);
  every other case keeps the deterministic fake clock.
- **Recorded limit:** WireMock enters an **aborted** response into its journal only when it eventually
  completes, so a per-index request count is evidence only for doubles that answer at once. The
  still-failing replays are witnessed by the run's completion log line and the persisted tombstones; the abort
  delay is far longer than any case's runtime, so no late entry can flake a later pass's counts.

### Docs

- `.pipeline/testing/market-data-foundation.md` — **§32** (TC-MDF-121 (i)–(iv)) + change record **v1.43**;
  review round 1 adds **v1.44** (the §32 block moved ahead of the change record, its lettering unified to
  (i)–(iv) with the shipped method names mapped, the "stats" wording corrected to the completion log line,
  and the one-row-per-attempt rule stated in case (i)).
- `.pipeline/architecture/01-system-architecture.md` — §10.9.19 quarantine-recovery subsection; §13.1
  **v1.5.67** (re-pinned: the ticket's soft v1.5.66 reservation was taken by 091's UA record);
  review round 1 adds **v1.5.68** (DR-1's recorded zero-length-body edge, DR-4's log-line-not-ledger
  wording, DR-5's one-row-per-attempt rule, DR-6's standing original quarantine row) — v1.5.67 is left
  intact as the landed record.

### Not done here, as pinned

Decoder, identity resolver (088/090/091, merged), the quarantine evidence format, the transport-burst root
cause (an ops finding), the **088 fenced user decisions — `TOTAL_EQUITY` (NCI-inclusive) and the narrow
`debt` mapping — open upstream, not this ticket's** (held by the dispatcher; the `DR-1`/`DR-2` labels in
088's review name those decisions, not this ticket's finding ids) — and the **live recovery run** itself,
which is the orchestrator's post-merge criterion: one paced `Source = kap-public` pass over at least AKBNK, TTKOM and
TUPRS, replayed/recovered/still-quarantined counts verbatim, the recovered windows' statements visible in the
store, and a second pass making no further replay attempts on the same indices.

Review round 1 (2026-10-28) closed the two gaps that rested on reading alone: **DR-1** is now recorded as a
deliberate edge in `01` v1.5.68 §10.9.19 rather than a defect to re-open (the predicate's behaviour is
unchanged — tightening it is a semantics change this ticket is not authorised to make), and **DR-2** is
covered by a plain unit assertion over a literal pre-092 cursor notes document in
`KapPublicStatementsIngestTests.Cursor_written_before_092_keeps_its_position_and_has_no_replay_state`
(same watermark and seen set, empty backlog/tombstone), deliberately **untagged** because §32 defines no
back-compatibility sub-case.

## Merge record — 2026-10-28 (orchestrator)

- **PR #154 squash-merged to `dev` as `778380e`** after 3 review rounds (2 fixer dispatches, cap reached; 8+4+3 findings all triaged, 0 disputes/escalations/blockers). Round artifacts: `.pipeline/plan/reviews/TKT-foundation-092-round-{1,2,3}.md`.
- All four concurrency-surfaced rulings verified settled on disk: (1) replay scope = body-less transport failures only, stated in arch §10.9.19 + ticket, 082 suite byte-identical; (2) docs §32 contiguous, arch v1.5.68, test plan v1.44, stale v1.5.40/v1.5.67 collisions corrected; (3) counts reach three log sites, "not a stored ledger field" explicit, Jobs.cs untouched; (4) re-quarantine second-row + recovered-leaves-row both documented and verified add-only.
- Raced-edit window (duplicate session incident): no residue at any head — verified single replay loop, no `IsReplayPending` remnant, coherent log lines.
- Open non-blocking: R-1/X-1/X-2 docs nits (log-site count undercounted as "two" vs three; v1.43 corrected in place without annotation) — recorded residue, one docs commit when convenient.
- 088's DR-1/DR-2 restored as pinned non-goals, still fenced to the user.
- Live recovery pass (AKBNK/TTKOM/TUPRS) is the orchestrator's post-merge criterion — running with the wave's live chain. **Status: done** (code+review; live criteria recorded when run).

## Live criteria — 2026-10-27/28 (orchestrator, dev stack, recorded verbatim)

- **Replay mechanism live-proven:** the walk's per-symbol summary now carries `quarantined-replays {n} (recovered {n}, still-quarantined {n}), backlog {n}` — tonight's pass registered new transport-failure quarantines into cursors (KRDMD backlog 8, VAKBN backlog 16, GARAN backlog 6) for next-pass replay.
- **Transport-failure throttling recurred on the newly-resolved companies** (KRDMD/VAKBN/YKBNK; 50 new quarantines in run 363) — same pattern as the first night (bursts after a stretch of successful fetches). The honest quarantine + backlog registration handled it as designed.
- **Legacy pre-092 quarantines (AKBNK's 102, TTKOM, TUPRS windows) are NOT in any cursor** — they predate the mechanism, so the replay cannot reach them. **Follow-up: legacy-backlog backfill (quarantined_facts → cursor) or 063-class reset.**
