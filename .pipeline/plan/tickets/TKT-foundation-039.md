---
scope:
  - src/Web/src/components/honest-data/markers.tsx
  - src/Web/src/components/honest-data/markers.test.tsx
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/routes/MarketOverviewPage.test.tsx
  - src/Web/src/routes/MacroStrip.tsx
  - src/Web/src/routes/MacroStrip.test.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
---

# TKT-foundation-039: honest-freshness UX copy — the stale badge and as-of lines must explain their own semantics ("en güncel tamamlanmış seans" / publication cadence), so a truthful flag is never read as a data error

- Status: in-review (PR #111 — https://github.com/sarperim/degerli/pull/111)
- Size: S
- **Priority: P2 — dispatch once 036/037/038 have landed.** Finding B3 of the user's 2026-10-20 morning check: the user's "it should have access to live data" is partly an **expectation gap, not a code defect** — the platform is EOD-by-design (the page footer already states "Makro veriler gün sonu (EOD) değerleridir; gerçek zamanlı değildir."; Tuesday's market data lands tonight 20:30 Istanbul via the scheduler). After 037/038 make the flags truthful, the remaining work is that the honest-freshness UX must be **self-explanatory**: a badge that can appear (genuinely trailing data) must tell the user what "stale" means, and the as-of lines must convey the completed-session / publication-cadence semantics without the user having to cross-check Google.
- Scope (Web copy/semantics layer only — minimal and unopinionated, per the "unspecified means undecided" standard): **(a)** the `StaleBadge` (`src/Web/src/components/honest-data/markers.tsx`) gains an accessible explanation (tooltip/`title`/aria-description) whose copy states the honest meaning: the shown data is the last-known-good value and a newer completed session/publication is missing — never "this number is wrong". **(b)** The overview's equity as-of line (SCR-001, `MarketOverviewPage.tsx`) gains the completed-session semantics: "en güncel tamamlanmış seans" / EOD nature (TR + EN). **(c)** The macro strip's per-series as-of lines (`MacroStrip.tsx`) gain publication-cadence semantics: a monthly series shows its latest PUBLISHED value ("en güncel yayımlanmış veri"), a daily series its latest completed day. **(d)** All copy lives in the i18n catalogs (`tr.json`/`en.json`, parity enforced by CI); no hardcoded prose, no second landmark, no layout change. **Flow-back (recorded):** the final wording is a UX-spec decision — this ticket ships minimal, honest placeholder copy pinned by tests; the ux-designer records the approved wording in the UX spec afterwards and re-validation applies if it changes structure/behavior.
- Traces to: FR-MOV-010/016 (as-of + stale marking — the user-comprehension half), FR-MOV-020 (EOD-nature disclosure — consistent with the footer), NFR-MOV-001; UXR-MOV-010 (as-of + stale rendering), UXR-MOV-008/009 (macro strip honest markers); screen-alignment convention (explicit criteria — verified against the UX spec, not only a test runner).
- Acceptance: **explicit criteria (screen-alignment style).** **(i)** Matches SCR-001 / UXR-MOV-010: the stale badge, overview as-of line and macro per-series as-of lines render their explanatory copy in TR default and EN toggle; **(ii)** i18n parity green (the existing parity test); **(iii)** a11y: the explanation is programmatically determinable (aria-describedby/title) — not colour-only, not hover-only information; **(iv)** web unit tests (`markers.test.tsx`, `MarketOverviewPage.test.tsx`, `MacroStrip.test.tsx`) assert the copy renders from the catalogs and the badge-without-tooltip regression is impossible; **(v)** Web + E2E CI green (existing specs unaffected — additive copy only). **Live criterion (orchestrator post-merge, browser):** on the dev stack, the overview row (fresh, post-037) and macro strip (fresh monthlies, post-038) show their semantics copy; if any badge is visible it carries the explanation. Recorded on the ticket, never fabricated.
- Architecture refs: none (Web copy layer; no contract change — 037/038 own the semantics this explains).
- UX refs: SCR-001 (overview as-of/stale marker + macro strip), UXR-MOV-008/009/010; the EOD-nature footer statement (FR-MOV-020, TKT-foundation-029) is the existing precedent this aligns with.
- Dependencies: TKT-foundation-036 (shared `MarketOverviewPage.tsx` + its test), TKT-foundation-037 + TKT-foundation-038 (the truthful flags whose semantics this copy explains — landing this before them would explain a lying badge).
- Parallel group: none.

## Implementation record (2026-10-20)

- **Status:** in-review — PR #111 (https://github.com/sarperim/degerli/pull/111), branch `feature/TKT-foundation-039`.
- **Delivered (copy layer only):** `StaleBadge` `title` + accessible name from `honestData.staleExplanation`; SCR-001 equity as-of explanation `screens.marketOverview.asOfExplanation`; macro per-series publication-cadence note `marketOverview.macro.cadence.{monthly,perRelease,daily}` (mirrors the seeded `macro_series.cadence`: CPI pair monthly, policy rate per release, FX/gold daily); all copy in `tr.json`/`en.json`. Architecture §13.1 **v1.5.16** appended.
- **Tests:** red first (6 failures — missing keys/testid), green after: `vitest run` 50 files / 295 tests passed (targeted markers/MacroStrip/MarketOverviewPage: 3 files / 35 tests); `tsc --noEmit`, `eslint`, `check:i18n` (655 keys parity) and `check:advice` clean. E2E is CI-run (no container stack touched locally); its specs select via `data-slot` and are unaffected by additive copy.
- **Flagged:** the §13.1 row is outside the ticket `scope:` list but is the recorded 026/027/029 convention (orchestrator-instructed, version v1.5.16); a third cadence key (`perRelease`) was added beyond the ticket's monthly/daily pair so the policy-rate series is not mislabelled daily; final wording remains the ux-designer flow-back.
- **Live criterion:** not yet attempted — orchestrator post-merge (browser) per the ticket.
