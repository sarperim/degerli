---
scope:
  - src/Ingestion/Scheduling/TradingCalendar.cs
  - src/Ingestion/IngestionServiceCollectionExtensions.cs
  - src/Api/appsettings.json
  - src/Api/Market/MarketEndpoints.cs
  - src/Api/Stocks/StockOverviewAndValuationEndpoints.cs
  - tests/Api.IntegrationTests/TradingCalendarTests.cs
  - tests/Api.IntegrationTests/MarketOverviewFreshnessTests.cs
  - tests/Api.IntegrationTests/EodChainTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-048: BIST holiday calendar — populate the existing `TradingCalendar.Holidays` seam, extend freshness honesty to holidays, and gate holiday runs (user's auto-update requirement, part 2)

- Status: todo
- Size: S
- **Priority: P2.** The user's requirement (2026-10-22): "at weekends it should display it too and also holidays it should display it too" — weekends already read fresh via 037's stored-trading-day rule; **the missing piece is the BIST holiday calendar** so a holiday Monday doesn't render `stale: true` on Tuesday morning.
- Source-verified starting point: the seam **already exists and is empty** — `src/Ingestion/Scheduling/TradingCalendar.cs` + `"TradingCalendar": { "Holidays": [] }` in `appsettings.json`, consumed today by `MarketSnapshotStore` (and wired via `IngestionServiceCollectionExtensions`). This ticket populates and extends it; it does not create a new abstraction.
- **PLANNER-PINNED DECISIONS:**
  1. **Calendar source — static config list, not a live source.** `TradingCalendar.Holidays` carries per-year BIST holiday dates (ISO `yyyy-MM-dd`), seeded from the official BIST trading-calendar publication (public, user-verifiable; seed 2025 + remaining 2026 at implementation). **Rejected (recorded):** a live calendar source — no known paced endpoint, the list is small/stable/published yearly, and a config list is honestly maintainable; a source-probe for it joins the parked hardening backlog (TKT-foundation-055-adjacent record, see 056's list).
  2. **Honesty rule:** a date is non-trading iff weekend ∪ configured holiday. Freshness (037's stored-trading-day rule, serving-side) treats coverage of the **last completed trading day** as fresh — a holiday Monday therefore reads fresh on Tuesday. Unconfigured-holiday risk is inherent to a static list and accepted honestly (a missed holiday shows `stale`, the conservative failure).
  3. **Scheduler gating:** intraday sweeps (047's cron) and the EOD chain skip on configured holidays. Pinned: a gated stream emits **one short ledger row noting the holiday skip** (named, dated) so 041's watchdog and ops see intent rather than silence — mirroring the honest-stats doctrine; never a silent no-run.
- Traces to: FR-MDF-010/011 (honest achieved depth/freshness — the holiday extension), FR-MOV-001/002 (overview display freshness); §10.9.x freshness doctrine (037's stored-trading-day rule — extended); user requirement 2026-10-22 (holidays display).
- Acceptance: **explicit criteria; L2 doubles only.** **(i) TC-MDF-087 — calendar semantics** (`TradingCalendarTests`): weekend ∪ configured holiday → non-trading; last-completed-trading-day computation correct across weekends, single holidays, and holiday-adjacent weekends (e.g. a holiday Monday → Friday is the last trading day). **(ii) Freshness honesty:** `MarketOverviewFreshnessTests` — with a holiday configured and Friday covered, Tuesday reads fresh (red today: holiday Monday reads stale). **(iii) Gating:** `EodChainTests` — on a configured holiday the chain emits the holiday-skip ledger row and fetches nothing. **(iv) Existing freshness tests re-run green** (weekend semantics unchanged). **(v) CI green.** **Live criterion (orchestrator post-merge, honest):** the next BIST holiday (or a temporarily-configured past holiday date re-verified read-only) shows the overview/stock price state fresh-not-stale on the following trading morning, with the holiday-skip ledger row present for the gated streams. Attempt and result recorded verbatim.
- Architecture refs: `01-system-architecture.md` — the freshness-doctrine section 037 added (extended with the holiday rule), §13.1 **v1.5.28**; test plan: `.pipeline/testing/market-data-foundation.md` TC-MDF-087 (delivery-planner-applied, test-planner confirmation pending).
- UX refs: none (backend freshness computation; the rendered states are 049's — SCR-001/SCR-005 display server-computed state).
- Dependencies: **TKT-foundation-047** (shared `appsettings.json`, the freshness surfaces in `MarketEndpoints`/`StockOverviewAndValuationEndpoints`, and `IngestionServiceCollectionExtensions` — serialized to avoid file collisions). Coordination: `.pipeline/testing/market-data-foundation.md` rebase convention (047/050/051/053).
- Parallel group: none
