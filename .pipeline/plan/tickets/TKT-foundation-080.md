---
scope:
  - src/Ingestion/KapPublic/
  - src/Persistence/Entities/MarketDataEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Ingestion/IngestionServiceCollectionExtensions.cs
  - tests/Api.IntegrationTests/KapPublicIdentityTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/02-data-model.md
---

# TKT-foundation-080: Public-site company identity — the public oid ↔ instrument mapping is stored, never assumed (three identifier spaces, never conflated)

- Status: todo
- Size: M
- **Priority: P1 — lane ticket 2/6.** The byCriteria list is queried by `mkkMemberOidList` (the public-site company oid), so every lane above this one needs the mapping.
- **Evidence (verified live, investigation doc):** the public-site company oid (ASELS `4028e4a1413b7ef401413bc2251e0047` — the same oid the batch-news endpoint uses) is resolved from `/tr/sirket/{ticker}` / `/tr/api/search/combined`. **The public oid is a different identifier space from VYK's numeric `companyId` (ASELS = 866) and from our `InstrumentId`** — mapping between them must be stored, not assumed (OQ-4). The `VykCompanyDirectory` comma-split conflict doctrine (046) is the precedent for resolution hygiene.
- **What to implement:** a **stored** mapping (own table — e.g. `kap_public_company_map` — or an equivalently explicit store; forward-only additive migration, `02` §8) keyed by instrument, carrying the public oid, its provenance (which resolution body/date), and an asOf; the resolution path over 079's committed identity capture (ticker → oid); idempotent re-resolution (`unchanged`); honest degradation — an identity-source transport failure fails honestly, an unmapped bound symbol is a **named + counted skip** (the 044 reason-code vocabulary, public-source variant), never a fabricated mapping; the **space-isolation invariant** — nothing ever writes a VYK `companyId` where a public oid belongs or vice versa (asserted by test, TC-MDF-105 (iii)).
- Traces to: FR-MDF-002 (company-scoped fetch), FR-MDF-008 (provenance), FR-MDF-012 (isolation — the 046 `COMPANY_IDENTITY_UNRESOLVED` doctrine extended to this source), `02` §8 (forward-only migrations).
- Acceptance: **TC-MDF-105** (test plan §21) — L2 doubles only (the committed identity capture); red→green recorded. Non-regression: `VykCompanyDirectoryTests`, `MemberSecuritiesIngestTests` re-run green (the VYK/shares identity paths untouched). **Live criterion (orchestrator, post-merge):** one paced resolution over the bound universe (XU100) recorded with the resolved/unresolved counts verbatim; unresolved symbols listed by name on the ticket.
- Architecture refs: `01-system-architecture.md` §10.9.19 (identity subsection — landed by this ticket) + §13.1 **v1.5.54** (soft reservation; re-pin at merge); `02-data-model.md` §3.1 gains the mapping table's semantics (in scope; if the store takes a different shape than the anticipated table, amend scope per the 034 Option-A precedent — never widen silently); test plan §21 (TC-MDF-105).
- UX refs: none.
- Dependencies: TKT-foundation-079 (client, config, committed identity capture).
- Parallel group: none (shares `src/Ingestion/KapPublic/`, the DI extensions file and both permanent docs with the lane; the two permanent docs serialize by last-lander rebase).
