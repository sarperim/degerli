# TKT-acc-010: SCR-010 Account Settings UI

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-010 route per the TKT-acc-001 design: exactly the required sections (language preference with immediate application + persistence, password change with inline validation and session preserved, entry points to My Saved Screens and My DCF Scenarios, account deletion behind a focus-trapping double-confirmation stating consequences, e-mail verification status + resend while unverified) and nothing else; anonymous access → sign-in path; signed-in-only navigation. Must NOT touch other account screens.
- Traces to: FR-ACC-005, FR-ACC-006, FR-ACC-007 (UI side); UC-ACC-003 (main + alternate a); UXR-ACC-016..021; BR-ACC-001 (minimal posture)
- Acceptance: TC-ACC-025, TC-ACC-026, TC-ACC-029, TC-ACC-032 — TDD: specs first, then green; specs in `.pipeline/testing/user-accounts.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-4), §8.3 (UXR-ACC-016..021 row), §8.4; `03-api-design.md` §6 (me rows)
- UX refs: SCR-010; UXR-ACC-016, UXR-ACC-017, UXR-ACC-018, UXR-ACC-019, UXR-ACC-020, UXR-ACC-021; UXR-G-013, UXR-G-014, UXR-G-022
- Dependencies: TKT-foundation-011, TKT-acc-006, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-16
