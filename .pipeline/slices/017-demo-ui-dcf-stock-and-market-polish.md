---
id: SLICE-017
status: done
scope:
  - .pipeline/slices/017-demo-ui-dcf-stock-and-market-polish.md
  - src/Web/src/screens/dcf/DcfCalculatorScreen.tsx
  - src/Web/src/screens/stock-page/StockPage.tsx
  - src/Web/src/screens/stock-page/ValuationSection.tsx
  - src/Web/src/screens/stock-page/Valuation.test.tsx
  - src/Web/src/screens/stock-page/StatementsView.tsx
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/components/ui/table.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
---

# SLICE-017 — Passable Demo UI Pass 2 (DCF Layout & Active Cell Highlight, Recharts Dark-Mode/Responsive Fix, Statement Number Alignment, Table Row Headers & Market Overview Scannability)

## Context & Goal

Completes the pre-demo UI polish pass for the core research screens:

1. **DCF Calculator (`DcfCalculatorScreen.tsx`):**
   - Move the live result summary cards (Fair Value, Current Price, Margin of Safety / Verdict) and the 5×5 Sensitivity Table **above** the Scenario Save/Load panel so editing any assumption shows live results immediately above the fold.
   - Visually highlight the active cell (`data-current="true"`) in the 5×5 Sensitivity Table (e.g., `bg-primary/15 font-semibold ring-1 ring-inset ring-primary/40`) so sighted users can immediately locate their current `(discount_rate, terminal_growth)` cell.
   - Show the stock ticker (`upperSymbol`) and a clean back-link (`<Link to={`/stocks/${upperSymbol}`}>`) in the DCF header.
2. **Price History Chart (`ValuationSection.tsx`):**
   - Replace fixed `width={640}` with `<ResponsiveContainer width="100%" height={240}>` so the chart adapts cleanly on both mobile and desktop.
   - Style `XAxis`, `YAxis`, and `<Line>` with theme-aware CSS variables (`var(--muted-foreground)` / `var(--primary)`) so axis ticks and the price line have crisp contrast in dark mode; update `ValuationTooltipContent` to `bg-popover text-popover-foreground`.
3. **Financial Statements alignment (`StatementsView.tsx`) & Table row headers (`table.tsx`):**
   - Right-align numeric `<TableCell>`s (`text-right tabular-nums`) in `StatementTable` so values line up cleanly under the right-aligned period `<TableHead>`s.
   - In `table.tsx`, avoid forcing `text-xs whitespace-nowrap text-muted-foreground` on body row headers (`scope="row"`), keeping column headers (`thead th`) crisp while row headers (`tbody th`) render at readable `text-sm font-medium text-foreground`.
4. **Stock Page DCF CTA (`StockPage.tsx`):**
   - Style the `"Open DCF calculator"` link with `buttonVariants({ variant: 'outline', size: 'sm' })` so it is clearly discoverable without altering its accessible role/text.
5. **Market Overview directional color coding (`MarketOverviewPage.tsx`):**
   - Add semantic directional colors (`text-emerald-600 dark:text-emerald-400` for positive, `text-rose-600 dark:text-rose-400` for negative) alongside the existing `↑`/`↓` arrow and `+/-` sign in `ChangeValue` so gainers, losers, indices, and sector performance are immediately scannable while remaining non-color-only.

## Rules & Invariants

- **Preserve all existing `data-testid`, `data-slot`, `data-current`, and ARIA contracts** used by L3 Vitest and L4 Playwright tests.
- **No Recharts JSDOM crash:** `ResponsiveContainer` in JSDOM can warn/render 0 width unless wrapped safely or tested cleanly — verify `Valuation.test.tsx` passes cleanly!
- **No advice vocabulary** and **zero TypeScript/build errors**.

## Blockers & Dependencies

- Depends on `SLICE-016` (`done`).

## Verification

- [ ] `cd src/Web && npm run build` succeeds cleanly.
- [ ] `cd src/Web && npx vitest run src/screens/dcf/DcfCalculator.test.tsx src/screens/dcf/DcfFlow.test.tsx src/screens/dcf/DcfScenarios.test.tsx src/screens/stock-page/Valuation.test.tsx src/screens/stock-page/StatementsView.test.tsx src/screens/stock-page/StockPage.test.tsx src/routes/MarketOverviewPage.test.tsx` passes.
- [ ] Playwright visual check confirms improved header height, dark mode contrast, single DCF disclaimer, and DCF result placement.
