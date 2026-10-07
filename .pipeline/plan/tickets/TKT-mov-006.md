# TKT-mov-006: SCR-001 UI — macro strip, inflation pair, stale states & language

- Status: todo
- Size: M
- Scope: `/src/Web` SCR-001 macro strip per the TKT-mov-001 design: six indicators with value/unit/source/as-of, always-paired inflation display with degradation note when the independent measure is unavailable, stale marker component (badge + as-of), no-real-time posture (no polling/countdowns), language toggle preserving context (same data, scroll, filters). Builds on the TKT-mov-005 screen files. Must NOT touch equity-block components.
- Traces to: FR-MOV-007..010 (UI side); UC-MOV-003 (main + alt a); NFR-MOV-002, NFR-MOV-004
- Acceptance: TC-MOV-017, TC-MOV-018, TC-MOV-022, TC-MOV-023, TC-MOV-024 — TDD: specs first, then green; specs in `.pipeline/testing/market-overview.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-1, M-4), §8.3 (UXR-MOV-007..012 rows); `03-api-design.md` §3 (`/market/macro`)
- UX refs: SCR-001; UXR-MOV-007, UXR-MOV-008, UXR-MOV-009, UXR-MOV-010 (macro side), UXR-MOV-012 (Could — placeholder only, see TKT-mov-007); UXR-G-008, UXR-G-012, UXR-G-013, UXR-G-015
- Dependencies: TKT-mov-005
- Parallel group: P-15
