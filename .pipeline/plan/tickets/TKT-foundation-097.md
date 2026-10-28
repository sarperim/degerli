---
scope:
  - src/Web/src/screens/stock-page/
  - src/Web/src/lib/api/contracts.ts
  - src/Web/src/lib/format.ts
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/test/msw/handlers.ts
  - tests/e2e/src/stock-page.spec.ts
  - .pipeline/testing/stock-research.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-097: The SCR-005 capital & dilution tracker and key value metrics & moat panel — the user's v0 spec's remaining two blocks rendered on the stock page

- Status: todo
- Size: M
- **Priority: P1 — slice 5 (final) of the 2026-10-28 financial-statements view wave. The user's spec lives verbatim on TKT-foundation-095; this ticket renders the last two blocks, consuming TKT-foundation-096's `GET /stocks/{symbol}/key-metrics` payload.**

- **What to render (the user's verbatim row sets, from the 095 spec):**
  1. **CAPITAL & DILUTION TRACKER** — Paid-In Capital (Ödenmiş), Split-Adjusted Share Count [C], Share Count Change Y/Y % [C] (honest N/M — no count history exists, the served state renders it, never a guess), Bedelli/Bedelsiz % History (from the served `corporate_actions` history, dated, with the corporate-action-adjusted disclaimer per UXR-RES-020), Dividend Payout Ratio % [C].
  2. **KEY VALUE METRICS & MOAT INDICATORS (Current vs. 5Y Historical Median + Sector Median)** — ROIC %, CROIC %, ROE (Parent) %, ROA %; FCF/Net Income (Annual & 3Y Avg), CFO/EBIT, Monetary Gain/Pretax % (TMS 29 distortion alert); EV/EBIT, EV/EBITDA, P/E, P/FCF, FCF Yield %, P/B, P/TBV, Div %; Net Debt/EBITDA, Net Debt/FCF (N/M if FCF ≤ 0), EBIT/Interest Exp., CFO/Cash Interest, Current Ratio; 3Y & 5Y CAGR (Revenue, EBIT, EPS Parent, FCF/Share, BVPS). Each row: current value, 5Y historical median, sector median with peer count — every figure **served, never client-computed** (BR-RES-004); N/M and no-data states per UXR-RES-025/UXR-RES-022.
- **Honesty rules that bind this surface specifically:** the **TMS 29 distortion alert** on Monetary Gain/Pretax is rendered from the served value (a visible alert when the ratio is high — the served fact, not a client threshold); the panel's **3Y/5Y CAGR rows carry the regime-break marker and footnote** exactly as the Growth section does (UXR-RES-026/027 — `metricFlags[<code>].regimeBreak` driven, distinguishable from restatement markers, value never hidden); 5Y medians render the served N/M where history is depth-limited (~3.5 years — the user's own honesty call); sector medians carry peer counts (UXR-RES-024 analog) and profile-gated exclusions.
- **Layout/copy posture:** structure and behavior only — visual grouping (moat/valuation/health/growth), styling and final copy stay minimal-unopinionated with the ux-designer flow-back noted (the 095 routing item covers both blocks; the ux spec's SCR-005 amendment folds them in).
- **Pinned non-goals:** no new computation (096 owns every number); no statements-view changes (095's); no USD/real basis, no 3M/TTM, no 10Y CAGR (the 095 exclusions, verbatim); no screener changes.
- Traces to: FR-RES-006/007 (valuation metrics + vs-sector strip — the panel generalizes the strip's honesty fields), FR-RES-009 (profitability), FR-RES-010 (growth), FR-RES-012/FR-RES-027 (dividend payout / per-share book values), FR-MDF-004 (corporate actions display), UC-RES-003.
- Acceptance: **TC-RES-038** (stock-research.md §8, v1.2 — planner-authored per the lane precedent, test-planner confirmation pending) — component tests on MSW doubles aligned to 096's payload (red = the blocks absent on the current contracts): both row sets complete, every state (value / N/M / no-data) rendered honestly, the regime-break marker on a spanning CAGR and its absence on a one-side CAGR, the TMS 29 alert case, peer-count rendering, the bedelli/bedelsiz history case with adjusted-data disclaimer, the share-count N/M case; i18n parity both catalogs; E2E (`tests/e2e/src/stock-page.spec.ts`) extended for the panel's honest states on the fixture world. **Live criterion (orchestrator-owned):** TUPRS's page renders both blocks with the served medians and honest N/Ms verbatim; a bank-profile company's panel shows only the profile-appropriate rows.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.73** (soft reservation; re-pin at merge).
- UX refs: **SCR-005** (§2 Valuation, §4 Profitability, §5 Growth — the panel's structure; the tracker's flow-back rides the 095 routing item), UXR-RES-012/013 (valuation content + sector-median disclosure), UXR-RES-015/016 (profitability/growth content), UXR-RES-017 (book value per share), UXR-RES-024/025 (peer count, N/M), UXR-RES-026/027 (regime-break markers), UXR-RES-020 (adjusted-data disclaimer), UXR-G-006/007.
- Dependencies: TKT-foundation-095 (shared Web stock-page files, contracts, i18n catalogs — serial), TKT-foundation-096 (the payload).
- Parallel group: none.
