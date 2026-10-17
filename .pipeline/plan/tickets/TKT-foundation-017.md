---
scope:
  - src/Ingestion/Statements/
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/RealSourceStatementsTests.cs
  - tests/Api.IntegrationTests/RealSourcePricesTests.cs
---

# TKT-foundation-017: KAP XBRL statement decoding in the product

- Status: in-review — PR https://github.com/sarperim/degerli/pull/89
- Size: M
- Scope: lift the KAP XBRL concept map, Turkish-locale number parser and fact-grouping logic from the PR #82 fixture catalog into `src/Ingestion/Statements/`, and teach the statements path the real KAP fact envelope per §10.9.5: inconsistent IFRS vs Turkish-taxonomy tag spellings collapse onto **one** canonical chart-of-accounts item code (`02` §6.1; unmapped → `OTHER_*` honest passthrough); values parse **culture-free** (`1.234.567,89` — `.` thousands, `,` decimal; tolerates accounting parentheses, en-dash minus, trailing `%`, NBSP; the host builds under `InvariantGlobalization`); facts group by (periodType, periodEndDate, statementType, version) — the store assigns the integer version sequence (v1.4); **duplicate concepts within one group: equal values collapse idempotently, unequal values quarantine as `CONFLICTING_VALUE` with both values retained — never silent latest-wins** (this deliberately overturns the fixture accumulator, DR-05; the fixture code is raw material, not contract). The statements-side real bodies land in the catalog with honest provenance labels. Must NOT alter metric formulas, value normalization, or the FU goldens.
- Traces to: FR-MDF-002, FR-MDF-012; UC-MDF-001
- Acceptance: TC-MDF-065, TC-MDF-067, TC-MDF-071
- Architecture refs: `01-system-architecture.md` §10.9.5, AD-14; `02-data-model.md` §6.1
- UX refs: —
- Dependencies: TKT-foundation-015
- Parallel group: P-29
