---
id: SLICE-027
status: done
scope:
  - .pipeline/slices/027-stock-list-chips-dcf-range-bar-and-admin-grids.md
  - src/Web/src/lib/api/contracts.ts
  - src/Web/src/routes/StockListPage.tsx
  - src/Web/src/screens/dcf/DcfCalculatorScreen.tsx
  - src/Web/src/screens/dcf/DcfCalculator.test.tsx
  - src/Web/src/screens/accountSettings/AccountSettingsScreen.tsx
  - src/Web/src/screens/admin/AdminScreen.tsx
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
---

# SLICE-027 — Stock List Sector Chips (`SCR-002`), DCF Range Bar & Financial-Sector State (`SCR-006`), and Account/Admin Grids (`SCR-010`, `SCR-012`)

## Context & Goal

Complete the final `geminisonuiux.html` screen fidelity details and fix the reported blank DCF page on financial-sector stocks:

1. **Stock List (`SCR-002` — `StockListPage.tsx`):**
   - Add an interactive horizontal `.chip-row` of sector filter pills (`All` + each distinct sector from `sectorOptions`, with `data-magnetic=""` and `aria-pressed`) synchronized with the existing `<select id="stock-list-sector">` and URL `?sector=` parameter so existing L3/L4 `selectOptions` tests continue to pass untouched.
2. **DCF Calculator (`SCR-006` — `DcfCalculatorScreen.tsx` & `contracts.ts`):**
   - **Bug fix (financial-sector empty page):** `GET /api/v1/stocks/{symbol}/dcf` returns `200` with `state: "sector_not_applicable"` and no `baseline` for financial-sector stocks (banks, insurers, brokerages like `GARAN`, `AKBNK`, `ISCTR`, `YKBNK`). Update `DcfBaselineResponse` in `contracts.ts` to include `'sector_not_applicable'` and `reason?: string`, and render an explicit editorial explanation state (`data-testid="dcf-sector-not-applicable"`) with the current EOD price (when served) and links back to the stock page (P/B, ROE, peer multiples) and `/stocks`. Also reset `seeded`/`fields` cleanly when `upperSymbol` changes.
   - **EOD Price vs. Sensitivity Range gauge (`data-slot="dcf-range-gauge"`):** Above the 5×5 `SensitivityTable`, compute `bear` (min computable fair value) and `bull` (max computable fair value) strictly from `result.sensitivity.fairValues`, and render the horizontal range track with bronze band (`--accent-soft` / `--brand-accent`), `2px` bronze Implied Fair Value tick, and `3px` ink EOD Price tick, plus mono `Bear` · `EOD · Implied` · `Bull` legend.
   - **Editorial KPI typography:** `44px` (`text-[2.75rem] leading-none`) mono readout on `Implied Fair Value` and `28px` (`text-[1.75rem] leading-none`) mono readout on `Current Price`.
3. **Account Settings (`SCR-010` — `AccountSettingsScreen.tsx`) & Admin (`SCR-012` — `AdminScreen.tsx`):**
   - Arrange Account Settings into the open 2-column `.grid-2` layout (`max-w-5xl`, `lg:grid-cols-2 gap-10 items-start`: Left: Language Preference, Saved Work Entry Points, Verification Status; Right: Password Change, Account Deletion) while preserving all 5 `data-settings-section` sections and 2 `<input>` elements.
   - Style Admin (`SCR-012`) header and section navigation cleanly with `data-magnetic=""` support.

## Rules & Invariants

- **Honest-data contract:** All range gauge bounds (`Bear`/`Bull`) come strictly from `result.sensitivity.fairValues` — never fabricated. Zero investment-advice vocabulary in TR/EN catalogs.
- **Preserve all L3/L4 contracts:** `StockListPage.test.tsx`, `DcfCalculator.test.tsx`, `DcfFlow.test.tsx`, `DcfScenarios.test.tsx`, `AccountSettingsScreen.test.tsx`, `AdminScreen.test.tsx`, and all Playwright E2E specs.

## Blockers & Dependencies

- Depends on `SLICE-024`–`SLICE-026` (all merged).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/routes/StockListPage.test.tsx src/screens/dcf/ src/screens/accountSettings/ src/screens/admin/` passes (14 files / 96 tests).

## Result

- Added interactive `.chip-row` sector pills (`data-magnetic=""`, `aria-pressed`) in `StockListPage.tsx` synced with `<select id="stock-list-sector">`.
- Fixed the blank DCF page on financial-sector stocks (`state: "sector_not_applicable"`) in `DcfCalculatorScreen.tsx` & `contracts.ts` with an explicit editorial explanation, served EOD price, links back to the stock page and `/stocks`, symbol-change draft reset, and Vitest coverage in `DcfCalculator.test.tsx`.
- Added the `EOD price vs. sensitivity range` visual gauge (`SensitivityRangeGauge`, computed strictly from `result.sensitivity.fairValues`) and `44px`/`28px` mono KPI typography to `DcfCalculatorScreen.tsx`.
- Upgraded `AccountSettingsScreen.tsx` to an open 2-column `.grid-2` layout (`max-w-5xl`, `lg:grid-cols-2`) and `AdminScreen.tsx` to numbered editorial `SectionNav` (`01`..`05`).
- Added symmetric EN/TR i18n keys (`dcf.sectorNotApplicable.*`, `dcf.range.*`) with zero advice vocabulary.
