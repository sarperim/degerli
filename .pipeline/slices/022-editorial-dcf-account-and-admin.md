---
id: SLICE-022
status: done
scope:
  - .pipeline/slices/022-editorial-dcf-account-and-admin.md
  - src/Web/src/screens/dcf/DcfCalculatorScreen.tsx
  - src/Web/src/screens/signIn/SignInScreen.tsx
  - src/Web/src/screens/register/RegisterScreen.tsx
  - src/Web/src/screens/register/VerifyEmailScreen.tsx
  - src/Web/src/screens/password-reset/PasswordResetScreen.tsx
  - src/Web/src/screens/savedScreens/SavedScreensScreen.tsx
  - src/Web/src/screens/dcfScenarios/DcfScenariosScreen.tsx
  - src/Web/src/screens/accountSettings/AccountSettingsScreen.tsx
  - src/Web/src/screens/admin/AdminScreen.tsx
  - src/Web/src/components/ui/label.tsx
  - src/Web/src/styles/tokens.css
---

# SLICE-022 — DCF Calculator (`SCR-006`), Account (`SCR-004`, `SCR-007`–`SCR-011`) & Admin (`SCR-012`) Editorial Polish

## Context & Goal

Complete the `degerlilatest.html` visual redesign across the remaining calculator, account, and builder-admin screens:

1. **DCF Calculator (`SCR-006` — `DcfCalculatorScreen.tsx`):**
   - 2-column open split (`lg:grid-cols-[0.92fr_1.08fr] gap-12`):
     - Left: 2-column mono parameter grid (`dcf-param-grid`) with uppercase mono field labels, canonical fact provenance notes (`note-line`), and reset/unsaved controls.
     - Right: large mono `Fair Value per Share` vs `Current Price` + `Margin of Safety` editorial readout, open borderless 5×5 **Sensitivity Matrix** with bronze `accent-soft` highlight (`sens-hl`) on `data-current="true"`, and hairline-separated `Scenarios` save/load section below.

2. **Auth Surfaces (`SCR-007`–`SCR-009` — `SignInScreen`, `RegisterScreen`, `VerifyEmailScreen`, `PasswordResetScreen`):**
   - Deliberately contained `auth-card` (`max-w-[450px] mx-auto border border-strong bg-card p-6`), mono uppercase field labels, inline consent checkbox layout on Register (fixing the stacked checkbox UX), and crisp `note-line` feedback banners.

3. **Account & Builder Surfaces (`SCR-004`, `SCR-010`, `SCR-011`, `SCR-012`):**
   - Open editorial page headers (mono kicker + display heading + hairline divider) and borderless tables/ledgers across `SavedScreensScreen`, `DcfScenariosScreen`, `AccountSettingsScreen`, and `AdminScreen`.

## Rules & Invariants

- **Preserve all L3/L4 contracts:** `DcfCalculator.test.tsx`, `DcfFlow.test.tsx`, `DcfScenarios.test.tsx`, `sign-in.spec.ts`, `password-reset.spec.ts`, `account-settings.spec.ts`, `admin.spec.ts`, `admin-dcf-baselines.spec.ts`, and `global-ux-a11y.spec.ts` must stay green.
- **No advice vocabulary:** DCF verdict remains neutral (`dcf.framing` / `verdictKey`).

## Blockers & Dependencies

- Depends on `SLICE-018`.

## Verification

- [x] `cd src/Web && npm run build` passes.
- [x] `cd src/Web && npx vitest run src/screens/dcf/ src/screens/signIn/ src/screens/register/ src/screens/password-reset/ src/screens/savedScreens/ src/screens/dcfScenarios/ src/screens/accountSettings/ src/screens/admin/` passes.
- [ ] Playwright visual check across `/stocks/THYAO/dcf`, `/sign-in`, `/register`, `/screens`, `/scenarios`, and `/admin`.

## Result

Implemented the editorial redesign across DCF calculator, auth, account, saved-work,
scenario and admin surfaces.

- `Label` now renders the shared mono uppercase editorial label.
- `DcfCalculatorScreen` uses the open 2-column split, mono parameter grid, note-line
  provenance, editorial readout ribbon, borderless sensitivity matrix with bronze
  `--accent-soft` current-cell highlight, and hairline-separated scenario panel.
- Auth screens are contained `auth-card` surfaces; Register consent is now an inline
  checkbox + label row with the required ARIA wiring and error alert.
- Saved Screens, DCF Scenarios, Account Settings and Admin use open editorial headers,
  borderless tables and hairline-separated sections.
- Added `--accent-soft` (alias of `--brand-accent-soft`) to both themes.

Verification: build, lint, `check:i18n`, `check:advice` and the targeted Vitest suite
(22 files / 149 tests) all pass. Playwright visual walkthrough remains outstanding.
