---
scope:
  - src/Web/src/lib/units.ts
  - src/Web/src/lib/format.ts
  - src/Web/src/lib/api/contracts.ts
  - src/Web/src/routes/MacroStrip.tsx
  - src/Web/src/screens/screener/criteria.ts
  - src/Web/src/screens/screener/ScreenerScreen.tsx
  - src/Web/src/screens/savedScreens/criteria.ts
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/stock-page/StockPage.test.tsx
  - src/Web/src/routes/MacroStrip.test.tsx
  - src/Web/src/screens/screener/ScreenerBuilder.test.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - .pipeline/architecture/03-api-design.md
---

# TKT-foundation-072: Units branding — display-side unit semantics (server unit codes are not display prose)

- Status: done — **merged 2026-10-24** as PR **#137** (squash, `dev@f40ff1d`), final head `ba25b0b`.
  Review loop ran **2 rounds**: `.pipeline/plan/reviews/TKT-foundation-072-round-{1,2}.md`. Round 1 returned
  MERGEABLE but with an open breach of the ticket's own pinned rule, so a fix round was commissioned
  rather than merging past it. Delivered: `src/Web/src/lib/units.ts` (unit-code → i18n map, unknown ⇒
  "Birim belirtilmemiş" / "Unit not specified", never the raw code), applied across `MacroStrip`,
  `ScreenerScreen`, `screener/criteria`, `savedScreens/criteria`, `StockSections`; TR+EN catalogues in
  parity (673/673).
  **DR-3/SEC-1 was a genuine rule breach, not a nit:** `UNIT_LABEL_KEYS[unit]` walked the prototype
  chain, so a unit code of `constructor`/`__proto__` printed `function Object() { [native code] }`
  instead of the honest label — red test evidence verbatim, now guarded with `Object.hasOwn`.
  **DR-4 routed to 065 and verified against the live store** (see the CRITICAL refinement section on
  TKT-foundation-065): this PR relabelled `lines.sharesOutstanding` (statement `SHARES_OUT`, **0 rows
  in the store**, always a dash) as paid-in capital — which is **not** what D4 was about, and is a
  claim-in-waiting if a decoder ever emits `SHARES_OUT`. Harmless today because the field is honestly
  empty; the real D4 field (`shareCount.sharesOutstanding`, **556 populated rows** of `currentCapital`
  money) is still unrelabelled and is 065's outstanding contract work.
  UX wording (`units.percentYoY`, `units.usdPerOunce`, `units.tryPerGram`, `units.unknown`, the
  paid-in-capital label) remains a recorded **ux-designer flow-back** item, as the ticket permits.
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 8 unparked).**
- **Evidence (gathered 2026-10-24):**
  - Raw server unit codes are rendered as display text today:
    - `MacroStrip.tsx:117` — `{series.unit}` verbatim.
    - `ScreenerScreen.tsx:261` — `` `${metricLabel(metric, language)} (${metric.unit})` ``; `:315` — `({metric?.unit})`.
    - `screens/screener/criteria.ts:143-144` — appends `${formatted} ${metric.unit}` (with an `x` special case); `screens/savedScreens/criteria.ts:19` branches on `unit === '%'`.
  - `03` §1 principle 1 is explicit: "**Codes and numbers from the server; words from the client.**" A raw unit code is a server code presented as a word.
  - The correct precedent already exists: the DCF screen maps params to `unitKey` and translates it (`screens/dcf/params.ts:47-96`, `DcfCalculatorScreen.tsx:307` → `dcf.unit.currency|percent|years|shares`). This ticket generalizes that pattern.
  - **The 065 decision gives the concrete case:** under Option A the served `shareCount` figure is **paid-in capital in TRY**, relabeled from a share count. The display side must therefore show a TRY unit and a paid-in-capital label — never a bare number that reads as a share count. (065 remains the decision record; this ticket consumes its outcome.)
- **Pinned approach:**
  - One `unit`-label map (server code → i18n key) in `src/lib/units.ts`, used by every surface that renders a unit.
  - Apply to: macro strip per-series units, screener result values / criteria editor / saved screens, the stock-page balance-sheet `shareCount` block (paid-in capital once 065 lands), and any other raw `.unit` render.
  - **Unknown codes** degrade honestly: a documented fallback (the raw code, or an explicit "unit not specified" label) — decide and pin it, never guess a label.
  - Copy lives in `tr.json`/`en.json`; no layout/styling change. UX wording is a ux-designer flow-back item, recorded, not a blocker.
- **Acceptance (explicit criteria):**
  - **(i)** No surface renders a raw server unit code in TR or EN (a grep-level proof plus a test per surface).
  - **(ii)** Every unit code in the contract has a TR and an EN label; an unknown code degrades honestly and is covered by a test.
  - **(iii)** The paid-in-capital/TRY label matches 065's recorded direction (Option A), and the field is not rendered as a share count.
  - **(iv)** i18n parity, `tsc --noEmit`, ESLint green; Web unit tests pass.
  - **(v)** If the unit-code vocabulary is pinned in `03` §2/§4, append a §13.1 row (**soft reservation v1.5.50**); otherwise record that no architecture change was needed.
- Traces to: FR-MOV-004/NFR-MOV-004 (plain-language labels), FR-RES-017 (a label is a claim about the data), `03` §1/§2.
- Architecture refs: `03-api-design.md` §1 (codes vs words), §2/§4 (units), `01` §10.6 (i18n discipline), §13.1 (only if the vocabulary is pinned there).
- UX refs: SCR-001 (macro strip), SCR-002 (screener results), SCR-005 (balance sheet / share-count block); UXR-MOV-008/009 (macro markers), UXR-SCR-0xx (screener result units), UXR-RES-0xx (balance-sheet labels) — the ux-designer confirms the exact entries during flow-back.
- Dependencies: **TKT-foundation-065** (its recorded decision defines the paid-in-capital label and the TRY unit; the rest of the unit map is independent and may be built first).
- Parallel group: none (shares the i18n catalogs with 073).

## Implementation record (2026-10-24)

- **PR:** https://github.com/sarperim/degerli/pull/137 (link filled at open).
- **Delivered:** single unit-code → localized label map in `src/Web/src/lib/units.ts`, applied at every raw-unit render.
  - `UNIT_LABEL_KEYS` covers the seeded macro/screener vocabulary (`%`, `% YoY`, `x`, `TRY`, `USD/oz`) plus `TRY/g`, which appears only in the MSW test fixtures.
  - `unitLabel` / `unitSuffix` / `isPercentUnit` are the only way a component resolves a served `unit`.
  - Surfaces: `MacroStrip.tsx` (per-series unit), `screener/ScreenerScreen.tsx` (metric select + criterion row + result values), `screener/criteria.ts` (`formatMetricValue` now takes `t`), `savedScreens/criteria.ts` (semantic branch uses `isPercentUnit`), `stock-page/StockSections.tsx` (paid-in-capital line).
- **Unknown-code decision (pinned):** an unrecognized or empty unit code renders the explicit label **"Birim belirtilmemiş" / "Unit not specified"** (`units.unknown`). The raw code is never shown and no unit is guessed — a label is a claim about the data (FR-RES-017). Covered by TR and EN tests in `MacroStrip.test.tsx`.
- **Paid-in capital (065 / USER DECISION D4, Option A):** the balance-sheet line served as `sharesOutstanding` is labelled **"Ödenmiş sermaye" / "Paid-in capital"** and carries the localized **TRY** unit (`PAID_IN_CAPITAL_UNIT_CODE`; the value is money — 64/556 rows carry sub-unit precision). The old "Ödenmiş pay sayısı" / "Shares outstanding" copy is removed from both catalogs; the row is never rendered as a share count. `lines.sharesDiluted` keeps its statement meaning and label. The served field name is unchanged (display-only ticket; the API rename belongs to the 065 implementation slice).
- **Acceptance mapping:**
  - **(i)** `grep -rn "\.unit\b" src/Web/src --include=*.tsx --include=*.ts` (excluding tests/contracts) shows only mapped calls (`unitLabel`/`unitSuffix`/`isPercentUnit`) and the pre-existing DCF `unitKey` pattern — no `{…unit}` render remains. One test per surface: `MacroStrip.test.tsx` (`TKT-foundation-072 (i)`), `ScreenerBuilder.test.tsx` (`(i)`, select + criterion + result cell), `StockPage.test.tsx` (`(iii)`).
  - **(ii)** Every served code has TR and EN labels; unknown code covered in both languages.
  - **(iii)** Paid-in capital label + TRY unit tested in TR and EN, with explicit "no count noun" assertions.
  - **(iv)** `vitest run` 360/360 Web tests pass; `tsc --noEmit` clean; ESLint clean; `check:i18n` 673 keys in sync; `check:advice` clean.
  - **(v)** **No architecture change needed:** `03` §2/§4 do not pin a unit-code vocabulary (they pin the percentage-as-fraction rule and call `unit` a display field per endpoint), so no §13.1 row is warranted and `03-api-design.md` is untouched.
- **Test placement note:** `src/Web/src/lib/units.ts` is new but the ticket's `scope:` list has no `units.test.ts`, so the mapping behaviour is proven through the three in-scope component tests (known code → TR/EN label, unknown → honest fallback, paid-in capital → TRY, never a count). A dedicated `units.test.ts` would need a scope amendment.
- **Flow-back / flagged items:**
  - **TKT-foundation-065's file still reads `Status: blocked — USER DECISION REQUIRED`.** The implemented direction follows USER DECISION D4 as dispatched (**Option A**: paid-in capital, TRY). 065 should record the decision verbatim; this ticket consumed it as instructed.
  - **UX wording flow-back (non-blocking, per the ticket):** the ux-designer should confirm the SCR-001/SCR-002/SCR-005 unit wordings — `units.percentYoY` ("% (yıllık)" / "% year-on-year"), `units.usdPerOunce` ("USD/ons" / "USD per ounce"), `units.tryPerGram` ("₺/g" / "TRY per gram"), `units.unknown`, and the paid-in-capital label.
  - The statement-reported `lines.sharesOutstanding` (`SHARES_OUT`) is not produced by the XBRL decoder today (no production mapping); the label change therefore rides only on 065's `currentCapital` figure, as intended.
