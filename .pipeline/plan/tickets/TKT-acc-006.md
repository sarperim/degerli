# TKT-acc-006: Account settings endpoints

- Status: done
- PR: https://github.com/sarperim/degerli/pull/16
- Size: M
- Scope: `/src/Api` `/me` endpoints (own endpoint files): `GET /api/v1/me` (documented fields only), `PATCH /api/v1/me {languagePref}` (persisted, applied at sign-in; invalid → 400), `PATCH /api/v1/me/password {current, newPassword}` (wrong current → 400 field error per I-ACC-1; session preserved on success), `DELETE /api/v1/me` (hard delete + cascade screens/scenarios; consent retained anonymized per `02` §5.4; session revoked). Must NOT touch auth endpoints.
- Traces to: FR-ACC-004, FR-ACC-006, FR-ACC-007; UC-ACC-003 (main + alternate a); NFR-ACC-004; BR-ACC-004/005
- Acceptance: TC-ACC-019, TC-ACC-020, TC-ACC-021 — TDD: tests first, then green; specs in `.pipeline/testing/user-accounts.md`.
- Architecture refs: `03-api-design.md` §6 (me rows); `01-system-architecture.md` §7 (FR-ACC-004/006/007 rows); `02-data-model.md` §5.4 (deletion vs consent retention)
- UX refs: —
- Dependencies: TKT-acc-002, TKT-foundation-007
- Parallel group: P-13

## Notes

Note: TC-ACC-021's saved screen/scenario preconditions are created in-test via the fixture builder / seeded user-a content per FU §9 — not via the SCR/VAL endpoints, so no cross-domain dependency.
