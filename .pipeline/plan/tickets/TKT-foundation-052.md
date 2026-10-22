---
scope:
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Web/src/screens/
  - src/Web/src/i18n/catalogs/
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
---

# TKT-foundation-052: Temettü honesty wording — "Bu şirket hiç temettü ödemedi" is an unbacked claim when the dividends lane has no data

- Status: todo
- Size: S
- **Priority: P1 (small honesty defect) — but serialized behind TKT-foundation-049** (shared `src/Web/src/screens/` + `i18n/catalogs/` files).
- Verified evidence (user finding 2026-10-20): the stock page (SCR-005) dividends section renders **"Bu şirket hiç temettü ödemedi"** ("neverPaidBody", `src/Web/src/i18n/catalogs/tr.json`) while `dividends` had **zero rows** and the dividends job had **never completed a run** at capture time — an unbacked negative claim violating the honest-empty doctrine (the 039 lane's language: absent data ≠ verified zero).
- **Pinned fix (minimal, honest):** the dividends payload gains a server-side availability signal (follow the 037/038/039 freshness-language precedent — e.g. a `dividends: { available: bool, … }` shape pinned by the coder against the existing stock-fundamentals payload structure; the signal derives from the dividends lane's coverage/run state, not from row-count-zero alone). The section then renders three honest states: **(a) no data / not yet processed** → "veri yok / no data yet" (neutral, no claim); **(b) processed, none found** → the existing neverPaid wording (now backed); **(c) processed, rows exist** → the existing list. Post-046 the dividends job completes, so state (b) becomes genuinely reachable and backed.
- Traces to: FR-RES-0xx dividends display lane (FR-RES-005–018), FR-MDF-010/011 (honest achieved depth — availability before claims); the honest-empty doctrine (039's UX copy lane); SCR-005 dividends section.
- UX refs: **SCR-005** (Stock Page — dividends section). Copy is minimal-unopinionated per the UX standard; the "veri yok" wording should flow back to the ux-designer with 049's flow-back note (routing item, not a blocker).
- Acceptance: **explicit criteria; doubles-only.** **(i) Server signal:** `StockFundamentalsEndpointsTests` — the dividends payload distinguishes not-processed from processed-empty from populated (doubles per state). **(ii) Web states:** Vitest — the three states render their distinct honest copy (neverPaid only in the backed state); TR/EN via catalogs. **(iii) Existing stock-page tests re-run green** with pre-listed deltas. **(iv) CI green.** **Live criterion (orchestrator post-merge):** on the dev stack, a stock with no dividends coverage shows "veri yok" (not the neverPaid claim); after a completed dividends run, a genuinely-zero company shows the backed wording. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.32** (the 036/039 Web-lane row precedent); test-plan TC assignment follows the RES plan's next free IDs (test-planner confirmation lane).
- Dependencies: **TKT-foundation-049** (shared Web files — serialize after it). No backend dependencies (046 merged; the dividends lane completes).
- Parallel group: none
