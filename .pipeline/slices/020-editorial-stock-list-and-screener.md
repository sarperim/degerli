---
id: SLICE-020
status: done
scope:
  - .pipeline/slices/020-editorial-stock-list-and-screener.md
  - src/Web/src/routes/StockListPage.tsx
  - src/Web/src/screens/screener/ScreenerScreen.tsx
  - src/Web/src/routes/StockListPage.test.tsx
  - src/Web/src/screens/screener/ScreenerBuilder.test.tsx
  - src/Web/src/screens/screener/ScreenerFlow.test.tsx
  - src/Web/src/screens/screener/ScreenerSave.test.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
---

# SLICE-020 — Stock List (`SCR-002`) & Fundamental Screener (`SCR-003`) Open Editorial Redesign

## Context & Goal

Bring `SCR-002` (Stock List) and `SCR-003` (Fundamental Screener) into the open borderless editorial layout from `/mnt/c/Users/sarpe/Desktop/degerlilatest.html`:

1. **Stock List (`SCR-002` — `StockListPage.tsx`):**
   - Editorial header with mono kicker (`Covered Universe`), display title, and `← Overview` link.
   - Clean horizontal filter bar: mono search input (`max-w-[280px]`) + sector selector/chips (`chip-row`, keeping the accessible `<select>` or synced chips so existing L3/L4 `selectOptions` tests remain 100% green) + active filter readout & clear action (also clearing `?sector=` from URL on reset).
   - Borderless full-width table (`Code`, `Company`, `Sector`, etc.) with bold mono ticker links and subtle row hover (`--surface-hover`).

2. **Fundamental Screener (`SCR-003` — `ScreenerScreen.tsx`):**
   - 2-column open split (`lg:grid-cols-[340px_1fr] gap-10`, no outer boxes):
     - **Left criteria rail:** metric family picker + `+ Add` button, hairline-separated criterion rows (`border-b`) with compact `Min` / `Max` mono inputs + inline unit hint (`%` / `x`) and clear inline validation when `canRun` is false, full-width primary `Run Screen` CTA, and hairline-separated `Save Named Screen` control (showing a clean sign-in prompt for guests instead of letting them submit into a `401`, while preserving `TC-SCR-028` gated-save flow).
     - **Right results ledger:** mono summary bar (`matchCount` / `excludedCount` / `asOf` / `StaleBadge`) above an open borderless results table with `tabular-nums` metric columns.

## Rules & Invariants

- **Preserve all L3/L4 contracts:** `StockListPage.test.tsx` (`stock-row-*`, `active-filters`, `no-match-state`), `ScreenerBuilder.test.tsx`, `ScreenerFlow.test.tsx`, `ScreenerSave.test.tsx`, `core-loop.spec.ts`, and `gated-save-lifecycle.spec.ts` must pass untouched (or with minimal intentional updates).
- **Responsive (`TC-XC-019`):** Collapses cleanly to 1 column on `<1040px` and stays fully operable at `375px`.

## Blockers & Dependencies

- Depends on `SLICE-018`.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/routes/StockListPage.test.tsx src/screens/screener/ src/test/i18nParity.test.ts src/test/adviceVocabulary.test.ts` passes (43 tests across 6 files).
- [ ] Playwright visual check on `/stocks` and `/screener` (dark & light, desktop & mobile).

## Result

- **SCR-002 (`StockListPage.tsx`):** Replaced the boxed filter card with an open editorial header (`kicker`, `h1`, `AsOfDate`, `StaleBadge`, and `← Overview` link), an open horizontal filter toolbar (mono search input `max-w-[280px]`, accessible sector `<select>`, `data-testid="active-filters"` summary, and URL-param-clearing `clearFilters` via `setSearchParams({}, { replace: true })`), and a borderless full-width table with bold mono ticker links, company links, and sector labels.
- **SCR-003 (`ScreenerScreen.tsx`):** Replaced the stacked cards with an open editorial header (mono kicker, `h1`, description, `active-screen` badge, `clear-all` action) and a responsive 2-column open split (`lg:grid-cols-[340px_1fr] gap-10`): left criteria rail (metric selector + `+ Add`, 5-family readout, hairline-separated criterion rows with compact `Min`/`Max` mono inputs and unit indicators, full-width `Run Screen` CTA with inline validation hint, and hairline-separated `Save Named Screen` preserving the anonymous gated-save contract) alongside the right results ledger (mono summary bar with match/excluded counts, `AsOfDate`, `StaleBadge`, and open borderless results table with `tabular-nums` metric columns).
- **i18n (`en.json`, `tr.json`):** Added symmetric `stockList.kicker`, `stockList.backToOverview`, `screener.kicker`, `screener.resultsInitialSummary`, and `screener.runValidationHint` keys.
