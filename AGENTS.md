# Agent Instructions (Değerli)

These instructions apply to all agent sessions working in `/home/sarp/degerli`.

## 1. Vertical Slice Workflow (`.pipeline/slices/`)

All features, fixes, and refactors are developed **one vertical slice at a time** and tracked in `.pipeline/slices/NNN-<slug>.md`:

1. **Before coding:** Create or update `.pipeline/slices/NNN-<slug>.md` with YAML frontmatter (`id: SLICE-NNN`, `status: in_progress`, `scope: [...]`) and sections for `Context & Goal`, `Rules & Invariants`, `Blockers & Dependencies`, and `Verification`.
2. **One change at a time:** Implement, verify, and report one slice (or one requested suggestion) at a time unless explicitly asked to batch.
3. **After verification:** Mark `status: done` and record the `Result` in the slice file.
4. **PR & Review Gate:** When a slice's work is finished and locally verified, commit on a slice branch and open a PR targeting `dev`. **Never merge a PR without running the review agents (`review-lead` / `diff-reviewer` / `security-reviewer`) first.**

## 2. Subagent Delegation (DeepSeek V4.1 Flash or Gemini Flash)

- **Always use a Flash subagent (DeepSeek V4.1 Flash or Gemini Flash) whenever coding is needed.**
- When implementing code, config, or test changes, delegate to a Flash subagent (e.g. `invoke_subagent` with `Model: "flash"` in Antigravity, or the DeepSeek V4.1 Flash / Gemini Flash subagent in your active harness).
- The main agent acts as planner, architect, and reviewer: it investigates context, defines the slice spec, dispatches the Flash subagent with a clear, self-contained prompt, and verifies the subagent's work.

## 3. Repository Map

- `src/Core` — Pure domain logic & financial math (DCF, ratios, zero I/O).
- `src/Persistence` — EF Core 10 (`DegerliDbContext`), forward-only migrations, idempotent seed (`DegerliSeed`).
- `src/Ingestion` — Source adapters (`Prices`, `Intraday`, `Universe`, `Kap`, `KapPublic`, `Macro`, `Funds`), `MetricsEngine`, and cron scheduler.
- `src/Api` — ASP.NET Core 10 Minimal API (`Program.cs`) + in-process hosted ingestion workers.
- `src/ContentPipeline` — Offline AI company-description CLI (`evren`).
- `src/Web` — React 19 + TypeScript + Vite SPA (`src/lib/api/generated.ts` from OpenAPI).
- `fixtures/Degerli.Fixtures` — Synthetic `FixtureUniverse` + embedded real-source payloads (`RealSourceCatalog`, `Payloads/`).
- `tests/` — `Core.UnitTests` (L1), `Api.IntegrationTests` (L2, Testcontainers + WireMock), `src/Web` Vitest (L3), `tests/e2e` Playwright (L4).
- `deploy/` & `compose.*.yml` — `compose.dev.yml` (local dev), `compose.ci.yml` (L4 CI), `compose.prod.yml` + `deploy/` (Caddy auto-TLS, backup/restore, KAP relay).
- `.pipeline/` — Architecture (`architecture/01..03`), UX (`ux/`), test plans (`testing/`), historical tickets (`plan/tickets/`), and active vertical slices (`slices/`).

## 4. Core Invariants & Guardrails

- **Honest-data contract:** Data responses carry `asOf`, `stale`, `sources`, and `warnings` (`EodSessionFreshness` for completed-session freshness). Never fabricate missing data or emit investment-advice terms (`al`/`sat`/`tut`, `buy`/`sell`/`hold`).
- **Zero live calls in tests:** L1–L4 tests must use `FixtureUniverse` or `RealSourceCatalog` WireMock doubles — never call live BIST/KAP/TCMB endpoints from tests.
- **OpenAPI drift gate:** After changing any `/api/v1/*` contract shape, run `./scripts/generate-openapi.sh` to update `openapi.snapshot.json` and `src/Web/src/lib/api/generated.ts`.
- **Targeted local verification:** Run narrow `--filter` tests locally (`dotnet test --filter ...`) rather than the full test suite; GitHub Actions CI is the full-suite authority.
- **Standalone network CLI calls:** Run `git push`, `git fetch`, `git pull`, `gh pr ...`, and `gh run ...` as standalone, unchained commands.
