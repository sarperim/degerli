---
scope:
  - src/Web/src/screens/stock-page/
  - src/Web/src/i18n/catalogs/
  - src/Web/src/test/msw/handlers.ts
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-057: Stock-page honesty pair — period-type-aware financials label ("FY2023" lying about a Q1 row) + which-stock header (symbol + company name)

- Status: todo
- Size: S
- **Priority: P1 — USER-DIRECTED small slice (2026-10-22, explicit request). Web-only; dispatch-ready immediately** (with the 049 coordination note below).
- **Verified defects (live on the dev stack, 2026-10-22):**
  1. **Period-type-blind financials label (honesty defect):** the stock-page financials table renders every period as `FY{{year}}` (`src/Web/src/i18n/catalogs/tr.json:133` / `en.json:133`, consumed at `src/Web/src/screens/stock-page/StockSections.tsx:213`). The API sends `periodType: "FY" | "Q"` plus `periodEndDate` (and the component already keys its rows on both — `StockSections.tsx:211`) — the Web simply ignores them for the label. **Live result on AEFES:** the Q1-2023 revenue row (24.5B) sits labeled "FY2023" beside the FY2022 row (90.5B) — reading as a −73% collapse. An unbacked claim about the data's meaning, the same honesty class as 036/039/052.
  2. **Which-stock header:** with multiple tabs open it is unclear which stock a page belongs to (user finding 2026-10-20, previously item (a) of TKT-foundation-055 — **removed from 055 by this ticket per the user's explicit direction**; 055 keeps daily-movement + sector-blocks and stays gated on ux flow-back, amended with a dated correction).
- **Pinned fixes:**
  1. **Honest period label:** render the period from what the API sends — `FY` → fiscal-year label as today; `Q` → quarter label with the quarter derived from `periodEndDate`'s month (Jan–Mar → Q1, Apr–Jun → Q2, Jul–Sep → Q3, Oct–Dec → Q4) and the correct year, both catalogs (TR/EN parity, the catalogs' `{{year}}` interpolation convention — e.g. TR "Ç1 2023" / EN "Q1 2023", exact wording minimal-unopinionated with a ux-designer flow-back note for final copy). The AEFES row must read as a quarter, never as a collapsed fiscal year. If `periodType` is absent or unexpected on a payload, the label degrades honestly (no fabricated quarter).
  2. **Header identity anchor:** the stock page header shows **symbol + company name** (the user-pinned shape: "AEFES — Anadolu Efes"), sourced from the stock payload the page already loads; unambiguous across tabs; bilingual as the payload provides.
- Traces to: FR-RES-005–018 lane (SCR-005 financials + header), FR-RES-017 (research-surface honest marking — the label is a claim about the data); user requirement 2026-10-22 (explicit); 036/039/052 honesty lineage.
- UX refs: **SCR-005** (Stock Page — financials table + page header). The user pinned both shapes in this ticket; copy polish flows back to the ux-designer with 049/052's flow-back notes (routing item, not a blocker).
- Acceptance: **explicit criteria; doubles-only (MSW), i18n parity.** **(i) Red→green period label:** an MSW double carrying a `periodType: "Q"` period (a Q1 `periodEndDate`) renders the quarter label with the correct year — red today: it renders "FY{{year}}" — and an `FY` double keeps the fiscal-year label; TR and EN catalogs both. **(ii) Header:** the stock page header renders symbol + company name from the payload double. **(iii) Existing stock-page tests re-run green** with pre-listed additive deltas only (`StockPage.test.tsx`, `StockSections`-adjacent tests in scope). **(iv) CI green.** **Live criterion (orchestrator post-merge):** on the dev stack, AEFES's financials table shows the Q1-2023 revenue row with an honest quarter label beside FY2022 (no implied −73% collapse), and the header reads "AEFES — Anadolu Efes"; console/network clean. Attempt and result recorded verbatim.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.34** — single-row append above the current landed-table max (v1.5.33, 053/#116). **Collision note:** the in-review PRs carry v1.5.27/29/30 (below max — they land as-pinned); 054's provisional "v1.5.34 if required" re-pins to the next free slot (≥35) if it ever needs a row; last-lander rebases per the serialize convention.
- Dependencies: none hard — **dispatch-ready.** **Coordination:** shares `src/Web/src/i18n/catalogs/` and the `screens/` tree with **049** (in review, v1.5.29) and 052 (serialized behind 049) — different keys/sections, but the same files: if 049 lands mid-flight, 057 rebases (last-lander-rebases); dispatching 057 after 049 merges avoids the conflict entirely. No backend files (the API already sends everything both fixes consume).
- Parallel group: none

### Closure + live criterion — armed (2026-10-22)
- Merged as PR #120 after a clean round-1 dual-lane review (diff approve; security no critical/high/medium; the amended scope including the overview identity fields verified clean). Live criterion (post-redeploy): AEFES financials show the Q1-2023 row as a quarter beside FY2022, and the header reads 'AEFES — Anadolu Efes' — the API force-recreate happens after tonight's EOD chain (pull-freeze until ~21:00 UTC); observation follows.
- ROUTED (major, upstream, non-blocking — DR-1): `VykStatementDecoder.ResolvePeriod` maps 3-, 6- AND 9-month KAP terms to periodType Q; KAP interims are YTD-cumulative, so a Jan–Sep figure can render as 'Q3 2023' (a 3-month claim). The PR implements the ticket's pin faithfully — the defect is in the pin. Planner + ux follow-up: label by served length or serve the term length additively. Recorded for the next planner pass.
- Minor leftovers: DR-2/3 (zone-dependent degradation date, unasserted degradation cases), DR-4/5/6/7 docs nits, SEC-3 CI scanning gap, balance-sheet period row (same FY-blind class, untouched).
