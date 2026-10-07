# TKT-scr-004: Saved screens CRUD & persistence gates

- Status: todo
- Size: M
- Scope: `/src/Api` screener module `/me` endpoints: `GET/POST /api/v1/me/screens`, `PATCH/DELETE /api/v1/me/screens/{id}` — criteria round-trip with CAGR windows, duplicate-name rejection via DB unique constraint → 409 `DUPLICATE_NAME`, auth (401) + verified-e-mail (403 `EMAIL_NOT_VERIFIED`) gates, ownership isolation via 404-not-403, re-run with stored criteria computing on latest data. Must NOT touch the run endpoint core or the screener UI.
- Traces to: FR-SCR-007..011; UC-SCR-002 (main), UC-SCR-003 (main); SC-003; UXR-G-029, UXR-G-030 (API side)
- Acceptance: TC-SCR-012, TC-SCR-013, TC-SCR-014, TC-SCR-015, TC-SCR-016, TC-SCR-017, TC-SCR-019 — TDD: tests first, then green; specs in `.pipeline/testing/stock-screening.md`.
- Architecture refs: `03-api-design.md` §4 (saved screens table); `01-system-architecture.md` §7 (FR-SCR-007..011 rows), §8.1 (M-7), §10.1 (verified-e-mail gating); `02-data-model.md` §3.3 (`saved_screens`)
- UX refs: UXR-G-029, UXR-G-030 (API side; UI side in TKT-scr-006/007 + TKT-int-005)
- Dependencies: TKT-scr-002, TKT-acc-002, TKT-acc-003
- Parallel group: P-13
