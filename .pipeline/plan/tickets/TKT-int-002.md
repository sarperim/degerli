# TKT-int-002: SC-002 core-loop e2e & cross-surface metric equality

- Status: done — merged to `dev` 2026-10-14 (PR https://github.com/sarperim/degerli/pull/69, merge commit `0ccaaf6`). Review loop round 1 MERGEABLE (facet gate clean, CI green incl. E2E). Artifact: `.pipeline/plan/reviews/TKT-int-002-round-1.md`. Non-blocking follow-ups recorded: DR-1 (TC-XC-008's pinned market-P/E golden 28.1218 compared only to the served API value, not the pinned golden), DR-2, DR-3, DR-4. NOTE: the review-lead could not spawn reviewer subagents (subagent depth limit 1) and reviewed in-context; independence not claimed.
- PR: https://github.com/sarperim/degerli/pull/69
- Size: M
- Scope: the system-level e2e specs that span domains: the anonymous SC-002 core loop (dashboard → screener run → stock page sections → DCF compute with own assumptions → plain-language verdict), the research→valuation hand-off leg, and the cross-surface metric-equality sweep (same stock/metric/date identical across screener results, stock page, and DCF price; dashboard market P/E equals the stored snapshot aggregate). Test specs + any wiring fixes only. Must NOT change feature behavior beyond defects these specs expose.
- Traces to: SC-002, OBJ-002; UC-RES-005; UXR-G-011; NFR-SCR-003, NFR-RES-003, NFR-VAL-003
- Acceptance: TC-XC-001, TC-XC-008, TC-RES-028 — TDD: specs first, then green; specs in `.pipeline/testing/cross-cutting.md` + `.pipeline/testing/stock-research.md`.
- Architecture refs: `01-system-architecture.md` §6.1 (e2e job scope), §8.1 (M-5), §9 (NFR translation — consistency row); `03-api-design.md` §1 principle 3
- UX refs: UXR-G-011
- Dependencies: TKT-mov-005, TKT-mov-006, TKT-scr-006, TKT-res-007, TKT-res-008, TKT-res-009, TKT-val-006, TKT-foundation-010
- Parallel group: P-18
