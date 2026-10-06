# Market Overview (Home) Specification — SCR-001

**Domain:** market-overview · **Screen:** SCR-001 · **Prepared:** 2026-10-06
**Traces:** UC-MOV-001, UC-MOV-003, UC-MOV-004; FR-MOV-001–013, 020, 021 (FR-MOV-017 Could) · BR-MOV-001–011
**Global rules applied:** UXR-G-001–029 (esp. G-007/008 as-of & stale, G-012 EOD framing, G-013/014 language, G-016 disclaimer, G-025–028 responsive/a11y)
**Status:** written under the standing no-stop instruction; flagged items (if any) are collected in `99-ux-audit.md`.

## 1. Purpose

The daily "discover" entry point of the core investing loop: let an anonymous user grasp today's BIST market state — index levels, sector performance, breadth, biggest movers, volume, market-wide valuation — and the six macro indicators, in plain bilingual language, and pick the next step (a stock, a sector, the screener). Traces to UC-MOV-001, UC-MOV-003, UC-MOV-004; SC-002.

## 2. Entry / Exit

**Entry:** platform root URL; primary navigation from every screen. Publicly accessible without an account (BR-MOV-007).

**Exit:** any stock page (via gainer/loser entries, UXR-MOV-004); the Stock List filtered to a sector (via sector entries, UXR-MOV-002); the Screener and Stock List (via navigation, UXR-MOV-010); header search; account screens. Nothing on this screen produces state that needs preserving on exit (no user input); the language selection persists globally (UXR-G-014).

## 3. Information Architecture

The dashboard presents these content blocks (structure only; placement is a design decision):

1. **Index levels** — BIST 100 and BIST 30, each with daily change.
2. **Sector performance** — per-sector daily performance for the covered universe; each entry navigates to the Stock List filtered to that sector.
3. **Market breadth** — share of advancing vs. declining stocks in the covered universe.
4. **Top movers** — biggest daily gainers and losers (top 10 each, per the volume-eligibility rule), each entry showing its traded volume and navigating to that stock's page.
5. **Market volume** — total market volume for the covered universe.
6. **Market valuation overview** — market P/E and market dividend yield, with the loss-maker-exclusion disclosure.
7. **Macro strip** — six indicators: TÜİK CPI, independent inflation measure, CBRT 1-week repo rate, USD/TRY, EUR/TRY, gold price. Inflation is always presented as a pair (BR-MOV-002).
8. **Informational-only disclaimer** (UXR-G-016).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-MOV-001 | The dashboard displays BIST 100 and BIST 30 index levels, each with its daily change. | FR-MOV-001 | Must |
| UXR-MOV-002 | The dashboard displays daily sector performance for the covered universe, and each sector entry navigates to the Stock List filtered to that sector. | FR-MOV-002, FR-MOV-012, UC-MOV-004 | Must |
| UXR-MOV-003 | The dashboard displays market breadth (advancing vs. declining share of the covered universe). | FR-MOV-003 | Must |
| UXR-MOV-004 | The dashboard displays the top-10 daily gainers and top-10 daily losers by % price change among stocks meeting the minimum traded-volume eligibility threshold, each entry showing its traded volume, and each entry navigates to that stock's page. | FR-MOV-004, FR-MOV-011, FR-MOV-021, BR-MOV-011 | Must |
| UXR-MOV-005 | The dashboard displays total market volume for the covered universe. | FR-MOV-005 | Must |
| UXR-MOV-006 | The dashboard displays the market-wide valuation overview — market P/E and market dividend yield — together with an on-screen disclosure that loss-making companies are excluded from the market P/E aggregate. | FR-MOV-006, BR-MOV-010, OQ-MOV-004 | Must |
| UXR-MOV-007 | The macro strip displays all six indicators (TÜİK CPI, independent inflation measure, CBRT 1-week repo rate, USD/TRY, EUR/TRY, gold price), each with its value, unit, plain-language label, source attribution, and release/as-of date. | FR-MOV-007, FR-MOV-009, NFR-MOV-004 | Must |
| UXR-MOV-008 | The official and independent inflation measures are always displayed side by side, each with its source label and as-of date; when the independent measure is unavailable, the official figure is shown alone with an explicit note that the independent measure is unavailable. | FR-MOV-008, BR-MOV-002, UC-MOV-003a | Must |
| UXR-MOV-009 | A macro indicator whose series has no data at all shows an explicit unavailable state (label retained, no value) rather than being hidden. | UC-MOV-003a pattern; OQ-MOV-002 mitigation; honest-display principle | Must |
| UXR-MOV-010 | The dashboard displays its equity data-as-of date, and every block or indicator whose data is stale (last-known-good) carries a visible stale marker together with its as-of date. | FR-MOV-010, BR-MOV-001, FR-MDF-016 | Must |
| UXR-MOV-011 | The dashboard provides navigation to the Screener and to the Stock List. | FR-MOV-013, UC-MOV-004 | Must |
| UXR-MOV-012 | The sector performance block offers a selectable time period (e.g., 1W / 1M / YTD). | FR-MOV-017 | Could |

## 5. States

- **Initial / loading:** visible loading indication per UXR-G-001 until blocks render.
- **Populated:** all blocks with values, as-of dates, sources (macro).
- **Stale (per block / per indicator):** last-known-good value + as-of date + stale marker (UXR-MOV-010); the dashboard never silently presents stale data as current (BR-MOV-001).
- **Macro unavailable (per indicator):** explicit unavailable state (UXR-MOV-009); for the independent inflation measure specifically, the paired display degrades to official-only with a note (UXR-MOV-008).
- **Error:** a block whose data fails to load shows a plain-language error for that block with retry (UXR-G-003); other blocks remain usable — a single block failure never blanks the dashboard.
- **Empty:** N/A — market aggregates exist whenever data is served; there is no legitimate zero-data condition for this screen beyond the per-block states above.

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Market overview snapshot (indices, breadth, sectors, movers, volume, market valuation) | Server | Daily (EOD), one per trading day | MDF canonical facts (BR-MOV-009) | Read-only; the dashboard serves the latest completed snapshot; no user mutation exists |
| Macro series values | Server | Per-series cadence (FX/gold daily, CPI monthly, rate per CBRT decision) | This domain's canonical stored values (BR-MOV-008) | Read-only; latest value per series with as-of date |
| Sector performance period selection (1W/1M/YTD) | UI | Session | User selection | Not persisted server-side (Could-level feature) |
| Language | Global | Per device / per account | UXR-G-014 | — |

## 7. Data-heavy surfaces

- **Top movers lists (×2):** fixed top-10 lists — loading (UXR-G-001), error with retry, stale marking, volume shown per entry (UXR-MOV-004). No sorting, filtering, or pagination (fixed lists by definition).
- **Sector performance list:** fixed universe of sectors; loading, error, stale; period selection per UXR-MOV-012 (Could). No pagination.
- **Macro strip:** six fixed indicators with mixed cadences — every value carries its own as-of date and source so monthly and daily data are never misread as equally current (RISK-MOV-002, FR-MOV-009). Unavailable and stale states per §5.
- **Refresh behavior:** data is EOD; the screen never auto-refreshes or implies live data (UXR-G-012). Revisiting the screen serves the latest snapshot available.

## 8. Responsive behavior

All blocks remain present and operable at narrow widths (UXR-G-025); the movers lists and macro strip degrade to compact rows without dropping any entry, value, as-of date, or link; the inflation pair remains side-by-side or stacked but always both visible (BR-MOV-002); navigation targets remain reachable.

## 9. Accessibility

- Movers and sector entries are links whose accessible names include the stock/sector name and the displayed change/value.
- Daily-change direction (up/down) is conveyed by more than color alone (sign, arrow, or text) — applies to index changes, sector performance, and movers.
- Macro values are labeled with unit and as-of date in text (NFR-MOV-004 doubles as labeling).
- Lists use semantic structures; focus order follows reading order; keyboard operability and visible focus per UXR-G-026/027.

## 10. Critical flows

1. **View today's market state (anonymous).** Given the daily EOD refresh has run and macro series are ingested, when an anonymous user opens the root URL, then all blocks render with values, the macro strip shows all six indicators each with source and as-of date, the disclaimer is visible, and the language toggle is available.
2. **Gainer → research.** Given the dashboard is rendered, when the user activates a gainer entry, then that stock's page opens.
3. **Sector → research.** Given the dashboard is rendered, when the user activates a sector entry, then the Stock List opens pre-filtered to that sector.
4. **Independent inflation unavailable.** Given the independent inflation source is unavailable, when the user views the macro strip, then the official CPI shows with its source and as-of date and an explicit note that the independent measure is unavailable — the pair is never silently reduced to one unlabeled figure.
5. **Stale equity data.** Given an equity-source outage, when the user views the dashboard, then the as-of date is shown prominently and affected blocks carry the stale marker (last-known-good values retained).
6. **Language switch preserves context.** Given the user is viewing the dashboard in Turkish, when they toggle to English, then the entire dashboard re-renders in English with the same blocks and scroll position.
