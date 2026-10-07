# TKT-res-010: Admin descriptions UI section (SCR-012 §5)

- Status: todo
- Size: M
- Scope: `/src/Web` admin descriptions section (own per-section files on the TKT-mdf-012 shell) per the TKT-mdf-001 design: review queue filterable by status, bilingual editor with KAP source refs displayed alongside (UXR-MDF-020), save preserving both texts on failure, publish control disabled while a language is empty with explanation, publication confirmation stating public visibility, coverage-count updates without manual refresh (M-2). Must NOT touch other admin sections or the public stock page.
- Traces to: FR-RES-020, FR-RES-021 (UI side); UC-RES-004 (steps 2–4); UXR-MDF-018..022 (screen side)
- Acceptance: TC-RES-034 — TDD: e2e spec first, then green; spec in `.pipeline/testing/stock-research.md`.
- Architecture refs: `01-system-architecture.md` §8.1 (M-2), §8.3 (UXR-MDF-018..022 row), AD-13; `03-api-design.md` §8 (descriptions endpoints + response sketch)
- UX refs: SCR-012 (description review & publication section); UXR-MDF-018, UXR-MDF-019, UXR-MDF-020, UXR-MDF-021, UXR-MDF-022; UXR-G-004, UXR-G-019, UXR-G-022
- Dependencies: TKT-res-006, TKT-mdf-012
- Parallel group: P-16
