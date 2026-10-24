---
scope:
  - .github/workflows/ci.yml
  - src/Api/openapi.json
  - src/Web/src/lib/api/generated.ts
  - src/Web/src/lib/api/contracts.ts
  - src/Web/package.json
  - src/Web/package-lock.json
  - scripts/generate-openapi.sh
  - .pipeline/architecture/03-api-design.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-071: OpenAPI contract codegen — contract drift eliminated at build time

- Status: todo
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 7 unparked).**
- **Evidence (gathered 2026-10-24):**
  - The API already serves a runtime OpenAPI document: `Program.cs:74` `app.MapOpenApi("/api/v1/openapi.json")` (Microsoft.AspNetCore.OpenApi), and `03-api-design.md` documents it as served.
  - `src/Web/src/lib/api/contracts.ts` is **hand-written**, and its header says so: "Client-side API contracts mirroring `.pipeline/architecture/03-api-design.md` … Domain tickets extend this module as they implement their endpoints; the shapes here are what MSW handlers in `src/test/msw` are typed against."
  - There are therefore **three** contract surfaces: `03` (design prose), the runtime OpenAPI document, and `contracts.ts` (hand types). Nothing compares them. Every recent contract change (036, 049, 057, 058/065) was a hand edit of `contracts.ts` alongside the API; a missed edit is only caught if a test happens to exercise the shape.
  - No checked-in OpenAPI snapshot exists, so a served-shape change is invisible in review unless someone diffs `03` and the code by hand.
- **Pinned approach (decision recorded):**
  - **Canonical machine contract = the generated OpenAPI document** (the code is the only thing that actually serves). `03` stays the design authority for intent/semantics; it is prose and is not a schema source.
  - Check in a snapshot (`src/Api/openapi.json`); a CI step starts/builds the API, regenerates the document, and **fails if the snapshot differs** (`--update` path documented).
  - Generate `src/Web/src/lib/api/generated.ts` with `openapi-typescript`; `contracts.ts` re-exports/aliases the generated types (keep curated doc comments where useful). `tsc --noEmit` then proves the Web builds against the generated contract.
  - **Options recorded:** (a) commit the snapshot and require regeneration in the PR (**recommended** — drift is visible in review); (b) generate at build and never commit (drift invisible); (c) contract tests only, no codegen (smaller, weaker).
  - Do **not** auto-generate from `03`; it is prose, not a schema.
- **Acceptance (explicit criteria):**
  - **(i)** CI fails when the served OpenAPI differs from the committed snapshot — a deliberate field rename/type change fails, an unchanged build passes; the regeneration command is documented.
  - **(ii)** Web types are generated; `tsc --noEmit` passes; MSW handlers compile against them.
  - **(iii)** No hand-maintained duplicate of a generated shape remains without an explicit, documented reason; existing runtime behaviour is unchanged.
  - **(iv)** The snapshot is deterministic (stable ordering/format) so CI does not flake on serialization noise.
  - **(v)** `01` §6.1 and `03`'s header note are updated; a §13.1 row is appended (**soft reservation v1.5.49**; re-pin at merge).
- Traces to: `03` §1/§9 (contract principles, additive rule), NFR-MOV-001 (client/server coherence), `01` §6.1.
- Architecture refs: `01-system-architecture.md` §6.1 (CI jobs), §13.1; `03-api-design.md` header + §1/§2.
- UX refs: none.
- Dependencies: none.
- Parallel group: none (touches `01`).

## Notes

- Keep the snapshot generation **inside CI only** (no requirement that a developer runs the API locally). If a build-time generator is chosen instead of start-the-API, record the exact mechanism and prove the output is byte-identical to the runtime document.
