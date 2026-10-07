# TKT-foundation-006: Backend test harness

- Status: in-progress
- Size: M
- Scope: `/tests/Core.UnitTests` + `/tests/Api.IntegrationTests` scaffolding: Testcontainers Postgres base (fresh migrated+seeded container per test class, order-independent tests); WireMock.Net server helper; fake `TimeProvider` wiring; in-process mail dispatcher double; Serilog test sink; fixture-builder helpers for creating users/screens/scenarios via the API. Must NOT implement the fixture universe data itself (TKT-foundation-007) or the canned source payloads (TKT-foundation-008).
- Traces to: foundation (test strategy §5.1–§5.2, §7 doubles policy)
- Acceptance (explicit): one sample L1 unit test and one sample L2 integration test pass deterministically; the L2 sample runs against real Postgres via Testcontainers with migrations applied by the harness; the fake clock controls a scheduler-tick assertion in the sample; the mail double captures a dispatched message; a failing assertion exits the runner non-zero.
- Architecture refs: `01-system-architecture.md` §5 (testing row), §6.1; test strategy `.pipeline/testing/00-test-strategy.md` §5.1–5.2, §7, §8.3
- UX refs: —
- Dependencies: TKT-foundation-001, TKT-foundation-005
- Parallel group: P-3
