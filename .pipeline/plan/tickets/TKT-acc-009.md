# TKT-acc-009: SCR-009 Password Reset UI

- Status: blocked — https://github.com/sarperim/degerli/pull/50 is open (backend + web CI green) but the E2E (compose · Playwright · MailPit) job fails: the password-reset e-mail is never delivered into MailPit in CI, so TC-ACC-030 (the L4 MailPit link-flow case) cannot pass. Root cause (pre-verified 2026-10-13): `IMailDispatcher` has only the log-sink `LoggingMailDispatcher` — no SMTP adapter exists, so the compose.ci `Smtp__*` env is read by nothing. **Routing resolved 2026-10-13 (builder, option A — fix the test infra): unblock ticket TKT-foundation-014 (P-22) adds the config-gated SMTP dispatcher and harness probe. After it lands on `dev`, this PR rebases, re-runs its E2E CI job (TC-ACC-030), and re-enters review — the re-gate belongs to this ticket.**
- Size: M
- Scope: `/src/Web` SCR-009 route per the TKT-acc-001 design: request stage (e-mail input, neutral confirmation that never signals registration), set-password stage directly reachable from an e-mailed link (external entry point), inline rule validation, expired/invalid link explanation with re-request path, success → sign-in possible (or signed-in state). Must NOT touch other account screens.
- Traces to: FR-ACC-009 (UI side); UC-ACC-004 (main + alternate a); UXR-ACC-012..015
- Acceptance: TC-ACC-030 — TDD: spec first, then green; spec in `.pipeline/testing/user-accounts.md` (MailPit link flow + seeded expired-link variant).
- Architecture refs: `01-system-architecture.md` §8.3 (UXR-ACC-012..015 row); `03-api-design.md` §6 (forgot/reset rows)
- UX refs: SCR-009; UXR-ACC-012, UXR-ACC-013, UXR-ACC-014, UXR-ACC-015; UXR-G-004, UXR-G-018, UXR-G-025
- Dependencies: TKT-foundation-011, TKT-acc-005, TKT-foundation-009, TKT-foundation-010, TKT-foundation-014 (unblock — rebase + re-gate after it lands)
- Parallel group: P-15
