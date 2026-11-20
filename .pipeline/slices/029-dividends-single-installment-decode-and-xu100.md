---
id: SLICE-029
status: done
scope:
  - .pipeline/slices/029-dividends-single-installment-decode-and-xu100.md
  - src/Ingestion/Dividends/DividendsSourceAdapter.cs
  - compose.dev.yml
  - tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs
---

# SLICE-029 — Fix Single-Installment VYK Dividend Decoding (`ReadRows` Object Form) & XU100 Dev Scope

## Context & Goal

Data-validity audit of the live DB revealed that `dividends` only held **4 rows across 2 stocks (`AEFES` and `BIMAS`)** even though `corporate_actions` already stored **65 `KP` (Kâr Payı / cash dividend) disclosures** (`FROTO`, `TUPRS`, `TCELL`, `ASELS`, `THYAO`, etc.):

1. **Root cause (`DividendsSourceAdapter.ReadRows`):**
   - MKK VYK's XML-to-JSON bridge serializes multi-row tables as `"void": [ { "object": ... }, ... ]` (`JsonValueKind.Array`), but serializes **single-row tables** (every lump-sum / 1-installment dividend like `FROTO 2023-10-26`, `TUPRS`, `TCELL`, etc.) as `"void": { "object": ... }` (`JsonValueKind.Object`).
   - `ReadRows` checked `rows.ValueKind != JsonValueKind.Array` and returned `yield break`, silently dropping **every single-installment dividend across the entire BIST** while only decoding 2-installment payers (`AEFES`, `BIMAS`).
2. **Confirmed ex-date preference & share-class deduplication (`DecodeDetail`):**
   - Prefer confirmed `usageDate` over proposed `advisedUsageDate` (`ParseDate(Text(payment, "usageDate")) ?? ParseDate(Text(payment, "advisedUsageDate"))`).
   - Deduplicate identical `(symbol, exDate, amount)` rows when multiple share classes in `cashDividendList` share the same `exchCode` (e.g., `FROTO` A/B share groups).
3. **Dev coverage (`compose.dev.yml`):**
   - Set `Ingestion__Dividends__UniverseIndex: ${Ingestion__Dividends__UniverseIndex:-XU100}` alongside `Statements` and `Disclosures` so dev walks all BIST 100 constituents instead of only `XU030`.

## Rules & Invariants

- **Honest-data contract:** Never invent dates or amounts; still reject missing/unparseable `grossTry` and omit rows with no ex-date.
- **Zero live calls in tests:** Test with deterministic WireMock VYK single-installment payload.

## Blockers & Dependencies

- None.

## Verification

- [x] `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~TKT_foundation_034_dividends_vyk_ca_route_ingests_kp_installments|FullyQualifiedName~SLICE_029_dividends_vyk_decodes_single_installment_object_form_and_deduplicates_share_classes"` passes (2/2).

## Result

- Fixed `DividendsSourceAdapter.ReadRows` to decode both multi-row `JsonValueKind.Array` and single-row `JsonValueKind.Object` XML-to-JSON tables.
- Updated `DividendsSourceAdapter.DecodeDetail` to prefer confirmed `usageDate` over proposed `advisedUsageDate` and deduplicate identical `(exDate, amount)` rows across share classes.
- Added `Ingestion__Dividends__UniverseIndex=XU100` to `compose.dev.yml` and added WireMock integration test coverage in `StatementsAndEventsIngestionTests.cs`.
