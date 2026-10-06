# Global Interaction Rules & Mutation Matrix — Gate 2

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** ux-designer · **Date:** 2026-10-06
**Consumes:** Gate 1 screen inventory (approved 2026-10-06) · `.pipeline/analysis/*` (all approved)
**Status:** ✅ GATE 2 APPROVED (2026-10-06). Duplicate-name decision: **reject with inline error** (UXR-G-029). Standing instruction from the user (2026-10-06): proceed through Gate 3 batches and Gate 4 without per-report confirmation stops unless a genuinely ambiguous decision arises; such items are recorded as flagged proposals and surfaced in the final Gate 4 summary.

**ID convention:** UXR-G-xxx IDs are permanent; removed rules leave a gap; new rules take the next free number. One requirement per UXR — each is independently testable. Rules marked *UX-lane baseline* have no direct BA trace (the BA is silent on them); they are interaction-quality baselines within the ux-designer's mandate, flagged here so the user can veto any of them.

**Mutation lifecycle (applies to every matrix row):** `idle → user action → pending (visible feedback, trigger locked) → success (state synchronized per matrix, without full-page reload) | failure (plain-language error per UXR-G-003, input preserved per UXR-G-004, retry offered where retryable)`.

---

## 1. Global interaction rules

### 1.1 Loading & pending feedback

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-001 | Every screen-level data load shows a visible loading indication until content, an empty state, or an error state renders. | UX-lane baseline | Must |
| UXR-G-002 | Every asynchronous user action shows a pending indication on its trigger (and disables it, UXR-G-020) until success or failure is visible. | UX-lane baseline | Must |

### 1.2 Error presentation & recovery

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-003 | Errors are presented in plain language in the active language, state what failed, and offer a retry action where the operation is retryable; raw technical/system errors are never displayed to users. | UC-ACC-002a; UC alternates across domains; NFR-ACC-002 (spirit) | Must |
| UXR-G-004 | On the failure of any mutation, all user-entered input (form fields, criteria, assumptions, chosen names) is preserved. | UC-ACC-001a/b; UC-SCR-002a | Must |

### 1.3 Empty & missing data

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-005 | Every list or result surface has an explicit empty state naming the condition (e.g., "no matching stocks") and suggesting a next step where one exists (e.g., relax criteria). | FR-SCR-006; UC-RES-001a | Must |
| UXR-G-006 | Where data is missing, the surface shows an explicit no-data state with the coverage boundary (e.g., "no data before 2019 — listed in 2019"); values are never shown as blank-treated-as-zero or silently omitted. | BR-RES-008; FR-RES-016; FR-MDF-011 | Must |

### 1.4 Data honesty & cross-surface consistency

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-007 | Every data-bearing display carries its data-as-of date. | FR-MOV-010; FR-SCR-005; FR-RES-015; BR-VAL-003 | Must |
| UXR-G-008 | Stale (last-known-good) data is visibly marked as stale, together with its as-of date, wherever it is displayed. | FR-MDF-016; BR-MOV-001; BR-SCR-006 | Must |
| UXR-G-009 | Restated figures are marked with an asterisk on the affected figures, a footnote at the bottom of the display, and a warning indicator. | BR-MDF-011; FR-RES-014; FR-MDF-019 | Must |
| UXR-G-010 | Any historical series or metric shown as corporate-action-adjusted carries a visible adjusted-data disclaimer. | BR-MDF-010; FR-RES-013; FR-MDF-018 | Must |
| UXR-G-011 | For the same stock, metric, and date, the identical value is rendered on every surface that shows it (dashboard, screener results, stock page, DCF). | BR-MDF-009; NFR-SCR-003; NFR-RES-003; NFR-VAL-003 | Must |
| UXR-G-012 | No surface implies real-time data: no "live" language, no auto-refresh countdowns; freshness is framed as daily end-of-day with the as-of date. | BR-MDF-003; brief §6 (permanent exclusions) | Must |

### 1.5 Language

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-013 | Every screen renders fully in Turkish (default) and English via the header toggle; switching language is immediate and preserves the user's current context — same view, filters, search terms, inputs, and scroll position. | Brief §7 (Language); NFR-MOV-002; NFR-SCR-001; NFR-RES-001; NFR-VAL-001; NFR-ACC-003 | Must |
| UXR-G-014 | An anonymous user's language choice persists per device across visits; on sign-in the account's stored preference is applied (overriding the device-local choice), and changing language while signed in updates the account preference. | BR-ACC-004; FR-ACC-004 | Must |

### 1.6 Content posture

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-015 | All user-facing copy uses plain language with explicit units, understandable by a novice-to-intermediate investor. | Brief §4; NFR-MOV-004; NFR-SCR-002; NFR-RES-002; NFR-VAL-002 | Must |
| UXR-G-016 | The informational-only disclaimer is displayed on all research surfaces: SCR-001, SCR-002, SCR-003, SCR-004, SCR-005, SCR-006, and SCR-011. | FR-MOV-020; FR-SCR-016; FR-RES-017; FR-VAL-008; SCR-011 per Gate 1 decision C | Must |
| UXR-G-017 | No surface contains buy/sell language, price targets, ranking scores, or "best/top picks" framing. | BR-MOV-005; BR-SCR-008; BR-RES-011; BR-VAL-005 | Must |

### 1.7 Forms & mutation safety

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-018 | Form fields validate inline, with plain-language validation rules visible at or before the first invalid submission. | UC-ACC-001 alternate (b) | Must |
| UXR-G-019 | Submit/save controls are disabled while required inputs are missing or invalid. | UX-lane baseline | Must |
| UXR-G-020 | A mutation trigger cannot be re-invoked while that mutation is pending (no duplicate submission). | UX-lane baseline; RISK-ACC-001 (spirit) | Must |
| UXR-G-021 | User-entered and user-selected state — screen criteria, DCF assumptions, list filters, search terms, active stock-page section, form input — is preserved across in-session navigation (leave and return) and across the sign-in/register hop. | FR-SCR-013; FR-ACC-010 | Should |
| UXR-G-029 | Saving a saved screen or DCF scenario under a name that already exists within the user's account (per stock, for scenarios) is rejected with an inline error identifying the collision; nothing is overwritten and no automatic suffix is created. | Gate 2 user decision (2026-10-06); BA silent | Must |

### 1.8 Destructive actions & the login-to-save pattern

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-022 | Destructive actions — deleting a saved screen, deleting a DCF scenario, deleting the account — require an explicit confirmation step that states what will be lost. | UC-ACC-003a (pattern); BA silent for screen/scenario deletion — UX-lane baseline | Must |
| UXR-G-023 | Attempting a persistence action while anonymous (save screen, save DCF scenario) presents the sign-in / register prompt. | FR-SCR-012; BR-SCR-004; BR-VAL-010 | Must |
| UXR-G-024 | After signing in or registering from a save prompt, the user is returned to their in-progress work with it preserved. | FR-SCR-013; FR-ACC-010 | Should |
| UXR-G-030 | A persistence action (save screen, save DCF scenario) attempted by a signed-in but unverified account is blocked with a plain-language verify-your-e-mail message and the verification path; the draft is preserved, and the action becomes available immediately after e-mail verification. | Gate 4 builder decision (2026-10-06, OQ-UX-003 → Option B); FR-ACC-008 rationale (RISK-ACC-002). Feature-level MoSCoW per screen: UXR-SCR-019 (Must), UXR-VAL-021 (Should) | Must |

### 1.9 Responsive & accessibility baselines

| ID | Rule | Traces to | MoSCoW |
|----|------|-----------|--------|
| UXR-G-025 | Every screen remains fully operable — navigation, reading, and every mutation — at narrow viewport widths; data-heavy tables degrade in presentation without losing access to any data or action. | UX-lane baseline (brief §7 public web deployment; native mobile apps excluded §6) | Must |
| UXR-G-026 | Every interactive element is reachable and operable by keyboard alone. | UX-lane baseline | Must |
| UXR-G-027 | Keyboard focus is visible at all times and is deliberately placed (and restored) on view changes and dialog open/close. | UX-lane baseline | Must |
| UXR-G-028 | Interactive elements and data displays carry programmatically determinable labels in the active language. | UX-lane baseline; UXR-G-013 | Must |

---

## 2. Mutation & state-synchronization matrix

**Read-only screens.** SCR-001 (Market Overview), SCR-002 (Stock List), and SCR-005 (Stock Page) host no server-side mutations; their filters, searches, and section navigation are UI state governed by UXR-G-021, and their loads follow the UXR-G-001/003/005/006 lifecycle. The header language toggle is a mutation for signed-in users (last two rows).

**The last column is the stale-UI contract:** after a successful mutation, the listed visible state updates without a full-page reload or manual refresh — this is the requirement the architecture must satisfy; the mechanism is the architect's choice.

| Screen | Mutation | Affected state | Success UI | Failure UI | Refresh / invalidation requirement |
|--------|----------|----------------|------------|------------|-----------------------------------|
| SCR-003 Screener | Run screen (query) | Results list, match count, exclusion count, as-of date | Results render with each stock's value per selected criterion, the match count, the excluded-for-missing-data count, and the data-as-of date; each row links to the stock page | Plain-language error + retry; criteria remain exactly as configured | Results correspond exactly to the criteria displayed at run time; no server state is changed |
| SCR-003 Screener | Save screen (auth) | Saved-screen list (SCR-004); active screen identity in SCR-003 | Confirmation; the chosen name is shown as the active screen; the screen appears in My Saved Screens | Error + retry; criteria and chosen name preserved (duplicate name → inline rejection per UXR-G-029) | My Saved Screens reflects the new screen when next opened, without manual refresh; the screener shows the saved name as active |
| SCR-004 My Saved Screens | Re-run saved screen (query) | Results (presented in the screener context), as-of date | Fresh results computed on the latest EOD data with current as-of date (never a stored result set) | Error + retry; the saved-screens list is unchanged | Re-run results replace any prior run's results; saved-screens list itself unchanged |
| SCR-004 My Saved Screens | Rename saved screen | Screen name (in SCR-004 list and wherever the screen appears) | Name updates in place immediately | Error; previous name retained; retry offered | New name visible wherever the screen appears (SCR-003 active-screen display, SCR-004 list) without manual refresh |
| SCR-004 My Saved Screens | Delete saved screen (destructive) | Saved-screen list | Confirmation (UXR-G-022) → row removed | Error; row retained; retry offered | Screen no longer appears in SCR-003/SCR-004; if it was the active screen in SCR-003, the screener no longer references it |
| SCR-006 DCF Calculator | Adjust an assumption (recompute) | Fair value, price comparison, verdict, sensitivity table | All computed outputs recompute without a full page reload, always reflecting the currently displayed inputs; deviation from the loaded/baseline state is indicated as unsaved changes (Should) | Invalid input (e.g., non-numeric, out-of-range) → inline validation (UXR-G-018); the last valid result remains visible until input is valid | No server state changed unless saved; outputs never show values computed from inputs other than those currently displayed |
| SCR-006 DCF Calculator | Save scenario (auth) | Scenario list for the stock (SCR-011 and the calculator's scenario picker) | Confirmation; scenario appears in the picker and in My DCF Scenarios | Error + retry; all assumptions and the chosen name preserved (duplicate name → inline rejection per UXR-G-029) | SCR-011 and the per-stock scenario picker reflect the new scenario when next opened, without manual refresh |
| SCR-006 DCF Calculator | Load scenario | Calculator inputs and all computed outputs | Assumptions restore exactly as saved (FR-VAL-010); outputs recompute from them | Error; current inputs unchanged; retry offered | If unsaved changes exist, loading requires confirmation before discarding them (destructive to the draft, UXR-G-022 pattern) |
| SCR-011 My DCF Scenarios | Rename scenario | Scenario name | Name updates in place immediately | Error; previous name retained | New name visible in SCR-011 and the SCR-006 scenario picker without manual refresh |
| SCR-011 My DCF Scenarios | Delete scenario (destructive) | Scenario list | Confirmation (UXR-G-022) → row removed | Error; row retained | Scenario no longer appears in SCR-011 or the SCR-006 picker |
| SCR-007 Register | Register (create account + consent) | Session, account, consent record | Signed in; verification-e-mail notice shown; returned to preserved in-progress work (UXR-G-024) | E-mail already registered → clear message with sign-in path; invalid/weak input → inline rules; all input preserved | Header shows authenticated state; persistence actions unlocked (save prompts no longer trigger); in-progress work intact |
| SCR-007 Register | Verify e-mail (link landing) | Account verification status; unverified-save gate | Success notice; with an active session, the verified state applies immediately (a blocked save becomes completable without re-authentication); status reflected on account surfaces | Link expired/invalid → plain-language explanation + resend path (UXR-ACC-021) | Verified status reflected wherever verification state is shown; persistence actions unlocked (UXR-G-030) |
| SCR-007 / SCR-010 | Resend verification e-mail (unverified account) | (Server dispatches e-mail) | Confirmation that the e-mail was sent | Dispatch failure → plain-language error + retry | Account known from session; no enumeration concern |
| SCR-008 Sign In | Sign in | Session, applied language preference | Signed in; account language applied; returned to preserved in-progress work | Generic error that does not reveal whether e-mail or password failed; e-mail input preserved | Header auth state updates; saved screens/scenarios become accessible; language switches to the account preference |
| SCR-008 Sign In | Sign out | Session | Signed-out (anonymous) state; current language selection retained as device-local | Error → remains signed in, with notice | Header auth state updates; persistence actions re-prompt on next attempt |
| SCR-009 Password Reset | Request reset link | (Server dispatches e-mail; no visible account state) | Neutral confirmation that does not reveal whether the address is registered | Dispatch failure → plain-language error + retry | No signal that could enumerate accounts |
| SCR-009 Password Reset | Set new password (via e-mailed link) | Account credentials | Success → the new password works for sign-in; user is directed to (or placed into) a signed-in state | Link expired → explanation + re-request path (UC-ACC-004a); validation errors inline; input preserved | Sign-in possible with the new password from any device |
| SCR-010 Account Settings | Change language preference (signed in) | Account preference; UI language | Applied immediately across the whole UI | Error → UI remains on the current language, with a notice that the preference was not saved | Preference persists and applies on sign-in from any device (BR-ACC-004) |
| SCR-010 Account Settings | Change password | Account credentials | Confirmation; session remains active | Failure (validation / authorization) → plain-language error; retry | New password effective at next sign-in |
| SCR-010 Account Settings | Delete account (destructive) | Account, personal data, saved screens, DCF scenarios | Confirmation warning that saved screens and scenarios will be lost (UC-ACC-003a) → account removed; signed out | Error → account intact, with notice | Session ends; all saved items cease to exist; header shows anonymous state |
| Header (any screen) | Language toggle — anonymous | UI language (device-local UI state) | Immediate switch; context preserved (UXR-G-013) | N/A (local operation) | Choice persists on the device across visits |
| Header (any screen) | Language toggle — signed in | UI language + account preference | Immediate switch; preference saved to account | Error → language switches locally; a notice states the preference was not saved | Preference synced to the account; applies on other devices at sign-in |

---

## 3. Flagged items & open questions (for the record)

1. **Scenario rename/delete (SCR-011 rows)** — UX-lane scope addition approved by the user at Gate 1 (2026-10-06); no corresponding FR exists in the BA package. Should be recorded in the brief delta so architecture and test plans trace it.
2. **Deletion confirmation for saved screens / scenarios (UXR-G-022)** — the BA requires confirmation only for account deletion (UC-ACC-003a); the rule extends the same pattern to screen/scenario deletion as a UX-lane baseline.
3. **Account-enumeration-neutral reset confirmation** — the BA does not state the reset-request confirmation wording; the neutral pattern ("if an account exists, an e-mail was sent") follows NFR-ACC-002's spirit (no revelation of account existence).
4. **UX-lane baselines with no BA trace** — UXR-G-001/002 (loading/pending feedback), UXR-G-019 (disabled submit), UXR-G-020 (duplicate prevention), UXR-G-025–028 (responsive, keyboard, focus, labeling). The BA is silent on these; they are interaction-quality baselines, not product-scope inventions. Veto any of them at this gate if unwanted.
5. **Duplicate names for saved screens / scenarios — RESOLVED at Gate 2 (2026-10-06):** reject with an inline error identifying the collision; no silent overwrite, no auto-suffix (UXR-G-029). Affects the save flows of SCR-003, SCR-006, SCR-011 and the rename flows of SCR-004, SCR-011.
6. **E-mail-verification gating of persistence (UXR-G-030, UXR-ACC-007 as amended) + resend-verification control (UXR-ACC-021)** — builder decision at Gate 4 (2026-10-06, OQ-UX-003 → Option B): saving screens/scenarios requires a verified e-mail. This deviates from the approved BA package's implied flow (UC-ACC-001 main flow returns the user to in-progress work with saving available; FR-ACC-010) — **a brief-delta line is recommended** so architecture and test plans trace it. The resend control was added as a necessary consequence (a lost first e-mail would otherwise permanently block saving).
