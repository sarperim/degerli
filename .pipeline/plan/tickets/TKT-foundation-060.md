---
scope:
  - tests/e2e/src/global-ux.spec.ts
  - src/Web/src/routes/StockListPage.tsx
  - src/Web/src/components/AppShell.tsx
---

# TKT-foundation-060: TC-XC-004 — the standing-waived E2E scroll-preservation failure (fix the defect or the test, honestly, from evidence)

- Status: todo
- Size: S
- **Priority: P2 (user priority #5: CI baseline) — dispatch-ready immediately.** The standing-waived red: TC-XC-004, `tests/e2e/src/global-ux.spec.ts:132` (SCR-002, stock list): **`|Δscroll| < 60` received 157** — red on dev since 2026-10-20, **fails identically everywhere** (local, PR CI, the 049/050/053 review rounds' recorded pre-existing red), currently waived to keep PRs mergeable.
- **Pinned approach — determine first, then fix (honesty doctrine applies to tests too):** the ticket MUST first determine, from evidence, whether this is **(a) an app defect** — the stock list genuinely loses/shifts scroll context on the interaction the test pins (e.g. language toggle, filter application, or list re-render re-anchoring the scroll position) — or **(b) a test defect** — a flaky/incorrect assumption about viewport size, scroll anchoring, lazy-render timing, or data-dependent list length (the E2E world's doubles vs the live list's height). The determination is recorded on the ticket with its evidence (controlled runs, viewport pinning, scroll-position instrumentation) BEFORE a fix is chosen. If (a): fix the app's scroll-context behavior so the interaction preserves position within the pinned tolerance. If (b): fix the test's assumption with justification (e.g. pin the viewport, await the list's stable height, or widen tolerance only with a measured reason — never to make a real defect pass). **A tolerance widening without measured evidence is forbidden.**
- Traces to: SCR-002 (Stock List — the interaction under test), TC-XC-004 (the standing-waived CI baseline red), FR-RES-001–004 lane (stock-list browsing UX); XC-004's cross-cutting concern (scroll-preservation UX quality).
- UX refs: **SCR-002** (the screen whose scroll-preservation is pinned).
- Acceptance: **explicit criteria.** **(i) Determination recorded** on the ticket: app-defect vs test-defect, with the controlled evidence that decided it. **(ii) The fix lands on the honestly-determined side** (app behavior, or the test's assumption — with justification). **(iii) The E2E suite is green on dev and in CI for TC-XC-004**, and the standing waiver is lifted (the waiver record updated to cite this ticket). **(iv) No tolerance change without a measured reason** recorded in the PR. **(v) CI green** (the point of the ticket — the baseline red is gone, not re-waived). **Live criterion (orchestrator):** the E2E suite passes on the dev stack across 3 consecutive runs (flakiness check — the identical-everywhere failure signature makes this the honesty bar). Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.38** (a row for the baseline repair; the XC-004 concern's home is the cross-cutting section — cited, not rewritten).
- Dependencies: none — dispatch-ready. Coordination: shares no files with in-flight lanes (Web routes/components + the E2E spec; 057 in review touches `screens/stock-page` + i18n — disjoint); §13.1 v1.5.38 above the in-review v1.5.35 (057/#120) — last-lander rebases.
- Parallel group: none
