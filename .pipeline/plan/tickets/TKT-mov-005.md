# TKT-mov-005: SCR-001 UI — equity blocks, navigation & disclaimer

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-001 route per the TKT-mov-001 design: index levels, sector performance (entries → Stock List pre-filtered), breadth, top movers with volume (entries → stock pages), market volume, market valuation with loss-maker exclusion disclosure, screener/stock-list navigation, informational-only disclaimer, per-block independent load/fail/retry (M-5), as-of dates, loading skeletons, data-as-of + TR default. Must NOT implement the macro strip (TKT-mov-006).
- Traces to: FR-MOV-001..006, FR-MOV-011..013, FR-MOV-020, FR-MOV-021 (UI side); UC-MOV-001, UC-MOV-004
- Acceptance: TC-MOV-016, TC-MOV-019, TC-MOV-020, TC-MOV-021, TC-MOV-025 — TDD: specs first, then green; specs in `.pipeline/testing/market-overview.md` (L3 with MSW, L4 against the compose stack).
- Architecture refs: `01-system-architecture.md` §8.1 (M-1, M-5), §8.3 (UXR-MOV-001..005 row); `03-api-design.md` §3
- UX refs: SCR-001; UXR-MOV-001..006, UXR-MOV-010, UXR-MOV-011; UXR-G-001, UXR-G-003, UXR-G-007, UXR-G-016
- Dependencies: TKT-foundation-011, TKT-mov-004, TKT-foundation-009, TKT-foundation-010
- Parallel group: P-14
