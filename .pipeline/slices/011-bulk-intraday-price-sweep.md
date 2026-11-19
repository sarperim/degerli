---
id: SLICE-011
status: done
scope:
  - .pipeline/slices/011-bulk-intraday-price-sweep.md
  - src/Ingestion/Intraday/IntradayQuoteSourceAdapter.cs
  - src/Ingestion/Intraday/IntradayRefreshJob.cs
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/IntradayQuotesTests.cs
  - tests/Api.IntegrationTests/EodFreshnessSemanticsTests.cs
---

# SLICE-011: Bulk Single-Request Intraday Price Sweep (`GetHissePerformans`) & Post-Close `18:15` Hourly Cadence

## Context & Goal

Previously, `intraday-quotes` (`IntradayRefreshJob`) swept the 100 `XU100` constituents sequentially by making **100 individual HTTP GET requests per hour** to `/_layouts/15/IsYatirim.Website/Common/ChartData.aspx/IntradayDelay?period=5&code={symbol}&last=108` (~800 HTTP calls/day, ~55 seconds of paced sequential requests per sweep), and stopped at `17:15` Europe/Istanbul — missing the `18:00–18:10` BIST closing window until the `20:30` EOD chain.

Meanwhile, İş Yatırım's `POST /_layouts/15/IsYatirim.Website/StockInfo/CompanyInfoAjax.aspx/GetHissePerformans` endpoint (with body `{"endeksKodu":"09","sektorKodu":"","exchange":""}`, already used by `UniverseSourceAdapter`) returns all ~600 listed BIST instruments in **one single HTTP POST request**, with each `Oran` entry carrying `HISSE_KODU` and the live/current delayed `PRICE_TL` (verified live on 2026-11-04: `ASELS` `PRICE_TL = 353.75` matching `IntradayDelay`'s closing bar `353.75`, `GARAN = 137.5`, `THYAO = 291.25`).

**Goal:**
1. Extend `IntradayQuoteSourceOptions`, `IntradayQuoteSourceAdapter`, and `IntradayRefreshJob` to support **single-request bulk intraday sweeps** (`Method = "POST"` with `RequestBody` or a `Path` without `{symbol}` returning the `GetHissePerformans` `Oran` envelope), reducing each hourly sweep from **100 HTTP requests to 1 HTTP request** (~9 calls/day instead of ~800).
2. Extend the `intraday-quotes` cron in `src/Api/appsettings.json` from `0 15 10-17 * * 1-5` to `0 15 10-18 * * 1-5` (10:15 through **18:15** Europe/Istanbul on trading days) so the post-close `18:15` sweep captures the day's closing prices right after the 18:00 session ends, without waiting until the 20:30 EOD chain.
3. Preserve 100% backward compatibility with all existing per-symbol envelopes (`HisseTekil`, `IntradayDelay`, Yahoo `chart`, canonical JSON double) and per-symbol `IntradayQuotesTests` / `IntradayChartFeedDecoderTests`.

## Rules & Invariants

1. **Per-symbol behavior & defaults preserved:** `IntradayQuoteSourceOptions.Method` defaults to `"GET"` and `Path` defaults to `"/intraday/{symbol}"` in code so all existing L2 `IntradayQuotesTests` and `IntradayChartFeedDecoderTests` run unchanged without config overrides.
2. **Bulk single-request execution (`IsBulkRequest`):**
   - `IntradayQuoteSourceOptions` adds `Method` (default `"GET"`), `RequestBody` (default `"""{"endeksKodu":"09","sektorKodu":"","exchange":""}"""`), and `IsBulkRequest => string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase) || !Path.Contains("{symbol}", StringComparison.Ordinal)`.
   - `IntradayQuoteSourceAdapter.FetchBulkAsync(DateOnly sessionDate, CancellationToken cancellationToken)` issues a single `POST` (when `Method` is `"POST"`) or `GET` request to `_options.Path` and decodes the İş Yatırım `GetHissePerformans` (`d` / `Oran`) envelope (or canonical `IntradayQuotePayload`), emitting an `IntradayQuoteFact(symbol, sessionDate, price, DayVolume: null)` for each `Oran` row that has a non-empty `HISSE_KODU` and a numeric `PRICE_TL`.
   - `IntradayQuoteSourceAdapter.FetchSymbolAsync` also supports the `GetHissePerformans` (`d` / `Oran`) envelope by filtering the decoded items to `MatchesSymbol(item.Symbol, symbol)`.
   - When `_options.IsBulkRequest` is true, `IntradayRefreshJob.RunAsync` calls `_adapter.FetchBulkAsync(request.Date, cancellationToken)` **once** (no per-symbol HTTP loop, no inter-request delay) and then iterates `constituents` against the fetched items to apply the exact same per-constituent `NO_DATA` (`Skipped`), `UpsertAsync`, quarantine, and `SymbolRunStat` accounting. A transport failure (`SourceFetchException`) on the bulk fetch raises the aggregate `IngestionAlert` (`SourceFetchFailed`) and returns `IngestResult.Failed`; a parse failure (`SourcePayloadException`) quarantines `UnparseablePayload` and returns `IngestResult.Failed`.
3. **Shipped configuration (`src/Api/appsettings.json`):**
   - `Ingestion:Scheduler:JobSchedules` entry for `intraday-quotes`: `"Cron": "0 15 10-18 * * 1-5"`, `"TradingDaysOnly": true`.
   - `Ingestion:Intraday`:
     - `"BaseUrl": "https://www.isyatirim.com.tr"`
     - `"Method": "POST"`
     - `"Path": "/_layouts/15/IsYatirim.Website/StockInfo/CompanyInfoAjax.aspx/GetHissePerformans"`
     - `"RequestBody": "{\"endeksKodu\":\"09\",\"sektorKodu\":\"\",\"exchange\":\"\"}"`
     - `"UniverseIndex": "XU100"`
     - `"RequestDelayMs": 500`
4. **Zero live calls in tests:** Pin the bulk `POST` sweep and the updated config constants in `tests/Api.IntegrationTests/IntradayQuotesTests.cs` using WireMock doubles.

## Blockers & Dependencies

- None (`SLICE-001`..`SLICE-010` are `done`).

## Verification

- [x] `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~IntradayQuotesTests|FullyQualifiedName~IntradayChartFeedDecoderTests|FullyQualifiedName~EodFreshnessSemanticsTests"` passes.

## Result

- **Bulk Intraday Adapter (`src/Ingestion/Intraday/IntradayQuoteSourceAdapter.cs`):**
  - Added `Method` (default `"GET"`), `RequestBody` (default `{"endeksKodu":"09","sektorKodu":"","exchange":""}`), and `IsBulkRequest` to `IntradayQuoteSourceOptions`.
  - Added `FetchBulkAsync` issuing a single POST or GET request and decoding İş Yatırım's `GetHissePerformans` (`d` ASP.NET AJAX wrapper and/or `Oran` array) into `IntradayQuoteFact` objects for rows with valid `HISSE_KODU` and numeric `PRICE_TL`.
  - Also supported `GetHissePerformans` envelope decoding in `FetchSymbolAsync` filtered to the requested ticker.
- **Bulk Sweep Ingestion Job (`src/Ingestion/Intraday/IntradayRefreshJob.cs`):**
  - When `_options.IsBulkRequest` is true, issues a single call to `_adapter.FetchBulkAsync(request.Date, cancellationToken)` without per-symbol HTTP looping or pacing delays.
  - Matches constituents against fetched items with honest accounting: constituents with no row or non-numeric `PRICE_TL` are counted as `NO_DATA` (`Skipped`), while matching facts are upserted into `IntradayQuotes` with rejected rows quarantined and identical results counted as `Unchanged`.
  - Retains existing per-symbol loop untouched when `!_options.IsBulkRequest`.
- **Shipped Production Configuration (`src/Api/appsettings.json`):**
  - Updated `intraday-quotes` schedule cron to `"0 15 10-18 * * 1-5"`, adding the 18:15 post-close sweep immediately following the 18:00–18:10 BIST session close.
  - Re-pointed `Ingestion:Intraday` to `POST /_layouts/15/IsYatirim.Website/StockInfo/CompanyInfoAjax.aspx/GetHissePerformans`.
- **Integration Tests (`tests/Api.IntegrationTests/`):**
  - Updated `TC_MDF_084_Intraday_cadence_and_source_are_shipped_config_constants` in `IntradayQuotesTests.cs` and `Eod_Freshness_kap_cadence_entries_are_shipped_config_constants` in `EodFreshnessSemanticsTests.cs` to assert the new schedule and endpoint constants.
  - Added `SLICE_011_Bulk_post_intraday_sweep_fetches_once_stores_quotes_and_counts_no_data` in `IntradayQuotesTests.cs` asserting that a bulk POST fetch issues exactly 1 HTTP request for a 3-symbol bound, writes 2 quotes (`ALFA`, `BETA`), skips 1 (`GAMA` as `NO_DATA`), and produces 0 writes to `daily_prices`.
  - Verified 27/27 integration tests passing cleanly.
