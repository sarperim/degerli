---
scope:
  - tests/e2e/src/admin.spec.ts
  - tests/e2e/src/helpers/
  - tests/e2e/playwright.config.ts
---

# TKT-res-011: Fix TC-RES-034 timezone date-boundary defect in admin.spec.ts

- Status: done — merged to `dev` 2026-10-14T23:52Z (PR https://github.com/sarperim/degerli/pull/72, merge commit `ef739bc`). Review round 1 was **ESCALATED** on gate finding G-1 `missing_tc=TC-XC-014`; the builder **waived G-1** (TC-XC-014 — the 5-minute E2E CI budget guardrail — is base-delivered by `tests/e2e/src/budget.spec.ts` / `config.ts` / `playwright.config.ts`, and is satisfied by the green E2E job on `6bb5bd1` at 3m17s; the ticket only cross-references it in acceptance (4), so no in-diff coverage was required). Code was clean: CI green on `6bb5bd1` (Backend/Web/E2E, run 38653525500), facet gate otherwise clean, `security_relevant=false`, diff-reviewer approve-with-nits (DR-1/DR-2 non-blocking), no test weakened. Review artifact: `.pipeline/plan/reviews/TKT-res-011-round-1.md`. Unblocks PR #71 (TKT-int-003) and PR #66 (TKT-int-004) re-gates; then TKT-int-006 (P-19).
- PR: https://github.com/sarperim/degerli/pull/72
- Size: S
- Scope: test-defect fix on the TKT-res-010 deliverable. `tests/e2e/src/admin.spec.ts` computes expected rendered dates Node-side — `new Intl.DateTimeFormat('tr', { dateStyle: 'medium' }).format(...)` at ~line 145 (admin-summary gold-row last-success) and ~line 300 (TC-RES-034 last-reviewed) — while the browser context is pinned to `locale: 'tr-TR'`, `timezoneId: 'Europe/Istanbul'` (UTC+3) in `tests/e2e/playwright.config.ts`. The Node test process runs in the runner's TZ (UTC on GitHub runners), so between 21:00:00–23:59:59 UTC the two environments are on different calendar days and the date assertions fail — a deterministic daily failure window, not a random flake. Confirmed on `dev`: runs 38643738824 and 38644590441 (2026-10-14T22:05Z) fail TC-RES-034's final assertion — the page rendered `Son inceleme: 15 Eki 2026` while the test expected `Son inceleme: 14 Eki 2026` (Playwright error context in the runs' uploaded reports). Test files only — the app is correct (it renders in the pinned browser context); `playwright.config.ts` may be touched only to export the pinned timezone constant if that fix variant is chosen (no behavior change); no `src/` changes.
- Traces to: FR-RES-021, UC-RES-004 (public-effect step), TC-RES-034
- Acceptance (explicit — test-defect fix; TC-RES-034 stays the contract): TC-RES-034 green, and (1) no date expectation in `admin.spec.ts` is computed by Node-side `Intl` formatting of an API timestamp — the expected string is derived in the browser context (e.g. `page.evaluate` running the same `Intl.DateTimeFormat('tr', { dateStyle: 'medium' })` options the app's `formatAsOfDate` uses) or pinned to the config's `timezoneId` via a constant shared from the config, never re-hardcoded; (2) both occurrences of the pattern are fixed (~145 and ~300); (3) the spec's date assertions are invariant to the Node process timezone, demonstrated by running the TC-RES-034 spec locally under two shifted `TZ` values (e.g. `TZ=UTC` and `TZ=Pacific/Kiritimati`); (4) the full E2E suite is green on the PR head within the 5-minute budget (TC-XC-014).
- Architecture refs: `01-system-architecture.md` §6.1 (e2e job); `.pipeline/testing/01-fixture-universe.md` §2 (calendar, units, and config); `.pipeline/testing/stock-research.md` TC-RES-034
- UX refs: — (test-only; no screen or interaction change)
- Dependencies: none — lands directly on `dev`, whose E2E job is currently red because of this defect; must land before PR #71 (TKT-int-003) is re-gated
- Parallel group: P-23 (solo hotfix group — dispatch immediately, ahead of the P-19 transitive chain)
