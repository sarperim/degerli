---
id: SLICE-030
status: done
scope:
  - .pipeline/slices/030-price-derived-share-counts-backfill-derivation.md
  - src/Ingestion/Prices/PriceDerivedShareCountStore.cs
  - src/Ingestion/Prices/PricesJob.cs
  - tests/Api.IntegrationTests/PriceDerivedShareCountsTests.cs
---

# SLICE-030 — Derive `price_derived_share_counts` During Prices Backfill (`UpsertHistoryAsync`)

## Context & Goal

Data-validity audit revealed that **46 / 100 BIST 100 stocks** (`AEFES`, `DOAS`, `DOHOL`, `EREGL`, etc.) had no row in `price_derived_share_counts` despite holding 713–714 `daily_prices` rows from backfill:

1. **Root cause:** `PricesJob.SweepSymbolAsync` (daily incremental path) calls `_derivedShareCounts.UpsertAsync(...)`, but `PricesJob.RunBackfillAsync` (historical backfill path) only calls `_store.UpsertHistoryAsync(...)` and **never derives share counts** from the `DatedPriceFact.MarketCap` values it already fetched and parsed.
2. **Why incremental runs alone miss ~46% of stocks:** İş Yatırım computes `PD` using a `float32` close price, so on days when `close` is not a binary-exact fraction (e.g. `16.04` or `31.20`), `PD ÷ close` carries float32 rounding noise (`> 0.0001` shares) and is honestly withheld by `IntegralTolerance = 0.0001m`. On days when `close` is binary-exact (`.00`, `.25`, `.50`, `.75`), `PD ÷ close` is an exact integer (`≤ 0.0001` shares). Because backfill spans hundreds of sessions, deriving from the **most recent integral row** in backfill populates exact, unrounded share counts across the universe without loosening `IntegralTolerance` and with zero additional HTTP requests.

## Solution

1. Add `PriceDerivedShareCountStore.UpsertHistoryAsync(sourceRef, recordedAt, IReadOnlyList<DatedPriceFact> rows, ct)`:
   - Groups `rows` by symbol, iterates each symbol's rows in descending `Date` order, and selects the **most recent** row that passes the exact same `MarketCap > 0`, `Close > 0`, and `|quotient - rounded| <= IntegralTolerance` checks — then applies the exact same monotonic as-of (`priceDate < existing.PriceDate` → `Stale`) and idempotent upsert rules.
2. Call `_derivedShareCounts.UpsertHistoryAsync(fetched.SourceRef, _clock.GetUtcNow(), rows, cancellationToken)` in `PricesJob.RunBackfillAsync` right after `_store.UpsertHistoryAsync(...)`.
3. Add a deterministic integration test in `PriceDerivedShareCountsTests.cs` verifying that backfill derives from the most recent integral dated row when the newest date is non-integral, while keeping `IntegralTolerance = 0.0001m` untouched.

## Rules & Invariants

- **Honest-data contract:** `IntegralTolerance = 0.0001m` stays untouched; non-integral rows are still withheld, never rounded. Monotonic as-of guarantees an older backfill row never overwrites a newer stored count.
- **Zero live calls in tests:** Test with committed `RealSourceCatalog` payloads (`PricesGaran` etc.).

## Blockers & Dependencies

- None.

## Verification

- [x] `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~PriceDerivedShareCountsTests"` passes (11/11).

## Result

- Added `PriceDerivedShareCountStore.UpsertHistoryAsync` (sharing `TryDeriveCount` with `UpsertAsync`) to derive each symbol's share count from the most recent integral dated row in a backfill payload while keeping `IntegralTolerance = 0.0001m` and monotonic as-of unchanged.
- Wired `UpsertHistoryAsync` into `PricesJob.RunBackfillAsync` with zero extra HTTP requests.
- Added integration test `SLICE_030_backfill_derives_from_most_recent_integral_dated_row` in `PriceDerivedShareCountsTests.cs`.
