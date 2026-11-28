# Değerli — Upcoming Features Roadmap

> **Purpose:** Living backlog of post-launch milestones and planned vertical slices for `degerli.tech` (a free, transparent, research-first fundamental value-investing platform for Borsa İstanbul).

---

## Phase 0 — Initial Production Launch (`degerli.tech`)

- [ ] Merge verified slices (`SLICE-040`..`SLICE-043`) to `dev` → `main` so GHCR production builds include the ROIC omitted-debt-leg fix and KAP truncated-429 resilience.
- [ ] Add `Ingestion__Macro__EvdsApiKey` (and optional `XU100` universe defaults for dividends/corporate-actions) to `compose.prod.yml`.
- [ ] Provision VPS + configure DNS (`degerli.tech`), SMTP credentials, and initial 3-year database restore.
- [ ] Data-quality follow-ups from initial audit:
  - Prefer `price_derived_share_counts` / balance-sheet `PAID_IN_CAPITAL` over stale MKK dev-gateway `share_counts` in `MarketSnapshotEngine`.
  - Fix `accounting_profile` classification for `GRSEL` (`industrial`, not `insurance`) and `ANHYT` (`insurance`, not `industrial`).
  - Investigate `EGEEN` and `HALKB` KAP public OID mappings (`1de05daa...`).

---

## Phase 1 — "My Stocks" & Fair-Value / Margin-of-Safety Alarms *(Core Milestone)*

The primary user-facing feature after launch: letting investors track their own watched stocks against their **own** DCF / fair-value estimates and get notified automatically when prices reach their target margin of safety.

- **My Stocks Watchlist:**
  - Add/remove BIST stocks to a personal "My Stocks" page.
  - Display current price, latest user-saved DCF fair value (`dcf_scenarios`), implied upside/downside, and current margin of safety side by side.
- **User-Defined Price & Margin-of-Safety Alarms:**
  - Trigger when a stock crosses above/below the user's estimated fair value.
  - Trigger when the discount reaches a user-specified **margin of safety** (e.g., `price <= fair_value * (1 - target_margin)`).
  - Support fixed price thresholds as well.
  - Cooldown / one-shot / recurring alarm controls and clear trigger history.
- **Evaluation & Delivery:**
  - Evaluate active alarms automatically after each `intraday-quotes` sweep and EOD `prices` run.
  - Initial delivery via email (reusing existing SMTP infrastructure) + in-app alert inbox; push notifications added in Phase 4.
- **Guardrail:** All fair-value estimates and thresholds are strictly user-defined — never platform-generated "buy/sell/hold" recommendations.

---

## Phase 2 — Visual Company Health Summary, Dividend Calculator & Portfolio Tracker

- **5-Axis Visual Company Health Summary:**
  - At-a-glance visual breakdown across Değerli's five canonical metric families (**Valuation, Profitability/Quality, Growth, Financial Health, and Dividends**), benchmarked against sector medians (`sector_metric_medians`) and explicit data-honesty badges (including TMS 29 monetary gain/loss flags).
- **Dividend Ingestion & Dividend Calculator:**
  - Populate live `dividends` and `corporate_actions` history.
  - Interactive dividend income / yield-on-cost / reinvestment compounding calculator using real historical BIST payouts and user-adjustable assumptions.
- **Portfolio Value & Valuation Tracker:**
  - Track user holdings (lots, cost basis, currency/inflation context).
  - Aggregate portfolio market value, unrealized P&L, sector concentration, weighted valuation multiples (P/E, P/B, dividend yield), and aggregate portfolio fair-value vs. market-value gap based on saved DCF scenarios.

---

## Phase 3 — ETF & TEFAS Fund Analysis

- Wire the TEFAS data source adapter onto the existing database tables (`funds`, `fund_navs`, `fund_performances`, `fund_holdings`) and ingestion job skeletons in `src/Ingestion/Funds/`.
- Enable scheduled `fund-nav`, `fund-performance`, and `fund-holdings` jobs.
- Add fund screening, historical NAV/return comparison (nominal & real vs. TÜİK CPI / USD / Gold), expense ratio & risk metrics, and top equity holdings overlap with BIST stocks.

---

## Phase 4 — Android App (React Native) with Real-Time Push Alerts

- Build a companion Android app in **React Native** consuming the existing typed `/api/v1/*` OpenAPI contract.
- Add mobile-friendly authentication + push-notification device token registration (FCM / Expo Push) on the API.
- Deliver instant Android push notifications the moment a user's **My Stocks** fair-value or margin-of-safety alarm triggers.
- Include mobile views for My Stocks, active/triggered alarms, stock overview, and saved DCF scenarios.
