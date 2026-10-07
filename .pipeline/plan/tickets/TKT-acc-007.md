# TKT-acc-007: SCR-007 Register UI & verification landing

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-007 route per the TKT-acc-001 design: exactly three inputs with visible password rules and inline validation, reviewable bilingual privacy notice before consent, submit disabled while invalid, pending lock, success state (signed-in + verification-e-mail notice stating saving requires verification + return to preserved work), `EMAIL_TAKEN` message with sign-in path, verification link landing states (verified / expired with resend path), in-session verified-state unblocking via session-query invalidation (M-9), `?returnUrl` + draft restoration (M-3). Must NOT touch the other account screens.
- Traces to: FR-ACC-001, FR-ACC-008, FR-ACC-010 (UI side); UC-ACC-001 (main + alternates); UXR-ACC-001..007, 021
- Acceptance: TC-ACC-022, TC-ACC-023, TC-ACC-024 — TDD: specs first, then green; specs in `.pipeline/testing/user-accounts.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-3, M-9), §8.3 (UXR-ACC-001..007/021 row); `03-api-design.md` §6
- UX refs: SCR-007; UXR-ACC-001, UXR-ACC-002, UXR-ACC-003, UXR-ACC-004, UXR-ACC-005, UXR-ACC-006, UXR-ACC-021; UXR-G-004, UXR-G-018..020, UXR-G-024, UXR-G-027
- Dependencies: TKT-acc-001, TKT-acc-002, TKT-acc-004, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-14
