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

- Status: in-review
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

## Notes

- PR: https://github.com/sarperim/degerli/pull/170 (TKT-foundation-073, base `dev`).
- Implementation note (2026-10-30): the pattern is extended **through the shared primitives**, not per page — `AsOfDate` (in the audited `markers.tsx`) now carries a catalog explanation, so every surface that renders an as-of marker inherits it, including the screener result list, the stock list, the DCF price marker and the price header, which are outside this ticket's file list and needed no edit. Two sentences, deliberately separate: the neutral one ("the as-of date is the point in time the data describes") is true in every state and always rendered (`title` + visually-hidden text); the recency one ("the latest date the source has published for this data") is a claim the caller licenses via a new `fresh` prop — absent when freshness is unknown, on a stale row (039 DR-1) and wherever an intraday price stands in for the completed close. SCR-001 passes `fresh` under exactly the two conditions it already used for its visible paragraph; the macro strip passes `fresh={!series.stale}`; **SCR-005 section markers inherit the neutral explanation only**, because `SectionMarkers.tsx` is not in this ticket's audited scope and stock-section freshness is not a session-recency fact — threading `stale` into `SectionMarkers` for a fresh-side claim is a recorded follow-up, not a defect (no claim beats a false one). Dividends' absence claim now states the period it is made over (`stockPage.dividends.neverPaidCoverage`) beside the existing coverage boundary. Three additive catalog keys per language; no existing copy or layout changed.
- **Red→green, measured (the red run, not remembered):** with the implementation absent the four touched suites read **17 failed / 59 passed** (every failure a missing catalog key or an unexplained marker — behaviour, not environment), and **76/76 green** with it; the full Web suite is **59 files / 411 tests** (394 before this branch, +17). `tsc --noEmit`, ESLint, `check:i18n` (846 keys, was 843) and `check:advice` all clean. `tests/e2e` reads `[data-slot="as-of-date"]` by visibility only, so the added visually-hidden copy cannot move those specs.
- Acceptance (iv), recorded as routed rather than invented: `.pipeline/ux/01-interaction-rules.md` §1.4 gains an interim-copy note and the flow-back item **UX-073-1** (owner: ux-designer) for the final wording and the per-surface inventory (SCR-005 markers, SCR-002 screener freshness, dividends wording). No new UXR IDs are created here — allocating them is the ux-designer's, per the 057 precedent. No architecture change; the landed §13.1 max is **v1.5.86** (071), so this ticket adds no row.
- Flagged, not hidden: (1) the primitive change intentionally reaches out-of-list surfaces (item above); (2) `ScreenerScreen.tsx` is untouched because it is outside the audited `scope:` — it inherits the explanation through `AsOfDate`; (3) an unmapped macro series is not rendered by the strip at all (its six-code set is contract-fixed) and an unmapped cadence renders no cadence note — the ticket's "renders nothing at all" gap is closable only to that extent without an API/UX decision.
