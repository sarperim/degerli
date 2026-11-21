---
id: SLICE-036
status: done
scope:
  - .pipeline/slices/036-bronze-reticle-and-difference-white-tick.md
  - src/Web/src/index.css
  - src/Web/src/components/TactileBackdrop.tsx
  - src/Web/src/components/TactileBackdrop.test.tsx
---

# SLICE-036 — Bronze Reticle (`1.5px`) + `11px` White `difference`-Blend `+` Tick

## Context & Goal

Refine `SLICE-035`'s cursorless reticle to match Değerli's warm brushed bronze design language without neon amber or shadows (`MODE 4: Bronze + White Tick`, `TICK SIZE: 11px`, `TICK STROKE: 1.5px`, `WHITE TICK: Difference Blend`):

1. **First-Iteration Brushed Bronze Outer Brackets (`#cursor-reticle`):**
   - Restore `1.5px` corner strokes in `var(--brand-accent)` (`#C68B49` dark / `#9B6123` light) with **zero `filter: drop-shadow`**.
   - Adjust sizing for clean breathing room around the `11px` inner `+` tick: `24×24` idle (`20×20` pressed), tightening to `18×18` (`16×16` pressed) over general interactive targets, and magnetic snap around `[data-magnetic]`, `[data-row-link]`, `[data-sector-bar-row]`.
   - Reset `started = false` in `clearCursor()` so re-entering the viewport snaps the brackets cleanly to the entry coordinate instead of sweeping across the screen.

2. **`11px` `1.5px`-Stroke White `difference`-Blend `+` Tick (`#cursor-dot`):**
   - Keep `1:1` zero-lag coordinate tracking locked directly to `mousemove`.
   - Render `#cursor-dot` as an `11px × 11px` white `#ffffff` `+` crosshair (`1.5px` stroke via `::before` and `::after` inside the `mix-blend-mode: difference` parent, with **zero `box-shadow` or `drop-shadow`**), appearing crisp white on dark obsidian and automatically inverting to black `#000000` over white surfaces and light mode.
   - Smoothly collapse the horizontal arm (`scaleX(0)`, `opacity: 0`) and stretch the vertical arm (`18px`) over text inputs (`.is-text`) to morph `+` into `|`.

3. **Unit Tests (`TactileBackdrop.test.tsx`):**
   - Preserve all existing cursorless lifecycle, `.is-text`, coarse-pointer, and reduced-motion opt-out checks, plus add tests covering `input[type="checkbox"]` exclusion, `window.blur` cleanup, rAF bracket geometry tightening/pressed/magnetic snap sizing, and `isConnected` unmounted-target guard.

## Rules & Invariants

- **Clean isolation:** Touch only `index.css`, `TactileBackdrop.tsx`, `TactileBackdrop.test.tsx`, and this slice doc.
- **Accessibility & OS fallback:** Both `#cursor-reticle` and `#cursor-dot` carry `aria-hidden="true"` and `pointer-events: none`; cursorless mode remains strictly gated behind `(pointer: fine)` and `(prefers-reduced-motion: no-preference)`.

## Blockers & Dependencies

- Builds on `SLICE-035` (`98cd59c`).

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/components/TactileBackdrop.test.tsx` passes (`9/9` tests).
- [ ] CI/E2E Playwright suite passes 100%.

## Result

- Restored first-iteration `var(--brand-accent)` `1.5px` brushed bronze corner brackets on `.cursor-reticle` in `src/Web/src/index.css` with zero `drop-shadow` filter and `24×24` idle (`20×20` pressed) / `18×18` hover (`16×16` pressed) breathing room.
- Replaced `.cursor-dot` with an `11px × 11px` `1.5px`-stroke `#ffffff` `+` crosshair using `mix-blend-mode: difference` (zero shadows), collapsing cleanly into a `1.5px × 18px` `|` I-beam over text inputs via `.is-text`.
- Updated `TactileBackdrop.tsx` sizing (`24/20` idle, `18/16` interactive hover) and reset `started = false` in `clearCursor()`.
- Expanded `TactileBackdrop.test.tsx` to `9/9` passing Vitest tests (checkbox non-text exclusion, `window.blur` cleanup, rAF idle/interactive/pressed/magnetic geometry + `isConnected` unmounted-target guard) and verified `build`, `lint`, `check:i18n` (`890` keys), and `check:advice`.
