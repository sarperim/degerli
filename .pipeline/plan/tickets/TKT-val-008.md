# TKT-val-008: SCR-011 My DCF Scenarios UI (Should)

- Status: in-review — https://github.com/sarperim/degerli/pull/58
- Size: M
- Scope: `/src/Web` SCR-011 route per the TKT-val-001 design: signed-in scenarios grouped by stock, activating one opens that stock's calculator loaded with it, rename in place with duplicate inline error, delete behind focus-trapping confirmation, anonymous sign-in path, empty guidance state, invalidation of the calculator picker (M-2). **Should-level screen (SCR-011) — deferral is the builder's call, recorded on the ticket.**
- Traces to: UC-VAL-002; FR-ACC-005 (entry point); Gate 1 decision C (rename/delete as Should)
- Acceptance: TC-VAL-022, TC-VAL-027 — TDD: specs first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-2), §8.3 (UXR-VAL-016..020 row), §8.4; `03-api-design.md` §5
- UX refs: SCR-011; UXR-VAL-016, UXR-VAL-017, UXR-VAL-018, UXR-VAL-019, UXR-VAL-020; UXR-G-005, UXR-G-022, UXR-G-029
- Dependencies: TKT-val-007
- Parallel group: P-17

## Notes

- Implemented the Should-level screen (no deferral).
- Recorded interpretations:
  - **Open hand-off:** SCR-011's per-row Open navigates to
    `/stocks/{symbol}/dcf?scenario={id}`; the SCR-006 calculator consumes the
    `scenario` query param and, once the per-stock picker
    (`GET /me/scenarios?symbol=`) serves that id, seeds its params exactly and
    recomputes through the canonical compute endpoint (FR-VAL-010, UXR-VAL-016).
    Chosen over seeding the M-3 draft store because `03` §5 defines no per-id GET
    and the param survives a direct open/refresh; it is applied once per arrival.
  - **Grouping:** client-side by symbol with groups ordered by symbol for a
    stable display (`03` §5: grouping client-side).
  - **Invalidation (§8.4, M-2):** rename updates `['me','scenarios']` in place
    from the mutation response and invalidates `['scenarios', symbol]`; delete
    removes the row from `['me','scenarios']` and invalidates
    `['scenarios', symbol]` — so the calculator's picker reflects the change
    without a manual reload.
  - No cross-link from the SCR-006 picker to SCR-011 was added (not in this
    ticket's acceptance TCs); SCR-011 stays reachable from global navigation and
    the SCR-010 "My saved work" entry point.
- Files: new `src/Web/src/screens/dcfScenarios/` (screen + L3/L4 tests + harness);
  `scenariosApi.ts` gains `useAllScenarios` / `useRenameScenario` /
  `useDeleteScenario`; `DcfCalculatorScreen.tsx` gains the `?scenario=` open
  hand-off; route wired in `App.tsx`; `dcfScenarios.*` i18n keys in TR/EN.
