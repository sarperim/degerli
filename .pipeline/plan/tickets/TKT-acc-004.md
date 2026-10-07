# TKT-acc-004: E-mail verification & resend

- Status: todo
- Size: M
- Scope: `/src/Api` Identity module (own endpoint files): `POST /api/v1/auth/verify-email {token}` (48h single-use; 410 `TOKEN_EXPIRED`; session gains verified state immediately — M-9 groundwork), `POST /api/v1/auth/resend-verification` (session-scoped; no enumeration surface; benign no-op for verified accounts per I-ACC-2). This is the gate behind UXR-G-030 (persistence requires verified e-mail) consumed by the SCR/VAL `/me` endpoints. Must NOT touch reset or settings endpoints.
- Traces to: FR-ACC-008; UXR-ACC-006, UXR-ACC-021; UXR-G-030; RISK-ACC-002
- Acceptance: TC-ACC-011, TC-ACC-012, TC-ACC-013, TC-ACC-014 — TDD: tests first, then green; specs in `.pipeline/testing/user-accounts.md` (fake clock drives 48h expiry; MailPit/mail double supplies tokens).
- Architecture refs: `03-api-design.md` §6 (verify-email/resend rows); `01-system-architecture.md` §10.1, §8.1 (M-9), §7 (FR-ACC-008 row)
- UX refs: UXR-G-030 (API side)
- Dependencies: TKT-acc-002
- Parallel group: P-12
