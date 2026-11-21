---
id: SLICE-038
status: done
scope:
  - .pipeline/slices/038-center-hero-above-kpi-ribbon.md
  - src/Web/src/routes/MarketOverviewPage.tsx
---

# SLICE-038 — Center Hero Copy & Photo Plate Above KPI Ribbon

## Context & Goal

Center the Market Overview hero copy and Salt Galata photo plate vertically inside the exact space remaining above the pinned 4-column KPI ribbon (`BIST 100 · XU100`, `BIST 30 · XU030`, `Breadth`, `Covered Volume`) on desktop viewports.

- Replace the asymmetric `lg:pt-8 lg:pb-[16svh]` padding on `<header ref={heroRef} id="hero-section">` in `src/Web/src/routes/MarketOverviewPage.tsx` (line 781) with symmetric `lg:py-16`, letting the existing `flex flex-1 items-center` container center the hero grid at the exact 50/50 vertical midpoint above the KPI ribbon while keeping `lg:min-h-[calc(100svh-55px)]` unchanged.

## Rules & Invariants

- **Clean isolation:** Touch only `src/Web/src/routes/MarketOverviewPage.tsx` and this slice file.
- **Responsive contract:** Preserve `py-14 sm:py-16` flow on mobile/tablet (`< lg`) and full-fold KPI ribbon framing at `>= lg`.

## Blockers & Dependencies

- Builds on `SLICE-037` (`2ce12a9`).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npx vitest run src/routes/MarketOverviewPage.test.tsx` passes.
- [ ] Diff review via `opencode run -m evren/glm-5.3` approves the slice cleanly.
- [ ] CI/E2E Playwright suite passes 100%.

## Result

Replaced the asymmetric desktop hero padding `lg:pt-8 lg:pb-[16svh]` with symmetric `lg:py-16` on `<header ref={heroRef} id="hero-section">` in `src/Web/src/routes/MarketOverviewPage.tsx` (line 781).

- The existing `flex flex-1 items-center` container now centers the hero grid at the 50/50 vertical midpoint of the space remaining above the pinned 4-column KPI ribbon.
- `lg:min-h-[calc(100svh-55px)]` and the mobile/tablet flow (`py-14 sm:py-16`) are unchanged.
- No other files were touched; local build, lint, and `MarketOverviewPage.test.tsx` verified via the checked box above.
