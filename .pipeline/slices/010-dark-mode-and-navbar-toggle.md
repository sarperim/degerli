---
id: SLICE-010
status: done
scope:
  - src/Web/src/lib/theme.ts
  - src/Web/src/components/ThemeToggle.tsx
  - src/Web/src/components/ThemeToggle.test.tsx
  - src/Web/src/components/Header.tsx
  - src/Web/src/main.tsx
  - src/Web/src/styles/tokens.css
  - src/Web/src/index.css
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - .pipeline/slices/010-dark-mode-and-navbar-toggle.md
---

# SLICE-010 — Dark Mode Support & Navigation Bar Theme Toggle

## Context & Goal

`src/Web/src/styles/tokens.css` already defines the full `.dark` semantic and honest-data token palette (`--background`, `--foreground`, `--card`, `--muted`, `--border`, `--stale`, `--restated`, `--adjusted`, `--not-meaningful`, etc.), but the SPA had no theme state manager or navigation bar control to toggle `.dark` on `document.documentElement`.

This slice adds full dark-mode support and a theme toggle button in the global navigation bar (`Header.tsx`):

1. **Theme state & persistence (`src/Web/src/lib/theme.ts`):**
   - Defines `SUPPORTED_THEMES = ['light', 'dark'] as const`, `DEFAULT_THEME = 'light'`, and `THEME_STORAGE_KEY = 'degerli.theme'`.
   - Resolves the initial theme from `window.localStorage.getItem('degerli.theme')` (falling back to `window.matchMedia('(prefers-color-scheme: dark)')` when available, and `DEFAULT_THEME` otherwise).
   - Synchronizes `document.documentElement.classList.toggle('dark', theme === 'dark')` and `document.documentElement.style.colorScheme = theme` at module load (imported in `src/Web/src/main.tsx` before React mounts, honoring Caddy's `script-src 'self'` CSP without inline HTML scripts) and on every theme change.
   - Exposes `useTheme()`, `getTheme()`, `setTheme(theme)`, and `toggleTheme()` via `useSyncExternalStore`.
2. **CSS token polish (`src/Web/src/styles/tokens.css` and `src/Web/src/index.css`):**
   - Adds `color-scheme: light;` on `:root` and `color-scheme: dark;` on `.dark` in `tokens.css` so native scrollbars and form controls adapt automatically.
   - Adds `@custom-variant dark (&:where(.dark, .dark *));` in `src/Web/src/index.css` for class-driven dark mode in Tailwind CSS v4.
3. **Global Header Theme Toggle (`src/Web/src/components/ThemeToggle.tsx` & `src/Web/src/components/Header.tsx`):**
   - Adds `<ThemeToggle />` in `Header.tsx` alongside `<LanguageToggle />`.
   - Renders an accessible toggle button (`aria-label={t('header.themeToggle')}`, `aria-pressed={theme === 'dark'}`) with Sun/Moon SVG iconography and localized mode label (`header.darkMode` / `header.lightMode`).
   - Adds symmetric `header.themeToggle`, `header.lightMode`, and `header.darkMode` keys in `tr.json` and `en.json`.
4. **Unit tests (`src/Web/src/components/ThemeToggle.test.tsx`):**
   - Verifies toggling between light and dark themes updates `aria-pressed`, `document.documentElement.classList.contains('dark')`, `window.localStorage.getItem('degerli.theme')`, and localized labels across TR/EN.

## Rules & Invariants

- **CSP compliance:** Do not inject inline `<script>` tags into `index.html` (`deploy/caddy/Caddyfile` enforces `script-src 'self'`).
- **i18n parity & advice vocabulary gates:** Every new key in `tr.json` and `en.json` must be symmetric, referenced in source code, and free of investment-advice vocabulary (`npm run check:i18n`).

## Blockers & Dependencies

- None.

## Verification

- `npm --prefix src/Web run check:i18n`
- `npm --prefix src/Web test -- --run src/components/ThemeToggle.test.tsx src/test/i18nParity.test.ts src/test/adviceVocabulary.test.ts`
- `npm --prefix src/Web run typecheck`
- `npm --prefix src/Web run lint`

## Result

All changes for SLICE-010 have been implemented and verified:
- `src/Web/src/lib/theme.ts`: Implemented theme state manager using `useSyncExternalStore`, resolving initial theme from `localStorage` (falling back to `prefers-color-scheme`), applying `dark` class and `colorScheme` style on `document.documentElement`, and exporting `useTheme`, `getTheme`, `setTheme`, and `toggleTheme`.
- `src/Web/src/styles/tokens.css`: Added `color-scheme: light;` to `:root` and `color-scheme: dark;` to `.dark`, with updated comment noting toggling via `<html class="dark">` by `@/lib/theme`.
- `src/Web/src/index.css`: Added `@custom-variant dark (&:where(.dark, .dark *));` right after tokens import for class-driven Tailwind CSS v4 dark styling.
- `src/Web/src/i18n/catalogs/tr.json` & `en.json`: Added symmetric translation keys for `header.themeToggle`, `header.lightMode`, and `header.darkMode`.
- `src/Web/src/components/ThemeToggle.tsx`: Built accessible theme toggle button with SVG Sun/Moon icons, localized labels, and proper ARIA states.
- `src/Web/src/components/Header.tsx`: Integrated `<ThemeToggle />` before `<LanguageToggle />` in the header control bar.
- `src/Web/src/main.tsx`: Imported `@/lib/theme` before mounting to apply theme eagerly on load.
- `src/Web/src/components/ThemeToggle.test.tsx`: Verified light/dark default state, toggling behavior, localStorage persistence, document class mutations, and English/Turkish localization.
- Verification commands passed:
  - `npm --prefix src/Web run check:i18n` (849 keys in sync across TR/EN)
  - `npm --prefix src/Web test -- --run src/components/ThemeToggle.test.tsx src/test/i18nParity.test.ts src/test/adviceVocabulary.test.ts` (11 passed across 3 test files)
  - `npm --prefix src/Web run typecheck` (clean exit 0)
  - `npm --prefix src/Web run lint` (clean exit 0)

