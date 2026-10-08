# TKT-foundation-007: Fixture universe seed module

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/7
- Size: L
- Scope: the checked-in `fixtures/` module implementing the fixture-universe contract (FU) in full — §3 sectors/instruments (16 archetypes), §4 daily prices T−9…T + index levels + EPSL adjusted rows, §5 statements (ALFA deep history + quarterly, REST restated pair, PART no-CF, ZETA/NEWP/KAPPA series), §6 dividends, §7 macro series (incl. TUIK revision pair + stale GOLD), §8 funds, §9 accounts + §9b business descriptions + `kap_disclosures` rows (8841/8842/8843/9999-stub); L2 absolute-date seeding (R = 2026-10-06) **and** L4 now-anchored seeding (T = seed day, GOLD stale = T−10) through one module; consumed by L2 tests and the compose.ci stack. Must NOT include the WireMock canned-source payloads (TKT-foundation-008).
- Traces to: foundation (test strategy decision F; FU is the golden-value source)
- Acceptance (explicit): seeding is idempotent and re-runnable; L2 seeds reproduce FU tables exactly (spot-assert a representative sample of every §, e.g. ALFA FY2025 row, EPSL adjusted prices, REST dual versions, macro revision pair, TEF0002 holdings gap, user-a pre-seeded screen/scenario); L4 variant anchors T to container start with GOLD = T−10 and emits flags/offsets (never wall-clock dates); both variants load through one deterministic module.
- Architecture refs: test strategy `.pipeline/testing/00-test-strategy.md` §8 (test data strategy); FU `.pipeline/testing/01-fixture-universe.md` (whole document — this ticket implements it)
- UX refs: —
- Dependencies: TKT-foundation-005, TKT-foundation-006
- Parallel group: P-4
