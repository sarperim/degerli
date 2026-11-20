---
id: SLICE-026
status: done
scope:
  - .pipeline/slices/026-stock-page-sticky-rail-charts-and-growth-matrix.md
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/screens/stock-page/StockPage.tsx
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/stock-page/ValuationSection.tsx
  - src/Web/src/components/ui/section-nav.tsx
---

# SLICE-026 — Full-Bleed Market Overview Bands (`SCR-001`) + Stock Page (`SCR-005`) Sticky Left Numbered Rail (`01`..`07`), Visual SVG Charts & Open Growth Matrix (`geminisonuiux.html`)

## Context & Goal

1. **Full-Bleed Viewport Hero, KPI Ribbon & Band Borders on Market Overview (`SCR-001` — `MarketOverviewPage.tsx`):**
   - In `geminisonuiux.html`, `.hero`, `.hero-ribbon`, and `.band` are full-bleed across the entire viewport width (`100vw`) with inner `.container` (`max-w-content mx-auto px-4 sm:px-8`) centering the content, so `#hero-bg-canvas` (the 64-session BIST 100 curve), the `.hero-ribbon` surface background and top/bottom rules, and each `.band`'s bottom hairline divider continue edge-to-edge to the left and right sides of the screen without gaps.
   - Apply full-bleed breakout (`relative left-1/2 right-1/2 -ml-[50vw] -mr-[50vw] w-screen`) to the hero header (`#hero-section`), the `.hero-ribbon` surface strip, and the horizontal bands (`Band 01`, `Band 02`, `Band 03`) on `MarketOverviewPage.tsx` while keeping their inner content constrained to `mx-auto w-full max-w-content px-4 sm:px-8`.

2. **2-Column Stock Layout (`lg:grid-cols-[200px_1fr] gap-12 items-start`) with Sticky Left Numbered Rail (`01`..`07`):**
   - On desktop (`lg+`), `SectionNav` renders as a sticky vertical left rail (`lg:sticky lg:top-[76px] flex lg:flex-col`) with numbered buttons (`01 Overview`, `02 Valuation`, `03 Financials`, `04 Profitability`, `05 Growth`, `06 Balance Sheet`, `07 Dividends`).
   - Active button carries `border-l-2 border-[var(--brand-accent)] bg-[var(--accent-soft)] font-semibold text-foreground` with bronze index number (`01`), collapsing cleanly to a horizontal scrollable strip on `<1040px`.
   - **DOM Invariant:** All 7 section panels remain mounted in the right column so every L3/L4 test (`StockPage.test.tsx`, `stock-page.spec.ts`, `metric-visibility.spec.ts`, `research-handoff.spec.ts`, `core-loop.spec.ts`) can query any section without clicking tabs.
2. **02 Valuation (`ValuationSection.tsx`):**
   - 5-column `.bullet-grid` (`grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-6`) showing each valuation multiple (`24px` mono readout), `6px` `.bullet-track` with bronze `.bullet-fill` and vertical `2px` ink `.bullet-median` marker, and `Median X · N peers` sub-caption above the Historical Price / Multiple SVG chart.
3. **03 Financials (`FinancialsSection` in `StockSections.tsx`):**
   - Adds the editorial **Revenue · EBITDA · Net Income Grouped Bar Chart SVG** (`.viz-block`) above the multi-period financial statements table when period figures are present, using real served period data (`revenue`, `ebitda`, `netIncome`) — never fabricated numbers.
4. **04 Profitability (`ProfitabilitySection` in `StockSections.tsx`):**
   - Renders proportional horizontal metric bars (`.infl-bar-row`: `140px` label, `8px` track with bronze fill for `ROIC` and secondary ink fill for `ROE`, `Gross Margin`, `Operating Margin`, plus right-aligned mono percentage) above the metrics summary.
5. **05 Compounding Growth (`GrowthSection` in `StockSections.tsx`):**
   - **Compounding Windows vs. Accounting Timeline SVG** (`.viz-block`) visualizing the `3Y`, `5Y`, and `10Y` horizons (`2016 → 2026`) relative to the `2023-12-31 TMS 29` regime boundary.
   - **Open Growth Matrix Table (`.growth-matrix`):** Full-width borderless matrix with columns `Metric`, `3Y`, `5Y`, `10Y`. Each cell renders the `22px` mono CAGR value (`data-slot="growth-window"` preserved on each window cell so existing `StockPage.test.tsx` selectors pass!), a `3px` proportional progress bar (`.growth-bar-track` / `.growth-bar-fill`), and the actual window / coverage note (`data-slot="actual-window"`).
6. **06 Balance Sheet & 07 Dividends (`BalanceSheetSection` & `DividendsSection` in `StockSections.tsx`):**
   - Balance Sheet highlights `Book Value` and `Book Value / Share` in a top 2-column or 4-column `26px` mono readout strip above the canonical balance sheet lines.
   - Dividends adds an **Annual Cash DPS SVG Bar Chart** (`.viz-block`) derived from the served `sectionData.dividends` rows above the dividend events table.

## Rules & Invariants

- **Honest-data contract:** All SVG bars and charts must be computed strictly from the API-served figures (`sectionData.periods`, `sectionData.metrics`, `sectionData.dividends`) — never hardcoded dummy values.
- **Test contract preservation:** Keep `data-section="<id>"`, `id="stock-section-<id>"`, `data-slot="growth-window"`, `data-slot="actual-window"`, `data-slot="no-value"`, `data-line="<code>"`, `dividends-never-paid`, and `dividends-unavailable`.

## Blockers & Dependencies

- Depends on `SLICE-024`.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/routes/MarketOverviewPage.test.tsx src/screens/stock-page/` passes (5 files / 75 tests).

## Result

- Updated `MarketOverviewPage.tsx` so the hero (`#hero-section` + `#hero-bg-canvas`), 4-column `.hero-ribbon`, and Bands 01–03 break out to `100vw` full-bleed while constraining inner content to `max-w-content`.
- Upgraded `SectionNav` (`section-nav.tsx`) with `orientation="rail"` and `aria-hidden="true"` numbered indices (`01`..`07`), wired into `StockPage.tsx`'s 2-column sticky left rail layout (`lg:grid-cols-[200px_1fr]`) with all 7 section panels mounted.
- Upgraded `ValuationSection.tsx` to a 5-column `.bullet-grid` with `24px` mono readouts and inline bullet tracks.
- Upgraded `StockSections.tsx` with FY-only grouped Revenue · EBITDA · Net Income SVG bars in Financials, proportional horizontal fill bars in Profitability, 2016–2026 TMS 29 Compounding Windows Timeline SVG + open Growth Matrix (`data-slot="growth-window"` / `data-slot="actual-window"` preserved) in Growth, `24px` mono Book Value summary strip in Balance Sheet, and Annual Cash DPS SVG bar chart in Dividends.
