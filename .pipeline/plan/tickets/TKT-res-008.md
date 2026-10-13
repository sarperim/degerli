# TKT-res-008: SCR-005 Stock Page UI — core sections & honest-data markers

- Status: done — merged to `dev` 2026-10-13 (PR https://github.com/sarperim/degerli/pull/54, merge commit 322c8fe). Forward-merged `dev` after the TKT-foundation-013 unblock, re-gated E2E (TC-RES-031 both halves pass), then review loop round 2 MERGEABLE (blocker B1 "never-paid dividends rendered for all missing-data states" fixed; code/compliance/security approve; CI green). Artifacts: `.pipeline/plan/reviews/TKT-res-008-round-1.md`, `-round-2.md`.
- Size: L
- Scope: `/src/Web` SCR-005 route per the TKT-res-001 design: one screen, seven navigable sections with the current section indicated and preserved across navigation; Overview (bilingual description, last-reviewed date, preparing state), Financials, Profitability, Growth (window indications, no-data with boundary), Balance Sheet, Dividends (never-paid state); honest-data marker components (stale badge, restatement asterisk + bottom footnote + warning, adjusted disclaimer placeholder, no-data w/ coverage); per-section independent load/fail/retry (M-5); DCF hand-off link; disclaimer. Must NOT implement the Valuation section UI (TKT-res-009).
- Traces to: FR-RES-005, FR-RES-008..012, FR-RES-014..016, FR-RES-026 (UI side); UC-RES-002, UC-RES-003 (main + alternates); UXR-RES-008..011, 014..022
- Acceptance: TC-RES-023, TC-RES-024, TC-RES-026, TC-RES-027, TC-RES-029, TC-RES-030, TC-RES-031, TC-RES-033 — TDD: specs first, then green; specs in `.pipeline/testing/stock-research.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-1, M-3, M-5), §8.3 (UXR-RES-008..025 row); `03-api-design.md` §3
- UX refs: SCR-005; UXR-RES-008, UXR-RES-009, UXR-RES-010, UXR-RES-011, UXR-RES-014, UXR-RES-015, UXR-RES-016, UXR-RES-018, UXR-RES-019, UXR-RES-021, UXR-RES-022; UXR-G-006..009, UXR-G-021
- Dependencies: TKT-foundation-011, TKT-res-004, TKT-foundation-009, TKT-foundation-010, TKT-foundation-013 (unblock — rebase + re-gate after it lands)
- Parallel group: P-15

## Notes

- Note: TC-RES-028 — the screener→stock-page→DCF research hand-off e2e — lands in TKT-int-002 once the DCF route exists.
- **RESOLVED (TC-RES-031, former upstream blocker):** the L4 `compose.ci` fixture seed previously never ran the metrics job, so `derived_metrics` was empty and `GET /stocks/ZETA/growth` returned `state:"no-data"`. Unblock TKT-foundation-013 (merge 839e041) now seeds the derived data at the fixture anchor (`fixtures/Degerli.Fixtures/FixtureDerivedData.cs`), so ZETA serves the 1-year actual-window CAGRs and the ZETA half of TC-RES-031 passes. Re-gate run 2026-10-14 against the seeded compose.ci stack.
- PR: https://github.com/sarperim/degerli/pull/54 — all `/src/Web` work complete; typecheck/lint/i18n/build green; 208 web unit tests green; full e2e **28/28 green** (TC-RES-029/030/031/033 inclusive). Forward-merge of `dev` resolved additive conflicts in `src/Web/src/lib/api/contracts.ts` (kept both the SCR-005 and the admin/DCF type blocks) and `src/Web/src/lib/format.ts` (kept `formatDecimal` + a single superset `formatPercent`; the stock-page call sites pass an explicit `2` fraction-digit cap to preserve their prior rendering).
