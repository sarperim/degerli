# TKT-foundation-011: Design system foundations (in-repo, web-first — no design tool)

- Status: in-progress
- Decision (user, 2026-10-10): **D-UX-TOOL resolved — no design tool.** Author the design-system foundations directly as in-repo code in `/src/Web` (Tailwind + shadcn/Radix primitives per architecture §5) and verify rendered behavior with chrome-devtools/Playwright. No `/design/**` export required. Resolves the prior blocker via its option (c). Design fidelity is functional-first; visual polish deferred to a later version.
- Size: M
- Scope: in `/src/Web` (NOT a design tool): design tokens (color, spacing, type scale covering TR + EN), core components (buttons, inputs with validation states, semantic tables, badges — stale / not-meaningful / restated / adjusted, focus-trapping dialogs/confirmations, section navigation, loading skeletons, empty/no-data states), the global shell (header with language toggle, auth-state area, stock search, primary navigation), and the informational-only disclaimer component. Delivered as working code + a component index; verified in the running app with chrome-devtools/Playwright. Must NOT touch `/src/Api`, `/src/Ingestion`, or `.github/**`.
- Traces to: UX-lane (visual design layer — post-UX-spec by design; the UX package fixes structure/behavior, this fixes layout/styling/copy direction)
- Acceptance (explicit, no TCs): the tokens/components/shell/disclaimer render in the running web app and are demonstrated via chrome-devtools/Playwright at desktop and narrow widths; covers every global non-screen surface in `00-screen-inventory.md` §3; renders the honest-data marker vocabulary (as-of, stale, restated, adjusted, no-data/preparing) as reusable components; TR + EN typography; builder approves the rendered result. Any behavioral decision discovered during build flows back through the ux-designer into the UX spec — never silently applied in code.
- Architecture refs: `01-system-architecture.md` §5 (UI kit row — Tailwind + shadcn/Radix primitives the design must stay compatible with), §8.1 (M-8)
- UX refs: `00-screen-inventory.md` §3; UXR-G-013, UXR-G-014, UXR-G-016, UXR-G-025, UXR-G-026, UXR-G-027, UXR-G-028
- Dependencies: none
- Parallel group: P-1
