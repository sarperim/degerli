# TKT-res-001: Design — SCR-002 Stock List & SCR-005 Stock Page (visual design)

- Status: todo
- Size: L
- Scope: visual design for SCR-002 and SCR-005 in the chosen design tool (D-UX-TOOL): the Stock List (search input, sector filter, list rows, no-match state, pre-filtered arrival); the Stock Page as **one screen with seven content sections** (Gate 1 decision A — the tab/accordion/scroll widget is a design decision made here): Overview & Business Description, Valuation (incl. vs.-sector strip + historical chart), Financials, Profitability, Growth, Balance Sheet, Dividends; all honest-data markers (as-of, stale badge, restatement asterisk + footnote + warning, adjusted-data disclaimer, no-data with coverage boundary, preparing state); DCF hand-off. All §5 states for both screens. Export to `/design/res/`. Must NOT touch `/src/**`.
- Traces to: UC-RES-001, UC-RES-002, UC-RES-003, UC-RES-005 (screens hosting these)
- Acceptance (explicit, no TCs — design work): every section and state in `ux/stock-research.md` (both screens, §3–§9) is represented; the vs.-sector strip design shows value + median + peer count + exclusion disclosure + not-meaningful state; tables degrade at narrow width without losing figures or markers; TR + EN copy direction; builder approves the export; behavioral decisions flow back through the ux-designer.
- Architecture refs: `01-system-architecture.md` §5 (charts row — historical valuation series), §8.1 (M-5 per-section independence), §8.3 (UXR-RES rows)
- UX refs: SCR-002, SCR-005; UXR-RES-001..025
- Dependencies: TKT-foundation-011
- Parallel group: P-7
