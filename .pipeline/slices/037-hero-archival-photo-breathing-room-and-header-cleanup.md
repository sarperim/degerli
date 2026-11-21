---
id: SLICE-037
status: done
scope:
  - .pipeline/slices/037-hero-archival-photo-breathing-room-and-header-cleanup.md
  - src/Web/src/assets/istanbul-financial-archive.webp
  - src/Web/src/screens/market-overview/HeroInteractivePlate.tsx
  - src/Web/src/screens/market-overview/HeroInteractivePlate.test.tsx
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/components/AuthState.tsx
  - src/Web/src/components/AuthState.test.tsx
  - src/Web/src/components/AuthState.signedIn.test.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
  - tests/e2e/src/core-loop.spec.ts
  - tests/e2e/src/market-overview.spec.ts
  - tests/e2e/src/sign-in.spec.ts
  - tests/e2e/src/account-settings.spec.ts
  - tests/e2e/src/stock-list.spec.ts
---

# SLICE-037 — Salt Galata Hero Plate, Fold Breathing Room & Header Auth Cleanup

## Context & Goal

Deliver three polish improvements on the Market Overview (`SCR-001`) hero and global header:

1. **Default Bayer 4×4 Archival Hero Image & Salt Galata Caption (`HeroInteractivePlate.tsx`):**
   - Bundle the uploaded Salt Galata archival engraving (`src/Web/src/assets/istanbul-financial-archive.webp`, `960×540` WebP) and load it by default through the existing Bayer 4×4 ordered-dither pipeline, falling back to the procedural skyline only while loading or when image decoding is unavailable (e.g., jsdom/offline) and keeping `Swap photo` overrides intact.
   - Replace the generic `FIG. 01 — İSTANBUL FINANCIAL ARCHIVE` caption (`marketOverview.hero.plateCaption`) with Salt Galata context (`"Salt Galata — Former Ottoman Bank HQ (1892), Bankalar Caddesi"` / `"Salt Galata — Eski Osmanlı Bankası (1892), Bankalar Caddesi"`), update the HUD coordinates to Salt Galata (`41.0239° N, 28.9735° E`), and allow the caption bar to wrap cleanly alongside `shrink-0` `Swap photo`.
   - Add a guarded `ResizeObserver` inside `useHeroBackdropCanvas` so the sweeping 64-session BIST 100 backdrop curve tracks hero height changes cleanly.

2. **Full-Viewport Hero Breathing Room (`MarketOverviewPage.tsx`):**
   - Give the full-bleed hero + 4-column KPI ribbon container `lg:min-h-[calc(100svh-55px)] flex flex-col` with `<header id="hero-section">` vertically centered (`flex flex-1 items-center py-14 sm:py-16 lg:pt-20 lg:pb-16`) so the hero copy, Bayer plate, and 4-column KPI ribbon cleanly frame the initial desktop fold.
   - Expand Band 01 (`Market valuation` + `Macro indicators`) vertical padding to `py-16 lg:py-20` so below-the-fold headings are never half-clipped at the bottom of desktop viewports.

3. **De-clutter Header Auth Area (`AuthState.tsx`, catalogs, tests):**
   - Remove the redundant `"Guest"` / `"Ziyaretçi"` (`header.anonymous`) text label beside `Sign in` and `Register`.
   - Remove the now-unused `header.anonymous` key from `en.json` and `tr.json` to preserve i18n parity cleanly.
   - Update unit (`AuthState.test.tsx`, `AuthState.signedIn.test.tsx`) and Playwright E2E specs (`core-loop`, `market-overview`, `sign-in`, `account-settings`, `stock-list`) to assert the anonymous header `Sign in` button (`page.getByRole('banner')`), plus add `HeroInteractivePlate.test.tsx`.

## Rules & Invariants

- **Honest-data & no-advice contracts:** No live API calls in tests; no fabricated financial copy or advice vocabulary.
- **i18n parity gate:** `npm run check:i18n` must pass with 100% TR/EN parity and zero unused catalog keys.
- **Accessibility & E2E scoping:** Header anonymous assertions in Playwright scope to `page.getByRole('banner')` so `exact: true` never collides with in-page `"Sign in"` buttons.

## Blockers & Dependencies

- None (builds cleanly on `dev` at `8a35ab7`).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes (`tsc --noEmit && vite build`, `0` lint errors, `889` TR/EN keys in sync).
- [x] `cd src/Web && npx vitest run src/components/AuthState.test.tsx src/components/AuthState.signedIn.test.tsx src/screens/market-overview/HeroInteractivePlate.test.tsx src/routes/MarketOverviewPage.test.tsx` passes (`4/4` files, `40/40` tests).
- [x] `cd tests/e2e && npx tsc --noEmit` passes.
- [x] `rg -n "header\.anonymous" src/Web tests/e2e` returns zero matches.
- [ ] CI/E2E Playwright suite passes 100%.

## Result

- Bundled `src/Web/src/assets/istanbul-financial-archive.webp` (`960×540`, 184 KB WebP) and wired `HeroInteractivePlate.tsx` to dither it by default through the Bayer 4×4 duotone matrix with unmount cancellation, center-cover scaling, procedural fallback while loading/offline, `Swap photo` override support, Salt Galata coordinates (`41.0239° N, 28.9735° E`), wrappable caption bar, and a guarded `ResizeObserver` on the hero backdrop canvas.
- Updated `en.json` and `tr.json` `marketOverview.hero.plateCaption` to explain Salt Galata (Former Ottoman Bank HQ, 1892, Bankalar Caddesi) and removed the unused `header.anonymous` key (`889` keys in sync).
- Framed `MarketOverviewPage.tsx` hero + 4-column KPI ribbon within `lg:min-h-[calc(100svh-55px)]` and expanded Band 01 padding (`py-16 lg:py-20`) so below-the-fold sections sit cleanly outside the initial desktop viewport.
- Removed the redundant `"Guest"` / `"Ziyaretçi"` label from `AuthState.tsx` and updated Vitest + Playwright E2E specs (`core-loop`, `market-overview`, `sign-in`, `account-settings`, `stock-list`) plus added `HeroInteractivePlate.test.tsx` (`4/4` Vitest files, `40/40` tests passing; `tests/e2e` TypeScript clean).
