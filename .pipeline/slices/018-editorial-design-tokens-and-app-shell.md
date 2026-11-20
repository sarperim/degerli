---
id: SLICE-018
status: done
scope:
  - .pipeline/slices/018-editorial-design-tokens-and-app-shell.md
  - src/Web/index.html
  - src/Web/package.json
  - src/Web/package-lock.json
  - src/Web/src/main.tsx
  - src/Web/src/styles/tokens.css
  - src/Web/src/index.css
  - src/Web/src/components/Header.tsx
  - src/Web/src/components/AppShell.tsx
  - src/Web/src/components/StockSearch.tsx
  - src/Web/src/components/StockSearch.test.tsx
  - src/Web/src/components/ThemeToggle.tsx
  - src/Web/src/components/LanguageToggle.tsx
  - src/Web/src/components/AuthState.tsx
  - src/Web/src/components/Disclaimer.tsx
  - src/Web/src/components/honest-data/markers.tsx
  - src/Web/src/components/ui/button.tsx
  - src/Web/src/components/ui/input.tsx
  - src/Web/src/components/ui/table.tsx
---

# SLICE-018 — Editorial Design Tokens, Typography & App Shell (`degerlilatest.html` Foundation)

## Context & Goal

Establish the editorial engineering-paper visual foundation from `/mnt/c/Users/sarpe/Desktop/degerlilatest.html` across the React SPA without altering any API contracts or breaking existing L3/L4 tests:

1. **Typography & Color Tokens (`index.html`, `tokens.css`, `index.css`):**
   - Load `Schibsted Grotesk` (UI sans) and `IBM Plex Mono` (tabular numerals, kickers, metadata, table headers).
   - Adopt the prototype's warm obsidian dark palette (`--bg: #111316`, `--surface: #16191E`, `--surface-alt: #1B1F25`, `--border: #252A32`, `--ink: #DCE1E6`, `--accent: #C68B49`, `--regime: #7E97B8`, `--pos: #5FA57C`, `--neg: #C86A6A`) and archival paper light palette (`--bg: #F4F3EF`, `--surface: #FAF9F6`, `--surface-alt: #EAEAE4`, `--border: #D9DBD3`, `--ink: #171A1E`, `--accent: #9B6123`, `--regime: #3E5D82`, `--pos: #267349`, `--neg: #A94141`).
   - Set crisp `2px` `--radius` and map semantic Tailwind CSS variables (`--background`, `--card`, `--popover`, `--primary`, `--muted`, `--border`, `--accent`, `--ring`) cleanly to the new palette.
   - Add subtle CSS hairline grid / grain background utilities (pure CSS, zero performance impact, disabled under `prefers-reduced-motion`).

2. **Sticky 54px Editorial Topbar (`Header.tsx`, `StockSearch.tsx`, `ThemeToggle.tsx`, `LanguageToggle.tsx`, `AuthState.tsx`):**
   - Convert `Header` to a sticky `54px` glassmorphic topbar (`max-w-[1240px]`, height-locked on `lg+` so `TC-MOV-023` TR/EN scroll stability stays within `0px`).
   - Brand mark: uppercase tracked `DEĞERLİ` + mono `BIST EOD` tag.
   - Nav links with `1.5px` bronze active bottom border.
   - Expanding `StockSearch` input with `/` keyboard focus shortcut hint (`/` focuses search when not typing in an input) and crisp popover dropdown.
   - Compact segmented `TR | EN` and `Theme` toggles + sharp `2px` `AuthState` controls.

3. **Shared UI Primitives & Honest-Data Markers (`AppShell.tsx`, `Disclaimer.tsx`, `markers.tsx`, `ui/button.tsx`, `ui/input.tsx`, `ui/table.tsx`):**
   - Restyle `Button`, `Input`, and `Table` (open borderless `1px` hairline rows, mono uppercase `10px` column headers, `tabular-nums` cells) to `2px` editorial geometry.
   - Restyle `StaleBadge` (bronze hairline pill), `RestatedMarker`, and `AsOfDate` to mono editorial markers.
   - Restyle `AppShell` footer (`max-w-[1240px]`, editorial 2-column footer with inline `Disclaimer`).

## Rules & Invariants

- **Preserve all L3/L4 selectors & ARIA contracts:** Keep every existing `data-testid`, `data-slot`, `data-screen`, `role="group"`, `aria-label`, and accessible button name (`tr.json`/`en.json`) intact.
- **Scroll stability (`TC-MOV-023`):** Desktop header height must not shift by >1px when toggling TR ↔ EN at `1280×400`.
- **Responsive (`TC-XC-019`):** Zero horizontal overflow and reachable primary nav at `375px`.
- **No advice vocabulary:** `adviceVocabulary.test.ts` and `i18nParity.test.ts` must pass.

## Blockers & Dependencies

- None (`dev` is clean at `e9d1e3c`).

## Verification

- [x] `cd src/Web && npm run build` passes with 0 TypeScript/Vite errors.
- [x] `cd src/Web && npx vitest run src/components/ src/test/ src/global-ux/` passes (`18` files, `73` tests).
- [ ] Playwright visual check at `1440×900` (dark & light) and `375×844` (mobile) + `1280×400` TR→EN scroll stability check (CI / post-fix re-verify).

## Result

- Implemented the editorial engineering-paper design system tokens (`tokens.css`, `index.css`), self-hosted `@fontsource/schibsted-grotesk` (`400`–`700`) and `@fontsource/ibm-plex-mono` (`400`–`600`) in `main.tsx` (removing external Google Fonts links for CSP compliance), sticky `54px` `Header` (`z-40`, `max-w-content`), `AppShell` (`max-w-content`), `StockSearch` (`/` global shortcut with `defaultPrevented`/`isComposing`/modal guards + unit tests), `LanguageToggle` (`focus-visible:ring-inset`), `ThemeToggle`, `AuthState`, `Disclaimer`, `StaleBadge` (`text-stale-foreground`), and `ui/*` primitives (`table.tsx`, `input.tsx`, `button.tsx`) using AA-compliant `text-muted-foreground` / `text-stale-foreground`.
- Verified after Round 1 fixes with `npm run build` (0 errors, bundled font assets) and `npx vitest run src/components/ src/test/ src/global-ux/` (`18` test files, `73` tests passing).
