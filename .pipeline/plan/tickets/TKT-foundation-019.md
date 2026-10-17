---
scope:
  - src/Ingestion/Macro/
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/MacroEvdsAdapterTests.cs
  - tests/Api.IntegrationTests/RealSourcePricesTests.cs
---

# TKT-foundation-019: EVDS real-envelope capture & macro adapter hardening

- Status: in-review
- Size: M
- Scope: capture a bounded real EVDS payload (the five official series over a bounded window; **API key redacted before the body lands in the catalog**) with honest provenance labels, verify the real response shape against `MacroSourceAdapter` — which today parses its own canonical `EvdsMacroPayload` envelope — and harden the adapter to parse the real shape per §10.9.6. The canonical envelope remains the L2 golden shape (TC-MOV-026 goldens unchanged); the five-official-series set, the bounded 90-day value-date lookback and the key-redaction posture are unchanged. `source_ref` stays `evds`.
- Traces to: FR-MOV-014
- Acceptance: TC-MOV-028 — plus explicit criterion (not CI-runnable): the bounded live EVDS capture itself lands in the real-source catalog (window-bounded, honestly labeled, API-key redaction checked).
- Architecture refs: `01-system-architecture.md` §10.9.6, §10.9.7 (catalog posture), AD-14
- UX refs: —
- Dependencies: TKT-foundation-017, TKT-foundation-018 (`RealSourceCatalog.cs` serialization — 018's live capture lands first)
- Parallel group: none

## Result

- PR: https://github.com/sarperim/degerli/pull/91 (base `dev`, branch `TKT-foundation-019`)
- Acceptance TC-MOV-028 delivered in `tests/Api.IntegrationTests/MacroEvdsAdapterTests.cs`
  (`MacroEvdsRealEnvelopeTests`): real + canonical shapes through one production adapter,
  bounded window, key redaction, and the DB job ingest path. Targeted suite green
  (macro + real-source lanes 29/29; the ticket's TC-MOV-028 set 5/5). `MacroSourceAdapter`
  hardened per §10.9.6 via the new `EvdsMacroDecoder`; canonical TC-MOV-026 goldens unchanged.
- **Explicit non-CI criterion — bounded live EVDS capture: GAP (not fabricated).** Live EVDS
  access is unavailable from this environment: the documented service route
  (`evds2.tcmb.gov.tr/service/evds`) now 302-redirects to `evds3.tcmb.gov.tr`, which serves
  the EVDS SPA shell (HTML), not JSON — no API key, no JSON service reachable. Per the
  ticket's fallback clause the catalog entry (`evds-macro`) is an honestly-labeled
  **constructed** body of the real column shape (never "captured verbatim"); the bounded live
  capture (window-bounded, API-key redaction checked) remains an open ticket criterion to be
  landed when live access + a key are available — same posture foundation-018 used for its
  live-only criteria.
