# API Design — v1.1

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** architect · **Date:** 2026-10-06 · **Status:** submitted for builder approval — v1.1 adds admin dashboard endpoints (change record in `01-system-architecture.md` §13.1)
**Serves:** the React SPA (C1) — the only first-party client; designed consumable by any future JSON client (mobile is delayed per builder decision D-01 and requires a brief amendment before being built).
**Style:** REST over JSON, HTTPS only, prefix `/api/v1`. OpenAPI document served at `GET /api/v1/openapi.json` (Microsoft.AspNetCore.OpenApi, anonymous).

---

## 1. Principles

1. **Codes and numbers from the server; words from the client.** The API returns stable error `code`s, metric codes, state markers — never user-facing prose. All display text (TR/EN) is rendered by the SPA from i18n catalogs (bilingual completeness enforced by CI, §01 §10.6).
2. **The honest-data envelope on every data response** (§01 §10.7): `asOf`, `stale`, and where applicable `adjusted`, per-figure `restated`, and `coverage`/`state` for missing data. Missing data is `200` + state, never blank-as-zero (BR-RES-008).
3. **One source per number** (BR-MDF-009): all metric values come from the Metrics Engine's tables; endpoints never compute.
4. **Write endpoints mirror the mutation matrix** (UX `01-interaction-rules.md` §2): every mutation the matrix lists has exactly one endpoint; list/picker freshness is the SPA's query-invalidation job (§01 §8.4).
5. **Public by default, auth only for `/me` and `/admin`** (BR-ACC-003): research surfaces are anonymous.

## 2. Conventions

- **Auth:** cookie session (`HttpOnly; Secure; SameSite=Lax`) issued by ASP.NET Core Identity. Unsafe methods (POST/PATCH/DELETE) require the `X-CSRF-Token` header (antiforgery token issued by `GET /api/v1/auth/csrf-token`). Documented extension point for a future non-web client: `POST /api/v1/auth/token` (refresh-token issuance) — **deliberately not built in V1** (AD-07).
- **Errors:** RFC 7807 `application/problem+json` with stable `code`, `params`, `traceId` (§7).
- **Pagination:** none — the universe is ~100 rows and saved-item lists are per-user; a `limit` guard (200) exists on `/me` lists as a hygiene backstop.
- **Caching:** public GETs → `Cache-Control: public, max-age=300` + strong `ETag` (data changes daily; 5 min is generous). `/me/*`, `/auth/*` → `Cache-Control: no-store`.
- **CORS:** same-origin only (Caddy serves SPA + API under one host); no CORS headers in V1. A future non-web client adds explicit allowed origins — config change, not redesign.
- **Content:** `application/json; charset=utf-8`; dates ISO-8601; all monetary/decimal values as JSON numbers.
- **Language:** API responses are language-neutral (codes, numbers). Transactional **e-mails are bilingual** (both languages in one message) — server does not guess locale.

## 3. Public read endpoints

| Endpoint | Serves | UC trace | Notes |
|---|---|---|---|
| `GET /api/v1/market/overview` | SCR-001 equity blocks: indices + change, breadth, volume, market P/E (+excluded count) & div yield, gainers/losers (with volume), sector performance (daily; `period=1w|1m|ytd` for FR-MOV-017 Could) | UC-MOV-001, UC-MOV-004 | one `market_snapshots` row; failure domain = the equity pipeline (see below) |
| `GET /api/v1/market/macro` | SCR-001 macro strip: six series, each `{code, value, unit, source, asOf, stale, state}`; inflation pair always both entries (independent measure may carry `state: unavailable`) | UC-MOV-001, UC-MOV-003 | separate endpoint because macro and equity have **separate failure domains** (UC-MOV-002 vs UC-MDF-001) — one outage must not blank the other (SCR-001 §5); per-series unavailable/stale states at payload level (UXR-MOV-008/009) |
| `GET /api/v1/stocks?sector=&q=` | SCR-002 list: `{symbol, name, sector, listingDate}` + envelope | UC-RES-001 | ILIKE on name/symbol; sector pre-fill from dashboard links via SPA URL params (UXR-RES-006) |
| `GET /api/v1/stocks/search?q=` | header search typeahead (FR-RES-004) | UC-RES-001 | lightweight: symbol + name only |
| `GET /api/v1/stocks/{symbol}/overview` | SCR-005 §1: business description | UC-RES-002 | `{status: published|preparing, textTr, textEn, lastReviewedAt, asOf}`; `preparing` per FR-RES-026 |
| `GET /api/v1/stocks/{symbol}/valuation` | SCR-005 §2: current multiples + historical series (adjusted, flagged) + vs.-sector strip `{metric: {value, notMeaningful?}, sectorMedian, peerCount, excludedCount}` | UC-RES-003 | strip per confirmed OQ-UX-002; UXR-RES-024/025 honesty fields |
| `GET /api/v1/stocks/{symbol}/financials` | SCR-005 §3: revenue → EBITDA → EBIT → NI → FCF, per period | UC-RES-003 | per-figure `restated` flags |
| `GET /api/v1/stocks/{symbol}/profitability` | SCR-005 §4: ROIC, ROE, margins | UC-RES-003 | |
| `GET /api/v1/stocks/{symbol}/growth` | SCR-005 §5: 3/5/10Y CAGRs + `windowYears` (actual) | UC-RES-003 | short-history → `windowYears < target` or no-data state w/ coverage (UXR-RES-016) |
| `GET /api/v1/stocks/{symbol}/balance-sheet` | SCR-005 §6: BS lines + book value + BVPS (FR-RES-027) | UC-RES-003 | |
| `GET /api/v1/stocks/{symbol}/dividends` | SCR-005 §7: dividend history | UC-RES-003 | never-paid → explicit state (SCR-005 §7) |
| `GET /api/v1/screener/metrics` | SCR-003 criteria builder: visible catalog (family, unit, labels, isCagr) | UC-SCR-001 | serves only `is_screenable = true` (UXR-SCR-011) |

Stock-page sections are separate endpoints by design (M-5): sections render, load, fail, and retry independently (SCR-005 §5); one mega-endpoint would couple their failure domains.

## 4. Screener

### `POST /api/v1/screener/run` — UC-SCR-001 (ad-hoc), UC-SCR-003 (re-run)

Request (AND logic, BR-SCR-003; bounds per FR-SCR-003; window per FR-SCR-015):

```json
{
  "criteria": [
    { "metricCode": "pe",    "bound": "max",  "maxValue": 15 },
    { "metricCode": "roe",   "bound": "min",  "minValue": 15 },
    { "metricCode": "rev_cagr", "bound": "min", "minValue": 10, "window": 5 }
  ]
}
```

Response (always `200` on a well-formed run — emptiness and exclusions are data, not errors):

```json
{
  "asOf": "2026-10-06", "stale": false,
  "matchCount": 7,
  "excludedCount": 3,
  "droppedCriteria": [],
  "rows": [
    { "symbol": "ASELS", "name": "Aselsan", "sector": "Savunma",
      "values": { "pe": 12.4, "roe": 18.2, "rev_cagr": 24.1 } }
  ]
}
```

- Stocks missing data for a criterion are excluded and counted (`excludedCount`, BR-SCR-009/FR-SCR-014); never pass, never zero-filled.
- **Re-run of a saved screen** referencing a since-hidden metric: the criterion is dropped, run proceeds, `droppedCriteria[]` names it (UC-SCR-003a → UXR-SCR-015). **Ad-hoc** submission of a hidden/unknown metric: `400 VALIDATION_FAILED` (`code: METRIC_NOT_AVAILABLE`) — the builder never offered it (UXR-SCR-011).
- Results are computed on demand from current `derived_metrics`; nothing is persisted (UC-SCR-003).
- Empty result set is a normal response (`matchCount: 0` → SPA renders the zero-match state, UXR-SCR-006).

### Saved screens (auth required; verified e-mail required — UXR-G-030)

| Endpoint | Serves | UC trace | Errors |
|---|---|---|---|
| `GET /api/v1/me/screens` | SCR-004 list | UC-SCR-003 | 401 |
| `POST /api/v1/me/screens` `{name, criteria}` | save from SCR-003 | UC-SCR-002 | 401; **403 `EMAIL_NOT_VERIFIED`** (UXR-SCR-019); **409 `DUPLICATE_NAME`** (UXR-G-029); 400 validation |
| `PATCH /api/v1/me/screens/{id}` `{name}` and/or `{criteria}` | rename / update | UC-SCR-003 | 404; 409 `DUPLICATE_NAME` |
| `DELETE /api/v1/me/screens/{id}` | delete (SPA confirms first, UXR-G-022) | UC-SCR-003 | 404 |

Duplicate-name rejection is backed by the DB unique constraint (§02 §3.3) — the 409 cannot race.

## 5. DCF

### `GET /api/v1/stocks/{symbol}/dcf` — UC-VAL-001 (open calculator)

```json
{
  "symbol": "ASELS",
  "baseline": {
    "version": 14, "buildDate": "2026-10-05",
    "params": {
      "base_fcf": 15230000000, "growth_rate": 0.12, "horizon_years": 5,
      "terminal_growth": 0.04, "discount_rate": 0.15,
      "net_debt": 8000000000, "cash": 21000000000, "share_count": 7900000000
    },
    "canonicalFactRefs": { "base_fcf": {"asOf": "2026-09-30", "restated": true}, "…": "…" }
  },
  "price": { "value": 62.40, "asOf": "2026-10-06", "stale": false }
}
```

Missing statement inputs → `200` with `state: "missing_inputs"` + what is missing (UC-VAL-001a, UXR-VAL-009) — the calculator explains instead of computing. Restatement flags carried on canonical-fact defaults (UXR-VAL-010).

### `POST /api/v1/stocks/{symbol}/dcf/compute` — UC-VAL-001 (adjust + recompute)

Request: the 8 parameters (confirmed set, OQ-UX-001). Response (single path for point result **and** sensitivity grid, FR-VAL-005/007):

```json
{
  "fairValuePerShare": 84.10,
  "marginOfSafety": 0.258,
  "price": { "value": 62.40, "asOf": "2026-10-06", "stale": false },
  "sensitivity": {
    "discountRates":     [0.13, 0.14, 0.15, 0.16, 0.17],
    "terminalGrowths":   [0.02, 0.03, 0.04, 0.05, 0.06],
    "fairValues": [[…25 cells…]]
  }
}
```

- Pure function; no auth; no persistence; stateless (NFR-VAL-004: nothing affects the result beyond the request + canonical price).
- Mathematically not computable (e.g., `discount_rate ≤ terminal_growth`, horizon outside 1..10): `422 DCF_NOT_COMPUTABLE` with the violated constraint in `params` — the SPA keeps the last valid result visible and marks the invalid field (UXR-VAL-008).
- The SPA debounces calls 300 ms and always computes through this endpoint — including on load with baseline params (no separate client-side model; single canonical formula, AD-08).
- The plain-language verdict is rendered client-side from `marginOfSafety` + i18n (FR-VAL-006, BR-VAL-004).

### Scenarios (auth + verified e-mail; Should)

| Endpoint | Serves | UC trace |
|---|---|---|
| `GET /api/v1/me/scenarios?symbol=` | SCR-011 grouped list (grouping client-side) + SCR-006 per-stock picker | UC-VAL-002 |
| `POST /api/v1/me/scenarios` `{symbol, name, params}` | save from SCR-006 | UC-VAL-002 (403 `EMAIL_NOT_VERIFIED` per UXR-VAL-021; 409 `DUPLICATE_NAME` per stock, UXR-G-029) |
| `PATCH /api/v1/me/scenarios/{id}` `{name}` | rename (UXR-VAL-017) | UC-VAL-002 + Gate 1 decision C |
| `DELETE /api/v1/me/scenarios/{id}` | delete (UXR-VAL-018) | UC-VAL-002 + Gate 1 decision C |

Loading = `GET` scenario → SPA seeds the calculator → `POST …/compute` (FR-VAL-010: restore exactly; outputs recompute from saved params).

## 6. Auth & account endpoints

| Endpoint | Serves | UC trace | Notes |
|---|---|---|---|
| `GET /api/v1/auth/csrf-token` | antiforgery token for unsafe calls | (mechanism) | anonymous |
| `POST /api/v1/auth/register` `{email, password, consent: {noticeVersion}}` | SCR-007 | UC-ACC-001 | 201 + session cookie; 409 `EMAIL_TAKEN` (clear message + sign-in path, UXR-ACC-004); consent recorded (BR-ACC-002); verification e-mail dispatched (FR-ACC-008) |
| `POST /api/v1/auth/login` `{email, password}` | SCR-008 | UC-ACC-002 | 200 + session + `{languagePref}` (applied client-side, UXR-ACC-009); 401 `INVALID_CREDENTIALS` (generic, NFR-ACC-002); 429 `LOCKED_OUT` after 10 fails/15 min |
| `POST /api/v1/auth/logout` | header control | UC-ACC-002 | revokes session |
| `GET /api/v1/auth/session` | header auth state on load/focus (M-9) | UC-ACC-002 | `{authenticated, email, verified, languagePref, role}` or `{authenticated: false}`; no-store |
| `POST /api/v1/auth/verify-email` `{token}` | SCR-007 link landing | UC-ACC-001 | 200 (verified; active session gains state immediately — M-9) / 410 `TOKEN_EXPIRED` + resend path (UXR-ACC-006) |
| `POST /api/v1/auth/resend-verification` | unverified-save gate + SCR-010 | UXR-ACC-021 | session-scoped; no enumeration surface |
| `POST /api/v1/auth/forgot-password` `{email}` | SCR-009 stage 1 | UC-ACC-004 | always 200 neutral confirmation (UXR-ACC-012); reset e-mail (2h single-use token) only if account exists |
| `POST /api/v1/auth/reset-password` `{token, newPassword}` | SCR-009 stage 2 | UC-ACC-004 | 200 (may establish session) / 410 `TOKEN_EXPIRED` + re-request path (UXR-ACC-014) |
| `GET /api/v1/me` | SCR-010 settings data (verification status etc.) | UC-ACC-003 | |
| `PATCH /api/v1/me` `{languagePref}` | SCR-010 language | UC-ACC-003 | 200; applied + persisted (BR-ACC-004) |
| `PATCH /api/v1/me/password` `{current, newPassword}` | SCR-010 | UC-ACC-003 (FR-ACC-007) | session preserved on success |
| `DELETE /api/v1/me` | SCR-010 delete | UC-ACC-003 (FR-ACC-006) | SPA double-confirms (UXR-G-022); hard delete + cascade; consent retained anonymized (§02 §5.4); session revoked |

Password policy: min 10 chars; rules visible client-side (UXR-ACC-003); server re-validates.

## 7. Error contract

RFC 7807; machine `code` + i18n-neutral `params`; the SPA renders all user-facing text from `code`:

```json
{
  "type": "https://<domain>/api/v1/errors/duplicate-name",
  "title": "Duplicate name",
  "status": 409,
  "code": "DUPLICATE_NAME",
  "params": { "name": "Ucuz hisseler" },
  "traceId": "00-9f2c…-01"
}
```

| code | HTTP | When |
|---|---|---|
| `VALIDATION_FAILED` | 400 | malformed/invalid input (field detail in `params.fields[]`) |
| `METRIC_NOT_AVAILABLE` | 400 | ad-hoc run references hidden/unknown metric |
| `UNAUTHENTICATED` | 401 | no/invalid session on `/me` |
| `INVALID_CREDENTIALS` | 401 | login failure (generic — never reveals which field) |
| `FORBIDDEN` | 403 | non-builder on `/admin` |
| `EMAIL_NOT_VERIFIED` | 403 | persistence save while unverified (UXR-G-030) |
| `NOT_FOUND` | 404 | unknown symbol/id (incl. stocks outside the covered universe) |
| `EMAIL_TAKEN` | 409 | registration with existing address |
| `DUPLICATE_NAME` | 409 | screen name (per account) / scenario name (per account+stock) |
| `DCF_NOT_COMPUTABLE` | 422 | math constraint violated (e.g., r ≤ g_t) |
| `TOKEN_EXPIRED` | 410 | expired/used verification or reset token |
| `LOCKED_OUT` | 429 | lockout window active |
| `RATE_LIMITED` | 429 | rate limit exceeded (`params.retryAfter`) |
| `INTERNAL` | 500 | unhandled — generic message + `traceId`; details only in server logs |

Background (e-mail dispatch, ingest) failures never surface as endpoint errors mid-flow — they alert the builder (C3d) and the user-facing flow states the operation is queued/failed with retry (UXR-G-003).

## 8. Admin endpoints (builder role)

| Endpoint | Serves | UC trace |
|---|---|---|
| `GET /api/v1/admin/summary` | **ops one-glance (v1.1)**: last run per job, freshness/staleness per data type, description coverage (published/draft vs universe), open-quarantine count — aggregated from existing tables (`ingest_runs`, `v_data_freshness`, `coverage_metadata`, `business_descriptions`) | UC-MDF-004, UC-MOV-005, FR-MDF-016 |
| `GET /api/v1/admin/quarantine?status=open\|dismissed` | **validation-failure review queue (v1.1)**: rejected payloads + reason per item | UC-MDF-001 (alternate b), FR-MDF-012 |
| `POST /api/v1/admin/quarantine/{id}/dismiss` `{note}` | record the accepted gap (BR-MDF-007); re-ingestion = re-triggering the job (endpoint below) after a fix | UC-MDF-001 (alternate b), BR-MDF-007 |
| `GET /api/v1/admin/coverage?scope=instrument\|fund` | coverage report (equity + funds) | UC-MDF-004, UC-FDF-003 |
| `GET /api/v1/admin/ingest-runs?job=&status=` | run-ledger monitoring | UC-MDF-001 (ops), UC-MOV-005, UC-FDF-002 (ops) |
| `POST /api/v1/admin/ingest/{job}/run` | trigger/backfill — `{job}` ∈ job codes below; body may carry `{backfillFrom}` | UC-MDF-002 |
| `GET /api/v1/admin/descriptions?status=draft\|reviewed\|published` | review queue | UC-RES-004 |
| `PATCH /api/v1/admin/descriptions/{id}` `{textTr?, textEn?}` | edit both languages (FR-RES-021) | UC-RES-004 |
| `POST /api/v1/admin/descriptions/{id}/publish` | publication gate — rejects if either language empty (FR-RES-020, BR-RES-003) | UC-RES-004 |
| `PATCH /api/v1/admin/screener-metrics/{code}` `{isScreenable}` | hide/unhide metric without code change (FR-SCR-017) | UC-SCR-004 |
| `POST /api/v1/admin/dcf-baselines/regenerate` | rebuild baselines from canonical facts (version bump) | UC-VAL-003 |
| `GET /api/v1/admin/stats` | **aggregate-only counts (v1.1)**: `{accounts: {registered, verified}, savedScreens, dcfScenarios}` — the SC-008/OBJ-005 (10-external-users) measurement without any tracking; **no per-user data** (KVKK posture, BR-ACC-002/008) | SC-008, OBJ-005 — see §10 direction-check note |

`GET /api/v1/admin/summary` — response sketch:

```json
{
  "asOf": "2026-10-06",
  "jobs": [
    { "job": "prices", "lastRun": { "status": "succeeded", "finishedAt": "2026-10-06T21:14:03Z" } }
  ],
  "freshness": [
    { "dataType": "prices", "lastSuccess": "2026-10-06", "stale": false },
    { "dataType": "macro-indep-cpi", "lastSuccess": "2026-09-03", "stale": true }
  ],
  "contentCoverage": { "descriptionsPublished": 61, "descriptionsDraft": 12, "universe": 100 },
  "openQuarantineCount": 4
}
```

Job codes for ingest triggers: `prices`, `statements`, `dividends`, `corporate-actions`, `disclosures`, `universe-sync`, `macro-daily`, `macro-cpi`, `metrics-recompute`, `snapshot`, `medians`, `fund-nav`, `fund-holdings`, `fund-performance`.

## 9. Non-HTTP operations (workers + CLI) — UC coverage

| Operation (component) | Cadence/trigger | UC trace |
|---|---|---|
| EOD ingest chain: prices → statements/dividends/actions/disclosures → metrics-recompute → snapshot → medians (C3a/C3b/C3c) | cron 20:30 TRT trading days; retries ×3; run ledger; validation failures → `quarantined_facts` (never into fact tables); alerts (C3d) | UC-MDF-001 (incl. alternates a/b), UC-MDF-003 |
| Backfill mode of the same chain | admin trigger (`POST /admin/ingest/{job}/run`) | UC-MDF-002 |
| Macro ingest jobs `macro-daily` / `macro-cpi` (+ per-release repo rate) | cron per cadence | UC-MOV-002 |
| Macro failure detection + staleness marking + builder alert | inside macro jobs | UC-MOV-005 |
| Fund jobs `fund-nav` / `fund-holdings` / `fund-performance` (+ backfill) | cron per TEFAS cadence; admin trigger | UC-FDF-001, UC-FDF-002 |
| Fund coverage measurement → coverage report | after backfill | UC-FDF-003 |
| AI drafting CLI: `docker compose run --rm contentpipeline draft [--symbol=]` (C4, evren API) | builder-run, offline | UC-RES-004 step 1 |
| Baseline regeneration job (C3b, via admin endpoint) | on demand / build cycle | UC-VAL-003 |
| KVKK pre-launch review | manual checklist against the data inventory in `02-data-model.md` §3.6 + privacy notice | UC-ACC-005 |

## 10. UC ↔ operation coverage (both directions)

| UC | Operations |
|---|---|
| UC-MDF-001 refresh daily | worker EOD chain (validation failures → `quarantined_facts`); monitored via `GET /admin/summary`, `GET /admin/ingest-runs`; quarantined items reviewed/dismissed via `GET/POST /admin/quarantine` |
| UC-MDF-002 backfill | `POST /admin/ingest/{job}/run`; coverage via `GET /admin/coverage` |
| UC-MDF-003 universe maintenance | worker `universe-sync` |
| UC-MDF-004 validate coverage | `GET /admin/coverage` (equity scope) + `GET /admin/summary` (freshness & content coverage at a glance) |
| UC-MDF-005 serve facts/metrics | all public read endpoints (§3) — canonical values + coverage/stale/window semantics |
| UC-MOV-001 check market state | `GET /market/overview` + `GET /market/macro`; SPA SCR-001 |
| UC-MOV-002 ingest macro | workers `macro-daily`/`macro-cpi` |
| UC-MOV-003 compare inflation | `GET /market/macro` (paired payload); SPA renders pair |
| UC-MOV-004 navigate from dashboard | SPA routes (movers→SCR-005, sector→SCR-002 filtered, screener→SCR-003) — no API (client-side links) |
| UC-MOV-005 macro failure detection | worker alerting; `GET /admin/summary` (staleness), `GET /admin/ingest-runs` |
| UC-SCR-001 ad-hoc screen | `GET /screener/metrics` + `POST /screener/run` |
| UC-SCR-002 save screen | `POST /me/screens` (+ auth hop: §3 auth endpoints) |
| UC-SCR-003 re-run/manage | `GET /me/screens`, `POST /screener/run` (with stored criteria; `droppedCriteria`), `PATCH`, `DELETE` |
| UC-SCR-004 verify metric coverage | `GET /admin/coverage` + `PATCH /admin/screener-metrics/{code}` |
| UC-RES-001 browse/find stocks | `GET /stocks`, `GET /stocks/search`; SPA SCR-002 |
| UC-RES-002 understand company | `GET /stocks/{s}/overview` |
| UC-RES-003 study fundamentals | `GET /stocks/{s}/valuation|financials|profitability|growth|balance-sheet|dividends` |
| UC-RES-004 produce/review/publish description | C4 `draft` CLI + `GET/PATCH /admin/descriptions`, `POST …/publish` |
| UC-RES-005 move to valuation | SPA route SCR-005 → SCR-006 (no API) |
| UC-VAL-001 value stock | `GET /stocks/{s}/dcf` + `POST /stocks/{s}/dcf/compute` |
| UC-VAL-002 save/reload scenario | `GET/POST /me/scenarios`, `PATCH`, `DELETE` |
| UC-VAL-003 define baselines | `POST /admin/dcf-baselines/regenerate` (C3b job) |
| UC-ACC-001 register | `POST /auth/register`, `POST /auth/verify-email` |
| UC-ACC-002 sign in/out | `POST /auth/login`, `POST /auth/logout`, `GET /auth/session` |
| UC-ACC-003 settings | `GET /me`, `PATCH /me`, `PATCH /me/password`, `DELETE /me` |
| UC-ACC-004 recover access | `POST /auth/forgot-password`, `POST /auth/reset-password` |
| UC-ACC-005 KVKK review | manual checklist + data inventory (documented, §9) |
| UC-FDF-001 fund backfill | fund workers (admin trigger) |
| UC-FDF-002 fund refresh | fund workers (cron) |
| UC-FDF-003 fund coverage | fund coverage measurement + `GET /admin/coverage?scope=fund` |

**Direction check:** every endpoint in §3–§8 traces to ≥1 UC (tables above); every UC (30 of 30) has ≥1 operation — HTTP endpoint, worker job, CLI, or documented manual procedure. Navigation-only UCs (UC-MOV-004, UC-RES-005) are satisfied by SPA routing and are flagged as such, not orphaned. **One recorded exception (v1.1, builder-approved AD-13):** `GET /admin/stats` traces to success criteria SC-008/OBJ-005 rather than a UC — it is the only endpoint without a UC trace, and it exists to *measure* a success criterion rather than serve a use case.

## 11. Versioning

- **URL major version** (`/api/v1`) — the SPA is the only client and ships in lockstep (same repo, same deploy), so in-V1 changes are **additive only**: new optional request fields, new response fields, new endpoints. Removed/renamed/semantic changes require `/api/v2` with the old version kept during a transition and announced via `Deprecation` + `Sunset` headers.
- Payload evolution rule: clients must ignore unknown fields (forward compatibility) — stated in the OpenAPI description.
- Rationale for URL versioning over header versioning: the public repo + possible future non-web client benefit from a self-describing, cacheable, visible version; header negotiation adds invisible state for zero benefit here.

## 12. Rate limits & abuse posture

| Scope | Limit | Enforcement |
|---|---|---|
| `/api/v1/auth/*` (login, register, forgot, resend) | 5 req/min/IP | fixed-window middleware; 429 + `retryAfter` |
| Account lockout | 10 failed logins / 15 min / account | ASP.NET Identity lockout (429 `LOCKED_OUT`) |
| Global API | 600 req/min/IP | comfortable for the SPA (page load ≈ 8–12 calls); blocks casual abuse |
| `/admin` | 60 req/min | builder-only surface |

No API keys/tiers in V1 (no third-party consumers; the public API is documented but not promoted as a product — consistent with informational-only posture and $0 ops).

## 13. Caching summary

| Surface | Policy | Rationale |
|---|---|---|
| `GET /market/*`, `GET /stocks*`, `GET /screener/metrics`, `GET /stocks/{s}/dcf` | `public, max-age=300` + ETag | daily data; 5-min freshness is far tighter than the EOD cadence; ETag revalidation keeps it cheap |
| `POST /screener/run`, `POST …/dcf/compute` | `no-store` | dynamic, user-specific inputs |
| `/me/*`, `/auth/*`, `/admin` | `no-store` | personal / mutating surfaces |

CDN: none (single-region VPS, static assets served by Caddy with far-future immutable hashes on filenames — sufficient at this traffic level).
