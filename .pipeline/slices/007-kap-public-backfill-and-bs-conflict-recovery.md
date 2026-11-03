---
id: SLICE-007
status: done
scope:
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/StatementsJob.cs
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
---

# SLICE-007 — Unstick `kap-public` Statement Cursors on Backfill & Fix Multi-Subgroup Lease Liability Balance-Sheet Conflicts

## Context & Goal

Two ingestion defects cause stock-page KAP financial statements and balance sheets to serve missing or stale periods:

1. **Multi-subgroup `Kiralama İşlemlerinden Borçlar` balance-sheet conflict (`PETKM`):**
   - In KAP's `general_role_210015` balance-sheet taxonomy, the child caption `Kiralama İşlemlerinden Borçlar` is repeated under each borrowing sub-heading within `KISA VADELİ YÜKÜMLÜLÜKLER` (`İlişkili Taraflardan Kısa Vadeli Borçlanmalar`, `İlişkili Olmayan Taraflardan Kısa Vadeli Borçlanmalar`, `Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları`, `İlişkili Olmayan Taraflardan Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları`) and within `UZUN VADELİ YÜKÜMLÜLÜKLER` (`İlişkili Taraflardan Uzun Vadeli Borçlanmalar`, `İlişkili Olmayan Taraflardan Uzun Vadeli Borçlanmalar`).
   - When a filer such as `PETKM` carries non-zero lease liabilities in more than one borrowing sub-group inside the same current or non-current block (e.g. both related-party and third-party lease liabilities), both rows map to `ST_LEASE_LIAB` (or `LT_LEASE_LIAB`), triggering `DUPLICATE_LABEL_CONFLICT` and quarantining the entire Balance Sheet as `CONFLICTING_VALUE`.
   - When a new borrowing sub-heading (`Borçlanmalar`, excluding `Diğer ...` leaf rows) sits between the first stored `ST_LEASE_LIAB`/`LT_LEASE_LIAB` row and a subsequent `Kiralama İşlemlerinden Borçlar` row in the same block, the subsequent row is a separate presentation sub-line and must fall back to its section-scoped `OTHER_BS_Kiralama_İşlemlerinden_Borçlar` passthrough (preserving both values without manufacturing an unreported sum or quarantining the balance sheet), while genuine same-subgroup duplicates with no intervening borrowing sub-heading continue to conflict and quarantine.

2. **`StatementsJob.RunKapPublicAsync` ignores `request.BackfillFrom` (`request.IsBackfill`):**
   - Companies whose `kap-public` statement cursors (`kap-public-statements-cursor`) advanced to the current watermark while reports failed transport or were refused prior to transport/decoder fixes remain stuck on `Seen`/`Retried` tombstones, because `StatementsJob.RunKapPublicAsync` always computes `plan` from `today.AddMonths(-lookbackMonths)` and never resets the cursor when `request.BackfillFrom` is supplied.
   - When `request.BackfillFrom` is supplied (`backfillFrom <= today`), `RunKapPublicAsync` must use `backfillFrom` as the walk plan start date and rewind each mapped constituent's `kap-public` statement cursor to `KapPublicStatementCursor.Start(backfillFrom)` (counting `cursorResets`) before computing `watermarks` and `walkOrder`, so an admin backfill trigger (`POST /api/v1/admin/ingest/statements/run` with `{"backfillFrom": "..."}`) cleanly re-walks and recovers cursor-locked companies across bounded passes.

## Rules & Invariants

- Preserve all 5 committed capture golden counts and assertions in `KapPublicReportDecoderTests.cs`.
- Do not weaken same-subgroup duplicate conflict detection: a repeated `Kiralama İşlemlerinden Borçlar` inside the same borrowing sub-group (without an intervening borrowing sub-group header) must still emit `DUPLICATE_LABEL_CONFLICT` and populate `ConflictingItemCodes`.
- Keep polite walk budget enforcement (`KapPublicWalkBudget`) intact on backfill passes: rewinding cursors before `GetWatermarksAsync` ensures any companies not completed in the first bounded pass remain at `Watermark = backfillFrom` and are prioritized first by the fair pass order on subsequent passes.
- Zero live calls in tests.

## Blockers & Dependencies

- None (`SLICE-001`..`SLICE-006` are merged to `dev`).

## Verification

- `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~KapPublicReportDecoderTests|FullyQualifiedName~KapPublicStatementsIngestTests"`

## Result

Implemented and verified:
- `KapPublicReportDecoder`: added `borrowingSubgroupAttestedAt` tracking via `AttestsBorrowingSubgroup` and fallback to section-scoped `OTHER_BS_*` passthrough for multi-subgroup balance sheet repeated lines.
- `StatementsJob`: wired `request.BackfillFrom` into `KapPublicStatementWalkPlan` start date, rewound cursors to `KapPublicStatementCursor.Start(resetTo)` (clearing `Seen` and `Retried`) prior to `GetWatermarksAsync`, and tracked `cursorResets` into `CompleteAsync`.
- Tests added:
  - Unit test `Multi_subgroup_lease_liabilities_fall_back_to_other_while_same_subgroup_duplicates_still_conflict` in `KapPublicReportDecoderTests.cs`.
  - Integration test `Backfill_resets_cursor_clears_tombstones_and_writes_recovered_statements` in `KapPublicStatementsIngestTests.cs`.
- Verification: 73 tests passed in `Degerli.Api.IntegrationTests`.
