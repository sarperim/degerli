---
id: SLICE-025
status: done
scope:
  - .pipeline/slices/025-hero-curve-scrubber-bayer-plate-and-movers.md
  - src/Web/src/screens/market-overview/HeroInteractivePlate.tsx
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
---

# SLICE-025 — Market Overview (`SCR-001`): Interactive BIST 100 Hero Curve Scrubber, Bayer 4×4 Dithered Archival Plate & Volume-Depth Movers (`geminisonuiux.html`)

## Context & Goal

Upgrade `SCR-001` (`MarketOverviewPage.tsx`) to match the full visual and interactive craft of `/mnt/c/Users/sarpe/Documents/degerli/geminisonuiux.html`:

1. **Full-Bleed Sweeping Hero with Interactive 64-Session Curve Scrubber (`#hero-bg-canvas`):**
   - Full-bleed hero section (`border-b border-border overflow-hidden`) with `<canvas id="hero-bg-canvas" aria-hidden="true">` drawing the 64-session BIST 100 curve (`--curve-stroke`, `--curve-fill`).
   - Moving the cursor across the hero draws dashed crosshairs (`--crosshair`), a `7×7px` bronze point marker (`--brand-accent`), and updates the live `.hero-scrub-hint` mono readout (`+ Move cursor across the backdrop to scrub the 64-session BIST 100 EOD curve` → `XU100 · YYYY-MM-DD · 10,418.60`).
2. **2-Column Hero Layout (`1.20fr / 0.80fr`) with Bayer 4×4 Ordered-Dithered Archival Plate (`HeroInteractivePlate.tsx`):**
   - Right column renders the `.photo-plate-wrap` (`border border-[var(--border-strong)] bg-card p-2.5`) with 4 corner `+` crop marks (`tl`, `tr`, `bl`, `br`).
   - Inside `.photo-viewport` (`aspect-[4/3] border border-border bg-background cursor-crosshair`):
     - `<canvas width="360" height="270" aria-hidden="true">` rendering the procedural İstanbul Bosphorus Bridge & financial skyline silhouette processed through the real-time 4-level **Bayer 4×4 ordered dithering** algorithm in the active theme's duotone obsidian/bronze/paper palette (re-rendering automatically when theme toggles between dark and light, or when the user uploads a custom image via `Swap photo`).
     - Radial bronze `.photo-loupe` following the cursor + 3D perspective tilt (`perspective(900px) rotateX(...) rotateY(...)`) on mousemove.
     - `.photo-hud` overlay (`41.0812° N, 29.0112° E` and `DUOTONE · BAYER 4×4`).
   - `.photo-caption-bar` (`FIG. 01 — İSTANBUL FINANCIAL ARCHIVE` + `Swap photo` file input trigger).
3. **Surface Hero Ribbon (`.hero-ribbon`):**
   - Anchored at the bottom of the hero (`border-t border-border bg-card`), housing the 4-column KPI strip (`BIST 100 · XU100`, `BIST 30 · XU030`, `BREADTH`, `COVERED VOLUME`) with `4px` tri-segment breadth bar (`--pos` / `--ink-muted` / `--neg`).
4. **Diverging Sector Bars & Volume-Depth Movers Ledgers:**
   - Sector rows carry `data-magnetic` / `data-sector-bar-row`, `32%` zero-axis line (`.div-zero`), and green/red diverging bars.
   - Movers ledgers (`Top 10 Gainers` with `2px solid var(--pos)` rule, `Top 10 Losers` with `2px solid var(--neg)` rule) include a proportional `2px` bottom `.vol-depth` bar (`(entry.volume / maxVolume) * 76%`) in the volume cell and deterministic 10-point SVG trend sparkline.

## Rules & Invariants

- **Preserve all L3/L4 contracts:** `MarketOverviewPage.test.tsx`, `market-macro.spec.ts` (including `TC-MOV-023` scroll stability at `1280×400` — ensure hero copy/eyebrow heights and `overflow-anchor: none` prevent any scroll shift on TR↔EN toggle), and `global-ux-a11y.spec.ts` (`TC-XC-017` axe-core).
- **Accessible file input:** The `Swap photo` `<input type="file">` inside `HeroInteractivePlate.tsx` must have an explicit accessible label (`aria-label`) so axe-core reports zero form-label violations.

## Blockers & Dependencies

- Depends on `SLICE-024`.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/routes/MarketOverviewPage.test.tsx src/routes/MacroStrip.test.tsx src/test/` passes (5 files / 65 tests including axe-core).

## Result

- Added `HeroInteractivePlate.tsx` with the 64-session interactive BIST 100 EOD backdrop curve scrubber (`#hero-bg-canvas` with dashed crosshairs, bronze point marker, and live `XU100 · YYYY-MM-DD · 10,418.60` scrub readout) and the right-hand Archival Photo Plate (`#photo-plate` with `+` corner crop marks, 3D perspective tilt, radial bronze loupe, HUD overlay, real-time 4-level Bayer 4×4 ordered-dithered İstanbul Bosphorus & financial skyline canvas, and `FIG. 01 — İSTANBUL FINANCIAL ARCHIVE` caption bar with accessible `Swap photo` upload).
- Updated `MarketOverviewPage.tsx` (`SCR-001`) to the 2-column hero (`1.20fr / 0.80fr`), added 10D SVG trend sparklines and proportional `2px` `.vol-depth` bars on the Gainers & Losers ledgers, and wired magnetic reticle hooks on sector rows and mover rows.
