# TKT-acc-002: Identity core — registration, consent & session

- Status: done
- PR: https://github.com/sarperim/degerli/pull/13
- Size: L
- Scope: `/src/Api` Identity module: ASP.NET Core Identity + cookie auth (HttpOnly; Secure; SameSite=Lax), `POST /api/v1/auth/register` (e-mail + password + consent `{noticeVersion}`; consent records; minimal-field posture; 409 `EMAIL_TAKEN`; password policy min 10 chars, no composition rules, server re-validation), `GET /api/v1/auth/session` (shape per `03` §6, no-store), `asp_net_users.language_pref` extension, PBKDF2 ≥100k iterations, bilingual e-mail dispatch plumbing via the SMTP double (real Brevo wiring is config, per FLG-04). Establishes the auth route-group file later ACC tickets extend with own endpoint files. Must NOT touch verify/reset/settings endpoints.
- Traces to: FR-ACC-001, FR-ACC-008 (dispatch side); UC-ACC-001 (main + alternates a/b); NFR-ACC-001, NFR-ACC-002
- Acceptance: TC-ACC-001, TC-ACC-002, TC-ACC-003, TC-ACC-004, TC-ACC-005 — TDD: tests first, then green; specs in `.pipeline/testing/user-accounts.md` (I-ACC-3 binding: unknown noticeVersion → 400).
- Architecture refs: `03-api-design.md` §6 (register/session rows); `01-system-architecture.md` §10.1, §5 (auth row), §7 (FR-ACC-001/008 rows); `02-data-model.md` §3.6, §5.4
- UX refs: — (API side; screens in TKT-acc-007..010)
- Dependencies: TKT-foundation-004, TKT-foundation-005, TKT-foundation-006
- Parallel group: P-10
