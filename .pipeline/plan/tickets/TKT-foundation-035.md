---
scope:
  - src/Ingestion/Quarantine/
  - src/Api/
  - tests/Api.IntegrationTests/AdminQuarantineEndpointsTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-035: prices `CONFLICTING_VALUE` quarantine hygiene — bulk-resolve the re-fetch drift backlog and settle the policy

- Status: todo
- Size: M
- **Priority: QUEUED — later lane. Do NOT dispatch tonight** (explicit 2026-10-19/20 routing; finding 5 is a data-hygiene follow-up, not urgent — stored prices are the first-write real values and serving is unaffected). **Additionally gated on a user policy decision** — see below; do not dispatch without it.
- Finding (live-run session finding 5): the paced Monday catch-up produced **74,008 `CONFLICTING_VALUE` quarantine rows** — İş Yatırım EOD values drift between fetches (revision/rounding churn), and the duplicate-guard design quarantines the re-fetched conflicting value instead of overwriting (correct and honest by design; first-write wins). The backlog is operational noise, not a product defect: nothing fabricated, nothing dropped silently.
- Scope: **two parts, policy before tooling.** **(a) Policy decision (USER, not the coder):** how re-fetch drift is handled going forward — options on the table: (1) keep-first + a bulk-resolve/dedup admin tool for the backlog (quarantine rows reviewed/bulk-acknowledged as drift, never auto-promoted into facts); (2) a bounded last-write-wins revision policy for prices (a data-model semantics change — would need its own architecture amendment and golden work); (3) accept-and-ignore (tooling only to cap/rotate the quarantine table). Default recommendation if the user is silent: option 1 — smallest honest surface, no fact semantics change. **(b) Tooling per the chosen policy:** a paced, admin-triggered bulk-resolve/acknowledge lane over `CONFLICTING_VALUE` quarantine rows (bounded batches, run-ledger integration, honest reporting — count resolved vs retained, never silent deletion), plus whatever drift-reporting the policy needs. The ticket's file scope above is provisional — re-run the file-scope audit at dispatch once the policy fixes the actual shape.
- Traces to: the duplicate-guard / quarantine doctrine (honest ingestion — unmapped/conflicting reported, never dropped or silently overwritten); FR-MDF-00x ingestion-integrity lineage; foundation pseudo-domain (explicit criteria below).
- Acceptance: **explicit criteria per the foundation convention (to be finalized against the chosen policy).** **(i)** The bulk-resolve lane (or chosen tooling) processes a bounded batch of seeded `CONFLICTING_VALUE` rows: acknowledged drift is recorded as resolved with an audit trail, retained rows stay quarantined, stored facts are NEVER modified by the tool (option 1 posture — strengthen/replace this criterion if the user picks another option). **(ii)** Existing quarantine golden tests (`AdminQuarantineEndpointsTests` and the ingestion lanes) re-run green unchanged (additive only; pre-list permitted deltas). **(iii)** CI green. **Live criterion (orchestrator post-merge):** one paced admin bulk-resolve run on the dev stack reduces the 74,008-row backlog by the bounded batch size with the count recorded honestly on the ticket; no live-source calls (quarantine-table work only).
- Architecture refs: `01-system-architecture.md` quarantine/honest-ingestion sections + §13.1 change-record row; `02-data-model.md` (quarantine tables); the duplicate-guard design notes in the prices ingestion lane.
- UX refs: none (admin surface only, if any — admin screens follow the existing admin-endpoint conventions).
- Dependencies: none hard. Gated on the user's policy decision (part a) — the routing user is asleep; this is a morning decision, which is why the ticket is queued.
- Parallel group: none (queued).
