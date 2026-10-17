---
scope:
  - src/Ingestion/Macro/
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/MacroEvdsAdapterTests.cs
---

# TKT-foundation-019: EVDS real-envelope capture & macro adapter hardening

- Status: todo
- Size: M
- Scope: capture a bounded real EVDS payload (the five official series over a bounded window; **API key redacted before the body lands in the catalog**) with honest provenance labels, verify the real response shape against `MacroSourceAdapter` — which today parses its own canonical `EvdsMacroPayload` envelope — and harden the adapter to parse the real shape per §10.9.6. The canonical envelope remains the L2 golden shape (TC-MOV-026 goldens unchanged); the five-official-series set, the bounded 90-day value-date lookback and the key-redaction posture are unchanged. `source_ref` stays `evds`.
- Traces to: FR-MOV-014
- Acceptance: TC-MOV-028 — plus explicit criterion (not CI-runnable): the bounded live EVDS capture itself lands in the real-source catalog (window-bounded, honestly labeled, API-key redaction checked).
- Architecture refs: `01-system-architecture.md` §10.9.6, §10.9.7 (catalog posture), AD-14
- UX refs: —
- Dependencies: TKT-foundation-017, TKT-foundation-018 (`RealSourceCatalog.cs` serialization — 018's live capture lands first)
- Parallel group: none
