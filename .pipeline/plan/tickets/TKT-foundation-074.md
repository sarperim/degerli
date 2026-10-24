---
scope:
  - IMPLEMENTATION_STATUS.md
  - README.md
  - scripts/implementation-status.sh
---

# TKT-foundation-074: IMPLEMENTATION_STATUS.md — a standing implementation-status surface

- Status: todo
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
