---
id: SLICE-035
status: done
scope:
  - .pipeline/slices/035-cursorless-tactile-reticle.md
  - src/Web/src/index.css
  - src/Web/src/components/TactileBackdrop.tsx
  - src/Web/src/components/TactileBackdrop.test.tsx
---

# SLICE-035 — Cursorless High-Contrast Tactile Reticle (1-Commit Revertible)

## Context & Goal

Replace the OS pointer on fine-pointer, non-reduced-motion desktops with a responsive, high-contrast dual-layer reticle without losing pinpoint usability or native typing feedback — kept strictly isolated in its own commit/PR so it can be cleanly reverted with `git revert <sha>` if desired:

1. **Zero-Lag 1:1 Center Dot (`#cursor-dot`) + Trailing Corner Brackets (`#cursor-reticle`):**
   - Lock the `3px × 3px` amber (`#f59e0b`) center focal dot 1:1 to real pointer coordinates (`mousemove` / `mousedown` / `mouseup`, zero CSS transform transition lag) so clicking compact navbar targets (`Türkçe` | `English`, `Sign in`) feels instant.
   - Keep smooth rAF interpolation (`0.24` lerp) exclusively on the outer `#cursor-reticle` corner brackets.

2. **High Contrast Across Dark & Light Surfaces:**
   - Bump reticle corners & center dot to full-opacity amber `#f59e0b` (`2px` corner strokes) with a crisp dark dual drop-shadow / outline so the reticle remains clearly legible over dark grid areas AND over pure-white buttons/headlines (`Screen BIST 100`, `Register`).

3. **Context-Aware Interactive & Text-Input States:**
   - **Cursorless activation:** Only apply `cursor: none !important` (via `html.cursorless-active`) inside `@media (pointer: fine) and (prefers-reduced-motion: no-preference)` when JS pointer tracking is active, and cleanly remove `html.cursorless-active` on `mouseleave`, `blur`, and unmount (safe fallback if JS/reduced-motion/touch is active).
   - **Buttons & links:** Slightly tighten outer reticle brackets inward (`14×14`, `11×11` pressed vs `22×22`, `18×18` pressed) on general interactive controls (`a, button, select, summary, label[for], [role="button"], [role="link"], [role="tab"], [role="option"], [role="menuitem"], [tabindex]:not([tabindex="-1"])`) and preserve magnetic bracket snap on `[data-magnetic], [data-row-link], [data-sector-bar-row]`.
   - **Text inputs:** When hovering text-editable controls (`textarea`, `[contenteditable="true"]`, `[contenteditable=""]`, `[role="textbox"]`, or text-like `<input>`), hide outer corner brackets (`is-text`) and morph `#cursor-dot` into a vertical `2px × 18px` I-beam cursor.

4. **Unit Tests (`TactileBackdrop.test.tsx`):**
   - Verify `aria-hidden="true"`, 1:1 `#cursor-dot` coordinate sync, `html.cursorless-active` lifecycle (`mousemove`, `mouseleave`, unmount), `.is-text` morphing over `<input>`/`<textarea>`, interactive/magnetic activation, and opt-out on coarse pointer / `prefers-reduced-motion: reduce`.

## Rules & Invariants

- **Isolation for 1-commit revertibility:** Touch only CSS reticle rules, `TactileBackdrop.tsx`, and its dedicated test file. Do not touch screen components or E2E specs unless CI reveals an issue.
- **Accessibility:** Both `#cursor-reticle` and `#cursor-dot` carry `aria-hidden="true"` and `pointer-events: none`; never activate `cursorless-active` under `prefers-reduced-motion: reduce` or coarse pointers.

## Blockers & Dependencies

- Depends on `SLICE-031`..`SLICE-034` being merged first so this slice sits cleanly at `HEAD` of `dev`.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/components/TactileBackdrop.test.tsx` passes (6/6 tests).
- [ ] CI/E2E Playwright suite passes 100%.

## Result

- Added high-contrast dual-layer reticle CSS in `src/Web/src/index.css`: `html.cursorless-active` hides OS cursor only under `@media (pointer: fine) and (prefers-reduced-motion: no-preference)`; upgraded `.cursor-reticle` to full-opacity `#f59e0b` `2px` corners with dual dark drop-shadow; added zero-lag `3px × 3px` `.cursor-dot` with `.is-text` (`2px × 18px` I-beam) morph and hidden rules under coarse/reduced-motion.
- Upgraded `src/Web/src/components/TactileBackdrop.tsx` with 1:1 `#cursor-dot` coordinate sync, trailing rAF corner brackets tightening on interactive controls and snapping on `[data-magnetic]`, text-input detection, `isConnected` guard, and `mouseleave`/`blur`/unmount `cursorless-active` cleanup.
- Added 6 Vitest tests in `src/Web/src/components/TactileBackdrop.test.tsx` and verified `tsc`, `vite build`, `eslint`, `check:i18n`, `check:advice`, and `vitest`.
