---
scope:
  - src/Ingestion/Statements/StatementsSourceAdapter.cs
  - src/Ingestion/Statements/StatementsJob.cs
  - src/Ingestion/Statements/KapDisclosureCursorStore.cs
  - src/Ingestion/Kap/VykDisclosureWalk.cs
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-063: Statements lane — per-company isolation + stale-disclosure-cursor recovery (`ER005` is not a source failure)

- Status: todo
- Size: M
- **Priority: P1 — tonight/tomorrow's chain risk (USER GRANT 2026-10-23 afternoon).** The lane fails 22–24 of 30 covered companies every run and, on a zero-write run, reports `failed` — which fail-stops the EOD chain **before `metrics-recompute` / `market-snapshot` / `medians`**, the same chain-loss class 061 closed for the event families.
- **Verified evidence (dev stack, 2026-10-23, orchestrator logs + direct gateway probes):**
  - Run **212 at 14:06 UTC** — `constituents 30, written 0, unchanged 0, quarantined 6, failed 24 → Status failed`; the 14:17 UTC re-run is identical (logs, `statements: run for 2026-10-22 complete`). Earlier same day: 11:00 UTC `failed 22` / `partial` (written 5), 12:24 UTC `failed 23` / `partial` (written 2).
  - **Root cause probed directly against the gateway:** `GET /api/vyk/disclosures?disclosureIndex=…&disclosureClass=FR&companyId=…` answers **HTTP 400 `{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}`** for the indices we request — two independently sampled paths, both repeating. The persisted per-company cursor points past the tail the gateway now serves; the adapter's own walk treats that 400 as a `SourceFetchException`, so each company is marked `failed`.
  - The identical unguarded per-item fetch at **`src/Ingestion/Statements/StatementsSourceAdapter.cs:291`** was recorded as the 061 follow-up. 061 landed the semantics in the **shared** `VykDisclosureWalker` (ER005 honest-empty, one stale-cursor reset, per-detail isolation + bounded fetch); the statements lane still carries its **own duplicated walk** (~`FetchVykCompanyAsync`, lines ~250–315) with neither.
- **Pinned fix (planner):**
  1. **One walk, not two.** The statements VYK walk is replaced by the shared `VykDisclosureWalker` (034-R8's extraction rationale): the adapter passes its list route (carrying `disclosureClass=FR`), detail route, family predicate (all `FR` disclosures), the statement decode callback and the cursor. The duplicated walk is deleted so the ER005/isolation/timeout semantics can never drift again. **Unchanged on purpose:** the non-VYK canonical/XBRL single-resource path and its goldens; malformed detail bodies stay `UNPARSEABLE_PAYLOAD`; a **directory/list transport failure that is not ER005** stays a source failure.
  2. **Stale-cursor recovery, recorded verbatim.** A first request made from a persisted cursor that answers **HTTP 400 `ER005`** is the gateway saying "no disclosure at/after this index", never a source failure: reset **once** from `EarlyDisclosureIndex`, re-walk, and carry the gateway's error body verbatim into the run's evidence (structured log + run stats/log line naming symbol, from-index → reset-index, code and body). **Never retried forever:** the reset outcome is persisted — the corrected cursor (last emitted index) when the re-walk emitted facts, or the cursor row is **cleared** (`KapDisclosureCursorStore.ClearAsync`) when the re-walk is itself honest-empty — so the next run does not repeat the same stale ER005.
  3. **Per-company isolation + honest status.** The unit of isolation is the company (the lane's loop) with 061's item-level semantics one level down: an unfetchable disclosure detail is an evidence-carrying skip (`DISCLOSURE_DETAIL_UNFETCHABLE`, disclosure index, failure kind, gateway error verbatim) quarantined per 061, counted in the per-symbol stats, and the other companies' rows are written. Every constituent appears in `symbolStats`; nothing is silently dropped. A run whose writes are zero because companies were **honestly skipped** (stale cursor reset, unfetchable detail, identity unresolved, quarantine) ends **`partial`** — **never `failed`** and never fail-stops the chain. A run where every company failed for a **genuine source-down** (directory or list transport / non-ER005 status) still ends `failed`, preserving the retry ladder's honesty.
  4. **Honesty doctrine (recorded with the grant):** never fabricate a statement, never silently drop a company; a skip is evidence + a named reason + a count.
- Traces to: FR-MDF-002 (statements ingest), FR-MDF-008 (identity/provenance), FR-MDF-010/011 (honest achieved depth — skips counted, never hidden), FR-MDF-016 (pipeline health — the chain completes), FR-MDF-012 (isolation doctrine); §10.9.11 (VYK gateway + per-company cursor), §10.9.15 (064/061 per-item doctrine applied to statements), §10.9.16/§10.9.17 (the chains/watchdog this keeps reachable); TKT-foundation-061 heritage (the shared walker + per-item evidence), TKT-foundation-046 heritage (per-constituent isolation).
- Acceptance: **explicit criteria; L2 doubles only, never live sources.**
  - **(i) TC-MDF-096 — per-company / per-detail isolation (red→green):** a statements double with one company's detail returning HTTP 500 and, separately, one with a transport reset → the run completes, the item is quarantined with the gateway's error verbatim and counted, every other company's rows are written, and the run ends `partial`. A world where **all** companies are isolated (zero writes) ends `partial`, not `failed` (red today: 24 `failed` + 6 quarantined → `failed`).
  - **(ii) TC-MDF-097 — stale-cursor recovery:** a seeded cursor past the gateway tail answers `400 {"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}` on the first list call → exactly one reset from the configured early index, the re-walked facts are written, the reset is recorded verbatim (code + body + symbol + from/to), and the corrected cursor is persisted (or cleared on an honest-empty re-walk); a second run makes **one** list call for that company with no repeated stale reset. A non-ER005 400/500 stays a source failure (no false recovery).
  - **(iii) Golden stability:** the non-VYK statements goldens are byte-identical; `StatementsAndEventsIngestionTests` (061 lane), `VykCompanyDirectoryTests`, `EodChainTests`, `MorningChainTests`, `SchedulerTests`, `EodStallWatchdogTests`, `MarketSnapshotTests`, `MetricsRecomputeIdempotencyTests` re-run green; `dotnet build` clean.
  - **(iv) CI green.**
  - **Live criterion (orchestrator post-merge, honest, recorded verbatim):** one paced `statements` run over the 30 covered companies makes **zero ER005-caused `failed` companies** (recovered or isolated with evidence), writes what the source serves, ends an honest `partial` where gaps remain, and the EOD chain reaches `metrics-recompute` / `market-snapshot` / `medians`; a second consecutive run shows no repeated stale-cursor reset and no ER005 on the same company. The gateway's actual bodies are recorded verbatim on the ticket.
- Architecture refs: `01-system-architecture.md` §10.9.11 (per-company cursor + walk), §10.9.15 (extended: statements joins the shared walker; ER005 reset persistence; the zero-write `partial` rule), §10.9.16/§10.9.17 (cited), §13.1 **v1.5.42 (reserved above landed max v1.5.41; the row lands with the implementation)**; test plan: `.pipeline/testing/market-data-foundation.md` §15 TC-MDF-096/097.
- UX refs: none (ingestion resilience; user-visible effect is the site no longer goes stale when statements fails).
- Dependencies: none hard — 061 is landed (PR #125), so the shared walker/`VykDetailSkip` surface exists. **Coordination:** `VykDisclosureWalk.cs` is shared with 061 (landed) and 064 is scheduler-only — files disjoint; the two doc files (`.pipeline/testing/market-data-foundation.md`, `01-system-architecture.md`) are shared with 064/065, so the doc edits serialize by last-lander rebase.
- Parallel group: none (doc-file overlap with 064/065)

## Evidence appendix (verbatim, 2026-10-23)

| Run (UTC) | Constituents | Written | Unchanged | Quarantined | Failed | Status |
|---|---|---|---|---|---|---|
| 11:00:10 | 30 | 5 | 2 | 8 | 22 | partial |
| 12:24:04 | 30 | 2 | 2 | 7 | 23 | partial |
| **14:06:17 (run 212)** | **30** | **0** | **0** | **6** | **24** | **failed — chain fail-stop** |
| 14:17:58 | 30 | 0 | 0 | 6 | 24 | failed |

Gateway answer on the requested stale indices (two independently sampled paths):

```
HTTP 400
{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}
```
