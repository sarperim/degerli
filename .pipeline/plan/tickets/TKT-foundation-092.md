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

- Status: in-review
- Size: M
- **Priority: P2 — follow-up slice 3b of the 2026-10-27 live-run wave: coverage repair, not new capability. AKBNK, TTKOM and TUPRS's Q2-2023/H1-2024 windows are locked out of their own history by quarantines that were transient transport failures.**
- **The verified live root cause (runs 302/303, recorded on 082):** after ~30 successful excel exports, report fetches failed in transport bursts (~180 `DISCLOSURE_DETAIL_UNFETCHABLE`, `failed in transport`, `bodyReceived: false` — AKBNK 102 in pass 1); the same exports fetch fine from the host moments later (886 KB in 2.7 s — suspected per-connection throttling or container-network MTU, **undetermined**). Per the 063/046 doctrine these quarantines are terminal: the walk marks the failed index completed (`state.With(index)` — it enters the seen set) and the cursor advances past it, so **the affected windows can never be re-walked by an ordinary pass**. The companies' history is silently bounded by a transient outage.
- **What to implement (the 063 stale-cursor-reset doctrine, applied one level down — recovery is bounded, recorded, never infinite):**
  1. **Quarantine replay, once per index, persisted:** a quarantined **transport-failure** detail index (`DISCLOSURE_DETAIL_UNFETCHABLE`) is replayed by a later pass **at most once** — the retry state persisted in the lane's own cursor row (`kap-public-statements-cursor`'s JSON notes carry a bounded `retried` marker set; no new table, no migration). A replay that succeeds writes the statements and records the recovery verbatim (symbol, index, from-quarantine → written). A replay that fails again re-quarantines **terminally** — the index keeps its evidence, the marker keeps its terminal state, and no third attempt ever fires (the 063 "never retried forever" rule).
  2. **Shape verdicts stay terminal (recorded rule, not a silent drop):** `UNPARSEABLE_PAYLOAD` quarantines are **not** replayed — a body the source delivered in a non-report shape is a verdict about the body, not a transport outage; replaying it would re-fetch ~900 KB to re-learn it. If the user later wants shape-quarantine replay, that is its own decision/ticket.
  3. **The replay is a walk, with the walk's own rules:** it rides the ordinary pass (no new job, no new request path — 079's client, ≥3 s pacing, single-flight, per-pass budget; replayed indices count against the detail ceiling like any fetch), ordered with the pass's existing fair ordering, and the run's stats name **recovered** and **still-quarantined** counts honestly (never a silent hole in either direction — NFR-MDF-001).
  4. **The explicit reset stays the escape hatch:** the v1.5.56-recorded condition — an explicit cursor reset makes any withheld/terminal index re-ingestable — is unchanged and remains the recorded route for anything beyond the one automatic replay (090's data-repair note and the future overlap-supersession slice both rely on it).
- **Pinned non-goals:** no change to the quarantine evidence itself (the verbatim bodies stand); no retry ladder/scheduler change; no VYK lane change; no root-causing of the transport bursts themselves (a separate ops finding, recorded on 082 — this ticket recovers from them, it does not diagnose the network).
- Traces to: FR-MDF-002 (statements ingest), FR-MDF-010/011 (achieved depth honesty — a recovered window updates the depth row), FR-MDF-012 (isolation — recovery is named+counted), FR-MDF-016/NFR-MDF-001 (pipeline health — no silent ledger hole), TKT-foundation-063 heritage (the stale-cursor-reset precedent this follows), TKT-foundation-082 heritage (the live finding this closes).
- Acceptance: **TC-MDF-121** (test plan §32, v1.40 reservation — planner-authored, test-planner confirmation pending) — L2 doubles only (the committed byCriteria/report captures; the transport-failure and recovery states derived by WireMock sequencing); red→green recorded (red = a seeded seen-set holding a quarantined index with a now-healthy source never re-fetches it on the current head — the live runs' own observed state). Non-regression: `KapPublicStatementsIngestTests` (the 082 suite's resume/idempotency cases green, unchanged), `StatementsAndEventsIngestionTests`, `EodChainTests`, `MorningChainTests` re-run green. **Live criteria (orchestrator-owned, recorded verbatim):** one paced `Source = kap-public` pass over the affected companies (AKBNK, TTKOM, TUPRS at minimum) — replayed/recovered/still-quarantined counts verbatim, the recovered windows' statements visible in the store, and a second pass making no further replay attempts on the same indices.
- Architecture refs: `01-system-architecture.md` §10.9.19 (walk/cursor subsections — the quarantine-recovery subsection) + §13.1 **v1.5.67** (re-pinned at implementation: v1.5.66 is taken by 091's UA record); test plan §32 (TC-MDF-121).
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
  (re-quarantined terminally, replay spent). The job logs a per-company line (with the remaining backlog)
  and the run summary names `quarantine-replays N (recovered R, still-quarantined S)`. No third attempt
  ever fires, in this process or a later one.

### Test evidence (executed locally — Docker/Testcontainers available)

- `dotnet test … --filter "FullyQualifiedName~TC_MDF_121"` → **4 passed / 0 failed / 0 skipped**.
- **Red evidence:** with the replay loop disabled (`foreach (var quarantinedIndex in
  Enumerable.Empty<long>())`) → **4 failed / 0 passed**: (i) `Expected: 1, Actual: 0` on the recovered
  report's request (the live cursor-locked world), (ii)/(iii) the `quarantine-replays …` run line missing,
  (iv) `Expected: "WALK_DETAIL_CAP", Actual: null`. Restored and re-run green.
- **Non-regression, targeted:** `KapPublicStatementsIngestTests` **20/20**; `StatementsAndEventsIngestionTests`
  + `EodChainTests` + `MorningChainTests` + `KapPublicIdentityJobTests` + `KapDisclosure*` **77/77**
  (**97/97** total). `dotnet build` 0 warnings, 0 errors.

### Harness notes (recorded so they are not re-discovered)

- The transport failure is a **real** connection that never delivers: WireMock answers after **30 s** while
  the lane's per-item bound is **150 ms** (`Ingestion:KapPublic:DetailFetchTimeoutMs`), so the client times
  out with no body read. `FaultType.EMPTY_RESPONSE` and `FaultType.MALFORMED_RESPONSE_CHUNK` were tried and
  neither produces a transport failure here (the first behaves as a body-less shape verdict).
- A per-item timeout is a real timer, so this class injects `TimeProvider.System` (`BuildTransportIngestion`);
  every other case keeps the deterministic fake clock.
- **Recorded limit:** WireMock enters an **aborted** response into its journal only when it eventually
  completes, so a per-index request count is evidence only for doubles that answer at once. The
  still-failing replays are witnessed by the run's own ledger line and the persisted tombstones; the abort
  delay is far longer than any case's runtime, so no late entry can flake a later pass's counts.

### Docs

- `.pipeline/testing/market-data-foundation.md` — **§32** (TC-MDF-121 (i)–(iv)) + change record **v1.43**.
- `.pipeline/architecture/01-system-architecture.md` — §10.9.19 quarantine-recovery subsection; §13.1
  **v1.5.67** (re-pinned: the ticket's soft v1.5.66 reservation was taken by 091's UA record).

### Not done here, as pinned

Decoder, identity resolver (088/090/091, merged), the quarantine evidence format, the transport-burst root
cause (an ops finding), DR-1/DR-2 (fenced user decisions) — and the **live recovery run** itself, which is
the orchestrator's post-merge criterion: one paced `Source = kap-public` pass over at least AKBNK, TTKOM and
TUPRS, replayed/recovered/still-quarantined counts verbatim, the recovered windows' statements visible in the
store, and a second pass making no further replay attempts on the same indices.
