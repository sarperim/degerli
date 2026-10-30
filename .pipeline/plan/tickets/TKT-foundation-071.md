---
scope:
  - .github/workflows/ci.yml
  - src/Api/openapi.json
  - src/Web/src/lib/api/generated.ts
  - src/Web/src/lib/api/contracts.ts
  # Ticket-scope amendment (review round 1, LEAD-1): acceptance (iii) cannot be
  # met while these two call sites re-declare request bodies the document already
  # carries (`DismissQuarantineRequest`, `RenameScenarioRequest`). Recorded in
  # `01` §13.1 v1.5.86, not hidden.
  - src/Web/src/screens/admin/api.ts
  - src/Web/src/screens/dcf/scenariosApi.ts
  - src/Web/package.json
  - src/Web/package-lock.json
  - scripts/generate-openapi.sh
  - .pipeline/architecture/03-api-design.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-071: OpenAPI contract codegen — contract drift eliminated at build time

- Status: done — **PR #169 squash-merged 2026-10-30 (`09d9ce1` on dev), round-3 MERGEABLE** (`.pipeline/plan/reviews/TKT-foundation-071-round-{1,2,3}.md`; 3 passes, 2 fixer rounds — LEAD-1: `DismissQuarantineRequest`/`RenameScenarioRequest` generated but re-declared inline at two call sites, fixed with a recorded scope amendment; round-2 false absolute corrected to the measured six-body set). **Non-blocking open:** SEC-4 low — `kill -0` liveness check in `generate-openapi.sh` proves liveness not socket ownership (squatter race, local-only, two-line fix — candidate for the next touch of the script); SEC-2 — no `securitySchemes` declared (rides the endpoint-typing follow-up); recorded follow-ups: response schemas via `Produces<T>`/`TypedResults`, typed request parameters. Web gains committed generated types + `check:api` drift gate; backend gains served-vs-snapshot drift check in CI.
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

- PR: https://github.com/sarperim/degerli/pull/169 (TKT-foundation-071).
- Implementation note (2026-10-30): the **start-the-API** mechanism was chosen (not a build-time generator), so the snapshot *is* the runtime document — the only transformation is `servers` dropped (throwaway bind URL) and keys sorted, proven byte-identical across two runs. Generation lives in `scripts/generate-openapi.sh`, which CI runs (`--check` in the backend job, `OPENAPI_SKIP_BUILD=1`); a developer never needs the API running to *pass* the gate, only `--update` (which runs the script) when a contract change is intended. **Recorded honesty limits (review round 1, DR-1 — both named in `03`'s header and `01` §13.1 v1.5.86):** the served document declares no response schemas (endpoints return untyped `IResult`), so response payload types in `contracts.ts` stay curated with stated reasons until endpoints declare response types; **and** every request body a hand-parsing handler reads is absent from the document entirely — six paths, counted against the snapshot on this head: `POST /api/v1/screener/run`, `POST /api/v1/me/screens`, `POST /api/v1/stocks/{symbol}/dcf/compute`, `PATCH /api/v1/admin/descriptions/{id}`, `PATCH /api/v1/admin/screener-metrics/{code}`, `PATCH /api/v1/me/screens/{id}` — because their handlers take `HttpRequest` and parse the body with `JsonDocument.ParseAsync`, which ASP.NET does not model (the requestBody-less paths the Web calls with no body or an empty `{}` — `POST /admin/dcf-baselines/regenerate`, `POST /admin/descriptions/{id}/publish`, `POST /auth/logout`, `POST /auth/resend-verification` — carry no fields to drift). **Recorded follow-up (not this ticket): endpoint typing** — bind a typed request record on those six handlers, delete the hand-written shape and alias the generated one; the same change is the precondition for declaring `securitySchemes`/response types, so it is also where SEC-2 of this round's review lands.
- Scope amendment (review round 1, LEAD-1): `src/Web/src/screens/admin/api.ts` and `src/Web/src/screens/dcf/scenariosApi.ts` were added to the frontmatter `scope:` list — acceptance (iii) cannot be met while those call sites re-declare `DismissQuarantineRequest`/`RenameScenarioRequest` inline. Recorded in `01` §13.1 v1.5.86 as "ticket-scope amendment, recorded not hidden".

- Keep the snapshot generation **inside CI only** (no requirement that a developer runs the API locally). If a build-time generator is chosen instead of start-the-API, record the exact mechanism and prove the output is byte-identical to the runtime document.
