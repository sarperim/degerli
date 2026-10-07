# TKT-int-004: Security, caching, performance & infrastructure contracts

- Status: todo
- Size: L
- Scope: the cross-cutting contract specs: i18n parity script as a formal CI gate (TC-XC-002 — the script ships with TKT-foundation-002; this ticket makes it a recorded, failing-case-verified gate); advice-vocabulary grep (TC-XC-007, term list maintained beside the script); security headers; CSRF on unsafe methods across endpoint classes; rate-limit edges (5/600/60, `retryAfter`, window reset) + PBKDF2 config assertion; performance budget smoke (p95 ≤ 2× targets, ≥30 calls each; Playwright ≤ 5 min gate); caching-policy decision table per surface; reproducibility compose-up smoke; OpenAPI document served with no fund routes. Test specs + middleware configuration fixes only (middleware itself ships with TKT-foundation-004).
- Traces to: `01` §10 (security posture), §9 (performance targets); `03` §2, §7, §12, §13; NFR-MOV-002/006; NFR-ACC-002; NFR-MDF-005; strategy decisions A/D
- Acceptance: TC-XC-002, TC-XC-007, TC-XC-009, TC-XC-010, TC-XC-011, TC-XC-014, TC-XC-015, TC-XC-016, TC-XC-018 — TDD: specs first, then green; specs in `.pipeline/testing/cross-cutting.md` (I-XC-1/I-XC-2 interpretations binding).
- Architecture refs: `01-system-architecture.md` §6.1, §9, §10.1–§10.5; `03-api-design.md` §2, §7, §12, §13
- UX refs: —
- Dependencies: TKT-foundation-003, TKT-foundation-004, TKT-mov-004, TKT-scr-002, TKT-scr-004, TKT-res-002, TKT-res-003, TKT-res-004, TKT-val-003, TKT-val-004, TKT-acc-002, TKT-acc-003, TKT-mdf-010
- Parallel group: P-18
