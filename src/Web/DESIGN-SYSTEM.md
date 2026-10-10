# Değerli design system (in-repo, web-first)

Foundation for every `/src/Web` screen (TKT-foundation-011). Authored directly as
code — no design tool (decision D-UX-TOOL). Stack: **Tailwind CSS v4 + shadcn/
Radix primitives** (architecture §5 / M-8). Visual polish is intentionally
functional-first.

Live index: run `npm run dev` and open **`/design-system`**, or run `npm run build
&& npm run preview` and open the same path in the production build. The index
renders every token and component below at desktop and narrow widths.

## Tokens — `src/styles/tokens.css`

Exposed as Tailwind utilities via `@theme inline`.

- **Colour**: shadcn semantic tokens (`background`, `foreground`, `primary`,
  `muted`, `destructive`, …) plus honest-data/status tokens: `stale`,
  `restated`, `adjusted`, `not-meaningful`, `success`, `warning`, `info`
  (each with a `-foreground` pair). Light + `.dark` sets.
- **Spacing**: numeric Tailwind scale (`p-4`, `gap-3`) plus semantic tokens
  `gutter`, `section`, `stack`. Raw `--space-*` primitives are in `:root`.
  ⚠ Do **not** add `--spacing-<named>` tokens named `xs/sm/md/lg/xl/…` — those
  collide with Tailwind's container scale (`max-w-lg`, `w-sm`) and silently
  override them.
- **Type scale (TR + EN)**: `text-caption`, `text-body`, `text-body-lg`,
  `text-lead`, `text-h3`, `text-h2`, `text-h1`, `text-display`, each with its own
  line height; font stacks `font-sans`/`font-mono` cover Turkish glyphs
  (İ ı Ş ş Ğ ğ Ç ç Ö ö Ü ü). Avoid `text-transform: uppercase` on Turkish copy.

## Components — `src/components/ui/`

| Component | File | Notes |
|---|---|---|
| `Button` | `button.tsx` | variants: default, destructive, outline, secondary, ghost, link |
| `Input` | `input.tsx` | validation: default / `aria-invalid` (destructive border+ring) / disabled |
| `Label`, `FormField` | `label.tsx`, `field.tsx` | `FormField` render-prop wires `id`/`aria-describedby`/`aria-invalid`; inline error is `role="alert"` (UXR-G-018) |
| `Badge` | `badge.tsx` | variants incl. `stale`, `restated`, `adjusted`, `notMeaningful` |
| `Table` set | `table.tsx` | semantic `<table>` + `TableContainer` (horizontal scroll keeps every figure at narrow widths, UXR-G-025) |
| `Dialog` set | `dialog.tsx` | Radix Dialog — focus trap, Esc/overlay close, focus restore, `aria-modal` |
| `ConfirmDialog` | `confirm-dialog.tsx` | destructive/consequential confirmations (UXR-G-022) |
| `Skeleton` / `SkeletonText` / `SkeletonTable` | `skeleton.tsx` | loading placeholders (UXR-G-001/002) |
| `EmptyState` | `empty-state.tsx` | explicit empty surfaces (UXR-G-005) |
| `SectionNav` | `section-nav.tsx` | in-page section navigation, `aria-current`, keyboard operable |

## Honest-data markers — `src/components/honest-data/`

Display layer of the honest-data envelope (architecture §10.7; UXR-G-006..010):

- `AsOfDate` — data-as-of date in the active language.
- `StaleBadge` — last-known-good data marked stale.
- `NotMeaningful` — explicit not-meaningful metric state.
- `RestatedMarker` — asterisk with accessible description; `RestatedNotice` —
  bottom-of-display footnote + warning.
- `AdjustedNotice` — corporate-action-adjusted series disclaimer.
- `NoDataState` — `state: 'no-data' | 'preparing' | 'unavailable'` with coverage
  boundary.

## Shell & global surfaces

- `AppShell` — persistent header + footer; footer renders `Disclaimer` on
  research routes only (`ScreenRoute.research`, UXR-G-016).
- `Header` — brand, primary navigation, stock search, language toggle, auth area.
- `Disclaimer` — informational-only notice; also rendered inline by screens.

## Conventions for downstream screens

- Copy lives in `src/i18n/catalogs/{tr,en}.json`; run `npm run check:i18n`
  (parity + unused-key gate). Never hardcode user-facing prose.
- Import primitives from `@/components/ui/*`, markers from
  `@/components/honest-data`, and the disclaimer from `@/components/Disclaimer`.
- Reserve a Radix-based dialog for confirmations; do not re-implement a focus trap.
