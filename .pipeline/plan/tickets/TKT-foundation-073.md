---
scope:
  - src/Web/src/components/honest-data/markers.tsx
  - src/Web/src/components/honest-data/markers.test.tsx
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/routes/MarketOverviewPage.test.tsx
  - src/Web/src/routes/MacroStrip.tsx
  - src/Web/src/routes/MacroStrip.test.tsx
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/stock-page/SectionMarkers.test.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - .pipeline/ux/01-interaction-rules.md
---

# TKT-foundation-073: Stale-"why" extensions — the explanatory freshness copy reaches every surface, not just SCR-001

- Status: todo
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 9 unparked).**
- **Evidence (gathered 2026-10-24):**
  - TKT-foundation-039 landed the pattern on SCR-001: `honestData.staleExplanation` (StaleBadge `title` + accessible name), `screens.marketOverview.asOfExplanation`, and macro `marketOverview.macro.cadence.{monthly,perRelease,daily}` — all in `tr.json`/`en.json`.
  - Round-1 review DR-1 made the claims **conditional**: the recency copy is suppressed while `stale` is true, so a stale badge never claims "freshest completed session". DR-2 made `SERIES_CADENCE` a `Partial<Record<...>>` so an unmapped series renders no cadence note rather than a wrong one.
  - Coverage is still partial: stock-page sections render `SectionMarkers` (as-of + stale) with **no explanation**; screener results and saved-screen lists show `asOf`/`stale` without a "why"; the dividends and financials sections explain neither never-paid/coverage nor restatement; an unmapped macro series renders nothing at all.
  - The user's recurring confusion is an **expectation gap**, not arithmetic: the platform is EOD-by-design, so every truthful flag must explain itself on every surface, not only the home screen.
- **Pinned approach:**
  - Extend the 039 pattern (programmatically determinable explanation + catalog copy + **suppression when the opposite claim would be false**) to: stock-page `SectionMarkers` (per-section as-of/stale/coverage/restated), screener result freshness, dividends never-paid/coverage, and any remaining `stale`/`asOf` render.
  - Keep the honesty rules: a fresh badge never says "last-known-good"; a stale badge never claims recency; an unmapped/unknown state renders no invented note.
  - **The surface list and final wording are the ux-designer's** (recorded flow-back). This ticket pins the **behaviour** — explanation present, programmatic, suppressed when contradictory — not the final prose.
- **Acceptance (explicit criteria — screen-alignment style):**
  - **(i)** Every stale/as-of marker on the listed surfaces carries an accessible explanation; tests assert the catalog copy in TR/EN and both suppression cases (fresh vs stale).
  - **(ii)** No surface can render a badge without an explanation — one regression test per surface.
  - **(iii)** i18n parity, `tsc --noEmit`, ESLint and the Web unit suite green; additive copy only, no layout change.
  - **(iv)** The UX spec entries are recorded (SCR-005 / UXR-RES markers; SCR-002 freshness) by the ux-designer, or a named routing item is opened — no architecture change.
- Traces to: FR-MOV-010/016 (as-of + stale marking — comprehension half), FR-MOV-020 (EOD-nature disclosure), FR-RES-017 (honest marking), NFR-MOV-001; 039 heritage.
- Architecture refs: none (Web copy/semantics layer; the served flags are unchanged).
- UX refs: SCR-001 (existing), SCR-002 (screener freshness), SCR-005 (section markers); UXR-MOV-008/009/010, UXR-RES-019/021, UXR-SCR-0xx — the ux-designer confirms the exact entries.
- Dependencies: none (039 landed).
- Parallel group: none (shares the i18n catalogs with 072).
