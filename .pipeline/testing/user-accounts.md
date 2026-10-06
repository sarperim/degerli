# User Accounts & Personalization Test Plan

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved (builder final review 2026-10-07 — §6 interpretations upheld; v1.0 unchanged)
**Derives from:** `analysis/user-accounts.md` (FR-ACC-001..012, UC-ACC-001..005, NFR-ACC-001..005) · `ux/user-accounts.md` (SCR-007..010, UXR-ACC-001..021) · `architecture/` v1.2 (`01` §10.1, `03` §6, `02` §3.6/§5.4) · strategy · fixture contract FU §9.
**TC ID convention:** `TC-ACC-NNN`, permanent. Levels: L1 · L2 · L3 · L4. Time is faked (`TimeProvider`) for lockout (10 fails/15 min), verification-token expiry (48h), and reset-token expiry (2h).

## 1. Scope

**Tested here:** FR-ACC-001..010 (10 non-Won't); UC-ACC-001..005 including alternates; NFR-ACC-001..005; UXR-ACC-001..021 (SCR-007/008/009/010).

**Not tested here:** FR-ACC-011/012 (Won't); rate-limit middleware thresholds and CSRF (XC — ACC asserts lockout, which is Identity-level, not IP-level); real Brevo deliverability (MailPit doubles SMTP; domain-dependent flows manual per FLG-04); the KVKK pre-launch *review decision* (UC-ACC-005 — manual ledger; the data surface it inspects is tested here).

## 2. Test Case Specification

### Group A — Registration (L2)

**TC-ACC-001 — Register: account, consent, verification e-mail, session**
- Traces: FR-ACC-001, FR-ACC-008, UC-ACC-001 main, BR-ACC-001/002, `03` §6 · Level: L2 · Design: EP — valid registration class
- Steps: POST `/api/v1/auth/register` `{email: "new@degerli.test", password: "ValidPass123!", consent: {noticeVersion: "2026-10"}}`.
- Expected: 201 + session cookie with `HttpOnly; Secure; SameSite=Lax`; `consent_records` row (user, notice_version, consented_at, action register); verification e-mail dispatched (MailPit receives it, containing a verification link); session authenticated but unverified (`GET /auth/session` → `{authenticated: true, verified: false}`).
- Dependencies: —

**TC-ACC-002 — Duplicate e-mail rejected with sign-in path data**
- Traces: UC-ACC-001 alternate a, UXR-ACC-004 · Level: L2 · Design: EP — taken class
- Expected: register with user-a's e-mail → 409 `EMAIL_TAKEN` (params carry the e-mail; the client renders the sign-in path — TC-ACC-023); no second account row; no consent row.
- Dependencies: —

**TC-ACC-003 — Password policy boundary**
- Traces: UXR-ACC-003, `01` §10.5 (min 10, no composition rules) · Level: L2 · Design: BVA — 9/10 chars; rule-content classes
- Expected: 9-char password → 400 VALIDATION_FAILED (field password); exactly 10 chars (`abcde12345`) → valid; no composition requirement (all-lowercase 10+ chars valid); server re-validates (client bypassed).
- Dependencies: —

**TC-ACC-004 — Consent required**
- Traces: FR-ACC-001, BR-ACC-002, NFR-ACC-001 · Level: L2 · Design: decision table — consent absent / stale noticeVersion / valid
- Expected: missing consent or unknown noticeVersion → 400 VALIDATION_FAILED; no account created in either case.
- Dependencies: —

**TC-ACC-005 — Minimal data collection**
- Traces: BR-ACC-001, BR-ACC-008, NFR-ACC-001 · Level: L2 · Design: EP — collected-fields class
- Expected: `GET /api/v1/me` for a fresh account exposes only the documented fields (e-mail, languagePref, verification status — no name/profile/demographic fields); the registration payload accepts exactly e-mail + password + consent (unknown fields ignored per payload-evolution, never stored).
- Dependencies: —

### Group B — Sign-in / session (L2)

**TC-ACC-006 — Sign-in happy; language preference returned**
- Traces: FR-ACC-002, FR-ACC-004, UC-ACC-002 steps 1–2, `03` §6 · Level: L2 · Design: EP — valid credentials class
- Expected: POST `/auth/login` user-c → 200 + session + `{languagePref: "en"}`; user-a → `{languagePref: "tr"}`; cookie flags as TC-ACC-001.
- Dependencies: —

**TC-ACC-007 — Generic credential error (enumeration-neutral)**
- Traces: UC-ACC-002 alternate a, UXR-ACC-008, NFR-ACC-002 · Level: L2 · Design: EP — wrong-password / unknown-email classes
- Expected: wrong password for user-a and correct password for a nonexistent address → **identical** 401 `INVALID_CREDENTIALS` responses (same code, same body — nothing revealing which field failed).
- Dependencies: —

**TC-ACC-008 — Lockout boundary**
- Traces: `01` §10.1 (10 fails / 15 min), `03` §6 (429 LOCKED_OUT) · Level: L2 · Design: BVA — 9th/10th failure; window expiry
- Expected: 9 consecutive failures → 9× 401; the 10th → 429 `LOCKED_OUT`; correct password during lockout → 429; after 15 minutes (fake clock) → correct password signs in; a *different* account is unaffected (per-account, not global).
- Dependencies: —

**TC-ACC-009 — Session endpoint shapes**
- Traces: `03` §6, architecture M-9 · Level: L2 · Design: EP — authenticated/anonymous classes
- Expected: signed-in → `{authenticated, email, verified, languagePref, role}`; anonymous → `{authenticated: false}`; `Cache-Control: no-store`.
- Dependencies: —

**TC-ACC-010 — Sign-out revokes the session**
- Traces: FR-ACC-003, UC-ACC-002 step 4, UXR-ACC-010 · Level: L2 · Design: state transition — authenticated → anonymous
- Expected: POST `/auth/logout` → subsequent `GET /auth/session` anonymous; `/me/screens` → 401; the cookie no longer authenticates.
- Dependencies: —

### Group C — E-mail verification (L2)

**TC-ACC-011 — Verify happy; session gains state immediately**
- Traces: FR-ACC-008, UXR-ACC-006, UXR-G-030, architecture M-9 · Level: L2 · Design: state transition — unverified → verified (same session)
- Steps: register; extract the token from the MailPit message; POST `/auth/verify-email` `{token}` in the same session.
- Expected: 200; the session's `GET /auth/session` now shows `verified: true` **immediately** — a previously blocked save (403) now succeeds without re-authentication.
- Dependencies: —

**TC-ACC-012 — Expired verification token**
- Traces: `01` §10.1 (48h single-use), UXR-ACC-006, `03` §6 (410) · Level: L2 · Design: BVA — expiry edge (fake clock)
- Expected: advancing the clock past 48h then consuming → 410 `TOKEN_EXPIRED`; plain-language explanation + resend path (client); account remains unverified; saving still gated.
- Dependencies: —

**TC-ACC-013 — Single-use verification token**
- Traces: `01` §10.1 · Level: L2 · Design: EP — reuse class
- Expected: consuming a valid token twice → second use 410 `TOKEN_EXPIRED`.
- Dependencies: —

**TC-ACC-014 — Resend verification**
- Traces: UXR-ACC-021, `03` §6 · Level: L2 · Design: EP — session-scoped resend classes
- Expected: POST `/auth/resend-verification` while signed-in-unverified → new e-mail dispatched (MailPit); new token valid; anonymous call → 401; verified account → no-op/confirmation (no new token issued); no enumeration surface (session-scoped).
- Dependencies: —

### Group D — Password reset (L2)

**TC-ACC-015 — Reset request is enumeration-neutral**
- Traces: FR-ACC-009, UC-ACC-004 step 1–2, UXR-ACC-012, `03` §6 · Level: L2 · Design: EP — registered/unregistered classes
- Expected: POST `/auth/forgot-password` for a registered and for an unknown address → **identical** 200 neutral confirmations (same body); the reset e-mail (2h single-use token) is dispatched only for the registered one (MailPit).
- Dependencies: —

**TC-ACC-016 — Reset happy path**
- Traces: UC-ACC-004 main, UXR-ACC-015, `03` §6 · Level: L2 · Design: state transition — old credential → new credential
- Expected: POST `/auth/reset-password` `{token, newPassword}` → 200 (session may be established); sign-in works with the new password; the old password → 401; saved work intact (user-a's screen/scenario still listed).
- Dependencies: —

**TC-ACC-017 — Expired reset token; re-request path**
- Traces: UC-ACC-004 alternate a, UXR-ACC-014 · Level: L2 · Design: BVA — 2h expiry edge
- Expected: past 2h (fake clock) → 410 `TOKEN_EXPIRED`; the client offers re-request; a fresh request produces a working token.
- Dependencies: —

**TC-ACC-018 — Reset token single-use; policy applies**
- Traces: `01` §10.1, UXR-ACC-013 · Level: L2 · Design: EP — reuse + weak-password classes
- Expected: second consume → 410; new password of 9 chars → 400 VALIDATION_FAILED (policy enforced on reset too); 10 chars → accepted.
- Dependencies: —

### Group E — Settings (L2)

**TC-ACC-019 — Language preference persists and applies at sign-in**
- Traces: FR-ACC-004, BR-ACC-004, UXR-ACC-017, `03` §6 · Level: L2 · Design: state transition — pref changed → applied cross-session
- Expected: PATCH `/api/v1/me` `{languagePref: "en"}` as user-a → 200; next login returns `en`; invalid value → 400.
- Dependencies: —

**TC-ACC-020 — Password change keeps the session**
- Traces: FR-ACC-007, UXR-ACC-018, `03` §6 · Level: L2 · Design: decision table — correct current / wrong current / weak new
- Expected: PATCH `/me/password` with correct current + valid new → success, **session still authenticated**; new password works at next sign-in, old fails; wrong current password → error with fields preserved *(code per I-ACC-1)*; weak new → 400.
- Dependencies: —

**TC-ACC-021 — Account deletion cascades; consent retained anonymized**
- Traces: FR-ACC-006, BR-ACC-005, UC-ACC-003 step 5, NFR-ACC-004, `02` §5.4 · Level: L2 · Design: state transition — account → deleted (terminal)
- Preconditions: a user with a saved screen and a DCF scenario (created in-test).
- Expected: DELETE `/api/v1/me` → user row gone; saved screens and scenarios gone (cascade); `consent_records` rows retained with `user_id` NULL and `user_ref_hash` populated (KVKK evidence); session revoked (subsequent /me → 401); sign-in with the old credentials → 401; sign-in for other users unaffected.
- Dependencies: —

### Group F — Components (L3)

**TC-ACC-022 — Registration form contract**
- Traces: UXR-ACC-001/002/003, UXR-G-018/019/020 · Level: L3 · Design: decision table — empty/invalid/valid; consent states
- Expected: exactly three inputs (e-mail, password, consent) — no other fields (BR-ACC-001); password rules visible at or before first invalid submission; inline per-field validation; submit disabled while invalid; pending lock on submit; privacy notice reviewable **before** consent; all input preserved on failure; focus placed on first field/error (UXR-G-027).
- Dependencies: —

**TC-ACC-023 — Error and outcome states render**
- Traces: UXR-ACC-004/005, UC-ACC-001 alternates · Level: L3 · Design: EP — error/outcome classes (MSW)
- Expected: EMAIL_TAKEN → clear message + sign-in path; success → signed-in confirmation + verification-e-mail notice stating that saving requires verification; return to preserved in-progress work.
- Dependencies: —

**TC-ACC-024 — Verification landing states**
- Traces: UXR-ACC-006, SCR-007 §3 · Level: L3 · Design: EP — verified/expired classes
- Expected: verified → success notice; expired/invalid → plain-language explanation + resend path; the open session's blocked-save state clears (query invalidation, M-9).
- Dependencies: —

**TC-ACC-025 — Account Settings contains exactly the required sections**
- Traces: UXR-ACC-016, FR-ACC-005 · Level: L3 · Design: EP — content-completeness class
- Expected: language preference, password change, entry points to saved screens and DCF scenarios, account deletion, e-mail verification status (+ resend while unverified) — and nothing else; anonymous direct access → sign-in path (UXR-ACC-020).
- Dependencies: —

**TC-ACC-026 — Deletion double-confirmation**
- Traces: UXR-ACC-019, UXR-G-022 · Level: L3 · Design: EP — confirm/cancel classes
- Expected: deletion requires an explicit confirmation stating that saved screens and DCF scenarios will be lost; focus-trapping dialog with cancel; cancel → nothing happens.
- Dependencies: —

### Group G — End-to-end flows (L4)

**TC-ACC-027 — Verification applies to an open session from another tab**
- Traces: UXR-ACC-006 (M-9), UXR-G-030 second half · Level: L4 · Design: state transition — verified elsewhere → open session unblocked
- Expected: a session blocked on save becomes completable after the verification link is opened in a second tab (refetchOnWindowFocus on the session query) — no re-authentication.
- Dependencies: —

**TC-ACC-028 — Sign-in from a save prompt returns to work; preference applied**
- Traces: UC-ACC-002 main, UXR-ACC-009, FR-ACC-010, SCR-008 flow 1 · Level: L4 · Design: EP — return-to-work class
- Expected: anonymous DCF assumptions + save attempt → prompt → sign in as user-c → returned to the calculator with assumptions intact, UI switches to English (account preference), save completes.
- Dependencies: —

**TC-ACC-029 — Language preference across devices**
- Traces: BR-ACC-004, UXR-ACC-017, SCR-010 flow 1 · Level: L4 · Design: EP — cross-device class
- Expected: set English in settings on browser context A → immediate UI switch; sign in on independent context B → English applied.
- Dependencies: —

**TC-ACC-030 — Password reset full flow (incl. expired link)**
- Traces: UC-ACC-004, UXR-ACC-012..015, SCR-009 flows 1–2 · Level: L4 · Design: state transition — request → neutral confirmation → set → sign in; expired variant
- Expected: request → neutral confirmation (no enumeration signal); MailPit link opens the set-password stage directly from an e-mail context; inline rule validation; success → sign-in possible with the new password and saved work intact; an expired link (seeded) shows the explanation + re-request path.
- Dependencies: —

**TC-ACC-031 — Sign out anywhere; language retained device-locally**
- Traces: UXR-ACC-010, SCR-008 flow 3 · Level: L4 · Design: state transition — signed-in → anonymous
- Expected: sign-out from the header on any screen ends the session; header reflects anonymous state; the current language selection remains active on the device.
- Dependencies: —

**TC-ACC-032 — Delete account end-to-end**
- Traces: UC-ACC-003 alternate a, SCR-010 flow 3 · Level: L4 · Design: state transition — confirm → removed → anonymous
- Expected: deletion confirmation warns about losing saved screens and scenarios; on confirm everything is removed, the user is signed out, and previously saved screens are no longer accessible; other users' data unaffected.
- Dependencies: —

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** registrations — valid / duplicate / weak password / missing consent (TC-001..004); credentials — correct / wrong password / unknown address (TC-006/007); reset requests — registered / unknown (TC-015); tokens — fresh / expired / reused (TC-012/013/017/018); callers — anonymous / unverified / verified / builder (throughout).
- **Boundary value analysis:** password length 9/10 (TC-003/018); lockout count 9/10 and window 15 min ± (TC-008); token expiries at 48h and 2h (fake clock, TC-012/017).
- **Decision tables:** consent states × acceptance (TC-004); password-change current/new validity (TC-020); form validity × submit state (TC-022).
- **State transition testing:** anonymous → registered(unverified) → verified (same session) (TC-001/011/027); authenticated → locked out → unlocked (TC-008); old-password → new-password (TC-016/020); account → deleted with anonymized consent retention (TC-021/032); signed-in → signed-out with language retained (TC-031); blocked-save → verified-elsewhere → unblocked (TC-027).
- **Security-relevant negative probes:** enumeration-neutrality via byte-identical responses (TC-007/015); ownership isolation via 404-not-403 (SCR TC-017, VAL TC-013); minimal collected-fields (TC-005).

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** exact `03` §7 codes; identical bodies where neutrality is asserted; token/lockout timings exact under the fake clock; cascade + anonymized-consent assertions at the storage level (via admin/API observability); UI states per mutation matrix. **Fail:** any enumeration leak, any session surviving logout/deletion, any token reuse succeeding. Flaky = fail.

**Suspension:** Testcontainers/compose or MailPit unavailable >1 day; Identity configuration change (lockout/expiry parameters) without re-derivation; a blocking defect in mail-dispatch doubling suspends the verification/reset groups only. **Resumption:** clean triaged run of the affected group.

## 5. Coverage Matrix

| Requirement | Flows covered | Test Cases | Status |
|---|---|---|---|
| FR-ACC-001 | main | TC-001, 004, 005, 022 | planned |
| FR-ACC-002 | main | TC-006, 007, 008 | planned |
| FR-ACC-003 | main | TC-010, 031 | planned |
| FR-ACC-004 | main | TC-006, 019, 028, 029 | planned |
| FR-ACC-005 | main | TC-025 | planned |
| FR-ACC-006 | main | TC-021, 032 | planned |
| FR-ACC-007 | main | TC-020 | planned |
| FR-ACC-008 | main | TC-001, 011, 012, 013, 014, 024, 027 | planned |
| FR-ACC-009 | main | TC-015, 016, 017, 018, 030 | planned |
| FR-ACC-010 | main | TC-028 (pattern with SCR-028/VAL-024) | planned |
| UC-ACC-001 | main; alt a; alt b | TC-001, 022, 023; TC-002; TC-003, 022 | planned |
| UC-ACC-002 | main; alt a; alt b | TC-006, 028, 031; TC-007, 008; TC-030 (reset path) | planned |
| UC-ACC-003 | main; alt a | TC-019, 020, 025; TC-021, 026, 032 | planned |
| UC-ACC-004 | main; alt a | TC-015, 016, 030; TC-017, 030 | planned |
| UC-ACC-005 | pre-launch review | manual ledger (data surface tested: TC-005, 021) | manual |
| NFR-ACC-001 | KVKK minimal collection | TC-004, 005, 021, 022; review ledger | planned |
| NFR-ACC-002 | credential security | TC-003, 007, 008, 015; PBKDF2 config assertion — XC-011 note | planned |
| NFR-ACC-003 | bilingual | XC parity; forms render TR default (TC-022/025 spot) | planned |
| NFR-ACC-004 | retention/deletion | TC-021 | planned |
| NFR-ACC-005 | optional-account posture | XC/SCR/VAL anonymous flows; gates TC-SCR-014, TC-VAL-013 | planned |
| UXR-ACC-001 | exact fields | TC-022 | planned |
| UXR-ACC-002 | notice before consent | TC-022 | planned |
| UXR-ACC-003 | visible rules | TC-003, 022 | planned |
| UXR-ACC-004 | taken → sign-in path | TC-002, 023 | planned |
| UXR-ACC-005 | post-registration notice | TC-023 | planned |
| UXR-ACC-006 | verification landing | TC-011, 024, 027 | planned |
| UXR-ACC-007 | verification gates persistence | TC-SCR-014/024/028, TC-VAL-013/021/024 (cross-ref) | planned |
| UXR-ACC-008 | generic error | TC-007 | planned |
| UXR-ACC-009 | pref applied + return | TC-028 | planned |
| UXR-ACC-010 | sign out anywhere | TC-031 | planned |
| UXR-ACC-011 | reset/register links | TC-025/030 render | planned |
| UXR-ACC-012 | neutral confirmation | TC-015, 030 | planned |
| UXR-ACC-013 | inline rules on reset | TC-018, 030 | planned |
| UXR-ACC-014 | expired link path | TC-017, 030 | planned |
| UXR-ACC-015 | sign in after reset | TC-016, 030 | planned |
| UXR-ACC-016 | settings content | TC-025 | planned |
| UXR-ACC-017 | language immediate + persisted | TC-019, 029 | planned |
| UXR-ACC-018 | password change UX | TC-020, 025 | planned |
| UXR-ACC-019 | deletion consequences | TC-026, 032 | planned |
| UXR-ACC-020 | settings signed-in only | TC-025 | planned |
| UXR-ACC-021 | resend verification | TC-014, 024, 025 | planned |

## 6. Flags and recorded interpretations (for final review)

- **I-ACC-1 (plan-specified):** wrong *current* password on PATCH `/me/password` → `400 VALIDATION_FAILED` (field `current`) — the session is already authenticated, so 401 INVALID_CREDENTIALS would conflate session state with credential verification; the `03` §7 catalog is silent on this case.
- **I-ACC-2 (interpretation):** resend-verification for an already-verified account returns a benign confirmation and issues no token (no enumeration surface; session-scoped by contract).
- **I-ACC-3 (interpretation):** unknown noticeVersion → 400 (consent must reference a currently served bilingual notice version; the version catalog is configuration).
- **Manual-ledger items:** UC-ACC-005 KVKK review (pre-launch, builder); real-domain e-mail deliverability check post-D-04 (FLG-04); PBKDF2 ≥100k iterations is asserted as a configuration check in XC-011's security group.

---
*Change record: v1.0 2026-10-06 — initial ACC test plan, batch mode.*
