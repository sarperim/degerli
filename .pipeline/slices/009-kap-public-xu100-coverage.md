---
id: SLICE-009
status: done
scope:
  - compose.prod.yml
  - compose.dev.yml
  - .env.example
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/LiveActivationTests.cs
  - tests/Api.IntegrationTests/EodFreshnessSemanticsTests.cs
  - .pipeline/slices/009-kap-public-xu100-coverage.md
---

# SLICE-009 — Expand KAP `kap-public` Coverage to `XU100` & Schedule Daily Identity Sync

## Context & Goal

With `kap-public` wired as the default KAP source for financial statements and disclosures (`SLICE-003`) and cursor/decoder recovery in place (`SLICE-007`), two deployment configuration gaps still prevented the 70 non-`XU030` constituents of `XU100` from receiving KAP financial statements and disclosures:

1. **Universe bound stuck on `XU030`:** While stock pages, daily prices, intraday quotes, and sector medians cover `XU100`, `Ingestion:Statements:UniverseIndex` and `Ingestion:Disclosures:UniverseIndex` remained at their VYK-era `"XU030"` default because `compose.prod.yml`, `compose.dev.yml`, and `.env.example` did not set `Ingestion__Statements__UniverseIndex` or `Ingestion__Disclosures__UniverseIndex` to `XU100`.
2. **Unscheduled `kap-public-identity` job:** `KapPublicIdentitySyncJob` (`kap-public-identity`) populates `kap_public_company_map` for all constituents in `Ingestion:Statements:UniverseIndex`, which `StatementsJob` and `DisclosuresJob` require before walking a company's public-site filings. However, `kap-public-identity` was not included in `Ingestion:Scheduler:JobSchedules` in `src/Api/appsettings.json`, leaving new or expanded `XU100` constituents unmapped unless triggered manually.

This slice:
- Sets `Ingestion__Statements__UniverseIndex: ${Ingestion__Statements__UniverseIndex:-XU100}` and `Ingestion__Disclosures__UniverseIndex: ${Ingestion__Disclosures__UniverseIndex:-XU100}` in `compose.prod.yml` and `compose.dev.yml`, and documents `Ingestion__Statements__UniverseIndex=XU100` and `Ingestion__Disclosures__UniverseIndex=XU100` in `.env.example` (preserving the VYK-era `appsettings.json` `"XU030"` fallback pinned by legacy VYK tests).
- Adds `{ "JobCode": "kap-public-identity", "Cron": "0 15 8 * * *", "TradingDaysOnly": true }` to `Ingestion:Scheduler:JobSchedules` in `src/Api/appsettings.json` so `kap_public_company_map` refreshes automatically at 08:15 Europe/Istanbul on trading days ahead of the 08:30 morning chain and 09:00 KAP sweeps.
- Extends `LiveActivationTests.cs` and `EodFreshnessSemanticsTests.cs` to pin the `XU100` KAP universe wiring and `kap-public-identity` schedule.

## Rules & Invariants

- **Zero live calls in tests:** Verify configuration contracts deterministically in `LiveActivationTests` and `EodFreshnessSemanticsTests`.
- **Config-only override:** All universe indices and schedules remain overridable via `.env` / environment variables without image rebuilds.

## Blockers & Dependencies

- Depends on `SLICE-003`, `SLICE-007`, and `SLICE-008`.

## Verification

- `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "FullyQualifiedName~LiveActivationTests|FullyQualifiedName~EodFreshnessSemanticsTests|FullyQualifiedName~TradingCalendarTests"`

## Result

- Configured `Ingestion__Statements__UniverseIndex: ${Ingestion__Statements__UniverseIndex:-XU100}` and `Ingestion__Disclosures__UniverseIndex: ${Ingestion__Disclosures__UniverseIndex:-XU100}` in `compose.prod.yml` and `compose.dev.yml`.
- Added `Ingestion__Statements__UniverseIndex=XU100` and `Ingestion__Disclosures__UniverseIndex=XU100` to `.env.example`.
- Added `{ "JobCode": "kap-public-identity", "Cron": "0 15 8 * * *", "TradingDaysOnly": true }` to `Ingestion:Scheduler:JobSchedules` in `src/Api/appsettings.json` ahead of the disclosure sweep.
- Updated `SLICE_003_deployment_wiring_selects_kap_public_by_default_and_passes_source_knobs` in `LiveActivationTests.cs` to assert `Ingestion__Statements__UniverseIndex` and `Ingestion__Disclosures__UniverseIndex` default to `XU100` across `.env.example`, `compose.dev.yml`, and `compose.prod.yml`.
- Updated `Eod_Freshness_kap_cadence_entries_are_shipped_config_constants` in `EodFreshnessSemanticsTests.cs` to verify `kap-public-identity` job schedule (`0 15 8 * * *`, trading days only).
- Verified targeted test suite: all 35 tests passed in `Degerli.Api.IntegrationTests`.
