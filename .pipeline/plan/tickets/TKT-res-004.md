# TKT-res-004: Financials, profitability, growth, balance-sheet & dividends section endpoints

- Status: todo
- Size: L
- Scope: `/src/Api` stocks module: `GET /stocks/{symbol}/financials` (revenue → EBITDA → EBIT → NI → FCF per period, per-figure `restated`), `/profitability` (ROIC, ROE, margins), `/growth` (3/5/10Y CAGRs + `windowYears`, no-data with coverage boundary), `/balance-sheet` (BS lines + book value + BVPS), `/dividends` (dated history, never-paid state); unknown symbol → 404 across section endpoints; honest envelope on every section (200-with-state for missing data, never blank-as-zero). Must NOT touch overview/valuation endpoints or any UI.
- Traces to: FR-RES-008..012, FR-RES-014..016, FR-RES-027; UC-RES-003 (main + alternates a/b); BR-RES-007/008
- Acceptance: TC-RES-009, TC-RES-010, TC-RES-011, TC-RES-012, TC-RES-013, TC-RES-014, TC-RES-015 + **TC-MDF-036** (MDF-plan-owned: canonical values or honest no-data at the serving layer — covered by the section payloads above) — TDD: tests first, then green; goldens in `.pipeline/testing/stock-research.md`.
- Architecture refs: `03-api-design.md` §3 (five section endpoints + M-5 note); `01-system-architecture.md` §7 (FR-RES-008..016/027 rows), §8.1 (M-1, M-5); `02-data-model.md` §6.1–§6.2
- UX refs: —
- Dependencies: TKT-mdf-008, TKT-mdf-009, TKT-foundation-007
- Parallel group: P-13
