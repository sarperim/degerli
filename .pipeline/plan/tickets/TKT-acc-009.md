# TKT-acc-009: SCR-009 Password Reset UI

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-009 route per the TKT-acc-001 design: request stage (e-mail input, neutral confirmation that never signals registration), set-password stage directly reachable from an e-mailed link (external entry point), inline rule validation, expired/invalid link explanation with re-request path, success → sign-in possible (or signed-in state). Must NOT touch other account screens.
- Traces to: FR-ACC-009 (UI side); UC-ACC-004 (main + alternate a); UXR-ACC-012..015
- Acceptance: TC-ACC-030 — TDD: spec first, then green; spec in `.pipeline/testing/user-accounts.md` (MailPit link flow + seeded expired-link variant).
- Architecture refs: `01-system-architecture.md` §8.3 (UXR-ACC-012..015 row); `03-api-design.md` §6 (forgot/reset rows)
- UX refs: SCR-009; UXR-ACC-012, UXR-ACC-013, UXR-ACC-014, UXR-ACC-015; UXR-G-004, UXR-G-018, UXR-G-025
- Dependencies: TKT-foundation-011, TKT-acc-005, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-15
