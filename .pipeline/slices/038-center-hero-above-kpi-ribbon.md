---
id: SLICE-038
status: done
scope:
  - .pipeline/slices/038-center-hero-above-kpi-ribbon.md
  - src/Web/src/routes/MarketOverviewPage.tsx
  - src/Web/src/screens/market-overview/HeroInteractivePlate.tsx
  - src/Web/src/screens/market-overview/HeroInteractivePlate.test.tsx
---

# SLICE-038 — Center Hero Copy & Photo Plate Above KPI Ribbon

## Context & Goal

Center the Market Overview hero copy and Salt Galata photo plate vertically inside the exact space remaining above the pinned 4-column KPI ribbon (`BIST 100 · XU100`, `BIST 30 · XU030`, `Breadth`, `Covered Volume`) on desktop viewports.

- Replace the asymmetric `lg:pt-8 lg:pb-[16svh]` padding on `<header ref={heroRef} id="hero-section">` in `src/Web/src/routes/MarketOverviewPage.tsx` (line 781) with symmetric `lg:py-16`, letting the existing `flex flex-1 items-center` container center the hero grid at the exact 50/50 vertical midpoint above the KPI ribbon while keeping `lg:min-h-[calc(100svh-55px)]` unchanged.
- Keep the archival photo plate canvas dark in light mode without forcing the whole frame dark: `HeroInteractivePlate` always renders the dark-room Bayer 4×4 duotone palette (`#111316` / `#444B54` / `#C68B49` / `#E0DCD4`), while `#photo-plate`, `#photo-viewport`, the corner `+` marks, HUD badges, and caption row keep their theme-aware tokens (`bg-card`, `border-border`, `bg-background`, `text-muted-foreground`, `var(--stale-foreground)`) so the frame adapts to light/dark mode.

## Rules & Invariants

- **Clean isolation:** Touch only `src/Web/src/routes/MarketOverviewPage.tsx`, `src/Web/src/screens/market-overview/HeroInteractivePlate.tsx`, its test, and this slice file.
- **Responsive contract:** Preserve `py-14 sm:py-16` flow on mobile/tablet (`< lg`) and full-fold KPI ribbon framing at `>= lg`.

## Blockers & Dependencies

- Builds on `SLICE-037` (`2ce12a9`).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npx vitest run src/routes/MarketOverviewPage.test.tsx` passes.
- [x] `cd src/Web && npx vitest run src/screens/market-overview/HeroInteractivePlate.test.tsx && npx tsc --noEmit` passes — the light-mode canvas only uses the dark palette levels and never the white `[246, 245, 240]` level, while the outer frame keeps theme-token classes.
- [ ] Diff review via `opencode run -m evren/glm-5.3` approves the slice cleanly.
- [ ] CI/E2E Playwright suite passes 100%.

## Result

Replaced the asymmetric desktop hero padding `lg:pt-8 lg:pb-[16svh]` with symmetric `lg:py-16` on `<header ref={heroRef} id="hero-section">` in `src/Web/src/routes/MarketOverviewPage.tsx` (line 781).

- The existing `flex flex-1 items-center` container now centers the hero grid at the 50/50 vertical midpoint of the space remaining above the pinned 4-column KPI ribbon.
- `lg:min-h-[calc(100svh-55px)]` and the mobile/tablet flow (`py-14 sm:py-16`) are unchanged.
- No other files were touched for the padding change; local build, lint, and `MarketOverviewPage.test.tsx` verified via the checked box above.

### Follow-up: light-mode archival plate

- `HeroInteractivePlate` no longer calls `useTheme()`; `useHeroBackdropCanvas` still uses it for the backdrop curve.
- The Bayer 4×4 duotone palette is now fixed to the dark-room levels `[[17, 19, 22], [68, 75, 84], [198, 139, 73], [224, 220, 212]]`, so the white `[246, 245, 240]` light-mode level is gone.
- The outer frame is theme-aware again: `#photo-plate` uses `border-[var(--border-strong)] bg-card`, the corner `+` marks and caption row use `text-muted-foreground`, `#photo-viewport` uses `border-border bg-background`, the HUD coordinate badge uses `text-muted-foreground`, and the HUD Bayer badge plus `Swap photo` label use `text-[var(--stale-foreground)]`. The dark-room look is confined to the canvas palette.
- `HeroInteractivePlate.test.tsx` asserts both halves: only the dark palette levels are rendered onto the canvas, and the plate/viewport/corner/HUD/caption classes resolve through theme tokens rather than fixed dark hex surfaces.
