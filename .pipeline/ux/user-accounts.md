# User Accounts Specifications — SCR-007 (Register), SCR-008 (Sign In), SCR-009 (Password Reset), SCR-010 (Account Settings)

**Domain:** user-accounts · **Prepared:** 2026-10-06
**Traces:** UC-ACC-001, UC-ACC-002, UC-ACC-003, UC-ACC-004; FR-ACC-001–010 · BR-ACC-001–008 · ASM-008 (KVKK)
**Global rules applied:** UXR-G-001–029 (esp. G-003/004 error & input preservation, G-013/014 language, G-018–020 form safety, G-022 destructive confirmation, G-023/024 login-to-save)
**Status:** written under the standing no-stop instruction. **E-mail verification gates persistence** — builder decision (2026-10-06, OQ-UX-003 → Option B); UXR-ACC-005/006/007/016 amended, UXR-ACC-021 added accordingly.

---

# SCR-007 — Register

## 1. Purpose

Create a server-side account with minimal data — e-mail, password, privacy-notice consent — at the moment a user wants their work to persist (typically arriving from a save prompt), and return them to that work. Traces to UC-ACC-001.

## 2. Entry / Exit

**Entry:** header (anonymous); the save prompt from the Screener (SCR-003) or DCF Calculator (SCR-006); link from Sign In. Also hosts the e-mail-verification link landing (external entry point from the verification e-mail, FR-ACC-008).

**Exit:** back to the in-progress work the user came from (preserved, UXR-G-024); Sign In (from the "already registered" error path); global navigation.

## 3. Information Architecture

1. **Registration form** — e-mail, password, privacy-notice consent (exactly these; BR-ACC-001).
2. **Privacy notice** — bilingual, reviewable before consent (BR-ACC-002, NFR-ACC-001).
3. **Post-registration states** — signed-in confirmation with verification-e-mail notice (stating that saving requires verification); verification-link landing outcome (verified / expired).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-ACC-001 | The registration form collects exactly e-mail address, password, and privacy-notice consent — no other fields. | BR-ACC-001, FR-ACC-001, ASM-008 | Must |
| UXR-ACC-002 | The bilingual privacy notice is reviewable before consent is given, and consent is captured together with registration. | BR-ACC-002, FR-ACC-001, NFR-ACC-001 | Must |
| UXR-ACC-003 | Password rules are visible on the form and validated inline. | UC-ACC-001 alternate (b), UXR-G-018 | Must |
| UXR-ACC-004 | Registering with an already-registered e-mail produces a clear message with a path to Sign In. | UC-ACC-001 alternate (a) | Must |
| UXR-ACC-005 | On successful registration, the user is signed in, shown a notice that a verification e-mail has been sent and that saving requires verification, and returned to their in-progress work with it preserved. | UC-ACC-001 steps 4–5, FR-ACC-008, FR-ACC-010, UXR-G-024; amended per Gate 4 decision (OQ-UX-003 → B) | Must |
| UXR-ACC-006 | The verification link landing shows a success notice when the e-mail is verified, and a plain-language explanation when the link is expired or invalid; when the verifying browser has an active session, the verified state applies to it immediately (a blocked save becomes completable without re-authentication). | FR-ACC-008; amended per Gate 4 decision (OQ-UX-003 → B) | Must |
| UXR-ACC-007 | Saving (persisting) screens and DCF scenarios requires a verified e-mail; every non-persistence capability — browsing, running screens, using the DCF calculator, signing in — remains available to unverified accounts. ⚑ Amended from the recorded default by the builder's Gate 4 decision (OQ-UX-003 → Option B, 2026-10-06); deviates from UC-ACC-001's implied post-registration saving — brief-delta line recommended. | FR-ACC-008 rationale (RISK-ACC-002); Gate 4 decision | Must |
| UXR-ACC-021 | The user can request that the verification e-mail be resent (offered from the unverified-save gate and from Account Settings while unverified). | Consequence of Gate 4 decision (OQ-UX-003 → B); RISK-ACC-002 | Should |

## 5. States

- **Initial:** empty form; privacy notice reachable.
- **Validation errors:** inline per-field (UXR-G-018); submit disabled while invalid (UXR-G-019).
- **Pending:** registration in progress; trigger locked (UXR-G-020).
- **Success:** signed in; verification notice stating that saving requires verification; return to preserved work.
- **Failure:** e-mail-taken error with Sign In path; other errors plain-language with retry; all input preserved (UXR-G-003/004).
- **Verification landing:** verified (success notice; an active session gains the verified state immediately — UXR-ACC-006) / expired-invalid (explanation, UXR-ACC-006).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| User account + consent record | Server | Until account deletion | User Accounts domain | Created here; consumed by save flows |
| Session (signed-in state) | Server | Session | User Accounts domain | Header reflects authentication immediately after success (mutation matrix) |
| In-progress work (criteria / scenario) | UI | Preserved draft across the hop | Originating screen (SCR-003/SCR-006) | Restored on return (UXR-G-024) |
| Language | Global | Per device / per account | UXR-G-014 | Account preference applies on sign-in |

## 7. Data-heavy surfaces

N/A — form-driven screen; no lists, tables, or series.

## 8. Responsive behavior

Form and privacy notice remain fully operable at narrow widths (UXR-G-025).

## 9. Accessibility

Form fields labeled (UXR-G-028); validation errors textual and associated with their fields; consent is an explicit, keyboard-operable control; focus is placed on the first field on load and on the error summary/field on failed submission (UXR-G-027).

## 10. Critical flows

1. **Register from a save prompt (UC-ACC-001, amended for verification gating).** Given anonymous screener criteria and a save attempt, when the user chooses register and completes the form, then they are signed in, a notice appears that a verification e-mail was sent and that saving requires verification, and they are returned to the screener with criteria intact; when they attempt the save while unverified, then the verify-your-e-mail gate appears (UXR-G-030); when they verify via the e-mailed link within the same session, then the save becomes completable.
2. **Already registered.** Given an existing account's e-mail, when the user submits registration, then a clear message appears with a path to Sign In.
3. **Verification link expired.** Given an expired verification link, when the user opens it, then a plain-language explanation is shown with a path to request a new verification e-mail (UXR-ACC-021); saving remains gated until verification succeeds (UXR-ACC-007).

---

# SCR-008 — Sign In

## 1. Purpose

Authenticate a registered user, applying their stored language preference and returning them to their in-progress work; hosts the sign-out control's counterpart session end. Traces to UC-ACC-002.

## 2. Entry / Exit

**Entry:** header (anonymous); the save prompt from SCR-003 / SCR-006; the "already registered" path from Register; links to password reset and registration.

**Exit:** back to the origin (in-progress work preserved); global navigation. Sign out (available in the header while signed in) ends the session from any screen.

## 3. Information Architecture

1. **Sign-in form** — e-mail + password.
2. **Recovery path** — link to Password Reset.
3. **Registration path** — link to Register.

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-ACC-008 | Sign in authenticates with e-mail and password; wrong credentials produce a generic error that does not reveal whether the e-mail or the password was wrong. | UC-ACC-002 alternate (a), FR-ACC-002, NFR-ACC-002 | Must |
| UXR-ACC-009 | On successful sign-in, the account's stored language preference is applied and the user is returned to their in-progress work with it preserved. | UC-ACC-002 steps 2–3, FR-ACC-004, FR-ACC-010, UXR-G-024 | Must |
| UXR-ACC-010 | Sign out, available in the header on every screen while signed in, ends the session; the current language selection is retained as the device-local choice. | UC-ACC-002 step 4, FR-ACC-003, mutation matrix | Must |
| UXR-ACC-011 | The Sign In screen links to password reset and to registration. | UC-ACC-004 step 1, UC-ACC-001a | Must |

## 5. States

- **Initial:** empty form (e-mail preserved from prior failed attempt).
- **Pending:** trigger locked (UXR-G-020).
- **Success:** session established; language preference applied; return to preserved work.
- **Failure:** generic credential error (UXR-ACC-008); e-mail input preserved (UXR-G-004).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Session | Server | Session | User Accounts domain | Header auth state, language preference, and access to saved items update immediately on success/sign-out (mutation matrix) |
| In-progress work | UI | Preserved draft across the hop | Originating screen | Restored on return (UXR-G-024) |

## 7. Data-heavy surfaces

N/A — form-driven screen.

## 8. Responsive behavior

Form remains fully operable at narrow widths (UXR-G-025).

## 9. Accessibility

Fields labeled; errors textual and associated; focus management on load and on error (UXR-G-027/028).

## 10. Critical flows

1. **Sign in from a save prompt (UC-ACC-002).** Given anonymous DCF assumptions and a save attempt, when the user signs in, then they are returned to the calculator with assumptions intact, the account language preference is applied, and the save can be completed.
2. **Wrong credentials.** Given an incorrect password, when the user submits, then a generic error appears (not revealing which field failed), the e-mail is preserved, and retry is possible.
3. **Sign out anywhere.** Given a signed-in user on any screen, when they sign out, then the session ends, the header reflects the anonymous state, and the current language remains active on the device.

---

# SCR-009 — Password Reset

## 1. Purpose

Let a user who cannot authenticate regain access: request a reset link by e-mail, then set a new password via the e-mailed, time-limited link. Traces to UC-ACC-004; FR-ACC-009 (Must, BA Gate 2).

## 2. Entry / Exit

**Entry:** "forgot password" from Sign In; the e-mailed reset link (external entry point — the screen must be directly reachable from an e-mail context, not only via in-app navigation).

**Exit:** Sign In (or a signed-in state) after a successful reset; re-request path from the expired-link state.

## 3. Information Architecture

1. **Request stage** — e-mail input, submits the reset request.
2. **Set-new-password stage** (reached only via the e-mailed link) — new password with rules validation.

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-ACC-012 | A reset can be requested with an e-mail address; the confirmation shown afterward is neutral and does not reveal whether the address is registered. | UC-ACC-004 step 1, FR-ACC-009, Gate 2 flagged-item 3 | Must |
| UXR-ACC-013 | Setting a new password via the e-mailed link enforces the password rules with inline validation. | UC-ACC-004 step 3, UXR-G-018 | Must |
| UXR-ACC-014 | An expired or invalid reset link shows a plain-language explanation with a path to re-request a new link. | UC-ACC-004 alternate (a) | Must |
| UXR-ACC-015 | After a successful reset, the user can sign in with the new password (directed to Sign In or placed into a signed-in state). | UC-ACC-004 postcondition | Must |

## 5. States

- **Request initial:** e-mail input.
- **Request pending / done:** trigger locked; neutral confirmation shown (UXR-ACC-012).
- **Set-password initial (valid link):** new-password input with visible rules.
- **Validation errors:** inline (UXR-G-018); input preserved (UXR-G-004).
- **Expired/invalid link:** explanation + re-request path (UXR-ACC-014).
- **Success:** path into a signed-in state / Sign In (UXR-ACC-015).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Reset request / dispatched link | Server | Time-limited | User Accounts domain | Neutral confirmation; no account-enumeration signal (mutation matrix) |
| New credentials | Server | Until next change | User Accounts domain | New password effective at next sign-in (mutation matrix) |

## 7. Data-heavy surfaces

N/A — form-driven screen.

## 8. Responsive behavior

Both stages remain fully operable at narrow widths (UXR-G-025), including when reached from an e-mail client on a phone.

## 9. Accessibility

Fields labeled; rules and errors textual; focus placed on the first field per stage (UXR-G-027/028).

## 10. Critical flows

1. **Full reset (UC-ACC-004).** Given a registered user who forgot their password, when they request a reset, then a neutral confirmation appears; when they open the e-mailed link and set a valid new password, then they can sign in with it and their saved work is intact.
2. **Expired link.** Given an expired link, when the user opens it, then an explanation with a re-request path appears.

---

# SCR-010 — Account Settings

## 1. Purpose

Let a signed-in user manage their account: language preference, password change, entry points to their saved work (screens, DCF scenarios), and account deletion. Traces to UC-ACC-003.

## 2. Entry / Exit

**Entry:** header (signed-in). **Exit:** My Saved Screens and My DCF Scenarios (entry points); global navigation. Signed-in only (UXR-ACC-020).

## 3. Information Architecture

1. **Language preference.**
2. **Password change.**
3. **Entry points** — My Saved Screens; My DCF Scenarios.
4. **Account deletion** (destructive, behind confirmation).
5. **E-mail verification status** — with the resend action while unverified (UXR-ACC-021).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-ACC-016 | Account Settings contains the language preference, password change, entry points to saved screens and DCF scenarios, account deletion, and the e-mail verification status (with the resend action while unverified) — and nothing else. | FR-ACC-005, BR-ACC-001 (minimal posture); amended per Gate 4 decision (OQ-UX-003 → B) | Must |
| UXR-ACC-017 | Changing the language preference applies it immediately across the UI and persists it to the account (effective on sign-in from any device). | BR-ACC-004, FR-ACC-004, mutation matrix | Must |
| UXR-ACC-018 | Password change validates inline, preserves input on failure, confirms on success, and keeps the session active. | FR-ACC-007, UXR-G-003/004/018 | Should |
| UXR-ACC-019 | Account deletion requires an explicit confirmation stating that saved screens and DCF scenarios will be lost; on confirmation, the account, personal data, and saved items are removed and the session ends. | UC-ACC-003 alternate (a), FR-ACC-006, BR-ACC-005 | Must |
| UXR-ACC-020 | Account Settings is reachable only while signed in; anonymous direct access presents the sign-in path. | UC-ACC-003 precondition, BR-ACC-003 | Must |

## 5. States

- **Initial:** current preference and account state loaded (incl. e-mail verification status).
- **Preference change pending / applied:** immediate UI application; save failure leaves UI on current language with a notice (mutation matrix).
- **Password change pending / success / failure:** per UXR-ACC-018.
- **Deletion confirmation:** explicit dialog stating consequences; on confirm — account removed, signed out, anonymous state.
- **Errors:** plain-language + retry; inputs preserved (UXR-G-003/004).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Language preference | Server | Per account | User Accounts domain | Applied immediately; synced across devices at sign-in (mutation matrix) |
| Account + saved items | Server | Until deletion | User Accounts + owning domains | Deletion removes screens and scenarios; session ends (mutation matrix) |
| UI language | Global | Per device / per account | UXR-G-014 | — |

## 7. Data-heavy surfaces

N/A — settings screen; the saved-items entry points lead to SCR-004 / SCR-011, which own their list surfaces.

## 8. Responsive behavior

All settings and actions remain fully operable at narrow widths (UXR-G-025).

## 9. Accessibility

Controls labeled; the deletion confirmation is a focus-trapping dialog with explicit consequence text and a cancel path (UXR-G-022/027); language preference change is keyboard operable and perceivable non-visually.

## 10. Critical flows

1. **Language preference across devices (BR-ACC-004).** Given a signed-in user on device A, when they set English in Account Settings, then the UI switches immediately; when they later sign in on device B, then English is applied.
2. **Change password.** When the user changes their password with valid input, then a confirmation appears, the session continues, and the new password works at the next sign-in.
3. **Delete account (UC-ACC-003a).** When the user deletes their account, then a confirmation warns that saved screens and DCF scenarios will be lost; on confirmation, everything is removed and the user is signed out.
