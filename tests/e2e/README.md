# L4 end-to-end harness (`/tests/e2e`)

Playwright (Chromium) end-to-end harness for the Değerli stack. It runs against the
`compose.ci.yml` API/Postgres/MailPit stack and serves the SPA itself.

Traces: architecture `01-system-architecture.md` §6.1 (e2e job); test strategy
`00-test-strategy.md` §5.4, §8.3, §9.1.

## What lives here

| File | Purpose |
|---|---|
| `playwright.config.ts` | Chromium project; fixed locale `tr-TR` + timezone `Europe/Istanbul`; 5-minute `globalTimeout` gate; serves the SPA via `webServer`. |
| `src/config.ts` | Shared URLs + the `E2E_BUDGET_MS` gate constant. |
| `src/helpers/mailpit.ts` | MailPit HTTP API client: bounded polling (`≤ 10 s`, explicit `MailpitTimeoutError`), message + link retrieval, `sendProbe` probe flow. |
| `src/helpers/network.ts` | Network-quiet window recorder/assertion (TC-XC-021) with a positive control. |
| `src/helpers/catalog.ts` | Loads `tr.json`/`en.json` from the repo so copy assertions never hardcode prose. |
| `src/smoke.spec.ts` | Stack boots (`GET /health` 200) and the SPA root renders Turkish. |
| `src/mailpit.spec.ts` | The MailPit helper retrieves a probe message + link; explicit timeout on absence. |
| `src/network-quiet.spec.ts` | Quiet-window helper reports quiet and detects requests (non-vacuous). |
| `src/budget.spec.ts` | Asserts the 5-minute runtime budget gate is wired. |

This directory deliberately contains **no product feature specs** — the SC-002 core
loop and other UX flows belong to their domain tickets.

## Running

```bash
# 1. From the repository root: start the CI stack (API + Postgres + MailPit + seed).
docker compose -f compose.ci.yml up -d --build --wait

# 2. Install web + e2e dependencies (once).
npm ci --prefix src/Web
npm ci --prefix tests/e2e

# 3. Run the suite (starts the SPA webServer, runs Chromium specs).
npm test --prefix tests/e2e

# 4. Tear down.
docker compose -f compose.ci.yml down -v --remove-orphans
```

`npm run stack:up` / `npm run stack:down` (in this directory) wrap steps 1 and 4.

Environment overrides: `E2E_API_BASE_URL` (default `http://127.0.0.1:8080`),
`E2E_MAILPIT_BASE_URL` (default `http://127.0.0.1:8025`),
`E2E_WEB_BASE_URL` (default `http://127.0.0.1:5173`).

## Determinism

The compose stack seeds the **now-anchored** fixture universe (`T` = container
start; `fixtures/` module). Tests assert flags, offsets, and payload values — never
wall-clock dates (test strategy §8.3). Retries are `0`: the test strategy treats a
flaky test as a defect, never retry-masked.
