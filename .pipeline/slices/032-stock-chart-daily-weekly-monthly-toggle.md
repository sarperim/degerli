---
id: SLICE-032
status: done
scope:
  - .pipeline/slices/032-stock-chart-daily-weekly-monthly-toggle.md
  - src/Web/src/screens/stock-page/valuation.ts
  - src/Web/src/screens/stock-page/ValuationSection.tsx
  - src/Web/src/screens/stock-page/Valuation.test.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
---

# SLICE-032 — Stock Price Chart Daily / Weekly / Monthly Granularity Toggle (`SCR-005`)

## Context & Goal

Currently the historical price chart in `ValuationSection.tsx` plots all raw daily points with no granularity selector.

1. **Pure Aggregation Helper (`valuation.ts`):**
   - Add `aggregateValuationHistory(points, granularity: 'daily' | 'weekly' | 'monthly')`:
     - `'daily'` (default): returns all served points unchanged.
     - `'weekly'`: groups by ISO week (`YYYY-Www`) and selects the last served trading session in each week.
     - `'monthly'`: groups by calendar month (`YYYY-MM`) and selects the last served trading session in each month.
   - Honest-data rule: strictly selects real served close dates/values — never interpolates or fabricates missing dates.
2. **Editorial Segmented Toggle (`ValuationSection.tsx`):**
   - Add a `Günlük / Haftalık / Aylık` (`Daily / Weekly / Monthly`) segmented control (`data-magnetic=""`, `aria-pressed`) in the chart header above the Recharts line chart.
   - Preserve all existing chart basis disclosures (`adjusted` / `raw`), hover tooltip (`ValuationTooltipContent`), and SVG styling.
3. **Symmetric i18n & Unit Tests:**
   - Add `stockPage.valuation.history.granularity.{label, daily, weekly, monthly}` to `en.json` and `tr.json`.
   - Add deterministic Vitest tests verifying daily, weekly, and monthly bucket selection and toggle interaction.

## Rules & Invariants

- Default granularity is `'daily'` so existing assertions remain deterministic.
- Never fabricate missing sessions or alter served close values.

## Blockers & Dependencies

- None.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/screens/stock-page/Valuation.test.tsx` passes (19/19 tests).

## Result

- Added `ValuationChartGranularity`, `VALUATION_CHART_GRANULARITIES`, and `aggregateValuationHistory` to `src/Web/src/screens/stock-page/valuation.ts` using ISO 8601 week (`YYYY-Www`) and calendar month (`YYYY-MM`) buckets selecting the last served trading session without interpolation.
- Added symmetric EN/TR i18n keys (`stockPage.valuation.history.granularity.{label, daily, weekly, monthly}`).
- Wired the Daily / Weekly / Monthly segmented toggle (`data-slot="chart-granularity-toggle"`, `aria-pressed`, `data-magnetic=""`) into `ValuationHistoryChart` in `ValuationSection.tsx`.
- Added 4 unit & component tests in `src/Web/src/screens/stock-page/Valuation.test.tsx`.
