---
id: SLICE-034
status: done
scope:
  - .pipeline/slices/034-price-chart-time-range-presets.md
  - src/Web/src/screens/stock-page/valuation.ts
  - src/Web/src/screens/stock-page/ValuationSection.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/screens/stock-page/Valuation.test.tsx
---

# SLICE-034 — Stock Price Chart Time-Range Presets (`1W` · `1M` · `1Y` · `Max`) (`SCR-005`)

## Context & Goal

In `SLICE-032`, the stock price history chart selector resampled daily closes into daily/weekly/monthly buckets. What the user actually intended was **time-window presets** over the served EOD database history (`daily_prices`):

1. **Pure Trailing Window Helper (`valuation.ts`):**
   - Replace `ValuationChartGranularity` (`daily` / `weekly` / `monthly`) and `aggregateValuationHistory` with `ValuationChartRange = '1W' | '1M' | '1Y' | 'max'`, `VALUATION_CHART_RANGES = ['1W', '1M', '1Y', 'max']`, and `filterValuationHistoryByRange(points, range)`.
   - Skip `Today (1D)` for now because our DB only stores 1 snapshot in `intraday_quotes` rather than intraday time-series bars; keep `max` (`Max` / `Tümü`) as default showing the full DB history.
   - Anchor trailing windows strictly to the **latest valid served session date** present in `points` (never wall-clock `Date.now()`):
     - `'max'`: returns all served points unchanged.
     - `'1W'`: trailing 7 calendar days (`anchor - 7 days`, inclusive).
     - `'1M'`: trailing 1 calendar month with month-end day clamping (e.g., `2026-03-31` → `2026-02-28`, inclusive).
     - `'1Y'`: trailing 12 calendar months with leap-day clamping (e.g., `2024-02-29` → `2023-02-28`, inclusive).
   - Never fabricate or interpolate points.

2. **UI & Symmetric i18n (`ValuationSection.tsx`, `en.json`, `tr.json`):**
   - Replace `stockPage.valuation.history.granularity` with `stockPage.valuation.history.range`:
     - EN: `label: "Time range"`, `"1W": "1W"`, `"1M": "1M"`, `"1Y": "1Y"`, `"max": "Max"`
     - TR: `label: "Zaman aralığı"`, `"1W": "1H"`, `"1M": "1A"`, `"1Y": "1Y"`, `"max": "Tümü"`
   - Update `ValuationHistoryChart` in `ValuationSection.tsx` (now mounted in `OverviewSection` via `SLICE-033`) to default to `'max'` and render `data-slot="chart-range-toggle"` with `role="group"`, `aria-pressed`, and `data-magnetic=""`.

3. **Unit & Component Tests (`Valuation.test.tsx`):**
   - Replace `SLICE-032` granularity tests with deterministic `SLICE-034` tests covering `filterValuationHistoryByRange` (`max`, `1W`, `1M`, `1Y`, month-end & leap-day clamping, empty/single/unsorted/malformed dates) and `ValuationHistoryChart` time-range toggle interaction (`<figcaption>` count & `aria-pressed`).

## Rules & Invariants

- **Honest-data contract:** Never fabricate dates, never use `Date.now()`, never mutate or interpolate closes; default `'max'` shows the full stored series cleanly.
- **No `i18n` / `advice` drift:** Replace `granularity` keys symmetrically in both EN and TR catalogs so zero orphan or missing keys remain.

## Blockers & Dependencies

- Depends on `SLICE-032` and `SLICE-033` (both merged into `dev`).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/screens/stock-page/Valuation.test.tsx` passes.

## Result

- Replaced `SLICE-032`'s Daily/Weekly/Monthly bucket resampling with trailing time-range presets (`ValuationChartRange = '1W' | '1M' | '1Y' | 'max'`, `VALUATION_CHART_RANGES`, `filterValuationHistoryByRange`) in `src/Web/src/screens/stock-page/valuation.ts`, anchored strictly to the latest valid served session date in `points` with month-end and leap-day UTC calendar clamping.
- Replaced `stockPage.valuation.history.granularity` with `stockPage.valuation.history.range` (`1H · 1A · 1Y · Tümü` / `1W · 1M · 1Y · Max`) symmetrically in `en.json` and `tr.json`.
- Wired `ValuationHistoryChart` in `ValuationSection.tsx` to default to `'max'` and render `data-slot="chart-range-toggle"`.
- Verified `npm run build`, `npm run lint`, `npm run check:i18n` (890 keys synced), `npm run check:advice`, `Valuation.test.tsx` (`19/19`), and `StockPage.test.tsx` (`16/16`).
