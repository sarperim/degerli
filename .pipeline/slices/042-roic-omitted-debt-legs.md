---
id: SLICE-042
status: done
scope:
  - .pipeline/slices/042-roic-omitted-debt-legs.md
  - src/Core/Metrics/CanonicalMetrics.cs
  - tests/Core.UnitTests/Metrics/ProfileMetricMatrixTests.cs
---

# SLICE-042 — Treat Omitted KAP Debt Legs as Zero on Present Balance Sheets

## Context & Goal

On real KAP financial statements, companies with no short-term bank borrowings (`AKFYE`, `BINHO`, `EGGUB` FY25, `LMKDC`) or no financial debt at all (`BRYAT`, `PAHOL`) omit the empty debt line rather than reporting `0`.

In `src/Core/Metrics/CanonicalMetrics.cs`, `debt = Sum(t.StDebt, t.LtDebt)` previously returned `null` whenever **either** `StDebt` or `LtDebt` was `null`. That caused `ev` and `investedCapital` to evaluate to `null`, leaving `roic`, `croic`, `ev_ebit`, `ev_ebitda`, `ev_fcf`, `net_debt_ebitda`, and `net_debt_fcf` missing for healthy, debt-free or single-debt-leg industrial companies even when `TOTAL_EQUITY`, `CASH`, and `EBIT` were all present.

Align debt aggregation with the existing `TangibleBook` pattern in `CanonicalMetrics.cs`: treat an omitted debt leg (`StDebt` or `LtDebt`) as `0` whenever the other leg is present or the balance sheet itself is present (`TotalEquity` or `TotalAssets` non-null), while keeping `debt` `null` when no balance sheet exists.

## Rules & Invariants

- **Honest-data contract:** Never fabricate debt when no balance sheet exists (`StDebt`, `LtDebt`, `TotalEquity`, and `TotalAssets` all `null` → `debt = null`).
- **Profile gating unchanged:** `bank` and `insurance` profiles continue to gate `roic`, `croic`, `ev_*`, and `net_debt_*` to `null`.
- **Non-regression:** Existing `MetricsEngineTests` (`ALFA`, `DELTA`, `PART`) and `ProfileMetricMatrixTests` keep exact golden values.
- **No OpenAPI contract changes.**

## Blockers & Dependencies

- None (pure `src/Core` domain math + L1 unit tests).

## Verification

- `dotnet test tests/Core.UnitTests/Degerli.Core.UnitTests.csproj --filter "ProfileMetricMatrixTests"` passes.
- Live `metrics-recompute` populates `roic` (and `croic`, `ev_ebit`, `ev_ebitda`, `net_debt_ebitda`) for `AKFYE`, `BINHO`, `BRYAT`, `EGGUB`, `LMKDC`, `PAHOL`, `RGYAS`.

## Result

- Updated [CanonicalMetrics.cs](../../src/Core/Metrics/CanonicalMetrics.cs) to treat omitted `StDebt`/`LtDebt` legs as `0m` whenever the other leg or the balance sheet (`TotalEquity` or `TotalAssets`) is present, keeping `debt = null` when no balance sheet exists.
- Added 3 L1 unit tests in [ProfileMetricMatrixTests.cs](../../tests/Core.UnitTests/Metrics/ProfileMetricMatrixTests.cs); `dotnet test --filter "ProfileMetricMatrixTests"` passed `8/8`.
