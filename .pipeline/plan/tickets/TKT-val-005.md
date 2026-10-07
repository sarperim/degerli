# TKT-val-005: DCF scenario CRUD

- Status: todo
- Size: M
- Scope: `/src/Api` DCF module `/me/scenarios` endpoints: `GET ?symbol=`, `POST {symbol, name, params}` (8-param schema), `PATCH {name}`, `DELETE {id}` — auth + verified-e-mail gates, duplicate name per (user, stock) → 409 `DUPLICATE_NAME`, cross-stock same name allowed, ownership isolation via 404, exact params round-trip. **Should-level feature (FR-VAL-009/010): deferral is the builder's explicit call, recorded on the ticket — never half-shipped.**
- Traces to: FR-VAL-009, FR-VAL-010 (API side); UC-VAL-002; UXR-VAL-012, UXR-VAL-017, UXR-VAL-018 (API side); Gate 1 decision C
- Acceptance: TC-VAL-013, TC-VAL-014 — TDD: tests first, then green; specs in `.pipeline/testing/valuation-dcf.md`.
- Architecture refs: `03-api-design.md` §5 (scenarios table); `01-system-architecture.md` §7 (FR-VAL-009/010 rows), §10.1 (verified gating); `02-data-model.md` §3.5 (`dcf_scenarios`)
- UX refs: UXR-G-029, UXR-G-030 (API side)
- Dependencies: TKT-acc-002, TKT-acc-003, TKT-foundation-004, TKT-foundation-005
- Parallel group: P-13
