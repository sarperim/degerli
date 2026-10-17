---
scope:
  - src/Ingestion/Prices/
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - fixtures/Degerli.Fixtures/Program.cs
  - fixtures/Degerli.Fixtures/Degerli.Fixtures.csproj
  - tests/Api.IntegrationTests/Harness/
  - tests/Api.IntegrationTests/RealSourcePricesTests.cs
  - tests/Api.IntegrationTests/PriceSourceContractTests.cs
---

# TKT-foundation-015: Real İş Yatırım price-envelope decoding in the product

- Status: todo — regenerated 2026-10-17 (re-plan of the escalated PR #82 wave; builder decisions: real data from now on, BIST-30-first live activation). The architecture v1.5 §10.9.1 is the design authority; escalation findings DR-01 (prices half), DR-02, DR-03 are resolved by that amendment plus test-plan v1.5. PR #82 was closed unmerged — branch `TKT-foundation-015` is raw material (captured HGDG bodies and working decode logic to lift, not contract).
- Size: M
- Scope: lift the `HGDG_*` decoding from the PR #82 fixture catalog into `src/Ingestion/Prices/` and teach `PricesSourceAdapter` its third envelope: the real İş Yatırım body (`{"ok":true,…,"value":[{HGDG_*}]}`) parsed into the canonical `PricesPayload`/`PricesHistoryPayload` on both the fetch and backfill paths, per the §10.9.1 field map (`HGDG_HS_KODU`→symbol, `HGDG_TARIH` dd-MM-yyyy→date, `HGDG_KAPANIS`→close with `HG_KAPANIS` fallback, `HGDG_MIN`/`HGDG_MAX`→low/high, `HGDG_HACIM`→volume as ₺ turnover stored bigint, open = NULL honest gap — never invented from AOF; `ok:false`/non-null `errorCode` = source failure, not a parse event). Envelope detection covers all three shapes (real / Yahoo / canonical-double) so the existing L2 golden path keeps working unchanged. The real-source catalog (`RealSourceCatalog.cs`) is created in the fixtures project with honest captured-vs-constructed provenance labels (§10.9.7 labeling rule — never "captured verbatim" on constructed bodies) and is served through the WireMock harness as **raw bytes** with correct `Content-Type; charset=` headers. TC-MDF-061's Yahoo-contingency leg is updated to the amended v1.5 expectation (volume NULL — FLG-09). Must NOT alter the synthetic FU catalog, the golden contract, storage, metrics, or the API.
- Traces to: FR-MDF-001, FR-MDF-008, FR-MDF-010; UC-MDF-001, UC-MDF-002
- Acceptance: TC-MDF-061 (amended v1.5), TC-MDF-063, TC-MDF-064, TC-MDF-068, TC-MDF-069, TC-MDF-070
- Architecture refs: `01-system-architecture.md` §10.9.1 (field map + volume semantics note), §10.9.7 (test-double posture), AD-14; `03-api-design.md` §1 principle 2 (per-row provenance — unchanged)
- UX refs: —
- Dependencies: TKT-mdf-021, TKT-mov-008 (both done)
- Parallel group: none (first ticket of the real-source wave)
