---
id: SLICE-039
status: done
scope:
  - .pipeline/slices/039-split-rail-fill-scroll-indicator.md
  - src/Web/src/index.css
  - src/Web/src/components/AppShell.tsx
  - src/Web/src/components/TactileBackdrop.tsx
  - src/Web/src/components/ViewportScrollRail.tsx
  - src/Web/src/components/ViewportScrollRail.test.tsx
---

# SLICE-039 — Split-Rail Fill Viewport Scroll Indicator

## Context & Goal

Replace the browser's native main-viewport scrollbar on desktop with a compact,
editorial Split-Rail Fill indicator that matches the bronze archival language of
the rest of the app. The rail must be visually smaller than a regular scrollbar,
read as a 1px architectural line at rest, widen only while hovered/dragged, and
expose a drag-only percentage readout next to a 4×1px split tick.

- Fixed 22px transparent hit target at `right: 2px`, with the inner rail centered
  at `right: 10px` inside the hit target (12px from the viewport edge).
- Compact vertical span: `top: clamp(88px, 16vh, 148px)` and
  `bottom: clamp(48px, 14vh, 132px)`, sitting clear of the 55px sticky header.
- `z-index: 45`: above page content/header, below `z-50` modals and the
  `z-100`/`z-101` reticle cursor.
- Progress is a `var(--brand-accent)` fill scaled with `transform: scaleY(progress)`;
  the split tick/readout move with `translate3d` only.
- Native main-viewport scrollbars are hidden on fine pointers; nested scroll
  containers keep a quiet 6px architectural scrollbar.

## Rules & Invariants

- **Fine-pointer only:** the rail and listener setup are gated on
  `(hover: hover) and (pointer: fine)` and opt out entirely on `(pointer: coarse)`
  / `(hover: none)`.
- **60fps GPU updates:** scroll/resize/`ResizeObserver` callbacks only schedule a
  `requestAnimationFrame`; all style/text/`aria-valuenow` writes happen there.
  No React renders per scroll frame.
- **Auto-hide:** hidden with `data-overflow="false"` and `pointer-events: none`
  when the document does not overflow; reveals on scroll, right-edge proximity
  (≤48px), hover, or drag; fades after 1000ms idle with the pointer away.
- **Gesture parity:** pointerdown jumps to the click ratio, pointer capture +
  document-body `scroll-rail-dragging` support 1:1 scrubbing, and
  pointerup/cancel/lostpointercapture cleanly end the drag.
- **Native scrollbar scoping:** the desktop scrollbar suppression is scoped to
  `html.scroll-rail-active` / `html.scroll-rail-active body` (plus their
  `::-webkit-scrollbar` pseudo-elements) inside
  `@media (hover: hover) and (pointer: fine)`. The class is added when the
  fine-pointer effect mounts and removed on cleanup, so native scrollbars are
  hidden only while `ViewportScrollRail` is actually mounted and active.
- **Pointer gating:** the rail is inert at rest (`pointer-events: none`) and only
  becomes interactive via
  `.viewport-scroll-rail[data-overflow='true'].is-visible { pointer-events: auto }`,
  so the faded-out rail never intercepts right-edge clicks.
- **Deliberate non-focusable keyboard design:** `tabIndex` is intentionally
  omitted. Native document keyboard scrolling (Arrow/Page/Home/End/Space) is
  preserved, so keyboard users are not injected with an extra tab stop inside
  AppShell's focus order. `aria-valuenow` is owned imperatively by the rAF loop
  (the single writer per frame); React supplies only the initial `0` and the
  static scrollbar semantics.
- **Accessibility:** `role="scrollbar"`, `aria-controls="app-main"`, vertical
  orientation, `aria-valuemin/max/now`, and a label; rendered inside `<main id="app-main">`
  so the scrollbar is owned by the main landmark without adding a second
  complementary landmark.
- **Honest-data contract:** no data is displayed or fabricated; the readout is a
  decorative `aria-hidden` percentage and the rail carries no advice vocabulary.

## Blockers & Dependencies

- Builds on `SLICE-024`/`SLICE-035`/`SLICE-036` (TactileBackdrop reticle), which
  now treats `[role="scrollbar"]` as an interactive control.
- Uses existing tokens `--brand-accent`, `--color-border`, `--border-strong`,
  `--color-muted-foreground`, and `--font-mono`; no new tokens.

## Verification

- [x] `cd src/Web && npm run build` — typecheck + production build pass.
- [x] `cd src/Web && npm run lint` — no errors (two pre-existing
  `HeroInteractivePlate.tsx` fast-refresh warnings only).
- [x] `cd src/Web && npm run check:i18n` — 889 keys in sync.
- [x] `cd src/Web && npm run check:advice` — no recommendation vocabulary.
- [x] `npx vitest run src/components/ViewportScrollRail.test.tsx
  src/components/TactileBackdrop.test.tsx src/components/DataSourceDisclaimer.test.tsx
  src/routes/MarketOverviewPage.test.tsx` — 57 tests pass across 4 files.
- [x] Rail axe-core audit inside `<main id="app-main">` reports zero violations.
- [x] Round-1 review fixes: `html.scroll-rail-active` scoping, `pointer-events`
  gated on `.is-visible`, unused `.is-hovered` selector removed, no-overflow
  visual/`aria-valuenow` reset, and non-focusable keyboard design documented.
- [x] Diff review via review agents.
- [ ] CI / Playwright e2e suite.

## Result

Implemented `ViewportScrollRail` as a fine-pointer-only fixed overlay rendered
inside the app main landmark:

- `src/Web/src/components/ViewportScrollRail.tsx` — compact Split-Rail Fill
  indicator with base rail, `scaleY` bronze fill, 4×1px split tick, drag-only
  monospaced percentage readout, overflow gating, 1s idle fade, 48px right-edge
  proximity reveal, click-to-jump, pointer-capture drag scrubbing, and guarded
  `ResizeObserver`/resize recalculation. All per-frame writes are batched in a
  single `requestAnimationFrame`; pointer capture is wrapped in `try/catch` for
  jsdom. Accessibility attributes are wired to `app-main`.
- `src/Web/src/components/AppShell.tsx` — `<main>` now has `id="app-main"` and
  renders `<ViewportScrollRail />` inside it, preserving landmark structure.
- `src/Web/src/components/TactileBackdrop.tsx` — `[role="scrollbar"]` added to
  `INTERACTIVE_SELECTOR` so the reticle tightens over the rail.
- `src/Web/src/index.css` — hides native `html`/`body` scrollbars on fine
  pointers, adds the quiet 6px nested-container scrollbar, styles the rail
  (base/fill/tick/readout, hover/drag width, body drag `user-select: none`),
  reduced-motion shortening, and coarse-pointer display suppression.
- `src/Web/src/components/ViewportScrollRail.test.tsx` — 9 tests covering
  semantics + axe, no-overflow hiding, progress/tick/readout updates, idle fade,
  resize recalculation, right-edge proximity, click/drag scrubbing cleanup, and
  coarse-pointer opt-out. jsdom's missing `PointerEvent` is worked around with
  pointer-typed `MouseEvent`s carrying real coordinates.

### Round 1 review fixes

- `html`/`body` native scrollbar suppression is now scoped to
  `html.scroll-rail-active` (added on fine-pointer mount, removed on cleanup),
  so it no longer leaks to desktop contexts where the rail is unmounted.
- `pointer-events: auto` moved onto
  `.viewport-scroll-rail[data-overflow='true'].is-visible`, so the faded-out rail
  cannot intercept right-edge clicks; the standalone `data-overflow` rule was
  removed.
- Dropped the unused `.viewport-scroll-rail.is-hovered` selector (kept `:hover`
  and `.is-dragging`).
- `render()` now resets fill/tick/readout transforms, readout text, and
  `aria-valuenow` to `0` when overflow disappears, avoiding stale progress.
- Added tests for `html.scroll-rail-active` mount/unmount on fine pointers, its
  absence on coarse pointers, and `aria-valuenow` reset on overflow removal.
