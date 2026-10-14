# TKT-int-005: Gated-save lifecycle flows (SCR + VAL + ACC, end-to-end)

- Status: done — merged to `dev` 2026-10-14 (PR https://github.com/sarperim/degerli/pull/67). Review loop round 1 MERGEABLE (three lenses, zero blocking; CI green on the pre-base-move head, merge clean/additive). Artifact: `.pipeline/plan/reviews/TKT-int-005-round-1.md`. Includes an in-scope fixtures wiring fix (adds `DcfBaselineGenerationJob` to the L4 derived-data pass, invoked directly so no ledger row — preserves TC-VAL-016's zero-row assertion).
- Size: M
- Scope: the cross-domain e2e lifecycle specs for the login-to-save pattern with e-mail-verification gating: anonymous screener save → register → verify (MailPit link) → return with criteria intact → save completes; unverified gate unblocking on in-session verification; the same flows on the DCF calculator (scenario appears in picker and SCR-011); sign-in from a save prompt returning to work with the account language applied; verification from another tab unblocking an open session (M-9). Test specs + wiring fixes only.
- Traces to: UC-SCR-002 (alternate a), UC-VAL-002 (alternate a), UC-ACC-002 (main, from save prompt); FR-SCR-012/013, FR-VAL-013, FR-ACC-008/010; UXR-G-023, UXR-G-024, UXR-G-030
- Acceptance: TC-SCR-028, TC-SCR-032, TC-VAL-024, TC-ACC-027, TC-ACC-028 — TDD: specs first, then green; specs in `.pipeline/testing/stock-screening.md`, `.pipeline/testing/valuation-dcf.md`, `.pipeline/testing/user-accounts.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-3, M-9, M-10), §8.4 (session invalidation); `03-api-design.md` §6
- UX refs: UXR-G-023, UXR-G-024, UXR-G-030; UXR-SCR-019, UXR-VAL-021, UXR-ACC-006
- Dependencies: TKT-scr-006, TKT-scr-007, TKT-val-006, TKT-val-007, TKT-val-008, TKT-acc-007, TKT-acc-010, TKT-foundation-010
- Parallel group: P-18
