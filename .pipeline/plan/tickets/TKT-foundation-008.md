# TKT-foundation-008: WireMock canned-source payload catalog

- Status: done
- PR: https://github.com/sarperim/degerli/pull/8
- Size: M
- Scope: the named canned-source payload catalog per FU §10 — `prices-ok.json`, `prices-ok-tminus1.json`, `prices-invalid-negative-close.json`, `prices-missing-provenance.json`, `prices-unparseable.json`, `prices-conflicting-value.json`, `prices-source-down` (500), `prices-source-slow.json` (5 s), `prices-variant-threshold.json`, `statements-ok-alfa-fy2025.json`, `statements-restated-rest.json`, `statements-no-cf-part.json`, `dividends-ok.json`, `corporate-actions-ok.json`, `disclosures-ok.json`, `universe-add-remove.json`, `universe-missing-sector.json`, `index-levels-ok.json`, `macro-ok.json`, `macro-indep-cpi-absent.json`, `tefas-*.json`, `evren-draft-ok.json` — plus WireMock mapping helpers consumed via the TKT-foundation-006 harness.
- Traces to: foundation (test strategy §7 doubles policy)
- Acceptance (explicit): every named payload is loadable through a helper; payload contents derive from the FU seed data (same instruments, dates, values — a consistency test cross-checks prices-ok against the seeded `daily_prices`); a sample stubbed ingest test consumes `prices-ok.json` end-to-end through the harness.
- Architecture refs: test strategy `.pipeline/testing/00-test-strategy.md` §7; FU `.pipeline/testing/01-fixture-universe.md` §10
- UX refs: —
- Dependencies: TKT-foundation-006, TKT-foundation-007
- Parallel group: P-5
