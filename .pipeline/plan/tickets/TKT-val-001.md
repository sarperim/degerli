# TKT-val-001: Design — SCR-006 DCF Calculator & SCR-011 My DCF Scenarios (visual design)

- Status: todo
- Size: M
- Scope: visual design for SCR-006 and SCR-011 in the chosen design tool (D-UX-TOOL): the calculator (stock context with price + as-of, the 8-parameter panel with canonical-fact default labels and restatement markings, results with the plain-language verdict framed "based on your assumptions", sensitivity grid, scenario save/load picker, disclaimer) and the scenarios list (grouped by stock, per-row open/rename/delete with confirmation, empty + anonymous states). All §5 states incl. missing-inputs, validation-error with last-valid-result retained, unsaved-changes indication. Export to `/design/val/`. Must NOT touch `/src/**`.
- Traces to: UC-VAL-001, UC-VAL-002 (screens hosting these)
- Acceptance (explicit, no TCs — design work): every element and state in `ux/valuation-dcf.md` (both screens, §3–§9) is represented; the verdict is designed as text (never color-only); all 8 parameters visibly editable with units; sensitivity grid degrades at narrow width; TR + EN copy direction incl. the verdict template; builder approves the export; behavioral decisions flow back through the ux-designer.
- Architecture refs: `01-system-architecture.md` §8.1 (M-6 debounced compute — design shows instant-feedback posture), §8.3 (UXR-VAL rows); `03-api-design.md` §5 (payloads the design lays out)
- UX refs: SCR-006, SCR-011; UXR-VAL-001..021
- Dependencies: TKT-foundation-011
- Parallel group: P-7
