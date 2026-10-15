---
scope:
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - fixtures/Degerli.Fixtures/Program.cs
  - fixtures/Degerli.Fixtures/Degerli.Fixtures.csproj
  - tests/Api.IntegrationTests/RealSourcePayloadTests.cs
---

# TKT-foundation-015: Real-source payload catalog & parsing fidelity

- Status: todo
- Size: L
- Scope: a **second, real-shape** WireMock payload catalog — capture the actual raw HTTP payloads for five test tickers (`ASELS`, `THYAO`, `GARAN`, `FROTO`, and one recent IPO chosen at capture time) from the live İş Yatırım / KAP / EVDS endpoints into a real-source catalog (separate from the synthetic FU catalog), and add parsing tests exercising real-world shapes: Turkish locale numeric formatting (`1.234.567,89`), `ISO-8859-9`/`windows-1254` encodings, and inconsistent KAP XBRL tags. The FU golden values are untouched — this catalog tests *parsing shape*, not goldens. Capture is bounded (5 tickers, limited history); redistribution terms recorded per RISK-MDF-001. Must NOT alter the synthetic FU catalog or the golden contract.
- Traces to: FR-MDF-001, FR-MDF-002, FR-MOV-014; UC-MDF-001, UC-MDF-002; NFR-MDF-004
- Acceptance: explicit criteria (this is test-harness hardening, not a formula) — (1) the real-source catalog loads through the same helper seam as the FU catalog; (2) each of the five tickers' prices/statements parse into the canonical models with locale-formatted numbers decoded correctly; (3) the ISO-8859-9/windows-1254 bodies decode without mojibake; (4) at least one real KAP XBRL tag variance is exercised; (5) a parsing test consumes one real payload end-to-end through the harness. Plus the new parsing cases recorded in `.pipeline/testing/market-data-foundation.md`.
- Architecture refs: `00-test-strategy.md` §7 (source doubles); `01-fixture-universe.md` §10 (catalog pattern), §1 (synthetic-everywhere principle — this catalog is the recorded exception)
- UX refs: —
- Dependencies: TKT-mdf-021, TKT-mov-008
- Parallel group: P-25
