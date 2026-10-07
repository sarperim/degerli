# TKT-foundation-009: Web test harness (Vitest + Testing Library + MSW)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/3
- Size: M
- Scope: Vitest + Testing Library + MSW setup for `/src/Web` (colocated or `/tests/Web` per test strategy §12): test setup files; MSW server + typed handler helpers mirroring the `03` payload shapes (honest-data envelope included); TanStack Query test wrapper; i18n test loading `tr.json`/`en.json` from the repo at test time; a11y assertion helpers; sample component test + sample MSW-backed query test.
- Traces to: foundation (test strategy §5.3, decision B)
- Acceptance (explicit): sample tests pass deterministically; MSW handlers are typed against the envelope and error-code contract; UI copy assertions load catalogs from the repo (no hardcoded prose in tests); the Vitest command is wired into the CI web job path; jsdom environment configured.
- Architecture refs: test strategy `.pipeline/testing/00-test-strategy.md` §5.3, §6 (decision B), §8.3, §12; `01-system-architecture.md` §5 (testing row)
- UX refs: —
- Dependencies: TKT-foundation-002
- Parallel group: P-2
