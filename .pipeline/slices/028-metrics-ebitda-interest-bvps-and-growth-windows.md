---
id: SLICE-028
status: done
scope:
  - .pipeline/slices/028-metrics-ebitda-interest-bvps-and-growth-windows.md
  - src/Core/Metrics/CanonicalMetrics.cs
  - src/Ingestion/Metrics/MetricsEngine.cs
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Web/src/screens/stock-page/StockSections.tsx
  - tests/Core.UnitTests/Metrics/ProfileMetricMatrixTests.cs
  - tests/Api.IntegrationTests/FinancialStatementsViewTests.cs
  - tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs
---

# SLICE-028 — Fix EBITDA (`DEPR_AMORT` on CF), Interest Coverage (`FIN_EXP` sign), Balance-Sheet BVPS Fallback & Growth Matrix Deduplication

## Context & Goal

Data-validity audit of the live BIST 100 database (`degerli-dev-postgres-1`) uncovered four high-impact calculation & presentation bugs:

1. **EBITDA / EV-EBITDA / Net Debt-EBITDA (`0 / 93` stocks):**
   - `DEPR_AMORT` lives on `statement_type = 'CF'` (833/833 rows across 80 stocks, 0 on `IS`).
   - Both `MetricsEngine.cs` (`DeprAmort: Line(is0, DeprAmort)`) and `StockFundamentalsEndpoints.cs` (`var depr = Line(is0, DeprAmort, lines)`) only queried `is0` (Income Statement), leaving `ebitda`, `ev_ebitda`, and `net_debt_ebitda` `null` for 100% of stocks.
   - Fix both to fall back to `cf0`: `Line(is0, DeprAmort) ?? Line(cf0, DeprAmort)` (and include `cf0` restatement status when `cf0` supplies `DEPR_AMORT`).
2. **Interest Coverage (`2 / 93` stocks):**
   - `FIN_EXP` follows KAP's negative-expense sign convention (809/886 rows `< 0`).
   - `CanonicalMetrics.cs` required `t.FinExp is > 0m` instead of taking `Math.Abs(t.FinExp.Value)` (unlike `CAPEX` and `INTEREST_PAID`, which already use `Math.Abs`).
   - Fix `InterestCoverage` to use `finExp = t.FinExp is null ? null : Math.Abs(t.FinExp.Value)` with `finExp is > 0m`.
3. **Balance Sheet `bookValuePerShare` (`null` when `shareCount.sharesOutstanding` is served):**
   - `StockFundamentalsEndpoints.cs` already falls back from statement `SHARES_DLT` to `price_derived_share_counts.SharesOutstanding` in Financials (lines 259-262), but Balance Sheet (line 545) only checked `SHARES_DLT`.
   - Apply the same `shares is > 0m ? shares : derivedCount?.SharesOutstanding` fallback for `bookValuePerShare` while keeping `lines.sharesDiluted` and `lines.sharesOutstanding` strictly statement-only (`TC-MDF-115 (v)`).
4. **Growth Matrix (`3Y` / `5Y` / `10Y`) repeating identical `actual 2Y` values across all 3 columns:**
   - When a stock only has 2–3 years of FY history (e.g., `CCOLA`), `MetricMath.Cagr` intentionally falls back to the available span (`TC-RES-031` / `ScreenerRunEndpointTests`).
   - In `StockSections.tsx` (`GrowthSection`), avoid displaying the exact same shorter-span `actual NY` value redundantly in longer columns (`5Y`, `10Y`) when an earlier column (`3Y`, or `5Y`) already displayed that same `windowYears` — render an honest `—` (`data-slot="no-value"`) for the unreached longer horizon while keeping the shortest applicable column (`3Y`) showing the value + `data-slot="actual-window"` (preserving `TC-RES-031`).

## Rules & Invariants

- **Honest-data contract:** Never fabricate missing data or use paid-in capital as share count.
- **Test contract preservation:** Keep `MetricMath.Cagr` fallback intact (`ScreenerRunEndpointTests`, `TC-RES-031`), keep `lines.sharesDiluted`/`lines.sharesOutstanding` statement-only (`TC-MDF-115 (v)`, `TC-RES-039 (i)`).

## Blockers & Dependencies

- None.

## Verification

- [x] `dotnet test tests/Core.UnitTests/Degerli.Core.UnitTests.csproj --filter "FullyQualifiedName~ProfileMetricMatrixTests"` passes (5/5).
- [x] `dotnet build src/Api/Degerli.Api.csproj --no-restore` succeeds (0 warnings, 0 errors).
- [x] `cd src/Web && npm run build && npm run lint && npx vitest run src/screens/stock-page/StockPage.test.tsx src/screens/stock-page/StatementsView.test.tsx` passes (2 files / 25 tests).

## Result

- Fixed `DEPR_AMORT` lookup in `MetricsEngine.cs` and `StockFundamentalsEndpoints.cs` to fall back to `cf0` (`Line(is0, DeprAmort) ?? Line(cf0, DeprAmort)`), restoring `EBITDA`, `EV/EBITDA`, and `Net Debt/EBITDA` across the market.
- Fixed `InterestCoverage` in `CanonicalMetrics.cs` to use `Math.Abs(t.FinExp.Value)` for KAP's negative-expense sign convention, verified by `SLICE_028_interest_coverage_handles_negative_kap_financial_expense`.
- Added `price_derived_share_counts.SharesOutstanding` fallback for `bookValuePerShare` in `StockFundamentalsEndpoints.cs` while keeping `lines.sharesDiluted`/`lines.sharesOutstanding` strictly statement-only.
- Deduplicated shorter-span `actual NY` repeats across `5Y` and `10Y` columns in `GrowthSection` (`StockSections.tsx`) while keeping `3Y` showing `actual NY` (`TC-RES-031` preserved).
