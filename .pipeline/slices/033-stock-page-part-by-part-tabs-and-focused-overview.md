---
id: SLICE-033
status: done
scope:
  - .pipeline/slices/033-stock-page-part-by-part-tabs-and-focused-overview.md
  - src/Web/src/store/draftStore.ts
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/screens/stock-page/StockPage.tsx
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/stock-page/ValuationSection.tsx
  - src/Web/src/screens/stock-page/StatementsView.tsx
  - src/Web/src/screens/stock-page/testSupport.tsx
  - src/Web/src/screens/stock-page/StockPage.test.tsx
  - src/Web/src/screens/stock-page/Valuation.test.tsx
  - src/Web/src/screens/stock-page/SectorComparison.test.tsx
  - src/Web/src/screens/stock-page/FinancialsPeriodLabel.test.tsx
  - src/Web/src/screens/stock-page/Dividends.test.tsx
  - src/Web/src/screens/stock-page/SectionMarkers.test.tsx
  - src/Web/src/screens/stock-page/StatementsView.test.tsx
  - src/Web/src/screens/stock-page/KeyMetricsPanel.test.tsx
  - tests/e2e/src/stock-page.spec.ts
  - tests/e2e/src/research-handoff.spec.ts
  - tests/e2e/src/core-loop.spec.ts
  - tests/e2e/src/cross-surface-equality.spec.ts
---

# SLICE-033 — Stock Page (`SCR-005`) Part-by-Part Tabbed Workspace (`01`..`08`), Price Chart in Overview, Focused Financials & Dedicated `08 KAP Reports`

## Context & Goal

Align `SCR-005` (`StockPage.tsx`, `StockSections.tsx`, `ValuationSection.tsx`, `StatementsView.tsx`) cleanly with `geminisonuiux.html` without redundant repetition:

1. **Part-by-Part Tabbed Rail (`01`..`08`) — No Continuous Wall:**
   - Sticky left rail (`SectionNav` with `orientation="rail"`) shows:
     - `01` Overview
     - `02` Valuation
     - `03` Financials
     - `04` Profitability
     - `05` Growth
     - `06` Balance Sheet
     - `07` Dividends
     - `08` KAP Reports (`kapReports`)
   - Render **only** the active section panel in the right column (matching `geminisonuiux.html`'s `.stock-panel.active` tabbed layout) and preserve active tab selection in `useDraftStore` across navigation away and back (`TC-RES-027` / `TC-RES-033`).
2. **Zero-Repetition Section Responsibilities:**
   - **`01 Overview` (`OverviewSection`):** Moves the **Historical Price Chart** (with its Daily / Weekly / Monthly granularity toggle from `SLICE-032`) here beside the company business description + `"Open KAP Reports ↗"` jump action.
   - **`02 Valuation` (`ValuationSection`):** Focuses strictly on the canonical valuation multiples & 5-column vs.-sector bullet strip (price chart moved to Overview).
   - **`03 Financials` (`FinancialsSection`):** Simplified, attention-first executive view showing the **FY Grouped Bar Chart SVG** + **5-line Executive Summary Table** (`Revenue`, `EBITDA`, `EBIT`, `Net Income`, `FCF`) + `"Full KAP Reports ↗"` jump button (no multi-hundred-row KAP statement dump).
   - **`04`..`07` (`Profitability`, `Growth`, `Balance Sheet`, `Dividends`):** Each focuses purely on its own visual & canonical figures.
   - **`08 KAP Reports` (`KapReportsSection`):** Dedicated home for comprehensive multi-period KAP financial statements (`StatementsView` with `Income (IS)` / `Balance sheet (BS)` / `Cash flow (CF)` statement filter sub-tabs, `Annual / Quarterly` basis toggle, `[C]` derived rows, filing badges, EV header, and `KeyMetricsPanel` + Capital Tracker).
3. **Test Alignment:**
   - Update `testSupport.tsx` (`section` option), Stock Page Vitest suites, and the 4 Playwright E2E specs (`stock-page.spec.ts`, `research-handoff.spec.ts`, `core-loop.spec.ts`, `cross-surface-equality.spec.ts`) to navigate via the numbered rail buttons / active tab state.

## Rules & Invariants

- **Honest-data contract:** Zero fabricated numbers, zero advice vocabulary; preserve all markers (`asOf`, `stale`, `restated`, `adjusted`), `no-data` / `N/M` states, and section independence (`TC-RES-023`).
- **Accessibility:** Rail indices (`01`..`08`) carry `aria-hidden="true"`, active rail button has `aria-current="true"`, jump buttons use distinct copy (`"Open KAP Reports ↗"`, `"Full KAP Reports ↗"`) so button accessible names never collide.

## Blockers & Dependencies

- Depends on `SLICE-031` and `SLICE-032` (both merged into `dev`).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/screens/stock-page/` passes.
- [ ] CI/E2E Playwright suite passes 100%.

## Result

- Converted `SCR-005` (`StockPage.tsx`) to an exclusive part-by-part tabbed rail (`01` Overview through `08` KAP Reports via `StockPageSection` + `useDraftStore`).
- Moved `ValuationHistoryChart` (and conditional `<AdjustedNotice />`) into `OverviewSection` alongside the `"Open KAP Reports ↗"` jump action, keeping `ValuationSection` focused on canonical multiples and the 5-col vs.-sector strip.
- Focused `FinancialsSection` on the FY grouped bar chart SVG, 5-figure summary table (`FinancialsSummaryTable`), layout notice, and `"Full KAP Reports ↗"` jump button.
- Created `KapReportsSection` (`08`) housing `<StatementsView />` (with `All / IS / BS / CF` filter sub-tabs in `StatementsView.tsx` + `Annual / Quarterly` basis toggle) and `<KeyMetricsPanel />`.
- Local verification: `npm run build` (`tsc --noEmit && vite build`), `npm run lint`, `npm run check:i18n` (889 TR/EN keys synced), `npm run check:advice`, `npx vitest run src/screens/stock-page/` (`11/11` files, `110/110` tests), and `cd tests/e2e && npx tsc --noEmit` all passed.
