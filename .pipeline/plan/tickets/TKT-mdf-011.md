# TKT-mdf-011: Admin API — quarantine review & dismissal

- Status: todo
- Size: M
- Scope: `/src/Api` admin quarantine endpoints on the TKT-mdf-010 route group (own endpoint files): `GET /api/v1/admin/quarantine?status=open|dismissed`, `POST /api/v1/admin/quarantine/{id}/dismiss {note}` — note required (inline validation), dismissal records the accepted gap, nothing ever deleted, re-ingestion = re-triggering the job. Must NOT touch the admin UI or other admin endpoints.
- Traces to: FR-MDF-012 (review side); UC-MDF-001 (alternate b); BR-MDF-007; UXR-MDF-007..010 (API side)
- Acceptance: TC-MDF-021, TC-MDF-022, TC-MDF-023, TC-MDF-024 — TDD: tests first, then green; specs in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `03-api-design.md` §8 (quarantine endpoints); `01-system-architecture.md` AD-13; `02-data-model.md` §3.1 (`quarantined_facts`)
- UX refs: UXR-MDF-007, UXR-MDF-008, UXR-MDF-009, UXR-MDF-010 (API side; browser side in TKT-mdf-012)
- Dependencies: TKT-mdf-006, TKT-mdf-010
- Parallel group: P-14
