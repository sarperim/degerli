# TKT-res-009: SCR-005 Stock Page UI — valuation section & vs.-sector strip

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-005 Valuation section per the TKT-res-001 design: current multiples, historical valuation series on the adjusted basis with the adjusted-data disclaimer (Recharts), the vs.-sector strip (stock value next to sector median, peer count, P/E loss-maker exclusion disclosure, not-meaningful own-value state, no-median degradation). Builds on the TKT-res-008 screen files. Must NOT touch the other sections' components.
- Traces to: FR-RES-006, FR-RES-007, FR-RES-013 (UI side); SD-002; UXR-RES-012, 013, 024, 025
- Acceptance: TC-RES-025, TC-RES-032 — TDD: specs first, then green; specs in `.pipeline/testing/stock-research.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-1), §8.3 (UXR-RES-012..013 row), §5 (charts row); `03-api-design.md` §3 (valuation endpoint)
- UX refs: SCR-005 (Valuation section); UXR-RES-012, UXR-RES-013, UXR-RES-024, UXR-RES-025; UXR-G-010, UXR-G-011
- Dependencies: TKT-res-003, TKT-res-008
- Parallel group: P-16
