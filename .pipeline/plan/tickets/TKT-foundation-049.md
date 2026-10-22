---
scope:
  - src/Web/src/screens/
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/routes/MarketOverviewPage.test.tsx
  - src/Web/src/lib/api/
  - src/Web/src/i18n/catalogs/
  - src/Web/src/test/
---

# TKT-foundation-049: Web display of the intraday/close/weekend/holiday price states on SCR-001 + SCR-005 price headers (user's auto-update requirement, part 3)

- Status: todo
- Size: M
- **Priority: P1 — dispatch-ready; parallel group P-1 with TKT-foundation-047** (backend). The user-visible half of the auto-update requirement: the overview and the stock page must render what the frozen `priceState` contract serves — never invent or freeze values client-side.
- **Frozen contract (from 047, item 4 — this ticket builds against it; any change flows back through 047, not around it):** `priceState: { state: "intraday" | "close", lastPrice, prevClose, changePct, asOf, sessionDate }`. `changePct` = vs previous session's close. During session → `intraday` + `asOf` ("not final" semantics); 18:00→EOD-landing → still `intraday` with last `asOf` (honest last-update, never relabeled close); after EOD lands → `close`; weekends/holidays → last completed session's `close` (freshness server-computed; before 048 lands, holiday Mondays still show `stale` — the current defect, completed by 048).
- **Pinned presentation semantics (structure + behavior only — copy/styling are minimal and unopinionated, flow-back to ux-designer for polish, per the UX-spec standard):** the price header shows lastPrice + changePct with the state's honest label — intraday: "son güncelleme HH:MM — kesinleşmedi / last update — not final" with the `asOf` timestamp; close: the session close, dated `sessionDate`. No client-side computation of changePct (server-provided). No client-side freeze at 18:00 (planner-rejected in 047). MSW doubles realign to the contract (real-shape fixtures, doubles-only).
- Traces to: FR-MOV-001/002/003 (overview price display), FR-RES-005–018 lane (stock-page header), FR-MOV-020/FR-RES-017 (research-surface disclaimers — the "not final" language is their sibling); user requirement 2026-10-22; 039's honest-freshness UX copy doctrine (extends to the intraday state).
- UX refs: **SCR-001** (Market Overview — price/index/movers headers), **SCR-005** (Stock Page — price header). UXR: the freshness-language entries 037/039 pinned (extended here; **flow-back note:** the intraday-state presentation labels should be pinned as UX-spec entries via the ux-designer after this ships minimal-unopinionated — recorded as a routing item, not a blocker).
- Acceptance: **explicit criteria (screen-alignment style) + Web tests, doubles-only.** **(i) Matches the frozen contract:** overview + stock-page price headers render `intraday` (lastPrice, changePct, asOf, "not final" label), `close` (session close, dated), and the between-close-and-EOD honest last-update state — verified against MSW doubles serving each state. **(ii) Never fabricated:** no client-side changePct computation; missing `priceState` degrades to the existing EOD display (037/039 semantics), never a stale-labeled-as-fresh value. **(iii) Bilingual:** TR/EN labels via `i18n/catalogs` (TR default, EN toggle — the catalogs' convention). **(iv) Tests:** Vitest + MSW per-state render assertions; existing overview/stock-page tests re-run green with pre-listed additive deltas only. **(v) CI green.** **Live criterion (orchestrator post-merge):** on the dev stack, during a session with ≥1 047 sweep landed, both screens show the intraday state with the correct asOf; after the EOD chain, both show the close; console/network clean. Attempt and result recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.x (047's new intraday-lane section — the contract's home; this ticket adds no architecture rows beyond a §13.1 **v1.5.29** note in 047's section if the repo convention requires the Web lane's row — the 036 precedent: Web-side changes carry their §13.1 row).
- Dependencies: **TKT-foundation-047's frozen contract** (parallel-dispatchable — P-1 — because the contract is pinned in both tickets and the scopes are disjoint: this ticket touches Web files only; 047 owns backend + API). Coordination: shared nothing with 050/051/053; 052 serializes behind this ticket (shared `screens/` + `i18n/catalogs/`).
- Parallel group: P-1
