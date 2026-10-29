---
scope:
  - src/Web/src/screens/stock-page/
  - src/Web/src/lib/api/contracts.ts
  - src/Web/src/lib/format.ts
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/test/msw/handlers.ts
  - tests/e2e/src/stock-page.spec.ts
  - tests/e2e/src/research-handoff.spec.ts
  - .pipeline/testing/stock-research.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-095: The SCR-005 financial-statements view — the user's v0 spec rendered on the stock page, replacing the 5-metric summary (controls bar, EV header, full IS/BS/CF, period toggle)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/159 (base `dev`)
- Size: M
- **Priority: P1 — slice 3 of the 2026-10-28 financial-statements view wave, and its PRIMARY TICKET: the user's spec lives here verbatim. 093 (decode) and 094 (serving) are its supply halves; 096/097 extend it with the metrics panel and capital tracker.**

## The user's v0 spec (verbatim intent, supplied 2026-10-28 — the binding requirement)

A full financial-statements view on the stock page (SCR-005 surface), replacing today's 5-metric summary (Dönem/Hasılat/FAVÖK/FVÖK/Net kâr/SNA):

**GLOBAL CONTROLS & FILING METADATA BAR** — Period toggle [Yıllık (Annual) | Çeyreklik (cumulative quarterly)]; status badges [Konsolide/Solo] [TMS 29 Enflasyon Düzeltmeli] [KAP Kaynak]. (Basis toggle is v0.1 — see exclusions.)

**MARKET & ENTERPRISE VALUE HEADER** — Share Price │ Split-Adj. Shares │ Market Cap │ + Gross Debt │ − Cash & ST Investments │ + Non-Controlling Interest │ = Enterprise Value.

**INCOME STATEMENT (Gelir Tablosu)** — [R] Revenue, Revenue Growth Y/Y % [C], COGS, Gross Profit, Gross Margin % [C], R&D, SG&A, Other Op. Inc/Exp Net, Operating Profit (EBIT), Operating Margin % [C], EBITDA (FAVÖK) [C], EBITDA Margin % [C], Investing Inc/Exp Net, Financing Inc/Exp Net, Net Monetary Pos. G/L (TMS 29), Pretax Income, Tax Expense, Effective Tax Rate % [C], Net Income (Total), Net Income (Parent), Net Margin (Parent) % [C].

**BALANCE SHEET (Finansal Durum Tablosu)** — CURRENT ASSETS: Cash & Equivalents, ST Financial Investments, Trade Receivables (A/R), Inventories, Other Current (OCA), Total Current Assets. NON-CURRENT: Net PP&E, Right-of-Use (TFRS 16), Goodwill & Intangibles, Other Non-Current (ONCA), Total Assets. LIABILITIES: ST Debt + Current LT Debt, ST Lease Liabilities, Trade Payables (A/P), Deferred Revenue (D/R), Other Current Liab. (ONCL), Total Current Liab., Long-Term Debt, LT Lease Liabilities, Other Non-Curr. Liab., Total Liabilities. EQUITY: Paid-In Capital, TMS 29 Capital Adj., Retained Earnings, Equity (Parent), Non-Controlling Int., Total Equity. DERIVED: Net Working Capital [C], Gross Debt [C], Net Debt [C], Tangible Book Value [C], BVPS & TBV/Share [C].

**CASH FLOW STATEMENT (Nakit Akış Tablosu)** — Net Income, D&A, Impairments & Provisions, Monetary Gain/Loss Adj., Working Capital Changes, Operating Cash Flow (CFO), CapEx, Free Cash Flow [C], FCF Margin % [C], FCF/Share [C], Asset Sale Proceeds, Cash Acquisitions (M&A), Rights Issue Cash (Bedelli), Share Buybacks, Dividends Paid, Cash Interest Paid, Net Debt Issued/(Repaid).

**CAPITAL & DILUTION TRACKER** — Paid-In Capital (Ödenmiş), Split-Adjusted Share Count [C], Share Count Change Y/Y % [C], Bedelli/Bedelsiz % History, Dividend Payout Ratio % [C]. *(rendered by TKT-foundation-097; the spec row set is recorded here for completeness)*

**KEY VALUE METRICS & MOAT INDICATORS (Current vs. 5Y Historical Median + Sector Median)** — ROIC %, CROIC %, ROE (Parent) %, ROA %; FCF/Net Income (Annual & 3Y Avg), CFO/EBIT, Monetary Gain/Pretax % (TMS 29 distortion alert); EV/EBIT, EV/EBITDA, P/E, P/FCF, FCF Yield %, P/B, P/TBV, Div %; Net Debt/EBITDA, Net Debt/FCF (N/M if FCF ≤ 0), EBIT/Interest Exp., CFO/Cash Interest, Current Ratio; 3Y & 5Y CAGR (Revenue, EBIT, EPS Parent, FCF/Share, BVPS). *(served by TKT-foundation-096, rendered by 097)*

**EXPLICIT EXCLUSIONS (user-approved "minus 5 items" — do NOT include, record as routed v0.1+):**

1. **USD basis** — no FX source ingested; Reel TL (TÜFE) basis also deferred to v0.1 (CPI is ingested, derivation is not).
2. **Standalone 3M quarters + TTM** — v0.1; interims stay cumulative-displayed (the 090 finding: main column IS cumulative; spans are stored per line).
3. **Gross Debt incl. Leases comprehensive resolution** — DR-2 (current-portion-of-LT-borrowings aggregation, the 25.6% TÜPRAŞ understatement) is a FENCED USER DECISION from 088's review; this slice consumes ST_DEBT/LT_DEBT as stored, never widens. Same for DR-1 (TOTAL_EQUITY NCI-inclusive stays as stored).
4. **Full per-share/EV coverage via the share-count slice** — paid-in-capital-derived counts for the ~80 withheld companies is a separate follow-up; v0 computes per-share rows only where counts exist, honest "—" elsewhere.
5. **10Y CAGR** — impossible on ~3.5 years of history; 5Y is marginal (honest N/M where insufficient).

Also: **financial-sector companies (banks/insurance) are out of v0's row set** — the layout is industrial-shaped; bank-format labels (Net Faiz Geliri etc.) are a separate later slice. v0 must present them honestly (bank layout notice from 094's payload) rather than half-filled industrial tables. **The Konsolide/Solo badge is IN v0 (USER-CONFIRMED 2026-10-28)** — from 093/094's capture-evidenced filing flag.

## Implementation

- **What to render:** the global controls & filing metadata bar, the market & EV header, and the three full statement tables (IS/BS/CF), consuming TKT-foundation-094's extended `/financials` payload. **[R] rows bind to the served `lines`; [C] rows bind to the served `derived` values — the page computes nothing (BR-RES-004, SCR-005 §6: no client-side margins, ratios, or EV arithmetic).** A row whose served value is null renders the honest "—"/no-data state (UXR-RES-022, UXR-G-006); restated figures keep the asterisk + footnote (UXR-RES-019); every table binds its as-of (UXR-RES-021).
- **The period toggle** is UI state (session, preserved across navigation per UXR-G-021): Yıllık filters to `periodType FY`; Çeyreklik shows interims **labeled honestly from the served span** (cumulative, e.g. the declared `01.01–30.06` range — closing 090's R-1 on the surface); a `Q` period with no served span degrades to today's label, never a fabricated range.
- **The badges** render only on served evidence: Konsolide/Solo from the filing flag (absent → suppressed), TMS 29 from the served derivation, KAP Kaynak from the served source class. No badge is client-derived.
- **The EV header** renders the served inputs and the served total; each operand carries its as-of (share price dated, debt/cash from the statement period — mismatched bases are visible, never smoothed over).
- **Honest degradation:** a row family 093 could not map (no evidenced label) renders as an absent row with the no-data state — never zeros; financial-sector companies render the bank layout notice; per-share rows render "—" where no count exists (exclusion #4).
- **UX flow-back (routing item, not a gate — the 057 precedent):** this spec restructures SCR-005's Financials section beyond UXR-RES-014's five-line sketch and adds new content blocks. The ux-designer folds it into `.pipeline/ux/stock-research.md` as a dated SCR-005 amendment so the permanent UX doc owns it; the ticket renders the user's verbatim spec in the interim, and visual layout/styling/copy stay minimal-unopinionated (structure and behavior only).
- **Pinned non-goals:** the capital & dilution tracker and key value metrics panels (097 — the spec records their row sets, this ticket does not render them); no USD/3M/TTM/10Y (the exclusions above); no screener/valuation-section changes; no API work (094 owns the payload).
- Traces to: FR-RES-005–012 (structural), FR-RES-008 (financials content), FR-RES-013–016 (adjusted/restated/as-of/no-data display), FR-RES-017 (disclaimer — unchanged), UC-RES-003 (study fundamentals).
- Acceptance: **TC-RES-039** (L3) / **TC-RES-040** (L4) (stock-research.md §7, v1.1 — planner-authored per the lane precedent, test-planner confirmation pending) — component tests on MSW doubles aligned to 094's payload shape (red = the current 5-column table on the current contracts), the period-toggle labeling case (cumulative interim labeled by span; FY filter), the badge evidence cases (each badge on/off by served field), the null-row honesty case, the bank-notice case, and the per-share "—" case; i18n parity both catalogs; E2E (`tests/e2e/src/stock-page.spec.ts`) extended for the toggle and honest states on the existing fixture world. **Live criterion (orchestrator-owned):** on the post-walk live stack, TUPRS's page renders the full three tables with derived rows and the cumulative interim honestly labeled; a bank-profile company renders the notice.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.75** (the row this ticket lands).
- UX refs: **SCR-005** (§3 Financials — restructured per this ticket's user spec; flow-back routing item above), UXR-RES-008 (sections navigable — the view lives inside the Financials section), UXR-RES-014 (content, superseded in breadth by the user spec), UXR-RES-019/020/021/022 (markers/as-of/no-data), UXR-G-006/007 (honesty), UXR-G-021 (state preservation — the toggle).
- Dependencies: TKT-foundation-094 (the payload). Shares Web stock-page files with 097 (serial: 095 first).
- Parallel group: none.

## Implementation notes (2026-10-29)

**What was built.** `StatementsView.tsx` (the controls & filing bar, the served badges, the market & EV header, the three tables, the layout notice, the empty-basis state), `statementsView.ts` (the pure row model — served code bindings, period labels, badge evidence, EV operand rows) and `statementsPeriod.ts` (the basis hook), all under `src/screens/stock-page/`; `StockSections.tsx`'s `FinancialsSection` now dispatches to the view when the payload carries a served line set (or a financial-sector notice) and keeps the legacy five-figure summary otherwise, and `anyServedFigureRestated` was widened to cover `lines`/`otherLines`/`derived`. `contracts.ts` grew the additive statement types.

**Honesty decisions pinned in code.** `[R]` → served `lines`, `[C]` → served `derived`, nothing computed (BR-RES-004). Spec rows the serving surface does not carry render as explicit dash rows: `ST financial investments`, the TMS 29 capital adjustment, `fcfMargin`, the split-adjusted share count of the EV header (094's `evHeader` serves no share-count fact), and the `enterpriseValue` total (094 serves the EV inputs, never a total). A spec row that 094 carries as **two** codes ("Other Op. Inc/Exp Net", "Investing Inc/Exp Net", "Financing Inc/Exp Net") renders as two rows rather than a client-computed net. `revenueGrowthYoy` is served as a fraction; the `%` rendering is the client's formatting only.

**State.** The basis toggle persists in `sessionStorage` (`degerli.statements.basis`, UXR-G-021) — the zustand draft store is outside this ticket's scope, and the toggle is view state, not draft state.

**TC ids (user decision).** The ticket originally pinned ids 037/038 for the statements cases, but those belong to TKT-foundation-052 (dividends availability — `StockFundamentalsEndpointsTests.cs`, `Dividends.test.tsx`); per the user's answer the statements cases are tagged **TC-RES-039** (L3) and **TC-RES-040** (L4), both recorded in `stock-research.md` (v1.1). The acceptance line above is corrected to 039/040 in this PR (review round 1, G-2); the planner should still record the renumbering so 096/097 do not re-collide.

**Evidence.** Red: with `StockSections.tsx` reverted to its pre-ticket dispatch, TC-RES-039 reads **15 failed / 1 passed** (the pass is the pre-094 legacy-payload case). Green: TC-RES-039 **16/16**, `src/screens/stock-page` **10 files / 82 tests**, full Web suite **381/381** (58 files), `tsc --noEmit` clean (Web and `tests/e2e`), `eslint .` clean, i18n parity **778 keys**, advice-vocabulary clean. E2E was not executed here (no `compose.ci` stack) — typechecked only; CI runs it.

**Flags.** (1) Copy reworded for the informational-only gate: `acquisitionCash` → "Devralma Nakit Çıkışları (M&A)" / "Acquisition Cash (M&A)", `buyback` → "Geri Edinilen Paylar" / "Share Repurchases" (the gate rejects "alım"/"buy") — ux flow-back. (2) The spec restructures SCR-005 §3 beyond UXR-RES-014; the dated SCR-005 amendment in `ux/stock-research.md` is owed by the ux-designer. (3) The fixture universe has no financial-sector instrument and no stored interim span, so the bank notice and the span label are pinned at L3 only. (4) Environment: on this session's dev stack `GET /api/v1/stocks/TUPRS/financials` (also ASELS) answered **500** while `/api/v1/stocks` answered 200 — the live criterion stays orchestrator-owned. (5) Both catalogs' `financials.macro.cadence` block was mis-indented at HEAD; re-serialising normalised it (5 lines/file).

**Review round 1 fixes (2026-10-29).** (a) **E2E locator hardening made necessary by the new revenue-growth row label** — `Hasılat` is now also a substring of "Hasılat Büyümesi (Yıllık %)", so the two pre-existing `getByText` lookups were exact-matched and the fiscal-year label was scoped to one table's header (product copy and the row model unchanged). This is why `tests/e2e/src/research-handoff.spec.ts` joins the scope list: it is a test-only locator fix, no behaviour change. (b) The bank-notice path no longer renders the basis toggle — a toggle over no tables is inert chrome; the served filing badges stay (they are facts about the filing). (c) The EV header renders the spec's split-adjusted share operand as the honest dash (see the dash rows above) instead of omitting the row. (d) `periodsForBasis`/`periodLabel` and `anyServedFigureRestated` guard non-string/absent served fields: Web has no ErrorBoundary above the stock-page sections, so one malformed period must degrade, not blank all four.

