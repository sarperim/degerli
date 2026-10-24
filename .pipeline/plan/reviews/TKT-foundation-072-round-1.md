---
ticket: TKT-foundation-072
round: 1
pr: 137
base: origin/dev
head: f8722299b5638d43a4f8be413ac4c0414e3894df
verdict: MERGEABLE
gates:
  facet: clean
  ci: green (Backend, E2E, Web — run 40055254854)
reviewers:
  - diff-reviewer
  - security-reviewer
open_blocking: 0
open_findings: 6
---

# TKT-foundation-072 — review round 1

PR: https://github.com/sarperim/degerli/pull/137 · branch `feature/TKT-foundation-072` · head `f872229`
Ticket: `.pipeline/plan/tickets/TKT-foundation-072.md`
Review worktree: `/tmp/opencode/worktrees/TKT-foundation-072-review` (detached at head)

## Gate 1 — facet gate (raw output, verbatim)

```
base=origin/dev
merge_base=8f4b5e59cfdfc9e89de092fc911d15a45676e501
files_changed=12
presentation_only=false
touches_exec=true
touches_src=true
touches_tests=true
touches_deps=false
touches_config=false
security_relevant=true
tc_pinned=false
missing_tc=
scope_declared=true
out_of_scope=
exit=0
```

Tool/script note (not a finding against the PR): `security_relevant=true` is driven solely by the
`routes/` path token in `src/Web/src/routes/MacroStrip.tsx`; the security reviewer confirmed the
trigger is a false positive for this diff (presentation-only, no auth/session/token surface).

Gate findings: **none** (`missing_tc` empty, `out_of_scope` empty).

## Gate 2 — CI gate

`gh pr checks 137` at head `f872229`:

| Check | Result |
|---|---|
| Backend (restore · build · test) | pass (3m22s) |
| E2E (compose · Playwright · MailPit) | pass (3m58s) |
| Web (lint · typecheck · test · i18n · build) | pass (1m31s) |

Run: https://github.com/sarperim/degerli/actions/runs/40055254854

Gate findings: **none**.

## Dispatch (data-driven)

`presentation_only=false` → diff-reviewer. `security_relevant=true` → security-reviewer, in
parallel. Both dispatched for a full first review.

## Findings

| id | source | severity | type | status | location |
|---|---|---|---|---|---|
| DR-1 | diff-reviewer | minor | test-faithfulness | open | `src/Web/src/lib/units.ts:83-89`; `src/Web/src/screens/screener/ScreenerBuilder.test.tsx` |
| DR-2 | diff-reviewer | minor | structure / dead code | open | `src/Web/src/lib/units.ts:61-63,53` |
| DR-3 | diff-reviewer | minor | correctness (prototype lookup) | open | `src/Web/src/lib/units.ts:66-69` |
| DR-4 | diff-reviewer | minor (latent-major if it ever fires — see note) | correctness / coordination | open, routed as a coordination item | `src/Web/src/screens/stock-page/StockSections.tsx:494-503,556-560` |
| DR-5 | diff-reviewer | nit | i18n / doc accuracy | open | `src/Web/src/lib/units.ts:28-30` |
| SEC-1 | security-reviewer | low | security (prototype lookup) | open | `src/Web/src/lib/units.ts:32,68` |

No finding was waived, downgraded, or dropped by the lead. DR-3 and SEC-1 are the **same defect**
reported independently by both lanes (kept separate so neither lane's report is lost; fix once).

### DR-1 — the unknown path is only proven for `unitLabel`, not `unitSuffix`/empty string (minor, test-faithfulness)

The screener criterion row and result cells resolve units through `unitSuffix`, not `unitLabel`.
No test feeds `unitSuffix` an unrecognized or empty-string code, and the EN screener label and EN
`units.ratio`/`units.tryPerGram` are never asserted (macro covers unknown in both locales). A
regression where `unitSuffix` returned the raw code or `''` after `t()` would fail no test.
Suggested fix: one case in `ScreenerBuilder.test.tsx` serving `unit: 'XAU/ons_v2'` and one
`unit: ''`, asserting the honest fallback (with its leading space) and absence of the raw code,
plus one `en` render of the same cell.

### DR-2 — dead export `isRatioUnit` (minor, structure)

Exported but referenced nowhere in the app; the ticket's implementation record lists it among
"the only way a component resolves a served unit", which is inaccurate. Delete it, or derive it
from a single `RATIO_UNIT_CODES` set that `unitSuffix` also consults.

### DR-3 / SEC-1 — plain-object lookup walks the prototype chain (minor / low, correctness+security)

`UNIT_LABEL_KEYS[unit] ?? UNKNOWN_UNIT_LABEL_KEY` on an open, server-supplied string means a code
named `constructor`/`toString`/`valueOf`/`hasOwnProperty` yields a function and `__proto__` yields
an object; the `??` fallback does not fire. Verified against i18next@24.2.3: `t()` does not throw
but stringifies them, so `MacroStrip.tsx:118`, `ScreenerScreen.tsx:262/316` and
`screener/criteria.ts:151` would render `15 function Object() { [native code] }` /
`1,24 [object Object]` instead of "Birim belirtilmemiş". Data-only attack path (a seeded/edited
`unit` row; `03` §4 treats `unit` as an open string) — no injection, no XSS, no disclosure; hence
low. It nevertheless defeats the ticket's pinned honesty rule at its core, so it is the cheapest
worthwhile fix in this PR. Fix: `Object.hasOwn(...)` check, `Object.create(null)`, or a `Map`.
The same bare-index pattern pre-exists in `MacroStrip.SERIES_LABEL_KEYS` and
`BALANCE_SHEET_LINE_KEYS` (constant-keyed there, so safe today).

### DR-4 — the paid-in-capital label is attached to the statement line, not 065's served figure (minor as recorded; read this one)

The relabelled row is `lines.sharesOutstanding`, which `StockFundamentalsEndpoints.cs:332` fills
from the **statement** code `SHARES_OUT` (`private const string SharesOut = "SHARES_OUT"`). That is
*not* the `shareCount.sharesOutstanding` object (lines 315-317) that TKT-foundation-065 / D4 Option
A defines as the monetary `currentCapital` figure. The Web does not model or render
`shareCount.*` at all (grep: no `shareCount` in `src/Web/src`, non-test).

Why this is recorded rather than blocking:
- No false claim renders **today**: nothing in the ingestion pipeline produces `SHARES_OUT`
  (`KapXbrlStatementDecoder` maps only `ifrs-full:NumberOfShares → SHARES_DILUTED`), so the line is
  always `null` and `FigureValue` returns the dash before reaching the unit span. The 065 figure is
  simply not displayed on the stock page, which is consistent with 065's own note ("no UI consumer
  displays it yet").
- But the code comment asserts a coupling that does not exist, and if a decoder ever emits a genuine
  `SHARES_OUT` share count the row would read "Ödenmiş sermaye 4.560.000.000 ₺" over a count —
  exactly the FR-RES-017 falsehood this ticket exists to prevent.

Recommendation (non-blocking): record on the 065 implementation slice that `SHARES_OUT` must never
be fed a plain share count while this label is attached, or gate the label/unit on the source once
065 lands. The `data-line="sharesOutstanding"` test hook cements the statement-line identity; if
072's (iii) intent was the `shareCount` block, that delivery is outstanding and belongs to the 065
implementation slice.

### DR-5 — doc comment overstates the map's completeness (nit, i18n)

The comment claims the map is complete for `DegerliSeed.MacroSeriesSql` + `DegerliSeed.ScreenerMetricSql`;
`TRY/g` appears in neither seed (macro serves `% YoY`, `%`, `TRY`, `USD/oz`; screener serves `%`,
`x`) — it exists only in `src/Web/src/test/msw/handlers.ts:86`. Harmless entry, wrong claim; also
the paragraph heading "Fixed-point units and money" is loose. Reword.

## Verified clean (per review angle)

1. **Unknown-unit path — clean, bar DR-1/DR-3.** Every production `.unit` render in the five
   surfaces now resolves through `unitLabel`/`unitSuffix`/`isPercentUnit`; grep over non-test
   `src/Web/src` finds no raw `{…unit}` render remaining. `null`, `undefined`, `''`, whitespace-only
   and unseen codes all land on `units.unknown`; the macro strip has TR **and** EN tests asserting
   both the fallback and the absence of the raw code. The criterion row now guards on `metric`,
   which also removes the old `(undefined)` leak.
   **Saved-screen persistence is semantic:** `SavedScreen.criteria` stores `metricCode` + numeric
   bounds only; `screenCriteriaToDrafts` re-derives `percent` from the *catalog* metric via
   `isPercentUnit(metric?.unit)`; nothing display-side is persisted and no semantic unit is lost on
   round-trip. `SavedScreensScreen` renders no unit.
2. **Presentation-only — clean.** Diff is `src/Web/src/**` + catalogs + the ticket record. No
   contract/DTO/endpoint/seed/migration change; `contracts.ts` and `format.ts` untouched;
   `lines.sharesOutstanding` field name unchanged. No display prose reached the server side.
3. **Paid-in-capital wording — clean in both locales.** "Ödenmiş sermaye" / "Paid-in capital" +
   `₺` / `TRY`; the old "Ödenmiş pay sayısı" / "Shares outstanding" copy is gone from both catalogs
   and from source; `sharesDiluted` keeps its statement label; no other line was relabelled. Row
   assertions exclude `/pay sayısı/i`, `/adet/i`, `/shares/i`. The *source-coupling* caveat is DR-4.
4. **TR/EN parity — clean.** `scripts/i18n-parity.mjs` (in the green Web CI job) reports 673 keys in
   sync; both `units.*` blocks present with matching keys; the removed `sharesOutstanding` line key
   leaves no orphan.
5. **Test placement — adequate for acceptance (ii), not exhaustive.** Per the ticket's scope list
   there is no `units.test.ts`; the map is proven through the three in-scope component tests. The
   macro tests cover all six served codes in TR *and* EN with distinct expected labels
   (`units.percentYoY` ≠ `units.percent`, so the assertions are not tautological) plus unknown code
   in both locales; the screener tests cover select/criterion/result cell with `₺` and
   `not 'TRY'`; the stock-page tests cover label + TRY + no count noun in both locales. Gaps are
   DR-1. Acceptance (ii) is met; the gaps are regression-protection, not a missing criterion.
6. **Architecture / correctness — clean bar DR-2/DR-5.** Follows the DCF `unitKey` precedent
   (code → i18n key → `t()`), centralizes it as the ticket pins, no `any`, no hardcoded prose, no
   styling change (only inert `data-slot` / `data-line` hooks).

## Flow-back / routing items (not findings against this PR)

- **UX wording** — `units.percentYoY`, `units.usdPerOunce`, `units.tryPerGram`, `units.unknown` and
  the paid-in-capital label are `pending ux-designer confirmation`. The ticket states UX wording is
  "a ux-designer flow-back item, recorded, not a blocker"; recorded here.
- **065 ticket status — the coder's flag is stale.** `dev` already carries commit `9fc54ef`
  ("065 records USER DECISION D4/Option A verbatim; display half landed in #137, contract half
  owed"), so 065 now reads `partially resolved — USER DECISION D4 = OPTION A recorded verbatim`.
  No action needed on 065's status; the contract half remains owed by 065's own acceptance (ii).
- **DR-4** as above: record the `SHARES_OUT` constraint on the 065 implementation slice.

## Verdict

**MERGEABLE.** Zero gate findings, CI green on the head, zero blocker-severity diff-reviewer
findings, zero critical/high security findings. Six open non-blocking findings (5 minor/nit, 1 low)
— none of which blocks merge under the mergeability rule; they are cheap follow-ups (DR-1 and
DR-3/SEC-1 are the two worth doing, ideally as a small fix commit before merge rather than after).

The user merges. On confirmation the ticket Status becomes `done` (the orchestrator updates it when
it dispatched this review).
