---
id: SLICE-019
status: done
scope:
  - .pipeline/slices/019-editorial-market-overview.md
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

- [ ] `cd src/Web && npm run build` passes.
- [ ] `cd src/Web && npx vitest run src/routes/MarketOverviewPage.test.tsx src/routes/MacroStrip.test.tsx src/test/i18nParity.test.ts src/test/adviceVocabulary.test.ts` passes.
- [ ] Playwright visual check on `/` (dark & light, desktop & `375px` mobile).
