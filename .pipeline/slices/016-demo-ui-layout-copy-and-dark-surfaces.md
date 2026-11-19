---
id: SLICE-016
status: done
scope:
  - .pipeline/slices/016-demo-ui-layout-copy-and-dark-surfaces.md
  - src/Web/src/components/Header.tsx
  - src/Web/src/components/StockSearch.tsx
  - src/Web/src/components/AuthState.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/routes/MacroStrip.tsx
  - src/Web/src/routes/StockListPage.tsx
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/screener/ScreenerScreen.tsx
  - src/Web/src/screens/dcf/DcfCalculatorScreen.tsx
---

# SLICE-016 — Passable Demo UI Pass 1 (Header Layout, Natural Turkish Copy, Dark-Mode Card Surfaces, Keyboard Search & Duplicate Disclaimer Fix)

## Context & Goal

Playwright screenshots (`1440×900` light/dark and `390×844` mobile) and a frontend audit identified immediate visual and UX issues that make the current UI look rough for a demo:

1. **Header wraps into 3 rows on desktop (`163px` tall) and 4 rows on mobile (`267px` tall):**
   - `Header.tsx` uses a tight `max-w-5xl` container with `flex-wrap` and an unsized `StockSearch` input, causing Brand + Nav on row 1, Search + Theme + Language floating in the middle on row 2, and AuthState dropping to row 3 even at `1440px` width.
   - `AuthState.tsx` uses `window.location.assign('/sign-in')` and `window.location.assign('/register')`, causing full page reloads. *(Note: replacing `window.location.assign` in `AuthState.tsx` is intentionally deferred to preserve routerless test compatibility in `AuthState.test.tsx`.)*
   - `StockSearch.tsx` lacks keyboard navigation (`ArrowDown`/`ArrowUp`/`Enter`/`Escape`), uses `bg-background` for its dropdown instead of `bg-popover`, and does not truncate long company names.
2. **Unnatural / machine-translated Turkish copy (`src/Web/src/i18n/catalogs/tr.json`):**
   - `"stale": "Bayat"` (food-spoilage word) → `"Güncel değil"`.
   - `"asOfExplanation": "As-of tarihi, verinin tanımladığı zaman noktasıdır."` → `"Veri tarihi, değerin ait olduğu zaman noktasını gösterir."`.
   - `"exclusion": "Kayıp açıklayan {{count}} şirket..."` → `"Zarar açıklayan {{count}} şirket..."`.
3. **Dark mode cards blend into the page background:**
   - `tokens.css` defines distinct `--card` (`oklch(0.205 0 0)`) and `--popover` surfaces in `.dark`, but cards across `MarketOverviewPage`, `MacroStrip`, `StockListPage`, `StockSections`, `ScreenerScreen`, and `DcfCalculatorScreen` use `bg-background` instead of `bg-card text-card-foreground`.
4. **Duplicate legal disclaimer on DCF Calculator:**
   - Playwright showed two identical disclaimer boxes stacked at the bottom of `/stocks/:symbol/dcf` because both `DcfCalculatorScreen` and `AppShell` render `<Disclaimer />`.

## Rules & Invariants

- **Honest-data & no-advice invariants:** Never emit investment-advice terms (`al`/`sat`/`tut`, `buy`/`sell`/`hold`); preserve all `data-testid` / `data-slot` / ARIA contracts used by L3 Vitest and L4 Playwright suites.
- **i18n parity:** Keep `tr.json` and `en.json` keys 100% in sync (`i18nParity.test.ts` must pass).
- **L3 Vitest verification:** Run targeted Vitest tests (`npx vitest run ...`) and `npm run build` in `src/Web` to verify zero TypeScript errors and zero test regressions.

## Blockers & Dependencies

- Depends on `SLICE-010` (dark mode tokens).

## Verification

- [ ] `cd src/Web && npm run build` succeeds cleanly.
- [ ] `cd src/Web && npx vitest run src/test/i18nParity.test.ts src/test/adviceVocabulary.test.ts src/components/StockSearch.test.tsx src/components/AuthState.test.tsx src/components/AuthState.signedIn.test.tsx src/routes/MarketOverviewPage.test.tsx src/routes/MacroStrip.test.tsx src/routes/StockListPage.test.tsx src/screens/dcf/DcfCalculator.test.tsx` passes.
