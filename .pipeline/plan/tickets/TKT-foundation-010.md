# TKT-foundation-010: E2E harness (compose.ci + Playwright + MailPit)

- Status: done
- PR: https://github.com/sarperim/degerli/pull/9
- Size: M
- Scope: `compose.ci.yml` (API + Postgres + MailPit + now-anchored fixture seeding); `/tests/e2e` Playwright project (Chromium; fixed browser locale tr-TR, timezone Europe/Istanbul); MailPit API helper retrieving message links with bounded polling (≤10 s, explicit error on timeout); network-quiet window assertion helper; smoke spec (stack boots → `GET /health` 200 → root renders TR). Must NOT write product feature specs (those belong to domain tickets).
- Traces to: foundation (test strategy §5.4)
- Acceptance (explicit): `docker compose -f compose.ci.yml up` from a clean checkout brings up a healthy stack with seeded fixtures; the Playwright smoke passes; the MailPit helper retrieves a message sent by a probe flow; the suite runtime budget gate (≤ 5 min) is configured; tests assert flags/offsets/payload values, never wall-clock dates.
- Architecture refs: test strategy `.pipeline/testing/00-test-strategy.md` §5.4, §8.3, §9.1; `01-system-architecture.md` §6.1 (e2e job)
- UX refs: —
- Dependencies: TKT-foundation-001, TKT-foundation-002, TKT-foundation-005, TKT-foundation-007
- Parallel group: P-5
