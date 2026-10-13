# TKT-acc-009: SCR-009 Password Reset UI

- Status: in-review — https://github.com/sarperim/degerli/pull/50 (re-gated after TKT-foundation-014 landed on `dev`). Forward-merged `origin/dev` (SMTP dispatcher, merge ac337d1). TC-ACC-030 (L4 MailPit link flow) now executes and passes: 3/3 in `tests/e2e/src/password-reset.spec.ts` against the seeded compose.ci stack — the e-mailed reset link is delivered into MailPit by the config-gated `SmtpMailDispatcher` and drives the full request → link → set → signed-in flow (saved work intact); the neutral-confirmation and expired/invalid-link variants also pass. Web gate green: lint, typecheck, 219 unit/component tests, i18n parity (528 keys), production build. Harness fix (this ticket's own spec): each navigation allocates a fresh `X-Forwarded-For` so the two full page loads — which each re-issue `GET /auth/session` + `GET /auth/csrf-token` under the same `auth` bucket — no longer exceed the 5/min limit on a single key (I-XC-2); the product's `03` §12 limit is unchanged and no assertion was altered.
- Size: M
- Scope: `/src/Web` SCR-009 route per the TKT-acc-001 design: request stage (e-mail input, neutral confirmation that never signals registration), set-password stage directly reachable from an e-mailed link (external entry point), inline rule validation, expired/invalid link explanation with re-request path, success → sign-in possible (or signed-in state). Must NOT touch other account screens.
- Traces to: FR-ACC-009 (UI side); UC-ACC-004 (main + alternate a); UXR-ACC-012..015
- Acceptance: TC-ACC-030 — TDD: spec first, then green; spec in `.pipeline/testing/user-accounts.md` (MailPit link flow + seeded expired-link variant).
- Architecture refs: `01-system-architecture.md` §8.3 (UXR-ACC-012..015 row); `03-api-design.md` §6 (forgot/reset rows)
- UX refs: SCR-009; UXR-ACC-012, UXR-ACC-013, UXR-ACC-014, UXR-ACC-015; UXR-G-004, UXR-G-018, UXR-G-025
- Dependencies: TKT-foundation-011, TKT-acc-005, TKT-foundation-009, TKT-foundation-010, TKT-foundation-014 (unblock — rebase + re-gate after it lands)
- Parallel group: P-15
