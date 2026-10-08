# TKT-foundation-003: CI pipeline (ci.yml)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/10
- Size: M
- Scope: create `.github/workflows/ci.yml` with the three jobs per architecture §6.1: **backend** (restore/build/test; Testcontainers Postgres service container; migrations applied; suite runs), **web** (`npm ci`, ESLint, `tsc --noEmit`, Vitest, i18n parity check, `vite build`), **e2e** (needs the other two; `docker compose -f compose.ci.yml up` stack + Playwright + MailPit; 5-minute budget gate). Must NOT touch `deploy.yml` (that is TKT-int-006) or product code.
- Traces to: foundation (D-06 CI required; C6)
- Acceptance (explicit): PR and push-to-main both trigger all three jobs; a deliberately failing test in each job fails that job (verified once, then reverted); e2e job boots the compose.ci stack, waits for `GET /health` 200, and runs the Playwright smoke from TKT-foundation-010 within the budget gate; jobs use only free public-repo runners; no secrets are required for CI to pass (MailPit + test config only).
- Architecture refs: `01-system-architecture.md` §6.1 (ci.yml), §6.3 (policies), §5 (testing row)
- UX refs: —
- Dependencies: TKT-foundation-001, TKT-foundation-002, TKT-foundation-005, TKT-foundation-006, TKT-foundation-007, TKT-foundation-008, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-6
