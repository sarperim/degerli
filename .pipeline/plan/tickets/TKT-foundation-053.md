---
scope:
  - src/Ingestion/Scheduling/MorningIngestChain.cs
  - src/Api/appsettings.json
  - tests/Api.IntegrationTests/MorningChainTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-053: 040 phase-2 wiring — flip the morning chain to `Phase2JobCodes` (dividends/CA/disclosures daily) and repair the stale §10.9.16 rationale prose

- Status: todo
- Size: S
- **Priority: P1 (functional: daily KAP event freshness — the user's "make it functional first (KAP)" priority) — GATED on PR #114 (TKT-foundation-041) merging** (shared scheduling files: `MorningIngestChain.cs` lives beside 041's scheduler/watchdog work; `appsettings.json` overlap risk).
- Verified unblock: `MorningIngestChain.Phase2JobCodes` already encodes the full 8-job phase-2 list (prices, statements, **dividends, corporate-actions, disclosures**, metrics-recompute, market-snapshot, sector-medians) — the code comment states "flipping the morning stream to this list is the whole phase-2 change". Both blockers are gone: 034 merged, and 046 (PR #113, v1.5.22) fixed the family jobs' fail-stop (they now complete with honest per-company skips on the dev archive).
- **Pinned change:** (1) the morning chain runs the phase-2 list (the one-line flip plus whatever the chain's gating flags require — the design already anticipates it); (2) **repair the stale phase-2 rationale prose** in §10.9.16 and 040's ticket lane (DR3-01 from 040's review: the prose still says the family jobs are blocked on 034 — 034 landed; the honest current rationale is "wired once 046 made the family lanes completable").
- Traces to: FR-MDF-003/004/005 (dividends/actions/disclosures ingest — now in the daily cadence), FR-MOV-006 lane (TTM DPS supply — morning-fresh dividends), FR-MDF-016 (pipeline health — the chain completes); §10.9.16 (the morning chain — phase-2 flip); TKT-foundation-040 heritage (merged, PR #110 — phase-2 lane), TKT-foundation-046 (the unblock), TKT-foundation-034 (merged).
- Acceptance: **explicit criteria; L2 doubles only.** **(i) TC-MDF-091 — phase-2 chain** (`MorningChainTests`): the morning chain executes the 8-job order incl. the three family jobs between statements and metrics-recompute; family-job honest skips (046's `COMPANY_IDENTITY_UNRESOLVED` counting) propagate into the chain's run stats; the chain terminates non-failed with the family lanes' completable-posture. **(ii) Existing morning-chain tests re-run green** (phase-1 semantics subsumed). **(iii) §10.9.16 prose matches reality** (the stale-034 rationale replaced). **(iv) CI green.** **Live criterion (orchestrator post-merge):** the next 08:30 morning chain run on the dev stack completes end-to-end with the three family jobs present in the ledger (honest skips counted, zero 500s) and metrics recomputed on fresh event data. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.16 (the flip + prose repair), §13.1 **v1.5.33**; test plan: `.pipeline/testing/market-data-foundation.md` TC-MDF-091 (delivery-planner-applied, test-planner confirmation pending; rebase convention).
- UX refs: none (scheduling lane).
- Dependencies: **GATED on PR #114 (041) merging** — shared scheduling files. Coordination: `appsettings.json` also touched by 047 (rebase convention if both in flight).
- Parallel group: none
