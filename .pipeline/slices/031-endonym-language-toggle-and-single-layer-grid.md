---
id: SLICE-031
status: done
scope:
  - .pipeline/slices/031-endonym-language-toggle-and-single-layer-grid.md
  - src/Web/src/i18n/catalogs/en.json
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/index.css
---

# SLICE-031 — Endonym Language Buttons (`Türkçe | English`) & Single-Layer Background Grid

## Context & Goal

1. **Endonym Language Labels (`en.json` & `tr.json`):**
   - Currently `tr.json` sets `"header.english": "İngilizce"` and `en.json` sets `"header.turkish": "Turkish"`. An English speaker landing on the default Turkish UI may not recognize `"İngilizce"`.
   - Set `"header.turkish": "Türkçe"` and `"header.english": "English"` in **both** `tr.json` and `en.json` so the switcher always reads **`Türkçe` | `English`** regardless of active UI language.
   - All existing L3/L4 tests read button labels via `TR('header.english')` / `EN('header.turkish')`, so test selectors stay 100% green automatically.
2. **Eliminate Double-Grid / 3D Moiré Effect (`src/Web/src/index.css`):**
   - Currently BOTH `body` (`background-image: linear-gradient(...) 48px 48px; background-position: top center`) AND `#global-grid-canvas` (`TactileBackdrop.tsx`, drawing 48px squares from `(0, 0)`) render a grid simultaneously with mismatched origins, creating distracting extra lines and a 3D double-grid effect.
   - Remove the CSS `background-image` / `background-size` / `background-position` grid rules from `body` in `src/Web/src/index.css` so only `#global-grid-canvas` draws the clean single-layer 48px engineering-paper grid (faithful to `geminisonuiux.html`).

## Rules & Invariants

- Keep `overflow-anchor: none`, `overflow-x: clip`, and `prefers-reduced-motion` support intact in `index.css`.
- Keep TR/EN i18n parity and zero advice vocabulary (`check:i18n`, `check:advice`).

## Blockers & Dependencies

- None.

## Verification

- [x] `cd src/Web && npm run build && npm run lint && npm run check:i18n && npm run check:advice` passes.
- [x] `cd src/Web && npx vitest run src/components/LanguageToggle.test.tsx src/global-ux/languagePersistence.test.tsx` passes (2 files / 2 tests).

## Result

- Updated `header.turkish` (`"Türkçe"`) and `header.english` (`"English"`) in both `src/Web/src/i18n/catalogs/en.json` and `tr.json` so language switcher buttons always render endonyms regardless of active UI locale.
- Removed the duplicate CSS `background-image` grid from `body` in `src/Web/src/index.css` so only the single-layer 48px `#global-grid-canvas` draws the grid, eliminating the offset double-grid / 3D moiré effect.
