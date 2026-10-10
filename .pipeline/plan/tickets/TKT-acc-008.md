# TKT-acc-008: SCR-008 Sign In UI & sign-out control

- Status: todo
- Size: S
- Scope: `/src/Web` SCR-008 route per the TKT-acc-001 design: e-mail + password form with generic error rendering (code → i18n copy, never which-field), e-mail preserved on failure, links to reset and registration, pending lock, return-to-work via `?returnUrl` with account language applied; the header sign-out control on every screen (session invalidation, device-local language retained). Must NOT touch other account screens.
- Traces to: FR-ACC-002, FR-ACC-003, FR-ACC-010 (UI side); UC-ACC-002 (main); UXR-ACC-008..011
- Acceptance: TC-ACC-031 — TDD: spec first, then green; spec in `.pipeline/testing/user-accounts.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-4), §8.3 (UXR-ACC-008..011 row), §8.4 (session invalidation); `03-api-design.md` §6
- UX refs: SCR-008; UXR-ACC-008, UXR-ACC-009, UXR-ACC-010, UXR-ACC-011; UXR-G-003, UXR-G-004, UXR-G-013
- Dependencies: TKT-foundation-011, TKT-acc-003, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-15

## Notes

- Note: TC-ACC-028 — sign-in from a save prompt with the account preference applied — lands in TKT-int-005.
