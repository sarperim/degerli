---
scope:
  - src/Ingestion/Prices/
  - src/Ingestion/Universe/
  - src/Ingestion/Jobs/
  - src/Api/appsettings.json
  - fixtures/Degerli.Fixtures/FixtureUniverse.cs
  - fixtures/Degerli.Fixtures/FixtureSeeder.cs
  - fixtures/Degerli.Fixtures/Program.cs
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/
---

# TKT-foundation-018: Live activation — BIST 30 real-data pull

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/90
- Size: L
- Scope: turn the production prices path on against the real İş Yatırım endpoint, **bounded to BIST 30**. Per §10.9.2 the per-ticker fetch loop lives in `PricesJob` (the adapter becomes symbol-scoped; envelope detection stays inside the adapter) and iterates the current constituents (`index_constituents`, `effective_to IS NULL`) of the index named by **`Ingestion:Prices:UniverseIndex`** (launch default `XU030`; `XU100` later = a config flip, not new code), strictly **sequential** with **`Ingestion:Prices:RequestDelayMs`** (default 500 ms) pacing — no parallelism against the undocumented public endpoint; per-symbol failure isolation (a failed ticker quarantines/alerts without aborting the run; per-symbol run-ledger stats). The XU030 membership launch-seed (checked-in, date-stamped, verified against BIST's actual XU030 at launch) reuses the TC-MDF-062 launch-seed machinery (§10.9.3). The production `BaseUrl`/path template points at the real per-ticker endpoint. The movers-eligibility threshold `Ingestion:MarketSnapshot:VolumeEligibilityThreshold` is **recalibrated from the observed BIST 30 ₺-volume distribution** — the calibration BR-MOV-011 always deferred to "once the real volume distribution is known"; it stays one config value and the FU test threshold (1,000,000) is unchanged. A bounded live capture replaces constructed catalog bodies where honest. The L2 harness binds `UniverseIndex` to the FU `XU100` so the existing price goldens survive the loop (test-plan v1.5 §8 preamble — load-bearing). Must NOT touch the statements/macro paths or any golden value.
- Traces to: FR-MDF-001, FR-MDF-006, FR-MDF-007, FR-MDF-010, FR-MDF-011; UC-MDF-001, UC-MDF-002, UC-MDF-003; FR-MOV-005, FR-MOV-021, BR-MOV-011 (threshold recalibration)
- Acceptance: TC-MDF-072, TC-MDF-073, TC-MDF-074, TC-MDF-075, TC-MDF-076 — plus explicit criteria (not CI-runnable): (a) the observed BIST 30 volume distribution and the chosen threshold value are recorded with the change; (b) the bounded live capture lands in the catalog with honest provenance labels, re-validated by re-running TC-MDF-063; (c) the checked-in XU030 membership list is verified against BIST's actual XU030 at launch, date-stamped; (d) the first live pull completes within the NFR-MDF-001 operational budget (run-ledger observation).
- Architecture refs: `01-system-architecture.md` §10.9.2 (loop, bound, pacing), §10.9.3 (XU030 seed), §10.9.1(c) (threshold recalibration), §10.4 (the two `Ingestion:Prices` knobs), AD-14
- UX refs: —
- Dependencies: TKT-foundation-015, TKT-foundation-017
- Parallel group: none
