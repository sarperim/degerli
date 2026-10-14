# TKT-acc-009: SCR-009 Password Reset UI

- Status: done — merged to `dev` 2026-10-13 (PR https://github.com/sarperim/degerli/pull/50). Re-gated after TKT-foundation-014 landed: TC-ACC-030 (L4 MailPit link flow) passes (config-gated `SmtpMailDispatcher` delivers the reset link). Review loop completed to pass 3 MERGEABLE (CI green on head `27c6724`); the earlier red TC-ACC-031 was a shared-fixture regression (password-reset spec reset user A's password) fixed by restoring user A via the public reset flow + per-request `/auth/*` rate-limit isolation — no assertion weakened. Artifact: `.pipeline/plan/reviews/TKT-acc-009-round-3.md` (rounds 1–2 artifacts were lost to a crashed session; reconstructed from the fixer commit trail).
- Size: M
- Scope: `/src/Web` SCR-009 route per the TKT-acc-001 design: request stage (e-mail input, neutral confirmation that never signals registration), set-password stage directly reachable from an e-mailed link (external entry point), inline rule validation, expired/invalid link explanation with re-request path, success → sign-in possible (or signed-in state). Must NOT touch other account screens.
- Traces to: FR-ACC-009 (UI side); UC-ACC-004 (main + alternate a); UXR-ACC-012..015
- Acceptance: TC-ACC-030 — TDD: spec first, then green; spec in `.pipeline/testing/user-accounts.md` (MailPit link flow + seeded expired-link variant).
- Architecture refs: `01-system-architecture.md` §8.3 (UXR-ACC-012..015 row); `03-api-design.md` §6 (forgot/reset rows)
- UX refs: SCR-009; UXR-ACC-012, UXR-ACC-013, UXR-ACC-014, UXR-ACC-015; UXR-G-004, UXR-G-018, UXR-G-025
- Dependencies: TKT-foundation-011, TKT-acc-005, TKT-foundation-009, TKT-foundation-010, TKT-foundation-014 (unblock — rebase + re-gate after it lands)
- Parallel group: P-15
