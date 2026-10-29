---
scope:
  # Re-audited 2026-10-29 against the ratified option-1 shape (the ticket's own text
  # declares this list provisional). Findings recorded in the audit section below.
  - src/Ingestion/Quarantine/
  - src/Api/
  - tests/Api.IntegrationTests/AdminQuarantineEndpointsTests.cs
  - tests/Api.IntegrationTests/AdminQuarantineBulkResolveTests.cs
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/02-data-model.md
---

# TKT-foundation-035: prices `CONFLICTING_VALUE` quarantine hygiene — bulk-resolve the re-fetch drift backlog and settle the policy

- Status: in-progress (dispatched 2026-10-29, branch `TKT-foundation-035`)
- Size: M
- **Priority: QUEUED — later lane** (2026-10-19/20 routing; the queue gate is superseded — the policy decision landed 2026-10-29 and the ticket is dispatch-ready). **Policy decision (a) RESOLVED 2026-10-29, user-ratified OPTION 1**: keep-first + a bulk-resolve/acknowledge admin tool for the backlog (quarantine rows reviewed/bulk-acknowledged as drift, never auto-promoted into facts). Options 2 and 3 rejected. Part (b) tooling follows option 1 as written.
- Finding (live-run session finding 5): the paced Monday catch-up produced **74,008 `CONFLICTING_VALUE` quarantine rows** — İş Yatırım EOD values drift between fetches (revision/rounding churn), and the duplicate-guard design quarantines the re-fetched conflicting value instead of overwriting (correct and honest by design; first-write wins). The backlog is operational noise, not a product defect: nothing fabricated, nothing dropped silently.
- Scope: **two parts, policy before tooling.** **(a) Policy decision (USER, not the coder):** how re-fetch drift is handled going forward — options on the table: (1) keep-first + a bulk-resolve/dedup admin tool for the backlog (quarantine rows reviewed/bulk-acknowledged as drift, never auto-promoted into facts); (2) a bounded last-write-wins revision policy for prices (a data-model semantics change — would need its own architecture amendment and golden work); (3) accept-and-ignore (tooling only to cap/rotate the quarantine table). Default recommendation if the user is silent: option 1 — smallest honest surface, no fact semantics change. **(b) Tooling per the chosen policy:** a paced, admin-triggered bulk-resolve/acknowledge lane over `CONFLICTING_VALUE` quarantine rows (bounded batches, run-ledger integration, honest reporting — count resolved vs retained, never silent deletion), plus whatever drift-reporting the policy needs. The ticket's file scope above is provisional — re-run the file-scope audit at dispatch once the policy fixes the actual shape.
- Traces to: the duplicate-guard / quarantine doctrine (honest ingestion — unmapped/conflicting reported, never dropped or silently overwritten); FR-MDF-00x ingestion-integrity lineage; foundation pseudo-domain (explicit criteria below).
- Acceptance: **explicit criteria per the foundation convention — finalized 2026-10-29 against the ratified option 1** (the criteria below stand; (i) is pinned to option 1's posture and additionally requires the audit trail to be the honest `quarantine-resolve` run-ledger row with its resolved/retained counts). **(i)** The bulk-resolve lane (or chosen tooling) processes a bounded batch of seeded `CONFLICTING_VALUE` rows: acknowledged drift is recorded as resolved with an audit trail, retained rows stay quarantined, stored facts are NEVER modified by the tool (option 1 posture — strengthen/replace this criterion if the user picks another option). **(ii)** Existing quarantine golden tests (`AdminQuarantineEndpointsTests` and the ingestion lanes) re-run green unchanged (additive only; pre-list permitted deltas). **(iii)** CI green. **Live criterion (orchestrator post-merge):** one paced admin bulk-resolve run on the dev stack reduces the 74,008-row backlog by the bounded batch size with the count recorded honestly on the ticket; no live-source calls (quarantine-table work only).
- Architecture refs: `01-system-architecture.md` quarantine/honest-ingestion sections + §13.1 change-record row; `02-data-model.md` (quarantine tables); the duplicate-guard design notes in the prices ingestion lane.
- UX refs: none (admin surface only, if any — admin screens follow the existing admin-endpoint conventions).
- Dependencies: none hard. The policy gate (part a) is cleared — user-ratified OPTION 1, 2026-10-29.
- Parallel group: none (queued).

---

## File-scope audit (at dispatch, 2026-10-29 — re-run against the ratified option-1 shape)

The frontmatter list was provisional by the ticket's own text. Audited against the lane the
policy actually implies (a builder-only bulk-acknowledge endpoint over `CONFLICTING_VALUE` rows,
no schema change):

- **`src/Api/`** — kept; the endpoint (route, validation, honest response) lands in
  `src/Api/Admin/AdminQuarantineEndpoints.cs`, following the existing admin-endpoint conventions.
- **`src/Ingestion/Quarantine/`** — kept; the lane itself (`QuarantineBacklogResolver`) lives
  beside the quarantine write path it drains, so the bounded/oldest-first/`SKIP LOCKED` selection
  and the ledger row are one testable unit rather than endpoint-local SQL.
- **`tests/Api.IntegrationTests/AdminQuarantineEndpointsTests.cs`** — kept, but **not modified**:
  the acceptance criterion is that the existing quarantine goldens re-run green *unchanged*
  (additive only). The new lane therefore needs its own file, which the provisional list did not
  have: **added `tests/Api.IntegrationTests/AdminQuarantineBulkResolveTests.cs`** (TC-MDF-129).
- **`.pipeline/architecture/01-system-architecture.md`** — kept; new §10.10 (the keep-first policy
  and the lane's contract), the FR-MDF-012 mechanism row's pointer, and the §13.1 row **v1.5.81**
  (the landed max was verified as v1.5.80 in the document itself; the board's soft reservations
  are stale and were ignored).
- **added `.pipeline/architecture/02-data-model.md`** — the ticket's own Architecture refs name
  `02-data-model.md` (quarantine tables), but the provisional `scope:` omitted it. The amendment
  is a §3.1 note plus the append-only v1.6 status clause: **no schema change, no migration**, since
  the lane reuses `status`/`resolution_note`.
- **Not on the list and deliberately untouched:** `src/Ingestion/IngestionServiceCollectionExtensions.cs`
  (no DI registration was needed — the lane is a static seam over the scoped context and clock, the
  `Xu030LaunchSeed.ApplyAsync` precedent), `src/Persistence/` (no migration), and
  `03-api-design.md` (§8's contract row for the new endpoint is an **architect follow-up**, recorded
  here and in the change record rather than silently edited out of scope).

## Implementation record (2026-10-29)

- Lane: `POST /api/v1/admin/quarantine/conflicting/resolve` `{note, batchSize?}` — class-scoped to
  open `CONFLICTING_VALUE` rows, bounded (max 1000, default 500, out-of-range is a 400 and never a
  silent clamp), oldest-first with `FOR UPDATE SKIP LOCKED`, note required, `open → dismissed` with
  `resolution_note`; nothing deleted, no fact read or written, no payload promoted.
- Audit trail: one `ingest_runs` row per run under job code `quarantine-resolve` (deliberately not a
  registered job — the `holiday-skip` precedent), committed in the same transaction as the rows it
  acknowledges; honest `resolved` + `retained` reporting (a drained backlog states zeros).
- Tests: TC-MDF-129 (a)–(d) in `tests/Api.IntegrationTests/AdminQuarantineBulkResolveTests.cs`.
  Red→green measured: endpoint reverted → **4 failed / 0 passed** (`Expected OK/BadRequest, Actual
  NotFound`); restored → **4/4 green**. Non-regression **19/19** (`AdminQuarantineEndpointsTests`,
  `AdminEndpointsTests`, `AdminDescriptionEndpointsTests`, `ValidationQuarantineTests`); build 0
  warnings / 0 errors.
- Live criterion (orchestrator, post-merge): one paced admin bulk-resolve run on the dev stack
  reduces the 74,008-row backlog by the applied bound, counts recorded verbatim — no live-source
  calls (quarantine-table work only).
