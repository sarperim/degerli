# TKT-mdf-001: Design — SCR-012 Admin Dashboard (visual design in design tool)

- Status: blocked — **USER DECISION 2026-10-23 (Option B): kept as the durable design record, NOT closed.** D-UX-TOOL was resolved by foundation-011 ("no design tool; foundations authored directly as in-repo code"), so the design-tool export this ticket specifies cannot be produced today. The user will run a **vision-capable model** design pass when access is available, and these files are the spec set that pass will consume. Do not dispatch; do not close.
- Deferred (user, 2026-10-10): design implemented directly in the web app (no design tool). This per-domain design-artifact ticket is off the critical path; the domain's UI ticket(s) now build the screens directly against `TKT-foundation-011`. Revisit if a later design-fidelity pass is wanted.
- Size: M
- Scope: visual design for SCR-012 in the chosen design tool (D-UX-TOOL): one screen with eight content sections per the UX spec §3 — ops summary, quarantine review queue, coverage report, run ledger + job triggers, description review & publication, screener metric visibility, DCF baseline regeneration, aggregate stats. All §5 states (loading, access-denied, populated, in-progress monitoring w/ periodic-update indication, empty, error, mutation pending, stale-data). Export to `/design/mdf/`. Must NOT touch `/src/**`.
- Traces to: UC-MDF-001 (alt b + ops), UC-MDF-002, UC-MDF-004, UC-MOV-005, UC-SCR-004, UC-RES-004, UC-VAL-003, UC-FDF-002/003 (screens hosting these)
- Acceptance (explicit, no TCs — design work): every section and state in `ux/market-data-foundation.md` §3–§5 is represented; role-gated admin entry + access-denied state designed; confirmations (job trigger, backfill, publish, metric toggle, baseline regen) designed as focus-trapping dialogs with consequence text; TR + EN copy direction for section/field labels; narrow-width behavior; builder approves the export; behavioral decisions flow back through the ux-designer, never silently into code.
- Architecture refs: `01-system-architecture.md` AD-13; `03-api-design.md` §8 (payload shapes the design lays out)
- UX refs: SCR-012; UXR-MDF-001..028
- Dependencies: TKT-foundation-011
- Parallel group: P-7
