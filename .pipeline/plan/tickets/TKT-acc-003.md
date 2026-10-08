# TKT-acc-003: Sign-in, lockout, sign-out & session endpoint

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/19
- Size: M
- Scope: `/src/Api` Identity module (own endpoint files): `POST /api/v1/auth/login` (session + `{languagePref}`; generic 401 `INVALID_CREDENTIALS`; 429 `LOCKED_OUT` after 10 fails / 15 min per account), `POST /api/v1/auth/logout` (revokes session). Must NOT touch registration/verification/reset/settings.
- Traces to: FR-ACC-002, FR-ACC-003, FR-ACC-004 (return side); UC-ACC-002 (main + alternate a); NFR-ACC-002
- Acceptance: TC-ACC-006, TC-ACC-007, TC-ACC-008, TC-ACC-009, TC-ACC-010 — TDD: tests first, then green; specs in `.pipeline/testing/user-accounts.md` (fake clock drives the lockout window).
- Architecture refs: `03-api-design.md` §6 (login/logout/session rows); `01-system-architecture.md` §10.1, §7 (FR-ACC-002..004 rows)
- UX refs: —
- Dependencies: TKT-acc-002
- Parallel group: P-11
