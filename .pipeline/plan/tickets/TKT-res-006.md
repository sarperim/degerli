# TKT-res-006: Admin descriptions API

- Status: todo
- Size: M
- Scope: `/src/Api` admin endpoints on the TKT-mdf-010 route group (own endpoint files): `GET /api/v1/admin/descriptions?status=` (queue payload **is** the editor payload — both texts, `sourceRefs` resolved against `kap_disclosures` with `{disclosureId}`-only stubs for unresolvable ids, wrapped root), `PATCH /admin/descriptions/{id} {textTr?, textEn?}` (strict contract — a `sourceRefs` write → 400 per I-RES-2), `POST /admin/descriptions/{id}/publish` (gate: both languages required; DB CHECK backstop). Must NOT touch the C4 CLI or the admin UI.
- Traces to: FR-RES-020, FR-RES-021; UC-RES-004 (steps 2–4); UXR-MDF-018..022 (API side); BR-RES-003
- Acceptance: TC-RES-018, TC-RES-019, TC-RES-020, TC-RES-021 — TDD: tests first, then green; specs in `.pipeline/testing/stock-research.md` (I-RES-1/I-RES-2 binding).
- Architecture refs: `03-api-design.md` §8 (descriptions endpoints + v1.2 response sketch); `01-system-architecture.md` §7 (FR-RES-020/021 rows), AD-13, v1.2 change record; `02-data-model.md` §3.4
- UX refs: UXR-MDF-018, UXR-MDF-019, UXR-MDF-020, UXR-MDF-021, UXR-MDF-022 (API side; UI side in TKT-res-010)
- Dependencies: TKT-mdf-010, TKT-res-005, TKT-foundation-007
- Parallel group: P-15
