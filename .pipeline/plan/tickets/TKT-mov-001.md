# TKT-mov-001: Design — SCR-001 Market Overview (visual design in design tool)

- Status: blocked — **USER DECISION 2026-10-23 (Option B): kept as the durable design record, NOT closed.** D-UX-TOOL was resolved by foundation-011 ("no design tool; foundations authored directly as in-repo code"), so the design-tool export this ticket specifies cannot be produced today. The user will run a **vision-capable model** design pass when access is available, and these files are the spec set that pass will consume. Do not dispatch; do not close.
- Deferred (user, 2026-10-10): design implemented directly in the web app (no design tool). This per-domain design-artifact ticket is off the critical path; the domain's UI ticket(s) now build the screens directly against `TKT-foundation-011`. Revisit if a later design-fidelity pass is wanted.
- Size: M
- Scope: visual design for SCR-001 in the chosen design tool (D-UX-TOOL): the eight content blocks per UX spec §3 (index levels, sector performance, breadth, top movers w/ volume, market volume, market valuation w/ exclusion disclosure, macro strip with the always-paired inflation display, disclaimer) and all §5 states (loading, populated, per-block/per-indicator stale, macro unavailable, per-block error). Export to `/design/mov/`. Must NOT touch `/src/**`.
- Traces to: UC-MOV-001, UC-MOV-003, UC-MOV-004 (screens hosting these)
- Acceptance (explicit, no TCs — design work): every block and state in `ux/market-overview.md` §3–§5 is represented; the inflation pair is designed to remain both visible at narrow width (stacked allowed); change direction conveyed non-color-only; TR + EN copy direction; narrow-width behavior; builder approves the export; behavioral decisions flow back through the ux-designer.
- Architecture refs: `01-system-architecture.md` §3 (C1), §5 (charts row — Recharts for any series), §8.1 (M-5 independent blocks)
- UX refs: SCR-001; UXR-MOV-001..012
- Dependencies: TKT-foundation-011
- Parallel group: P-7
