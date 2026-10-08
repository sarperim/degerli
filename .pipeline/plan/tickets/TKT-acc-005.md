# TKT-acc-005: Password reset

- Status: in-progress
- Size: M
- Scope: `/src/Api` Identity module (own endpoint files): `POST /api/v1/auth/forgot-password {email}` (always-200 neutral confirmation; enumeration-neutral; reset e-mail with 2h single-use token only for registered addresses) and `POST /api/v1/auth/reset-password {token, newPassword}` (policy enforced on reset; may establish session; 410 on expired/reused). Must NOT touch verification or settings endpoints.
- Traces to: FR-ACC-009; UC-ACC-004 (main + alternate a); NFR-ACC-002
- Acceptance: TC-ACC-015, TC-ACC-016, TC-ACC-017, TC-ACC-018 — TDD: tests first, then green; specs in `.pipeline/testing/user-accounts.md` (byte-identical neutral confirmations asserted).
- Architecture refs: `03-api-design.md` §6 (forgot/reset rows); `01-system-architecture.md` §10.1, §7 (FR-ACC-009 row)
- UX refs: —
- Dependencies: TKT-acc-002
- Parallel group: P-13
