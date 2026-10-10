# TKT-scr-001: Design — SCR-003 Screener & SCR-004 My Saved Screens (visual design)

- Status: blocked
- Deferred (user, 2026-10-10): design implemented directly in the web app (no design tool). This per-domain design-artifact ticket is off the critical path; the domain's UI ticket(s) now build the screens directly against `TKT-foundation-011`. Revisit if a later design-fidelity pass is wanted.
- Size: M
- Scope: visual design for SCR-003 and SCR-004 in the chosen design tool (D-UX-TOOL): criteria builder (five families, min/max/range, growth window selector, remove/clear), run control, results table (per-criterion values, match count, exclusion count, as-of, row links), save control + name input, all save-flow states (anonymous prompt, unverified gate, duplicate-name inline error, pending, error-retry); SCR-004 list, per-row actions (re-run, rename, delete w/ focus-trapping confirmation), dropped-criterion notice, empty + anonymous states. Export to `/design/scr/`. Must NOT touch `/src/**`.
- Traces to: UC-SCR-001, UC-SCR-002, UC-SCR-003 (screens hosting these)
- Acceptance (explicit, no TCs — design work): every IA element and state in `ux/stock-screening.md` (both screens, §3–§5) is represented; duplicate-name and deletion-confirmation patterns match the global dialog components; TR + EN copy direction; narrow-width table degradation; builder approves the export; behavioral decisions flow back through the ux-designer.
- Architecture refs: `01-system-architecture.md` §8.1 (M-3 draft store surfaces the design must account for), §8.3 (UXR-SCR rows)
- UX refs: SCR-003, SCR-004; UXR-SCR-001..019
- Dependencies: TKT-foundation-011
- Parallel group: P-7
