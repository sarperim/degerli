---
id: SLICE-024
status: done
scope:
  - .pipeline/slices/024-tactile-canvas-grid-grain-and-reticle.md
  - src/Web/src/styles/tokens.css
  - src/Web/src/index.css
  - src/Web/src/components/TactileBackdrop.tsx
  - src/Web/src/components/AppShell.tsx
  - src/Web/src/components/Header.tsx
  - src/Web/src/components/LanguageToggle.tsx
  - src/Web/src/components/ui/button.tsx
---

# SLICE-024 — Interactive Engineering-Paper Canvas Grid, Film Grain & Magnetic Targeting Reticle (`geminisonuiux.html`)

## Context & Goal

Bring the signature tactile atmosphere and header micro-interactions from `/mnt/c/Users/sarpe/Documents/degerli/geminisonuiux.html` into the global application shell:

1. **Interactive Engineering-Paper Grid Canvas (`TactileBackdrop.tsx` — `#global-grid-canvas`):**
   - Fixed full-viewport `<canvas id="global-grid-canvas" aria-hidden="true">` (`pointer-events-none z-0`) drawing a crisp `48px` engineering-paper grid (`--grid-line`) offset smoothly by `window.scrollY % 48`.
   - When the pointer moves across the viewport, illuminates bronze `+` intersection crosshairs (`--grid-glow`) within a `165px` radius with distance-attenuated alpha (`(1 - d / 165) * 0.85`).
2. **Archival Film-Grain Overlay (`.grain-overlay`):**
   - Fixed full-viewport `<div className="grain-overlay" aria-hidden="true">` (`pointer-events-none z-[80]`) with inline SVG `feTurbulence` fractalNoise (`baseFrequency="0.85" numOctaves="3"`) at `--grain-opacity` (`0.032` dark / `0.025` light).
3. **Magnetic 4-Corner Targeting Reticle (`.cursor-reticle`):**
   - Fixed `<div id="cursor-reticle" aria-hidden="true">` (`pointer-events-none z-[100]`) with 4 bronze corner brackets (`tl`, `tr`, `bl`, `br`, `6px × 6px`, `1.5px solid var(--brand-accent)`).
   - Smoothly interpolates (`requestAnimationFrame`, `0.24` lerp factor) to follow the pointer (`20×20px`) and magnetically expands (`width + 10`, `height + 8`) around hovered `[data-magnetic]`, `[data-row-link]`, and `[data-sector-bar-row]` elements.
   - Automatically disabled on coarse pointers (`(pointer: coarse)`) and when `(prefers-reduced-motion: reduce)` is active.
4. **Topbar & Segmented Toggle Fidelity (`Header.tsx`, `LanguageToggle.tsx`, `ThemeToggle.tsx`):**
   - Add `data-magnetic` attributes to brand, nav links, auth buttons, and segmented `TR | EN` and `D | L` toggles matching `geminisonuiux.html`.
   - Ensure `Header.tsx` preserves its fixed `54px` height (`lg:min-h-[54px]`) so `TC-MOV-023` (zero scroll shift on TR↔EN toggle at `1280×400`) remains 100% green.

## Rules & Invariants

- **Zero axe-core violations (`TC-XC-017`):** All decorative canvas/grain/reticle layers carry `aria-hidden="true"` and `pointer-events-none`. All interactive buttons keep their accessible names matching visible text (no `label-content-name-mismatch`).
- **JSDOM / Vitest compatibility:** Guard canvas `getContext('2d')`, `window.matchMedia`, and `requestAnimationFrame` cleanly so all L3 Vitest tests pass without canvas mocks throwing.
- **Zero scroll shift (`TC-MOV-023`):** Keep `overflow-anchor: none` on `html, body` and fixed header height.

## Blockers & Dependencies

- Depends on `SLICE-018`..`SLICE-022`.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run` passes (15 files / 68 tests in `src/components/` + `src/test/`).

## Result

- Added `TactileBackdrop.tsx` (`#global-grid-canvas` with 48px engineering-paper grid and 165px pointer bronze crosshair illumination, `.grain-overlay` SVG fractalNoise film grain, and `.cursor-reticle` 4-corner magnetic targeting reticle with `requestAnimationFrame` interpolation and coarse-pointer / reduced-motion guards).
- Added `--grid-glow`, `--crosshair`, `--curve-stroke`, `--curve-fill`, and `--grain-opacity` tokens to `tokens.css` (both light and dark themes) and tactile CSS rules to `index.css`.
- Wired `TactileBackdrop` into `AppShell.tsx` and added `data-magnetic` targets to `Header.tsx`, `LanguageToggle.tsx`, and `button.tsx`.
