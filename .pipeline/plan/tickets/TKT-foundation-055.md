---
scope:
  - src/Web/src/screens/
  - src/Web/src/i18n/catalogs/
---

# TKT-foundation-055: Stock-page UX findings — daily-movement clarity, sector-comparison/performance honest states (SCR-005; header item moved to 057 on 2026-10-22)

- Status: todo
- Size: M
- **Priority: P2 — NOT dispatch-ready: gated on the ux-designer flow-back** (the three findings are UX-spec gaps, not pinned behavior; per the UX standard, unspecified means undecided — the coder must not invent the presentation).
- Verified user findings (2026-10-20, dev stack):
  - **(a) Which-stock ambiguity — REMOVED 2026-10-22 (dated correction, user-directed):** the user explicitly sliced the header fix into **TKT-foundation-057** (symbol + company name, user-pinned shape, dispatch-ready there). This ticket no longer covers it.
  - **(b) Daily movement unclear:** price + change for the day is not shown clearly on the stock page (rides on 047/049's `priceState` contract once landed — the movement display consumes it).
  - **(c) Sector blocks:** the sector-comparison block shows all-"Anlamsız"/"0 emsal" and the sector-performance block "Kullanılamıyor". Investigation note (planner, from the data layer): these are consistent with the **zero-shares world** (share-count-derived metrics blocked on TKT-foundation-033, prod-gateway NXDOMAIN) rendering as honest empties — "0 emsal" = no sector peers with computable metrics. The UX work must present the honest empty honestly ("veri yok" family, the 052 doctrine), and the blocks populate as 033's data lands; a separate check whether any *resolvable* presentation defect hides among them belongs to the ux-designer pass.
- **Gate (routing item to the user):** the ux-designer must pin SCR-005 spec entries for (b) the daily-movement display's structure/behavior (consuming `priceState`) and (c) the sector blocks' honest-empty presentation — then this ticket re-validates against them. This ticket's file scope is provisional until then (the screens it touches are known; the exact components follow the spec).
- Traces to: SCR-005 (Stock Page); FR-RES-005–018 lane (the page's sections); FR-RES-017 (research-surface honest marking — the (c) empty states); TKT-foundation-033 dependency (share counts — the (c) data supply); 047/049's `priceState` contract (the (b) supply).
- UX refs: **SCR-005** — pending the ux-designer's additions for the three findings (flow-back recorded; SCR/UXR IDs to be pinned there before dispatch).
- Acceptance: **screen-alignment style — explicit criteria once the UX spec lands:** "matches the pinned SCR-005/UXR entries for (b)/(c)"; until then this ticket stays gated. Web tests (Vitest, doubles-only) per state; existing stock-page tests re-run green with pre-listed deltas; CI green. **Live criterion (orchestrator post-merge):** on the dev stack, the daily movement renders per the pinned spec; the sector blocks show their honest states with no unbacked claims. Recorded verbatim.
- Architecture refs: none beyond a §13.1 row (**v1.5.35**, the pinned allocation) when implemented.
- Dependencies: **gated on the ux-designer flow-back (user routing)**; consumes 047/049's contract for (b); records 033's data dependency for (c). Serialized behind 049/052 (shared `screens/` + catalogs).
- Parallel group: none
