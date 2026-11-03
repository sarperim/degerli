---
id: SLICE-003
status: done
scope:
  - .pipeline/slices/003-kap-public-deploy-wiring.md
  - AGENTS.md
  - compose.prod.yml
  - compose.dev.yml
  - .env.example
  - deploy/README.md
  - tests/Api.IntegrationTests/LiveActivationTests.cs
---

# SLICE-003: Wire `kap-public` as the Default Deployed KAP Source in Compose & Env Templates

## Context & Goal

In `src/Api/appsettings.json`, `Ingestion:Statements:Source` and `Ingestion:Disclosures:Source` ship as `"vyk"` so that the L2 integration suite (`TC-MDF-109`, `StatementsAndEventsIngestionTests`, etc.) exercises the VYK double without per-test source overrides.

However, neither `compose.prod.yml`, `compose.dev.yml`, nor `.env.example` passes `Ingestion__Statements__Source` or `Ingestion__Disclosures__Source` into the `api` container environment. As a result, deploying `compose.prod.yml` to a VPS (or booting `compose.dev.yml` from `.env.example`) leaves both KAP lanes on `"vyk"` (the key-gated MKK dev gateway) with empty credentials, causing both the EOD/morning chains and SLICE-002's 2-hour intraday KAP sweeps to fail instead of running the keyless live `kap-public` lane (`TKT-foundation-082` / `TKT-foundation-084`).

**Goal:** Wire `Ingestion__Statements__Source` and `Ingestion__Disclosures__Source` (defaulting to `kap-public`) in `compose.prod.yml`, `compose.dev.yml`, and `.env.example` (plus `Ingestion__Kap__ApiKey` / `Ingestion__Kap__ApiSecret` passthrough in `compose.prod.yml` so switching back to `vyk` remains config-only), document the env keys in `deploy/README.md`, and pin the deployment wiring in `LiveActivationTests`.

## Rules & Invariants

1. **`src/Api/appsettings.json` and C# option defaults stay `"vyk"`.** `TC_MDF_109_i_source_selection_routes_the_lane_and_leaves_the_other_source_alone` (`tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs`) and the existing L2 VYK suite require `appsettings.json` and `new StatementsSourceOptions().Source` / `new DisclosuresSourceOptions().Source` to remain `"vyk"`.
2. **Compose & `.env.example` select `kap-public` by default.**
   - `compose.prod.yml` `x-api-environment`:
     - `Ingestion__Statements__Source: ${Ingestion__Statements__Source:-kap-public}`
     - `Ingestion__Disclosures__Source: ${Ingestion__Disclosures__Source:-kap-public}`
     - `Ingestion__Kap__ApiKey: ${Ingestion__Kap__ApiKey:-}`
     - `Ingestion__Kap__ApiSecret: ${Ingestion__Kap__ApiSecret:-}`
   - `compose.dev.yml` `x-api-environment`:
     - `Ingestion__Statements__Source: ${Ingestion__Statements__Source:-kap-public}`
     - `Ingestion__Disclosures__Source: ${Ingestion__Disclosures__Source:-kap-public}`
   - `.env.example`:
     - Document and set `Ingestion__Statements__Source=kap-public` and `Ingestion__Disclosures__Source=kap-public`.
3. **Reversibility:** Setting `Ingestion__Statements__Source=vyk` or `Ingestion__Disclosures__Source=vyk` in `.env` (alongside `Ingestion__Kap__ApiKey` and `Ingestion__Kap__ApiSecret`) switches either lane back to the VYK gateway without touching compose files or images.

## Blockers & Dependencies

- Depends on `TKT-foundation-082`, `TKT-foundation-084`, and `SLICE-002` (all `done`).

## Verification

- [x] `LiveActivationTests` includes a fact asserting `.env.example`, `compose.dev.yml`, and `compose.prod.yml` carry `Ingestion__Statements__Source` and `Ingestion__Disclosures__Source` defaulted to `kap-public` (and `compose.prod.yml` passes `Ingestion__Kap__ApiKey` / `Ingestion__Kap__ApiSecret`).
- [x] `dotnet test tests/Api.IntegrationTests --filter "FullyQualifiedName~LiveActivationTests|FullyQualifiedName~TC_MDF_109_i_source_selection"` passes.

## Result

- **Production Compose Wiring (`compose.prod.yml`):** Added `Ingestion__Statements__Source: ${Ingestion__Statements__Source:-kap-public}`, `Ingestion__Disclosures__Source: ${Ingestion__Disclosures__Source:-kap-public}`, `Ingestion__Kap__ApiKey: ${Ingestion__Kap__ApiKey:-}`, and `Ingestion__Kap__ApiSecret: ${Ingestion__Kap__ApiSecret:-}` to `x-api-environment`.
- **Dev Compose Wiring (`compose.dev.yml`):** Added `Ingestion__Statements__Source: ${Ingestion__Statements__Source:-kap-public}` and `Ingestion__Disclosures__Source: ${Ingestion__Disclosures__Source:-kap-public}` to `x-api-environment`.
- **Environment Template (`.env.example`):** Added and documented `Ingestion__Statements__Source=kap-public` and `Ingestion__Disclosures__Source=kap-public`, preserving the VYK secret slots (`Ingestion__Kap__ApiKey` / `ApiSecret`) for reversible configuration.
- **Documentation (`deploy/README.md`):** Updated the Secrets table (`VPS .env (compose)`) to record the optional `Ingestion__*` source and credential knobs, and documented the default `kap-public` behavior and VYK fallback override under `## KAP public-site access: the relay fallback`.
- **Preserved L2 Defaults:** Did NOT touch `src/Api/appsettings.json` or C# option defaults (`StatementsSourceOptions` and `DisclosuresSourceOptions` remain `"vyk"`).
- **Automated Verification:** Added `SLICE_003_deployment_wiring_selects_kap_public_by_default_and_passes_source_knobs` in `tests/Api.IntegrationTests/LiveActivationTests.cs`. Narrow verification passing 3/3 tests, full LiveActivationTests + source selection passing 10/10 tests green.
