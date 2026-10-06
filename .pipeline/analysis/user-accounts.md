# User Accounts & Personalization Analysis

**Domain code:** ACC · **Slug:** `user-accounts` · **Report order:** 6 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V1 — user accounts; §7; §8 ASM-008) · **Domain map:** `.pipeline/analysis/00-domain-map.md` · **Consumes decisions:** BR-SCR-004 (login-to-save), FR-VAL-009/010 (DCF scenarios)

## 1. Overview

This domain provides registration and login, giving users a server-side identity that unlocks the platform's persistence features — saved screens (Stock Screening) and saved DCF scenarios (Valuation & DCF). It owns the user's language preference (Turkish default, English toggle) and the registration data, collected minimally to keep KVKK (Turkey's personal data protection law) exposure low. Accounts are strictly optional: every research surface — dashboard, screener running, stock pages, DCF calculator — stays public; accounts exist only to make a user's work durable.

Business value delivered:

- Server-side persistence per account for saved screens is an explicit success criterion, and DCF scenario persistence was confirmed at the Valuation gate. [SC-003; FR-VAL-009/010]
- KVKK-aware minimal data collection with a pre-launch compliance review keeps the platform's only personal-data surface small and defensible. [ASM-008]
- The optional-account posture keeps the anonymous first-run experience frictionless — the audience for OBJ-005's 10-user validation. [OBJ-001, OBJ-005]

Screens owned by this domain: **Register**, **Sign In**, **Account Settings**.

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Anonymous visitor | Human | Becomes a registered user at the moment they want to save work (screen save prompt, DCF scenario save) | Once |
| Registered user | Human, authenticated | Sign in/out; manage language preference and account; use saved screens and DCF scenarios | Per visit |
| Consuming domains (Stock Screening, Valuation & DCF) | System actors | Authenticate users to attach persistence (saved screens, scenarios); trigger the save prompt for anonymous users | Continuous |
| The builder | Human; compliance owner | Performs the pre-launch KVKK review of collected account data (ASM-008 validation); operates the service | Once before launch, then exception-driven |
| External obligations | Legal framework (KVKK), e-mail delivery service (verification/reset — confirmed, OQ-ACC-001) | — | — |

## 3. Business Rules

- **BR-ACC-001** — Registration collects the minimum data needed to operate the account: e-mail address and password; no name, profile, or demographic fields. *Source: brief §8 ASM-008 (minimal data collection).* *Enforced: system (registration form) + policy.*
- **BR-ACC-002** — Personal data is processed only to operate the platform (authentication and persistence of the user's own saved work); it is not used for marketing, not shared with third parties, and consent to a bilingual privacy notice is captured at registration. *Source: brief §8 ASM-008, §7 (KVKK applies).* *Enforced: policy + system (consent record).*
- **BR-ACC-003** — Accounts are optional: all research content remains publicly accessible without one; an account only unlocks persistence (saved screens, DCF scenarios) and preferences. *Source: brief §5; user decisions BR-SCR-004 and BR-VAL-010 (Gate 2, 2026-10-06).* *Enforced: system.*
- **BR-ACC-004** — The user's language preference (TR default / EN toggle) is stored per account and applied on sign-in, persisting across sessions and devices. *Source: approved domain map; brief §7.* *Enforced: system.*
- **BR-ACC-005** — Users can delete their own account; deletion removes their personal data and their saved items (screens, scenarios). *Source: KVKK data-owner rights; approved domain map (Account Settings includes account deletion).* *Enforced: system.*
- **BR-ACC-006** — Credentials are stored securely — passwords only as salted hashes, never plaintext or recoverable. *Source: KVKK data-security obligation; standard duty of care.* *Enforced: system (design detail with architect).*
- **BR-ACC-007** — All account-related screens and the privacy notice exist in both TR and EN, TR default. *Source: brief §7.* *Enforced: system.*
- **BR-ACC-008** — The platform stores no sensitive financial or personal data beyond the account itself (no portfolios, no payment data) — the data-breach surface is deliberately minimal. *Source: brief §6 (no portfolio tracking, no monetization), §8 ASM-008.* *Enforced: product boundary.*

## 4. Use Cases

### UC-ACC-001 — Register an account
**Primary actor:** anonymous visitor (typically arriving from a save prompt)
**Preconditions:** user wants to persist work; e-mail address available.
**Main success scenario:**
1. The user chooses to register (from the save prompt or the header).
2. The user provides e-mail and password and accepts the bilingual privacy notice (consent recorded).
3. The system creates the account.
4. If e-mail verification is enabled (OQ-ACC-001), a verification e-mail is sent.
5. The user is signed in and returned to their in-progress work (screening criteria / DCF scenario preserved per FR-SCR-013 and the same pattern for DCF).
**Alternate / error flows:** (a) E-mail already registered → clear message with sign-in path. (b) Weak password → plain-language password rules shown.
**Postconditions:** an account exists; the user can save screens and DCF scenarios server-side.

### UC-ACC-002 — Sign in and sign out
**Primary actor:** registered user
**Preconditions:** account exists.
**Main success scenario:**
1. The user signs in with e-mail and password.
2. The user's language preference is applied.
3. The user's saved screens and DCF scenarios are available.
4. The user signs out, ending the session.
**Alternate / error flows:** (a) Wrong credentials → clear error without revealing which field failed. (b) Forgotten password → password reset flow (UC-ACC-004).
**Postconditions:** session established/ended; persistence features available/unavailable accordingly.

### UC-ACC-003 — Manage account settings
**Primary actor:** registered user
**Preconditions:** signed in.
**Main success scenario:**
1. The user opens Account Settings.
2. The user changes their language preference (applied immediately).
3. The user reaches their saved screens and DCF scenarios from here.
4. The user changes their password.
5. The user deletes their account; personal data and saved items are removed.
**Alternate / error flows:** (a) Account deletion → explicit confirmation step warning that saved screens and scenarios will be lost.
**Postconditions:** preferences and account state reflect the user's choices.

### UC-ACC-004 — Recover access (forgot password)
**Primary actor:** registered user
**Preconditions:** e-mail delivery available; account exists.
**Main success scenario:**
1. The user requests a password reset from the sign-in screen.
2. A reset link is e-mailed to the registered address.
3. The user sets a new password and signs in.
**Alternate / error flows:** (a) Reset link expired → re-request.
**Postconditions:** the user regains access; saved work intact.

### UC-ACC-005 — Pre-launch KVKK review
**Primary actor:** the builder (compliance owner)
**Preconditions:** registration flow implemented.
**Main success scenario:**
1. The builder reviews the collected account data fields against ASM-008's minimal-collection principle.
2. The builder confirms the privacy notice covers the collected data, its purpose, and retention.
3. The builder records the review outcome before launch.
**Alternate / error flows:** (a) A field or use is found excessive → removed before launch.
**Postconditions:** the ASM-008 validation is closed; the account data surface is defensible.

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-ACC-001 | The system shall let a visitor register an account with e-mail address and password, capturing consent to the bilingual privacy notice. | UC-ACC-001 | Must |
| FR-ACC-002 | The system shall authenticate a user with e-mail and password (sign in). | UC-ACC-002 | Must |
| FR-ACC-003 | The system shall end the user's session (sign out). | UC-ACC-002 | Must |
| FR-ACC-004 | The system shall store the user's language preference per account and apply it on sign-in. | UC-ACC-002, UC-ACC-003 | Must |
| FR-ACC-005 | The system shall provide an Account Settings screen containing language preference, entry points to saved screens and DCF scenarios, password change, and account deletion. | UC-ACC-003 | Must |
| FR-ACC-006 | The system shall let the user delete their own account, removing their personal data and saved items. | UC-ACC-003 | Must |
| FR-ACC-007 | The system shall let a signed-in user change their password. | UC-ACC-003 | Should |
| FR-ACC-008 | The system shall send a verification e-mail on registration. | UC-ACC-001 | Must |
| FR-ACC-009 | The system shall let a user reset a forgotten password via an e-mailed reset link. | UC-ACC-004 | Must |
| FR-ACC-010 | The system shall return the user to their in-progress work (preserved criteria/scenario) after registration or sign-in from a save prompt. | UC-ACC-001, UC-ACC-002 | Should |
| FR-ACC-011 | The system shall support social login (Google, Apple, etc.). | — | Won't (this release — not in brief; adds third-party data processors and KVKK complexity) |
| FR-ACC-012 | The system shall support two-factor authentication. | — | Won't (this release) |

## 6. Non-Functional Requirements

- **NFR-ACC-001 — KVKK compliance:** collected fields are limited to e-mail + password (+ consent record); the bilingual privacy notice states purpose, retention, and data-owner rights; a builder review closes ASM-008 before launch. No data sharing, no marketing use.
- **NFR-ACC-002 — Credential security:** passwords stored only as salted hashes (BR-ACC-006); sign-in errors do not reveal whether e-mail or password was wrong.
- **NFR-ACC-003 — Bilingual completeness:** 100% of account-related content (forms, notices, errors, settings) in TR and EN, TR default.
- **NFR-ACC-004 — Data retention:** account data retained while the account is active and removed on deletion (BR-ACC-005); consent records retained as required by KVKK.
- **NFR-ACC-005 — Optional-account posture:** every research surface remains fully usable without an account (BR-ACC-003) — no feature other than persistence and preferences requires signing in.

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| User account | e-mail (identifier), password hash, language preference, registration date, status | the only personal-data entity the platform owns |
| Consent record | account, privacy-notice version, consent date | 1 per account per notice version (KVKK evidence) |

Saved screens (Stock Screening) and DCF scenarios (Valuation & DCF) are owned by their domains and reference the user account; this domain owns identity, preferences, and the account lifecycle only.

## 8. Dependencies

**Upstream:** none internal — this is the identity root. External: e-mail delivery service (verification/reset, pending OQ-ACC-001); KVKK legal framework.

**Downstream (consumers):**
- **Stock Screening** — identity for saved screens; sign-in prompt on save with criteria preservation (FR-SCR-007, FR-SCR-012, FR-SCR-013).
- **Valuation & DCF** — identity for saved scenarios; the same prompt pattern (FR-VAL-009/010, BR-VAL-010).
- **All public domains** — none: research content never requires authentication (BR-ACC-003).

## 9. Open Questions & Risks

- **OQ-ACC-001 — E-mail verification and password reset — RESOLVED at Gate 2 (2026-10-06).** Decision: both are included — verification e-mail on registration (FR-ACC-008, promoted to Must) and password reset via e-mailed link (FR-ACC-009, Must); UC-ACC-004 confirmed in scope. Requires a free-tier e-mail delivery service. *Rationale on record: reset prevents permanent loss of a user's saved work; verification blocks junk accounts and proves address ownership.*
- **OQ-ACC-002 — KVKK review timing.** ASM-008's validation (review collected account data before launch) is scheduled as UC-ACC-005. *Impact if skipped: regulatory exposure on the platform's only personal-data surface; the informational-only SPK posture (ASM-004) would also need re-checking before any monetization step anyway.*
- **RISK-ACC-001 — Account data breach.** Single-factor e-mail+password auth on a public site will attract credential-stuffing attempts. *Mitigation: hashed credentials (BR-ACC-006), minimal data surface (BR-ACC-008 — there is nothing financially sensitive to steal), generic sign-in errors (NFR-ACC-002); rate limiting is a design/architect matter.*
- **RISK-ACC-002 — Junk/bot registrations** if verification is not enabled. *Mitigation: FR-ACC-008 (Should); deletion hygiene is the fallback.*
