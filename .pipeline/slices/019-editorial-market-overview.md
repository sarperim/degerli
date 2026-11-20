---
id: SLICE-019
status: done
scope:
  - .pipeline/slices/019-editorial-market-overview.md
  - src/Web/src/index.css
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/routes/MacroStrip.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/routes/MarketOverviewPage.test.tsx
  - src/Web/src/routes/MacroStrip.test.tsx
---

# SLICE-019 — Market Overview Editorial Redesign (`SCR-001`)

## Context & Goal

Redesign `SCR-001` (`MarketOverviewPage.tsx` and `MacroStrip.tsx`) from the uniform 2×2 boxed card grid into the borderless editorial engineering-paper bands from `/mnt/c/Users/sarpe/Desktop/degerlilatest.html`:

1. **Editorial Hero + 4-Column KPI Ribbon:**
   - Top hero section with mono eyebrow (`BIST 100 · EOD · asOf` + `StaleBadge`), strong display `<h1>`, concise lead copy, and primary/secondary CTAs (`Screen BIST 100` → `/screener`, `Browse Stock List` → `/stocks`), plus a subtle responsive SVG/canvas market-curve backdrop (disabled under `prefers-reduced-motion`).
   - Full-width 4-column **KPI Ribbon** (`border-t` hairline, `--surface` band) showing:
     - `BIST 100 · XU100` (large mono value + directional `%` change)
     - `BIST 30 · XU030` (mono value + directional `%` change)
     - `Breadth` (advancing / declining / unchanged + 4px stacked green/muted/red breadth bar)
     - `Covered Volume` (compact formatted volume)

2. **Band 01 — Market Valuation + Paired Inflation Bars & Macro Strip:**
   - 2-column open split (`0.36fr / 0.64fr`):
     - Left: large `46px` mono `Market P/E` (with loss-maker exclusion note) + hairline divider + `Market Dividend Yield`.
     - Right: **Annual Inflation visual comparison bars** (Official TÜİK vs Independent ENAG horizontal tracks with bronze accent on Independent + spread readout when both available) above a 4-column compact macro row (`CBRT Repo`, `USD/TRY`, `EUR/TRY`, `Gold`) with honest source, `asOf`, and per-series stale/unavailable handling.

3. **Band 02 — Diverging Sector Return Bars:**
   - 2-column open split (`0.29fr / 0.71fr`): left kicker, heading, timeframe segmented toggle (`1G | 1H | 1A | YBB`), and `asOf`; right interactive diverging horizontal bar list (positive bars extend right from zero line in `--pos`, negative bars extend left in `--neg`), clicking any row navigates to `/stocks?sector=...`.

4. **Band 03 — Open Borderless Movers Ledgers:**
   - 2-column open tables (`Top Gainers` with `2px solid --pos` rule, `Top Losers` with `2px solid --neg` rule), tabular numerals, volume formatting, and clickable ticker links to `/stocks/:symbol`.

## Rules & Invariants

- **Honest-data contract:** Preserve per-block independent loading/error/retry (`TC-MOV-025`), `PriceStateHeader`, `AsOfDate`, `StaleBadge`, unavailable `INDEP_CPI` degradation (`TC-MOV-018`), and zero fabricated numbers.
- **Test contract parity:** Keep all existing `data-testid`s (`market-block-macro`, `macro-TUIK_CPI`, `macro-INDEP_CPI`, `macro-source-*`, etc.) and `MarketOverviewPage.test.tsx` / `MacroStrip.test.tsx` / `market-overview.spec.ts` / `market-macro.spec.ts` passing.

## Blockers & Dependencies

- Depends on `SLICE-018`.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/routes/MarketOverviewPage.test.tsx src/routes/MacroStrip.test.tsx src/test/i18nParity.test.ts src/test/adviceVocabulary.test.ts` passes (4 files, 61 tests).
- [x] Playwright verification on `/` (`1280×400`, `768×800`, `375×800` against `vite preview`) confirms 0 axe-core violations, `0px` scroll shift on TR↔EN toggle at `1280×400`, and responsive KPI ribbon borders.

## Result

- Implemented the borderless editorial layout for `SCR-001` (`MarketOverviewPage.tsx` and `MacroStrip.tsx`) with editorial hero + 4-column KPI ribbon, Market Valuation + paired inflation tracks + 4-column macro row, diverging sector return bars, and open movers ledgers while preserving all honest-data invariants and `data-testid` contracts.
- Resolved Review Round 1 findings on PR #195:
  - **CI-1:** Removed `aria-label` from `sector-entry-*` `<Link>` elements in `MarketOverviewPage.tsx` (and unused `marketOverview.blocks.sectors.linkLabel` from `tr.json`/`en.json`) so visible text matches the accessible name (`label-content-name-mismatch` clean).
  - **CI-2:** Added `overflow-anchor: none` on `body` in `src/Web/src/index.css` so Chromium viewport scroll anchoring does not shift `window.scrollY` when TR/EN hero copy changes line wrap height (`0px` shift at `1280×400`).
  - **DR-1:** Added `marketOverview.hero.eyebrowIntraday` in `tr.json`/`en.json` and switched the hero eyebrow away from `EOD` / `GÜN SONU` when `envelope?.priceState?.state === 'intraday'`.
  - **DR-2:** Replaced `divide-y sm:divide-y-0 sm:divide-x` on the KPI ribbon with explicit responsive borders on `IndexLevelsBlock` and `BreadthBlock`.
  - **DR-3 / DR-4:** Added unit tests for proportional/empty breadth bar, diverging positive/negative sector bars, intraday eyebrow, and omitted inflation spread when `INDEP_CPI` is unavailable; updated this slice doc.
