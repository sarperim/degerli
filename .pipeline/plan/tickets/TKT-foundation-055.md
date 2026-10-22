---
scope:
  - src/Web/src/screens/
  - src/Web/src/i18n/catalogs/
---

# TKT-foundation-055: Stock-page UX findings — daily-movement clarity, sector-comparison/performance honest states (SCR-005; header item moved to 057 on 2026-10-22)

- Status: in-progress
- Size: M
- **Priority: P2 — DISPATCH-READY (unblocked 2026-10-22 by USER GRANT: the planner pins the minimal honest shapes below, the 057 precedent — user-pinned minimal, ux-designer flow-back as future refinement, NOT a gate).** Serialized behind 057 for the file overlap only.
- Verified user findings (2026-10-20, dev stack):
  - **(a) Which-stock ambiguity — REMOVED 2026-10-22 (dated correction, user-directed):** the user explicitly sliced the header fix into **TKT-foundation-057** (symbol + company name, user-pinned shape, dispatch-ready there). This ticket no longer covers it.
  - **(b) Daily movement unclear:** price + change for the day is not shown clearly on the stock page (rides on 047/049's `priceState` contract once landed — the movement display consumes it).
  - **(c) Sector blocks:** the sector-comparison block shows all-"Anlamsız"/"0 emsal" and the sector-performance block "Kullanılamıyor". Investigation note (planner, from the data layer): these are consistent with the **zero-shares world** (share-count-derived metrics blocked on TKT-foundation-033, prod-gateway NXDOMAIN) rendering as honest empties — "0 emsal" = no sector peers with computable metrics. The UX work must present the honest empty honestly ("veri yok" family, the 052 doctrine), and the blocks populate as 033's data lands; a separate check whether any *resolvable* presentation defect hides among them belongs to the ux-designer pass.
- **Gate REMOVED (2026-10-22, USER GRANT — dated correction):** the ux flow-back becomes a future refinement, not a dispatch gate. **Planner-pinned minimal honest shapes (the 057 style):**
  - **(b) Daily movement:** consume the LANDED `priceState` contract (049 merged, PR #115) and render the movement block — `lastPrice` + `changePct` + the state's honest label (intraday: "son güncelleme HH:MM — kesinleşmedi"; close: dated session close) — **prominently in the stock page header area, adjacent to 057's identity anchor** (symbol + name + movement read as one glanceable unit). No client-side computation; missing `priceState` degrades to 049's existing honest states. TR/EN parity.
  - **(c) Sector blocks honest empties:** when no sector peers have computable metrics (the zero-shares world until 058 lands), the blocks render the "veri yok" family (the 052 doctrine) — TR "Bu sektör için yeterli veri yok" / EN "Not enough data for this sector" — instead of the all-"Anlamsız"/"0 emsal" wall and the unexplained "Kullanılamıyor". Per-row "Anlamsız" verdicts remain ONLY where a computable-but-insignificant comparison genuinely exists. The blocks populate as 058's share counts land (no dependency — the empty is correct until then).
  - The ux-designer may later refine copy/layout through the normal flow-back; that refinement re-validates against the UX spec, it does not gate this ticket.
- Traces to: SCR-005 (Stock Page); FR-RES-005–018 lane (the page's sections); FR-RES-017 (research-surface honest marking — the (c) empty states); TKT-foundation-033 dependency (share counts — the (c) data supply); 047/049's `priceState` contract (the (b) supply).
- UX refs: **SCR-005** — pending the ux-designer's additions for the three findings (flow-back recorded; SCR/UXR IDs to be pinned there before dispatch).
- Acceptance: **explicit criteria (the pinned shapes above are the contract):** (i) the movement block renders per (b) against MSW doubles for each `priceState` state (Vitest); (ii) the sector blocks render the honest-empty family per (c) with doubles proving the no-peers world (per-row verdicts only where computable); TR/EN parity; (iii) existing stock-page tests re-run green with pre-listed deltas; (iv) CI green. **Live criterion (orchestrator):** on the dev stack, the stock page header shows the movement block beside the identity anchor, and the sector blocks show the honest empty (no unexplained "Anlamsız"/"0 emsal" wall); console/network clean. Recorded verbatim. **Live criterion (orchestrator post-merge):** on the dev stack, the daily movement renders per the pinned spec; the sector blocks show their honest states with no unbacked claims. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.39** when implemented (above the in-review v1.5.35 — last-lander rebases).
- Dependencies: **serialize behind 057 (shared `screens/stock-page/` + `i18n/catalogs/` files — 057 in review at #120)**; consumes 049's LANDED priceState contract for (b); records (does not depend on) 058 for (c)'s data — the honest empty is correct until the counts land.
- Parallel group: none
