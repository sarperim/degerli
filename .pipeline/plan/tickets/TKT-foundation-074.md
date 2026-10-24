---
scope:
  - IMPLEMENTATION_STATUS.md
  - README.md
  - scripts/implementation-status.sh
---

# TKT-foundation-074: IMPLEMENTATION_STATUS.md — a standing implementation-status surface

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/136
- Size: S
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 11 unparked).**
- **Evidence (gathered 2026-10-24):**
  - No implementation-status file exists at the repo root (`ls` → `README.md`, `deploy/`, `.pipeline/`, source trees). The board is **gitignored/regenerable** by design (`.gitignore`), and ticket files are the source of record — there are 140+ of them.
  - Answering "what is actually implemented vs pending vs blocked, and which live criteria are still open?" requires reading individual tickets: e.g. 063's live run 249, 064's 4h workaround, 065's 64/556 sub-unit finding, 044/045/048 still `todo`.
  - `run-report.md` is a running transcript, not a standing status. The acceptance agent writes a report at the end, not a maintained surface.
  - The risk this ticket must avoid is the one the board already has: a status artifact that silently goes stale.
- **Pinned approach (decision recorded):**
  - Create a committed `IMPLEMENTATION_STATUS.md` with: per-domain implemented / pending / blocked, the **live-criteria ledger** (ticket, criterion, status, evidence link), known gaps and parked items, and an explicit **update rule**.
  - **Options recorded:** (a) hand-maintained with a "last verified" date and a rule that every merged ticket updates it (simple, drifts); (b) **generated from ticket frontmatter/status by a script, committed, with the script failing when stale** (repeatable, more work — recommended if a small parser is feasible; wiring it into required CI is a follow-up, since `ci.yml` belongs to TKT-foundation-066 this wave); (c) generated on demand, never committed (no standing surface).
  - Never invent status: every line cites a ticket or a doc; unknown is written as "not recorded".
  - The board remains a view; this file is a deliberate standing artifact.
- **Acceptance (explicit criteria):**
  - **(i)** The file exists and lists every domain plus every non-done ticket's status and blocker, each with a source citation.
  - **(ii)** The update rule is documented and mechanically checkable (script or CI check), or — if option (a) is chosen — the doc says honestly that it is manual and carries the last-verified date.
  - **(iii)** If generated/checked, a deliberately stale entry is caught by the script; the check is deterministic and does not depend on GitHub state. Wiring it into required CI is a follow-up (TKT-foundation-066 owns `ci.yml` in this wave).
  - **(iv)** `README.md` links to it; no status is asserted without a citation.
  - **(v)** No source change; no architecture change (this is a delivery artifact, not a contract).
- Traces to: none directly (delivery/traceability artifact; supports the acceptance lane and SC-007's honesty posture).
- Architecture refs: none.
- UX refs: none.
- Dependencies: none.
- Parallel group: **P-1** (scopes disjoint from 068/075/076).

---
## Implementation record (coder, 2026-10-24)

### What landed

- **`IMPLEMENTATION_STATUS.md`** (repo root): 19 domain rows, each with one fixed-vocabulary
  status (`live`, `live-degraded`, `wired-not-configured`, `scaffolded`, `blocked-upstream`), a
  one-line cited evidence pointer and a `Checks` cell naming the code/config the claim stands on;
  a **live-criteria ledger** (LC-01..LC-14: claim / what CI proves / what only a live run proves /
  last live observation, dated or explicitly `not yet observed`); the open-ticket ledger for all
  28 non-terminal tickets; known gaps and parked items; and the **update rule**.
- **`scripts/implementation-status.sh`**: read-only, no network, no DB, deterministic (clock
  pinnable with `--today`). It fails when a documented claim no longer holds: a `live`/
  `live-degraded` domain whose `dir:`/`file:` is gone, a claimed `config:` key missing from
  `src/Api/appsettings.json`, a `config-empty:` claim that is no longer empty (EVDS key), a
  `noconfig:` claim that is now configured (Funds), a status word outside the fixed vocabulary, a
  missing required section, a ledger row without a dated/`not yet observed` last observation, a
  missing README pointer, an open ticket missing from the ledger (the set of
  `todo`/`in-review`/`in-progress`/`blocked`/`deferred` tickets must match exactly), a ledger row
  whose class disagrees with its ticket, and a `Last verified` date older than 60 days.
- **`--self-test`**: builds a valid fixture in `mktemp -d`, then breaks it eleven ways (missing
  implementation path, absent config key, absent-claimed-present section, undeclared status word,
  missing section, undated ledger row, missing open-ticket row, stale date, ticket/doc class
  mismatch, README without the pointer) and requires a non-zero exit for each — **11/11 pass**;
  the valid fixture passes. No fixture files are committed (they are generated in `/tmp`), so the
  ticket's three-file scope is exactly what changed.
- **`README.md`**: one pointer paragraph in "Project Status".

### Cross-checks performed (bash accuracy over optimism)

Read against the tree at `origin/dev@8f4b5e5` plus the appsettings/ticket text, not guessed:

| Row | What was checked | Result |
|---|---|---|
| Funds | `src/Api/appsettings.json` has **no** `Funds` section (`python3 json` key walk: `Ingestion` keys = Scheduler, TradingCalendar, Prices, Intraday, Universe, Kap, Statements, Dividends, CorporateActions, Disclosures, Macro, MarketSnapshot) and `TefasSourceOptions.SectionName = "Ingestion:Funds"` exists in code with a `SOURCE_NOT_CONFIGURED` guard | `wired-not-configured` (OQ-FDF-002), encoded as `noconfig:Ingestion:Funds:BaseUrl` — the checker fails the day someone wires it until the row is updated |
| Statements | `Ingestion:Statements:Path` present; TKT-foundation-063 live run 249 `partial` (0 written / 60 unchanged / 25 quarantined) | `live-degraded`, not "done" |
| Prices | `Ingestion:Prices:UniverseIndex = XU100`; the 2026-10-23 9/100 thin day and the chore-#130 re-sweep to 100/100; 043 superseded, not done | `live-degraded` |
| Intraday | `Ingestion:Intraday:BaseUrl` present; appsettings `Path` is now the `ChartData/IntradayDelay` chart endpoint (chore #129) while the only live sweep (run 218) predates the repoint | `live-degraded`, ledger LC-06 `not yet observed` |
| Shares | Shares lane reads the **Kap** section (no own config section) and is **not** in `EodIngestChain.JobCodes` (universe-sync, prices, statements, dividends, corporate-actions, disclosures, metrics-recompute, market-snapshot, medians) — so run 219 was operator-triggered | `live-degraded`, unit claim unresolved (065 blocked) |
| Pipeline | `IngestionSchedulerOptions.MaxRunDuration` default is still `90 min` (064 todo), the 4 h override is operational | `live-degraded`; LC-05 records the workaround explicitly instead of calling the watchdog solved |
| Macro | `Ingestion:Macro:EvdsApiKey` exists but is empty; TKT-foundation-019 recorded "constructed, capture pending" | `live-degraded` with `config-empty:` — the manual-entry path, not an EVDS pull, is what was observed 2026-10-20 |

### Honesty notes (deliberately not flattered)

- The snapshot row is `live-degraded` and LC-07 says the coverage guard has **no ticket yet**
  (user decision D7), so the nine-symbol aggregate is recorded as a live counter-example rather
  than a fixed defect.
- TKT-foundation-065 is listed by its **ticket** status (`blocked`); the user's D4-A direction is
  recorded only in the gitignored `run-report.md`, which is called out as such in the row.
- TKT-foundation-061/044 rows stay `in-review` (PRs #125/#132 merged, ticket flips pending) — the
  checker's one documented tolerance, stated in the open-ticket section and the update rule.
- CI does **not** run the checker: `ci.yml` belongs to TKT-foundation-066. The gap is stated in
  the file's "Known gaps" rather than implied to be wired.

### Evidence

- `scripts/implementation-status.sh --self-test` → **11/11 cases behaved as required** (exit 0).
- `scripts/implementation-status.sh` on this tree → **131 checks, 0 failures** (exit 0).
