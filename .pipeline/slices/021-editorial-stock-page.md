---
id: SLICE-021
status: done
scope:
  - .pipeline/slices/021-editorial-stock-page.md
  - src/Web/src/screens/stock-page/StockPage.tsx
  - src/Web/src/screens/stock-page/StockPriceHeader.tsx
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/stock-page/ValuationSection.tsx
  - src/Web/src/screens/stock-page/StatementsView.tsx
  - src/Web/src/screens/stock-page/KeyMetricsPanel.tsx
  - src/Web/src/screens/stock-page/StockPage.test.tsx
  - src/Web/src/screens/stock-page/Valuation.test.tsx
  - src/Web/src/screens/stock-page/StatementsView.test.tsx
  - src/Web/src/screens/stock-page/KeyMetricsPanel.test.tsx
  - src/Web/src/components/ui/section-nav.tsx
  - src/Web/src/screens/price-state/PriceStateHeader.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
---

# SLICE-021 — Stock Page (`SCR-005`) Sticky Rail, Bullet Gauges, Open Growth Matrix & Editorial Statements

## Context & Goal

Transform `SCR-005` (`StockPage` and its section components) into the open editorial research workspace from `/mnt/c/Users/sarpe/Desktop/degerlilatest.html`:

1. **Editorial Stock Hero (`StockPage.tsx`, `StockPriceHeader.tsx`):**
   - Full-width bottom-ruled header (`border-b border-strong`) showing sector/ticker mono kicker, real company identity (`THYAO — Türk Hava Yolları A.O.`) as the prominent title (eliminating duplicate ticker display), large `48px` mono price + directional change + `StaleBadge`, and compact right-aligned action bar (`← Stock List`, `KAP Reports` jump, primary `DCF Valuation →` CTA).

2. **Sticky 200px Numbered Section Rail (`StockPage.tsx`):**
   - 2-column layout (`lg:grid-cols-[200px_1fr] gap-12`) with sticky numbered left rail (`01 Overview` … `08 KAP Disclosures`, bronze left accent bar on active section, collapsing to a horizontal tab strip on `<1040px`).
   - Open borderless section panels (`panel-top` with hairline divider, mono `asOf` / `StaleBadge` / `RestatedMarker`) instead of heavy nested boxes.

3. **Data-Rich Editorial Visuals (`ValuationSection.tsx`, `StockSections.tsx`, `StatementsView.tsx`, `KeyMetricsPanel.tsx`):**
   - **Valuation:** horizontal bullet-track gauges comparing stock multiples (`P/E`, `P/B`, `EV/EBITDA`, etc.) against sector median marker (`2px` vertical median tick), plus Recharts price-history chart styled with bronze `#C68B49` / `#9B6123` stroke, subtle area fill, and mono axes.
   - **Growth:** redesigned open **Growth Matrix** (metric rows × `3Y / 5Y / 10Y` columns with inline proportion bars and honest coverage notes).
   - **Financials & Key Metrics:** crisp borderless tables with `row-group` bronze section headers, `row-total` strong top borders, and `tabular-nums` right-aligned values.
   - **KAP Disclosures:** clean open ledger with accented `FR` financial-report code tags and direct links.

## Rules & Invariants

- **Honest-data & no-advice:** Never fabricate missing multiples, growth horizons, or Enterprise Value figures; preserve all `RestatedMarker`, `NotMeaningful`, and `no-data` coverage notices.
- **Preserve L3/L4 selectors:** All tests in `src/Web/src/screens/stock-page/*.test.tsx`, `stock-page.spec.ts`, `metric-visibility.spec.ts`, and `research-handoff.spec.ts` must remain green.

## Blockers & Dependencies

- Depends on `SLICE-018`.

## Verification

- [x] `cd src/Web && npm run build` passes — ran.
- [x] `cd src/Web && npx vitest run src/screens/stock-page/` passes — ran together with
  `src/screens/price-state/` and `src/test/` (117 tests, all green).
- [ ] Playwright visual check on `/stocks/THYAO` (dark & light, desktop & mobile) — not run
  in this environment.

## Result

Implemented the incremental editorial stock page without changing the scroll-navigation
model (all seven sections stay stacked in the DOM; `SectionNav` keeps scrolling to
`stock-section-<id>`):

- **Hero:** bottom-ruled header (`border-b border-[var(--border-strong)] pb-6`); the
  `screens.stockPage.title` `<h1>` remains the single level-1 heading as a mono kicker,
  the real `StockIdentity` is the dominant identity (`text-3xl sm:text-4xl`), and the
  served price renders large (`font-mono text-4xl sm:text-5xl` via a new optional
  `valueClassName` on the shared `PriceStateHeader`, default unchanged elsewhere).
  Actions are the outline `← Stock List` link to `/stocks` (new
  `stockPage.backToStockList` key in both catalogs) and the default-variant
  `stockPage.dcfHandoff` CTA. `stock-page-identity-anchor` and all price-state test ids
  are preserved; the ticker stays on the price header because `StockPriceState` /
  `StockMovementBlock` assert `price-state-symbol`.
- **Section nav:** sticky below the 54px app header on desktop
  (`lg:sticky lg:top-[54px] z-30 bg-background/90 backdrop-blur`), bronze active
  underline via `border-[var(--brand-accent)]`; roles/labels/`aria-current` unchanged.
- **Open section panels:** `SectionShell` is now `border-t border-border pt-6` (no boxed
  card); `scroll-mt-32`; data attributes, heading ids and markers preserved.
- **Valuation:** bullet-track gauges added only where both the stock multiple and sector
  median are non-null (h-1.5 track, bronze fill, 0.5-wide foreground median tick,
  `aria-hidden`); the numeric rows, not-meaningful and no-median states are unchanged.
- **Tables:** statements and key-metrics group header rows are mono uppercase in
  `text-[var(--stale-foreground)]`; cell values remain `tabular-nums` on hairline rows.

Verification run: `npm run build`, `npm run lint`, `npm run check:i18n`,
`npm run check:advice`, and
`npx vitest run src/screens/stock-page/ src/screens/price-state/ src/test/` — all pass
(15 files, 117 tests). No backend, other screens or global CSS touched; no commit/push.
