---
id: SLICE-008
status: done
scope:
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
  - .pipeline/slices/008-honest-kap-statement-staleness.md
---

# SLICE-008 — Honest KAP Statement Staleness & Empty-Coverage Handling on Stock Pages

## Context & Goal

In `StockFundamentalsEndpoints.cs`, both `GET /api/v1/stocks/{symbol}/financials` (`FinancialsAsync`) and `GET /api/v1/stocks/{symbol}/balance-sheet` (`BalanceSheetAsync`) currently compute `stale` exclusively from `EodSessionFreshness.FromDailyPricesAsync(...)`:

1. **Outdated KAP statement periods served with `stale: false`:** When a company's latest ingested financial statement is years behind the current clock date (for example, stuck on a 2023 or 2024 period while EOD prices are current), both `/financials` and `/balance-sheet` return `stale: false`, suppressing the UI's stale badge (`SectionMarkers`).
2. **Missing KAP statements on `/financials` served as an empty period array instead of `DataEnvelope.Missing`:** When an industrial/REIT company has zero stored financial statements (`entries.Count == 0 && !financialSectorLayout`), `FinancialsAsync` returns `{ periods: [] }` with `stale: false` instead of `DataEnvelope.Missing(asOf, DataStates.NoData, new DataCoverage(instrument.ListingDate?.ToString("yyyy-MM-dd"), "no financial statements reported"), eodStale)`. Because `FinancialsSection` in `StockSections.tsx` already checks `isMissingData(data)` to render the honest `no-data` state and falls back to an empty legacy table when `periods` is `[]`, returning `DataEnvelope.Missing` aligns `/financials` with `/growth` and `/balance-sheet`.

This slice updates `FinancialsAsync` and `BalanceSheetAsync` so that:
- `FinancialsAsync` returns `DataEnvelope.Missing(asOf, DataStates.NoData, new DataCoverage(instrument.ListingDate?.ToString("yyyy-MM-dd"), "no financial statements reported"), eodStale)` when `entries.Count == 0 && !financialSectorLayout`.
- `FinancialsAsync` marks `stale = eodStale || latest is null || latest.PeriodEndDate < today.AddDays(-365)` so any stock whose latest served statement period (annual or interim) is more than 365 days behind `today` is honestly flagged `stale = true`.
- `BalanceSheetAsync` marks `stale = eodStale || latest.PeriodEndDate < today.AddDays(-450)` so any stock whose latest served `FY` balance sheet is more than 450 days (~15 months, covering the annual filing window) behind `today` is honestly flagged `stale = true`.

## Rules & Invariants

- **Honest-data contract:** Never fabricate missing figures or disguise outdated statement periods as fresh.
- **Zero live calls in tests:** Verify using `StockFundamentalsEndpointsTests` against `FixtureUniverse`.
- **No OpenAPI drift:** Both endpoints return `IResult` and their `SectionResponse<T>` union is already typed in `src/Web/src/screens/stock-page/api.ts`.

## Blockers & Dependencies

- Depends on `SLICE-007` (merged in PR #179).

## Verification

- `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~StockFundamentalsEndpointsTests|FullyQualifiedName~FinancialStatementsViewTests"`

## Result

Implemented and verified:
- Added `MaxFreshStatementAgeDays = 365` and `MaxFreshAnnualBalanceSheetAgeDays = 450` in `StockFundamentalsEndpoints`.
- `FinancialsAsync`:
  - Returns `DataEnvelope.Missing(asOf, DataStates.NoData, new DataCoverage(instrument.ListingDate?.ToString("yyyy-MM-dd"), "no financial statements reported"), eodStale)` when `entries.Count == 0 && !financialSectorLayout`.
  - Computes `stale = eodStale || latest is null || latest.PeriodEndDate < today.AddDays(-MaxFreshStatementAgeDays)`.
- `BalanceSheetAsync`:
  - Renamed `stale` to `eodStale` and passes it to `DataEnvelope.Missing` when `latest is null`.
  - Computes `stale = eodStale || latest.PeriodEndDate < today.AddDays(-MaxFreshAnnualBalanceSheetAgeDays)`.
- Tests added in `StockFundamentalsEndpointsTests.cs`:
  - Verified `GET /api/v1/stocks/NEWP/financials` returns `200 OK` with `state == "no-data"`, no `periods` property, and `coverage.availableFrom == "2025-08-01"` in `TC_MDF_036_downstream_serves_canonical_values_or_honest_no_data`.
  - Added `Outdated_statement_periods_mark_financials_and_balance_sheet_stale_even_when_eod_prices_are_fresh` verifying that outdated statements (2023-12-31) mark `/financials` and `/balance-sheet` stale while `/profitability` remains fresh, and `ALFA` (2025-12-31) remains fresh across both.
- Verification: 27 tests passed in `Degerli.Api.IntegrationTests`.
