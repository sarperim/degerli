---
scope:
  - tests/e2e/src/global-ux.spec.ts
  - src/Web/src/routes/StockListPage.tsx
  - src/Web/src/components/AppShell.tsx
---

# TKT-foundation-060: TC-XC-004 — the standing-waived E2E scroll-preservation failure (fix the defect or the test, honestly, from evidence)

- Status: in-review
- Size: S
- **Priority: P2 (user priority #5: CI baseline) — dispatch-ready immediately.** The standing-waived red: TC-XC-004, `tests/e2e/src/global-ux.spec.ts:132` (SCR-002, stock list): **`|Δscroll| < 60` received 157** — red on dev since 2026-10-20, **fails identically everywhere** (local, PR CI, the 049/050/053 review rounds' recorded pre-existing red), currently waived to keep PRs mergeable.
- **Pinned approach — determine first, then fix (honesty doctrine applies to tests too):** the ticket MUST first determine, from evidence, whether this is **(a) an app defect** — the stock list genuinely loses/shifts scroll context on the interaction the test pins (e.g. language toggle, filter application, or list re-render re-anchoring the scroll position) — or **(b) a test defect** — a flaky/incorrect assumption about viewport size, scroll anchoring, lazy-render timing, or data-dependent list length (the E2E world's doubles vs the live list's height). The determination is recorded on the ticket with its evidence (controlled runs, viewport pinning, scroll-position instrumentation) BEFORE a fix is chosen. If (a): fix the app's scroll-context behavior so the interaction preserves position within the pinned tolerance. If (b): fix the test's assumption with justification (e.g. pin the viewport, await the list's stable height, or widen tolerance only with a measured reason — never to make a real defect pass). **A tolerance widening without measured evidence is forbidden.**
- Traces to: SCR-002 (Stock List — the interaction under test), TC-XC-004 (the standing-waived CI baseline red), FR-RES-001–004 lane (stock-list browsing UX); XC-004's cross-cutting concern (scroll-preservation UX quality).
- UX refs: **SCR-002** (the screen whose scroll-preservation is pinned).
- Acceptance: **explicit criteria.** **(i) Determination recorded** on the ticket: app-defect vs test-defect, with the controlled evidence that decided it. **(ii) The fix lands on the honestly-determined side** (app behavior, or the test's assumption — with justification). **(iii) The E2E suite is green on dev and in CI for TC-XC-004**, and the standing waiver is lifted (the waiver record updated to cite this ticket). **(iv) No tolerance change without a measured reason** recorded in the PR. **(v) CI green** (the point of the ticket — the baseline red is gone, not re-waived). **Live criterion (orchestrator):** the E2E suite passes on the dev stack across 3 consecutive runs (flakiness check — the identical-everywhere failure signature makes this the honesty bar). Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.38** (a row for the baseline repair; the XC-004 concern's home is the cross-cutting section — cited, not rewritten).
- Dependencies: none — dispatch-ready. Coordination: shares no files with in-flight lanes (Web routes/components + the E2E spec; 057 in review touches `screens/stock-page` + i18n — disjoint); §13.1 v1.5.38 above the in-review v1.5.35 (057/#120) — last-lander rebases.
- Parallel group: none

## Determination (recorded before any fix) — **(b) TEST DEFECT**, not an app defect

**Verdict.** The stock list does **not** shift or lose scroll context on a language switch. `window.scrollY` is preserved exactly (delta **0**) by the switch. The 157px delta was produced entirely by the *instrumentation*: (1) the test's scroll target was unreachable on the filtered page and silently clamped, and (2) Playwright's `click()` scrolled the non-sticky header back into view before the toggle was pressed.

**Evidence** (worktree `/tmp/opencode/worktrees/TKT-foundation-060` @ `origin/dev@58e6b62`, compose.ci fixture stack, built SPA served on :5273, Chromium 1280×720 unless stated; instrumented runs recording `window.scrollY`, `documentElement.scrollHeight`, `innerHeight` and the toggle's `getBoundingClientRect()`):

| # | Probe | Result |
|---|---|---|
| 1 | Page after sector `A` + search `beta` | 1 row; `scrollHeight 877`, `innerHeight 720` → **maxScroll 157** |
| 2 | `window.scrollTo(0, 250)` on that page | `scrollY = **157**` — the target is clamped to the page bottom; the "mid-page" premise the comment claims is never met, so the comparison measured "bottom → ?" |
| 3 | Header at that position | static, not sticky (`Header.tsx`: `border-b bg-background`, no `sticky`/`fixed`); height **163px**, toggle `top = **−86**` → off-screen |
| 4 | Exactly the failing step: Playwright `click()` on the toggle | `scrollY 157 → **0**` (delta **157**) |
| 5 | **Control A** — Playwright `click()` on an unrelated header button ("Giriş yap") at the same `scrollY 157` | `scrollY 157 → **0**` — the reset is the click's scroll-into-view of an off-screen header control, not language logic |
| 6 | **Control B** — language switch dispatched on the same position (`element.click()` via `evaluate`, no scroll-into-view) | `scrollY 157 → **157**`, delta **0**; `lang=en`, heading EN, search value `beta`, sector `A`, `stock-row-BETA` and the request recorder all intact. **5/5 runs identical** |
| 7 | **Control C** — long unfiltered list (16 rows, maxScroll 724), Playwright `click()` at `scrollY 250` | `250 → **0**` — identical artifact, so it is not list-length dependent |

The app side is therefore clean: `LanguageToggle` calls `i18n.changeLanguage` only (no navigation, no scroll mutation), and the shell keeps the header outside the route `Outlet` (no remount). No app change was made; `StockListPage.tsx` / `AppShell.tsx` are untouched.

**Fix (test side, `tests/e2e/src/global-ux.spec.ts` only).** (1) Pin the viewport to `1280×400` — the filtered page then has a 477px scroll range, so `scrollTo(0, 250)` lands at 250, genuinely mid-page — and assert `scrollBefore === 250` so a future clamp cannot make the check vacuous again. (2) Invoke the toggle's click handler directly (`.evaluate(el => el.click())`) with the reason documented in-code — the technique the repo already uses for the sibling TC-MOV-023, whose comment records exactly this header-scroll artifact. **Tolerance unchanged: `|Δscroll| < 60`** (measured delta after the fix: **0**); no tolerance widening. All other assertions of the case (filters, search, rows, URL, cache-no-refetch) are unchanged.

**Stability evidence.** Case run 5× consecutively (`--repeat-each=5`): **5/5 pass**. Full E2E suite on a **clean compose.ci stack re-created (`down -v` + `up --wait`) before each run**, 3 consecutive runs: **76/76 pass, 76/76 pass, 76/76 pass**.

**PR:** https://github.com/sarperim/degerli/pull/126

**Notes for the review loop.** The standing waiver record lives in the gitignored `.pipeline/plan/reviews/*` files owned by the review-lead; this PR removes the cause, so those records can be updated to cite TKT-foundation-060 and the waiver dropped (this coder lane does not edit review artifacts). The §13.1 change-record row is outside this ticket's file scope (docs are not in `scope:`), hence flagged rather than written. Local full-suite runs used remapped ports (API 18080 / MailPit 18025 / SPA 5273) because the developer's own dev stack owns 8080/8025/5173; two initial failures in `gated-save-lifecycle`/`password-reset` were traced to `APP_URL` in the test stack still pointing at :5173 and vanished once it matched the served SPA — an environment artifact, not a regression.
