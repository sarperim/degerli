---
ticket: TKT-foundation-072
round: 2
pr: 137
base: origin/dev
head: ba25b0b301346357902da457dcaa0e77c70f6ec3
verdict: MERGEABLE
gates:
  facet: clean
  ci: green (Backend, E2E, Web — run 40064703966, headSha ba25b0b)
reviewers:
  - diff-reviewer (delta)
  - security-reviewer (delta)
open_blocking: 0
open_findings: 4
closed_findings: 6
---

# TKT-foundation-072 — review round 2 (commissioned fix round)

PR: https://github.com/sarperim/degerli/pull/137 · branch `feature/TKT-foundation-072`
Head at review: `ba25b0b301346357902da457dcaa0e77c70f6ec3`
Ticket: `.pipeline/plan/tickets/TKT-foundation-072.md`
Review worktree: `/tmp/opencode/worktrees/TKT-foundation-072-review`
Round 1: `.pipeline/plan/reviews/TKT-foundation-072-round-1.md`

This round was **commissioned by the dispatcher** for two named findings (DR-3/SEC-1, DR-1)
plus the two trivial closes (DR-2, DR-5). DR-4 was explicitly excluded (recorded design
constraint being recorded on the 065 implementation slice) and UX copy remains a
ux-designer flow-back item. No full re-review was run; delta verification only.

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

Gate findings: **none** (`missing_tc` empty, `out_of_scope` empty). Same classification as
round 1; the fix commits added no files and no new path tokens.

## Gate 2 — CI gate

Intermediate head `f942d9b` — run 40063450453, headSha `f942d9b9ee5aebaaf9299988f4d0ab9a50e17e98`:
Backend pass · Web pass · E2E pass.

**Final head `ba25b0b` — run 40064703966, verified `headSha=ba25b0b301346357902da457dcaa0e77c70f6ec3`:**

| Check | Result |
|---|---|
| Backend (restore · build · test) | pass (2m50s) |
| E2E (compose · Playwright · MailPit) | pass (3m38s) |
| Web (lint · typecheck · test · i18n · build) | pass (1m07s) |

Run: https://github.com/sarperim/degerli/actions/runs/40064703966

The run is bound to the final head SHA (not merely "pushed"); `gh pr view 137` reports
`headRefOid=ba25b0b…` and `mergeable=MERGEABLE`. Gate findings: **none**.

## Fix commits

| SHA | Subject | Files |
|---|---|---|
| `f942d9b` | fix(web): own-property unit lookup; test the unitSuffix fallback | `lib/units.ts`, `screener/ScreenerBuilder.test.tsx`, ticket record |
| `ba25b0b` | docs(web): finish the units.ts header accuracy fix (DR-5) | `lib/units.ts` (2 comment lines) |

Cumulative diff `f872229..ba25b0b`: 3 files, +119/−12. `StockSections.tsx`, catalogs,
contracts, and architecture docs untouched.

## Delta dispatch (data-driven)

`presentation_only=false` → diff-reviewer delta. `security_relevant=true` → security-reviewer
delta, in parallel. Both received only their own prior findings + the fix commits; neither ran
the test suite (CI is the test authority).

## Findings — round-2 status

| id | source | severity | type | status | evidence |
|---|---|---|---|---|---|
| DR-1 | diff-reviewer | minor | test-faithfulness | **fixed-verified** | `ScreenerBuilder.test.tsx`: unknown `XAU/ons_v2` and empty `''` through the real result cell assert `' ' + units.unknown` (explicit leading space) and no raw code; EN render asserts "Unit not specified". |
| DR-2 | diff-reviewer | minor | dead code | **fixed-verified** | `isRatioUnit` deleted; repo-wide grep empty; ticket record bullet corrected. |
| DR-3 | diff-reviewer | minor | correctness | **fixed-verified** | `lib/units.ts:77-81` gates on `Object.hasOwn(UNIT_LABEL_KEYS, unit)`; TS lib ES2022. |
| SEC-1 | security-reviewer | low | security | **fixed-verified** | Same guard; security delta walked every server-code → label path (`unitLabel`/`unitSuffix`/`formatMetricValue`/`MacroStrip`/`ScreenerScreen`/`StockSections`) and found no residual non-string sink. |
| DR-5 | diff-reviewer | nit | doc accuracy | **fixed-verified** | Map comment corrected in `f942d9b`; module header overclaim and "Fixed-point units and money" heading corrected in `ba25b0b`. |
| DR-6 | diff-reviewer (delta) | nit | doc accuracy | **fixed-verified** | Residual DR-5 (header line 7 + heading) found by the delta and fixed in `ba25b0b`; commit is comment-only (`git diff f942d9b..ba25b0b` = 2 lines). |
| DR-4 | diff-reviewer | minor (latent-major if it fires) | coordination | **open by design — routed** | Untouched as instructed. The dispatcher is recording the `SHARES_OUT` constraint on the 065 implementation slice. Not a defect in this PR. |
| DR-7 | diff-reviewer (delta) | nit | test structure | **open — non-blocking** | New tests add a local `renderScreenerIn` harness instead of extending `testSupport.renderScreener`; two render setups now coexist. No behavioural effect. |
| DR-8 | diff-reviewer (delta) | nit | test convention | **open — non-blocking** | `expect(catalogValue('en','units.unknown')).toBe('Unit not specified')` hardcodes EN prose against the `test/i18n.ts` "never hardcoded prose" convention; it does pin the ticket's pinned wording, so defensible. |
| SEC-N1 | security-reviewer (delta) | nit | test hardening | **open — non-blocking** | Regression test exercises `constructor` only; `__proto__`/`toString`/`valueOf` are covered by the same `hasOwn` gate but not individually asserted. |

No finding was waived, downgraded, or dropped by the lead. DR-3 and SEC-1 are the same defect
fixed once; both lanes independently verified closure.

### Regression-test strength (verified, not taken on trust)

- **Prototype chain:** pre-fix `unitSuffix('constructor')` resolved `Object` via the prototype
  chain and rendered `12,5 function Object() { [native code] }`; the test asserts the honest
  label and the absence of `function` / `[object Object]` / `constructor`. Genuine red → green,
  confirmed against the pre-fix tree and by the delta reviewer.
- **`unitSuffix` edge:** uses `textContent` (not `toHaveTextContent`, which normalizes
  whitespace) to prove the leading space survives; would fail on a raw-code passthrough or a
  dropped separator.
- **EN:** `renderWithProviders({ language: 'en' })` is honoured by the harness
  (`test/render.tsx`), so the EN catalog value is genuinely exercised.
- No existing assertion was weakened — the test change is pure addition.

## Nothing else moved

Delta check of `f872229..ba25b0b` confirms exactly:
1. the `Object.hasOwn` guard (+ explanatory comment),
2. deletion of `isRatioUnit` and the corresponding ticket-record correction,
3. the doc-comment/heading rewording,
4. the added screener tests.
No behaviour, scope, styling, catalog, contract, dependency, or architecture change. DR-4 and
UX copy untouched.

## Flow-back / routing items

- **DR-4** — `SHARES_OUT` must never be fed a plain share count while the paid-in-capital label
  is attached; dispatcher is recording it on the 065 implementation slice.
- **UX wording** — `units.percentYoY`, `units.usdPerOunce`, `units.tryPerGram`, `units.unknown`,
  paid-in-capital label remain pending ux-designer confirmation (recorded, not a blocker, per
  the ticket).
- **DR-7 / DR-8 / SEC-N1** — cheap test-only nits; never block merge.

## Verdict

**MERGEABLE.** Zero gate findings; CI green on the final head `ba25b0b` with the run verified
against that SHA; zero blocking diff-reviewer findings; zero critical/high security findings;
SEC-1 fixed-verified. DR-1, DR-2, DR-3, DR-5 and SEC-1 are closed. Four open non-blocking
findings remain (DR-4 routed by design; DR-7/DR-8/SEC-N1 nits). The user merges — the review
lead does not merge.
