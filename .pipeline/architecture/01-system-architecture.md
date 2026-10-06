# System Architecture — v1.2

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** architect · **Date:** 2026-10-06 (v1.0 → v1.1 → v1.2 same day)
**Status:** submitted for builder approval — v1.1 is a builder-directed amendment (admin dashboard extension); v1.2 is a small additive amendment (admin descriptions payload serves KAP source refs — UXR-MDF-020); change records in §13.1
**Inputs:** `.pipeline/00-project-brief.md` · `.pipeline/analysis/*` (7 approved domain reports + domain map + brief delta) · `.pipeline/ux/*` (12 screens, 156 UXRs, mutation matrix, audit — all approved; SCR-012 admin dashboard added 2026-10-06 per AD-13)
**Builder decisions incorporated (2026-10-06):** mobile delayed to post-V1 versions (D-01); cheap VPS hosting (D-02); public repo `github.com/sarperim/degerli` (D-03); domain via GitHub Student Pack, ETA ≈2026-10-09 (D-04); market P/E = cap-weighted aggregate (D-05); CI required (D-06).

---

## 1. What shapes this design

Constraints inherited from the brief and approved analyses; every architectural decision below traces to one or more of these:

1. **Solo builder, AI-assisted, December 2026 launch** (brief §4, §7) → one deployable, one database, monorepo, minimal moving parts; complexity budget spent on the data pipeline (the portfolio artifact), not on orchestration.
2. **$0 data budget, free/public sources only** (§7; BR-MDF-002) → source-adapter pattern; every source is swappable because the remaining source OQs (OQ-MDF-001/005, OQ-MOV-001/002, OQ-FDF-002) are unresolved by design until V0 validation.
3. **Daily EOD cadence, never real-time** (§6, §7; BR-MDF-003) → scheduled batch jobs; no websockets/streaming anywhere.
4. **History as first-class, append-only facts** (§5; BR-MDF-004, FR-MDF-015) → relational model with dated, versioned records; no in-place mutation of facts.
5. **One canonical definition per metric** (BR-MDF-009) → a single Metrics Engine computes every number any surface displays; no surface-side math.
6. **Bilingual TR/EN, TR default** (§7) → server returns codes + numbers; all user-facing copy rendered client-side from i18n catalogs; CI enforces TR/EN key parity.
7. **Optional accounts, KVKK-minimal** (§5, ASM-008) → e-mail + password only; cookie auth; hard-delete path; consent records retained.
8. **Honest-data display obligations** (UXR-G-006..012) → every data payload carries as-of/stale/adjusted/restated/coverage metadata; the API contract makes dishonest display impossible.
9. **Public repo as portfolio artifact** (OBJ-004, SC-007) → monorepo, docker-compose reproducibility, README + CI visible on GitHub Actions.
10. **Informational-only posture (SPK)** (§6, §7) → disclaimers on research surfaces (FR-MOV-020, FR-SCR-016, FR-RES-017, FR-VAL-008); no recommendation language in any generated content.

## 2. System overview

One VPS, one database, one backend process, one SPA — plus an offline content CLI and CI/CD on GitHub.

```
                        Internet
                           │
                    ┌──────┴──────┐
                    │    Caddy     │  TLS, reverse proxy (auto-HTTPS)
                    └──────┬──────┘
              ┌────────────┴────────────┐
              │                         │
      / (SPA assets)            /api/* (REST, JSON)
              │                         │
   ┌──────────▼──────────┐   ┌──────────▼─────────────────────┐
   │  C1 Web SPA          │   │  C2 Public API                 │
   │  React 19 + Vite     │   │  ASP.NET Core (.NET 10 LTS)    │
   │  11 screens + admin  │   │  feature modules:              │
   │  TR/EN, a11y         │   │   Market · Stocks · Screener · │
   └──────────┬──────────┘   │   DCF · Identity · Admin        │
              │  HTTPS       └──────────┬─────────────────────┘
              │                         │
              │              ┌──────────▼─────────────────────┐
              │              │  C3 Data Platform (in-process)  │
              │              │   C3a Ingestion workers         │
              │              │      (KAP, İşbank, macro, TEFAS)│
              │              │   C3b Metrics Engine             │
              │              │   C3c Scheduler + run ledger     │
              │              │   C3d Alerting (e-mail)          │
              │              └──────────┬─────────────────────┘
              │                         │
   ┌──────────▼─────────────────────────▼──────────┐
   │  C5 PostgreSQL 17+  (single database)          │
   └──────────▲─────────────────────────┬──────────┘
              │                         │ nightly pg_dump → B2 (30d)
   ┌──────────┴───────────┐             │
   │  C4 Content Pipeline  │◄───────────┘  run on VPS via
   │  (console CLI,       │   `docker compose run --rm`
   │   offline/build-time) │
   │  AI drafting (evren   │
   │  API), review/publish │
   └──────────────────────┘

   C6 CI/CD: GitHub Actions → GHCR images → SSH deploy to VPS
   C7 Ops: UptimeRobot (free) · Serilog logs · alert e-mails
```

**Deployable units (3):** the API+workers container (C2+C3, one image), the SPA static assets (built in CI, served by Caddy), and the Content Pipeline CLI (same image as API, alternate entrypoint). PostgreSQL runs as a container on the same host. One host, one compose file — a deliberate choice (AD-04).

## 3. Components

| ID | Component | Responsibilities | Boundaries — does NOT |
|----|-----------|------------------|----------------------|
| **C1** | **Web SPA** (React 19 + Vite + TS) | All 11 public screens (SCR-001..011); global header (language toggle, auth state, stock search); disclaimers; honest-data markers; a builder-only `/admin` area (SCR-012: daily ops summary, quarantine review, aggregate user stats — §03 §8; description review/edit/publish, metric visibility, coverage report). TR default, EN toggle. | Compute any financial figure (BR-RES-004); store server state; call any external service directly. |
| **C2** | **Public API** (ASP.NET Core, .NET 10) | REST/JSON under `/api/v1`; feature modules mirror domains; Identity (register/login/verify/reset, cookie auth, lockout, rate limiting); DCF compute (pure endpoint); saved screens & scenarios CRUD; admin endpoints (role-gated). | Talk to external sources on user request; own metric formulas (delegates to C3b results in DB); send transactional e-mail outside Identity flows. |
| **C3** | **Data Platform** (in-process workers in the API container) | **C3a Ingestion**: source adapters (KAP primary; İşbank candidate for prices; TÜİK/ENAG/CBRT/FX/gold macro; TEFAS funds), validation, quarantine, idempotent upserts, backfill mode. **C3b Metrics Engine**: all canonical metrics (18 screener + display metrics), CAGRs with window indication, market snapshot (breadth, movers, market P/E, market div yield), sector medians, DCF baseline generation. **C3c Scheduler**: cron-triggered jobs (Cronos) with a DB run ledger; retries with backoff. **C3d Alerting**: builder e-mails + log alerts on failures/staleness. | Serve HTTP (runs inside C2's process but no endpoints of its own); nothing user-triggered except admin-triggered jobs. |
| **C4** | **Content Pipeline** (console CLI, offline) | Build-time AI drafting of bilingual business descriptions from KAP disclosures (evren API); DCF baseline regeneration. Runs on the VPS via `docker compose run --rm contentpipeline …`; review/edit/publish happens through C1 admin + C2 admin endpoints. | Run on user request (BR-RES-002/NFR-RES-006: never on-demand AI); publish without builder review (FR-RES-020 gate lives in C2). |
| **C5** | **PostgreSQL** | Single source of truth for all facts, metrics, snapshots, content, accounts. Append-only fact tables; versioned statements; run ledger. | No business logic beyond constraints (uniqueness, CHECKs) that enforce invariants (e.g., duplicate-name rejection, published-requires-both-languages). |
| **C6** | **CI/CD** (GitHub Actions + GHCR) | On PR: build + test backend (xUnit, Testcontainers), web (lint, typecheck, Vitest, i18n parity, build), e2e (Playwright vs compose stack). On main: publish images to GHCR, SSH deploy, run EF migrations, smoke-check `/health`. | Deploy secrets (only GH secrets + VPS `.env`); no paid runners needed (public repo → free minutes). |
| **C7** | **Infrastructure & ops** | Hetzner VPS (EU), Caddy auto-TLS, nightly encrypted `pg_dump` → Backblaze B2 free tier (30-day retention), UptimeRobot 5-min checks, docker `restart: always`. | No HA/replicas (single instance by design, §9 NFR-RES… availability translation). |

## 4. Domain ↔ component mapping

| Domain (report) | C1 SPA | C2 API | C3 Data Platform | C4 Content | C5 DB |
|---|---|---|---|---|---|
| Market Data Foundation (MDF) | admin coverage view | admin endpoints (coverage, runs, backfill trigger) | **owner**: ingestion, metrics engine, scheduler, alerting | — | **owner**: all fact tables |
| Market Overview & Macro (MOV) | SCR-001 | market module (snapshot + macro read) | **owner**: macro adapters, daily snapshot computation | — | macro tables, snapshots |
| Stock Screening (SCR) | SCR-003, SCR-004 | screener module (run, catalog, saved-screens CRUD) | metrics engine supplies values | — | saved_screens, metric_catalog |
| Stock Research (RES) | SCR-002, SCR-005, admin review UI | stocks module (7 section endpoints, description) | metrics engine (incl. sector medians) | **owner**: AI drafting of descriptions | descriptions, sector_medians |
| Valuation & DCF (VAL) | SCR-006, SCR-011 | DCF module (baseline read, compute, scenarios CRUD) | baseline generation from canonical facts | baseline regeneration CLI | dcf_baselines, dcf_scenarios |
| User Accounts (ACC) | SCR-007..010 | **owner**: Identity module | — | — | users, consent (via Identity + custom) |
| Fund Data Foundation (FDF) | — (headless, BR-FDF-001) | — | **owner**: TEFAS worker, coverage | — | fund tables |

Every domain lands on at least one owning component; no domain spans an unowned boundary. Conflicts (e.g., sector medians listed as RES-owned but computed from MDF facts) are resolved in `02-data-model.md` §5.

## 5. Technology stack

Every row: choice, why, and a rejected alternative with the reason.

| Area | Choice | Why | Rejected (reason) |
|---|---|---|---|
| Backend runtime | **.NET 10 (LTS, Nov 2025)** | Current LTS, supported to Nov 2028 — covers launch + post-launch; builder request. | .NET 8 (LTS support ends Nov 2026 — one month after launch); .NET 9 (STS, already EOL). |
| API style | **ASP.NET Core Minimal APIs**, organized as feature modules | Least ceremony at ~35 endpoints; explicit route groups per module; AI-assist-friendly (small files). | MVC controllers (more ceremony, no benefit at this size); gRPC (no non-browser consumer). |
| ORM | **EF Core 10 + Npgsql**, hybrid: raw SQL (`SqlQuery`/keyless queries) for metric/CAGR computation | Fast to build the CRUD + migration story; window functions and set-based metric math belong in SQL. | Dapper-only (hand-mapped SQL everywhere is slower to iterate for a solo builder); no ORM (migration + mapping burden). |
| Database | **PostgreSQL 17+** | Free; window functions + CTEs for CAGRs/medians; JSONB for criteria/criteria payloads; strong constraint story for the invariants; single free engine for facts + accounts. | SQL Server (licensing cost beyond Express limits in production; brief's cheap-hosting constraint); SQLite (no concurrent multi-user writes: ingest workers + API + Identity); MySQL (weaker fit for window-heavy quantitative SQL; no advantage). |
| Background jobs | **In-process `IHostedService` workers + Cronos cron + DB run ledger** | 6 cron jobs; one deployable; ledger gives retry/idempotency/audit (UC-MDF-001 alternates); reproducible via compose. | Hangfire (adds dashboard + storage dependency for 6 jobs); external cron SaaS (splits deployment, harder to reproduce for the portfolio artifact); Azure Functions (cost/complexity, off-constraint). |
| Frontend framework | **React 19 + Vite + TypeScript SPA** | Builder request (React); SPA fits EOD read-heavy app; simplest AI-assisted solo dev; TanStack Query covers all state sync. | Next.js/SSR (no FR requires SEO — SC-008's 10 users are reachable by direct outreach; SSR server complexity not justified. **Accepted trade-off, veto-able**: stock pages could later be prerendered if organic search ever matters). |
| Data fetching / cache | **TanStack Query v5** | The mutation-matrix refresh/invalidation contract (§8.4) maps 1:1 to query-key invalidation; retries, loading/error states built in (UXR-G-001/003). | Redux Toolkit Query (more boilerplate); SWR (weaker mutation/invalidation story). |
| Routing | **React Router v7 (library mode)** | Standard; SPA route-per-screen + `?returnUrl` auth-hop pattern. | Next.js App Router (tied to SSR choice above). |
| Client state | **zustand** (persisted to `sessionStorage`) | Draft preservation (criteria, DCF assumptions, filters, active section) across navigation and the auth hop — UXR-G-021/024. | Redux (ceremony); URL-only state (search terms/filters do go in URL; drafts don't fit). |
| i18n | **react-i18next**, ICU messages, TR default | Immediate no-reload language switch preserving context (UXR-G-013); account + device persistence (UXR-G-014). Server sends codes/numbers only → one source of bilingual truth. | Server-side localization (splits copy across two stacks); next-intl (couples to Next.js). |
| UI kit / styling | **Tailwind CSS v4 + shadcn/ui (Radix)** | Accessible primitives (dialogs, focus traps — UXR-G-026/027, deletion confirmations UXR-G-022); fast consistent styling for solo/AI workflow; responsive by utility. | MUI (heavier, opinioned theming); hand-written CSS (slower, inconsistent, a11y risk). |
| Charts | **Recharts** | SVG line/bar charts for historical valuation + price series; screen-reader-exposable elements. | TradingView lightweight-charts (canvas → weaker a11y, trading focus not needed); ECharts (bundle size). |
| Auth | **ASP.NET Core Identity + cookie auth** (HttpOnly, Secure, SameSite=Lax), lockout, e-mail verification & reset tokens | Battle-tested register/login/verify/reset/lockout with least custom code; cookie is the safest browser option (no token storage in JS → XSS-resistant). **Future-client note (D-01):** a `/api/v1/auth/token` endpoint can be added for a non-web client without redesign; building any native client still requires a brief amendment first (§12 FLG-02). | JWT + refresh rotation now (custom rotation logic, footguns, zero V1 consumers); IdentityServer/Duende/Keycloak (single first-party client — overkill). |
| Transactional e-mail | **Brevo SMTP free tier** (300/day) | Covers verification + reset + pipeline alerts at $0; SMTP is swappable. Sender `no-reply@<domain>` (domain via Student Pack, D-04). | SendGrid free (100/day, tighter); self-hosted SMTP (deliverability risk). |
| Reverse proxy | **Caddy 2** | Automatic TLS with a 5-line config; serves SPA statics + proxies `/api`. | nginx + certbot (manual cert management); Traefik (more moving parts). |
| Logging | **Serilog** → structured JSON to rolling files + stdout | Queryable logs on-box; docker-friendly; grep-able for FR-MDF-012 anomaly alerts. | ELK/Seq hosted (cost/ops); plain Console (no retention). |
| Monitoring/uptime | **UptimeRobot free** (5-min) + alert e-mails | $0; covers "publicly accessible" (SC-001) watchdog; pipeline health surfaces via run ledger + alerts. | Paid APM (off-constraint). |
| Backups | **nightly `pg_dump` (gzip) → Backblaze B2 free tier (10 GB), 30-day retention, rclone** | DB estimate 1–5 GB (§02 §6); B2 free tier suffices; documented restore drill (RTO 4h, RPO 24h). | No backups (unacceptable — accounts + KVKK data); managed snapshot (VPS-level, less portable). |
| CI/CD | **GitHub Actions + GHCR + SSH deploy** (D-06) | Public repo → unlimited free minutes; images public in GHCR (portfolio artifact); deploy = compose pull + migrate + smoke-check. Full detail §6. | Self-hosted runners (ops burden); other CI vendors (cost/features for a public repo). |
| Testing | **xUnit + Testcontainers (Postgres)** backend; **Vitest + Testing Library** web; **Playwright** e2e | Testcontainers proves SQL/migrations against real Postgres; Playwright covers the SC-002 core loop end-to-end; i18n parity script enforces 100% bilingual completeness in CI. | In-memory DB provider (SQL behavior differs — false confidence). |
| Repo layout | **Monorepo** (`/src/Api`, `/src/Core`, `/src/Ingestion`, `/src/ContentPipeline`, `/src/Web`, `/tests`, `docker-compose*.yml`, `README.md`) | Solo builder, one CI, one story for the portfolio audience (SC-007). | Polyrepo (sync/CI overhead, weaker narrative). |
| Containerization | **Docker + docker-compose** (prod compose + dev compose with MailPit + hot-reload volumes) | Reproducibility NFR-MDF-005/NFR-FDF-004; the compose file *is* the deployment doc; local e-mail testing via MailPit until the domain arrives (D-04). | Bare-metal installs (not reproducible). |

**Shared quantitative core:** all formulas (canonical metrics, CAGR, market aggregates, DCF model) live in `/src/Core` (C#), consumed by C3b and C2 — one definition per number (BR-MDF-009). The DCF fair-value formula is specified in `03-api-design.md` §7.

## 6. CI/CD (builder requirement D-06)

Two workflows, both on GitHub Actions (free — public repo):

### 6.1 `ci.yml` — on every PR and push to `main`

1. **Backend job** — `dotnet restore && dotnet build && dotnet test` (xUnit; Testcontainers spins up a throwaway Postgres service container; migrations applied; metric-engine golden-value tests against seeded fixture data).
2. **Web job** — `npm ci`, ESLint, `tsc --noEmit`, Vitest unit/component tests, **i18n parity check** (custom script: every key present in both `tr.json` and `en.json`, no unused keys — enforces the 100% bilingual NFRs mechanically), `vite build`.
3. **E2E job** (PR + main; needs the other two) — Playwright against a `docker compose -f compose.ci.yml` stack (API+Postgres+seeded fixture data+MailPit): covers the SC-002 core loop (dashboard → screener run → stock page → DCF compute), the anonymous save → register → verify (via MailPit) → save flow (UXR-SCR-019), duplicate-name rejection (UXR-G-029), and deletion confirmations.

### 6.2 `deploy.yml` — on push to `main` after CI passes

1. Build + push `ghcr.io/sarperim/degerli/api:<sha>` and `:latest` (multi-stage; SPA built and embedded in the image for Caddy or published as an artifact — final call at implementation).
2. SSH to the VPS (secret `SSH_PRIVATE_KEY`; host/user in secrets): `docker compose pull`, `docker compose run --rm api migrate` (EF Core `database update`, forward-only), `docker compose up -d`, then smoke-check `GET /health` → non-200 fails the job and alerts.
3. Rollback: previous image tag remains on GHCR; `docker compose` tag pin makes rollback a re-deploy of the prior SHA.

### 6.3 Policies

- **Secrets:** GitHub Actions secrets (SSH key/host/user) + VPS-side `.env` (DB password, Brevo SMTP, evren API key, `APP_URL`, admin e-mail). Repo carries `.env.example` only. **No secrets in images** (runtime env only) — safe for a public repo and public GHCR.
- **Branch protection (recommended, not enforced by me):** require `ci.yml` green on PRs to `main`; builder retains admin-merge for speed.
- **Commits to `main` deploy automatically** — deliberate for a solo workflow; gated by CI. Feature branches → PR → review-by-self → merge.
- Content pipeline and backfills are **not** in CD: they run on-box via CLI/admin triggers (§2), keeping the deploy path minimal.

## 7. FR traceability matrix

All FRs from the 7 domain reports. Component codes from §3. Won't FRs are listed once at the end (out of scope by BA decision — not architectural defects).

### MDF (market-data-foundation)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-MDF-001 EOD prices+volume | Must | C3a | İşbank/price adapter → `daily_prices` upsert (idempotent on instrument+date) |
| FR-MDF-002 financial statements | Must | C3a | KAP adapter → XBRL/PDF parse → `financial_statements` + line items, versioned |
| FR-MDF-003 dividends | Must | C3a | KAP adapter → `dividends` |
| FR-MDF-004 corporate actions | Must | C3a | KAP adapter → `corporate_actions` (terms JSON) |
| FR-MDF-005 KAP disclosures | Must | C3a | KAP adapter → `kap_disclosures` (metadata; documents to disk/object storage) |
| FR-MDF-006 index levels + sectors | Must | C3a | index adapter → `index_levels`, sector sync |
| FR-MDF-007 constituent changes, effective-dated | Must | C3a | append-only `index_constituents` (effective_from/to), never mutated |
| FR-MDF-008 provenance per fact | Must | C3a+C5 | `source_ref` NOT NULL on every fact table + run ledger FK |
| FR-MDF-009 daily auto refresh | Must | C3c | cron 20:30 Europe/Istanbul each trading day; retries; run ledger |
| FR-MDF-010 backfill to 10Y/limit + record depth | Must | C3a+C2 | backfill mode in adapters + admin trigger; achieved depth in `coverage_metadata` |
| FR-MDF-011 coverage metadata exposed | Must | C3a+C2+C1 | `coverage_metadata` table + admin coverage endpoint/report |
| FR-MDF-012 anomaly alerts (log + e-mail) | Should | C3a+C3d+C2+C1 | invalid facts quarantined in `quarantined_facts` (never written to fact tables, UC-MDF-001b); Serilog + Brevo alert; admin quarantine review endpoints |
| FR-MDF-013 canonical metrics, 5 families | Must | C3b | Metrics Engine → `derived_metrics` (definitions in `/src/Core`, documented in data model) |
| FR-MDF-014 CAGR from available history + window | Must | C3b | CAGR SQL computes over actual span; `window_years` column carries the actual window |
| FR-MDF-015 no overwrite, as-of retrieval | Must | C5 | append-only fact tables; restatements as new versions; dated rows |
| FR-MDF-016 last-known-good + stale marking | Should | C3b+C2 | `data_freshness` view per data type; API payload `stale` + `asOf` flags |
| FR-MDF-017 screen backtesting | Won't | — | out of scope (brief §6) |
| FR-MDF-018 adjusted-price marking | Must | C3b+C2 | both series stored; payload `adjusted: true` on historical series |
| FR-MDF-019 restatement marking | Must | C3b+C2 | statement version → per-figure `restated` flag in payloads |

### MOV (market-overview)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-MOV-001..006 dashboard blocks (indices, sectors, breadth, movers w/ eligibility, volume, market P/E + div yield) | Must | C3b+C2+C1 | nightly `market_snapshots` (Metrics Engine); `GET /api/v1/market/overview`; SCR-001 blocks; market P/E = cap-weighted aggregate excluding loss-makers w/ disclosure (D-05, BR-MOV-010) |
| FR-MOV-007..009 macro display + pair + attribution | Must | C3a+C2+C1 | macro adapters → `macro_values`; overview payload includes six series w/ source + as-of; inflation always paired (BR-MOV-002) |
| FR-MOV-010 as-of + stale marking | Must | C2+C1 | payload flags; UI stale markers (UXR-MOV-010) |
| FR-MOV-011..013 navigation (movers, sectors, screener) | Must | C1 | SPA links (routes) |
| FR-MOV-014 macro ingest per cadence | Must | C3a/C3c | per-series cron (daily FX/gold/rate; CPI monthly; per-release) |
| FR-MOV-015 macro failure notify | Should | C3d | alert e-mail + log |
| FR-MOV-016 last-known macro + stale | Should | C2 | latest value + `stale` flag |
| FR-MOV-017 sector period selector (1W/1M/YTD) | Could | C3b+C2+C1 | snapshot stores daily + period aggregates; selector param — **Could: implement last** |
| FR-MOV-018/019 | Won't | — | out of scope / permanently rejected (10Y yield) |
| FR-MOV-020 disclaimer | Must | C1 | i18n disclaimer component on SCR-001 |
| FR-MOV-021 volume per mover entry | Should | C3b+C2+C1 | mover entries include traded volume |

### SCR (stock-screening)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-SCR-001 18 metrics screenable | Must | C2+C5 | metric catalog + `POST /api/v1/screener/run` over `derived_metrics` |
| FR-SCR-002 AND logic | Must | C2 | criteria evaluated conjunctively in one SQL query |
| FR-SCR-003 min/max/range bounds | Must | C2+C1 | criterion model + bound controls |
| FR-SCR-004 results + row links | Must | C2+C1 | run response (rows w/ per-criterion values) → SPA table → stock-page links |
| FR-SCR-005 results as-of + stale | Must | C2+C1 | response `asOf`/`stale`; UI markers |
| FR-SCR-006 zero-match empty state | Must | C1 | explicit empty state (UXR-SCR-006) |
| FR-SCR-007 save named screen server-side | Must | C2+C5 | `saved_screens`, unique (user_id, name) |
| FR-SCR-008 list saved screens | Must | C2+C1 | `GET /api/v1/me/screens` |
| FR-SCR-009 re-run on latest data | Must | C2 | run endpoint w/ stored criteria — never stored results |
| FR-SCR-010 rename | Must | C2 | `PATCH …/screens/{id}` |
| FR-SCR-011 delete | Must | C2 | `DELETE …/screens/{id}` |
| FR-SCR-012 anonymous save prompt | Must | C1 | save button → auth prompt modal/route |
| FR-SCR-013 preserve criteria across auth hop | Should | C1 | zustand draft + sessionStorage + `?returnUrl` |
| FR-SCR-014 exclusion count | Should | C2+C1 | run response `excludedCount` + UI display |
| FR-SCR-015 CAGR window select (3/5/10Y) | Must | C1+C2 | criterion field; window param in run |
| FR-SCR-016 disclaimer | Must | C1 | i18n component |
| FR-SCR-017 hide metric w/o code change | Should | C2+C5+C1 | `metric_catalog.is_screenable` flag + admin toggle endpoint; catalog endpoint serves visible only |
| FR-SCR-018/019 | Won't | — | out of scope |

### RES (stock-research)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-RES-001 stock list (name, code, sector) | Must | C2+C1 | `GET /api/v1/stocks` (current-universe view) |
| FR-RES-002 sector filter | Must | C2+C1 | query param + UI filter |
| FR-RES-003 search by name/code | Must | C2+C1 | query param (ILIKE) + UI |
| FR-RES-004 global header search | Should | C2+C1 | `GET /api/v1/stocks/search?q=` + header component |
| FR-RES-005 description in selected language | Must | C2+C1 | published `business_descriptions` row; both TR+EN served; client renders chosen |
| FR-RES-006 valuation section (current + historical) | Must | C3b+C2+C1 | `derived_metrics` history + adjusted price series → section endpoint |
| FR-RES-007 vs.-sector strip | Should | C3b+C2+C1 | nightly `sector_metric_medians` (5 valuation metrics, medians, peer+exclusion counts) |
| FR-RES-008 financials section | Must | C2+C1 | section endpoint over statements + FCF metrics |
| FR-RES-009 profitability section | Must | C2+C1 | ROIC/ROE/margin metrics |
| FR-RES-010 growth section + window indication | Must | C2+C1 | CAGRs + `window_years` surfaced |
| FR-RES-011 balance sheet section | Must | C2+C1 | BS line items |
| FR-RES-012 dividend history section | Must | C2+C1 | `dividends` table |
| FR-RES-013 adjusted-data disclaimer | Must | C1 (+C2 flags) | UI component bound to `adjusted` flag |
| FR-RES-014 restatement asterisk/footnote/warning | Must | C1 (+C2 flags) | UI component bound to per-figure `restated` flag |
| FR-RES-015 per-section as-of + stale | Must | C2+C1 | every section payload carries flags |
| FR-RES-016 no-data states w/ coverage boundary | Must | C2+C1 | 200-with-`coverage` payload (not 404); honest states |
| FR-RES-017 disclaimer | Must | C1 | i18n component |
| FR-RES-018 DCF hand-off | Must | C1 | route link |
| FR-RES-019 offline pipeline drafts TR+EN | Must | C4 | CLI: KAP disclosures → evren API → draft rows (status=draft) |
| FR-RES-020 publication gate | Must | C2+C5 | publish endpoint requires builder role + both languages (CHECK constraint) |
| FR-RES-021 builder edits both languages | Must | C1+C2 | admin description editor |
| FR-RES-022 last-reviewed date | Should | C2+C1 | `last_reviewed_at` on published row |
| FR-RES-023..025 | Won't | — | out of scope (AI summaries, comparison, education) |
| FR-RES-026 "description in preparation" state | Must | C2+C1 | 200 with `status: preparing` payload state |
| FR-RES-027 book value + BVPS | Should | C3b+C2+C1 | canonical book-value metrics in Balance Sheet section |

### VAL (valuation-dcf)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-VAL-001 calculator per stock, from stock page | Must | C1+C2 | route + `GET /stocks/{symbol}/dcf` (baseline) |
| FR-VAL-002 current EOD price + as-of | Must | C2 | canonical price from `daily_prices` |
| FR-VAL-003 baseline defaults + immediate result | Must | C3b+C2+C1 | `dcf_baselines` (versioned) + initial compute server-side on GET |
| FR-VAL-004 adjust every parameter | Must | C1 | 8-parameter panel (confirmed set, UX §4.1) |
| FR-VAL-005 recompute on change | Must | C2+C1 | debounced `POST …/dcf/compute` (300 ms); no reload |
| FR-VAL-006 plain-language verdict, MOS % | Must | C1 | i18n verdict template over compute response (TR/EN) |
| FR-VAL-007 sensitivity table (r × g_terminal) | Should | C2+C1 | compute response includes full grid |
| FR-VAL-008 disclaimer + "your assumptions" | Must | C1 | i18n components |
| FR-VAL-009 save named scenario | Should | C2+C5 | `dcf_scenarios`, unique (user, stock, name) |
| FR-VAL-010 reload scenario exactly | Should | C2+C1 | scenario params restore; recompute |
| FR-VAL-011..013 | Won't | — | out of scope |

### ACC (user-accounts)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-ACC-001 register (e-mail+password+consent) | Must | C2+C5 | Identity + `consent_records` (bilingual notice, versioned) |
| FR-ACC-002 sign in | Must | C2 | Identity cookie auth |
| FR-ACC-003 sign out | Must | C2 | cookie revoke |
| FR-ACC-004 language pref per account | Must | C2+C1 | `user_profiles.language_pref`; applied on sign-in (UXR-ACC-009) |
| FR-ACC-005 settings screen (lang, password, entry points, delete, verification status) | Must | C1 | SCR-010 |
| FR-ACC-006 account deletion (data + saved items) | Must | C2+C5 | hard delete + cascade (screens, scenarios); consent retained anonymized (§02 §5.3) |
| FR-ACC-007 password change | Should | C2+C1 | Identity change-password, session preserved |
| FR-ACC-008 verification e-mail | Must | C2+C7 | Identity tokens (48h, single-use) via Brevo; blocks persistence saves (UXR-G-030) |
| FR-ACC-009 password reset via e-mail | Must | C2 | Identity reset tokens (2h, single-use); enumeration-neutral |
| FR-ACC-010 return to in-progress work | Should | C1 | draft store + `?returnUrl` |
| FR-ACC-011/012 | Won't | — | out of scope (social login, 2FA) |

### FDF (fund-data-foundation)

| FR | MoSCoW | Components | Mechanism |
|---|---|---|---|
| FR-FDF-001 NAV history ingest | Must | C3a | TEFAS adapter → `fund_navs` |
| FR-FDF-002 performance ingest | Must | C3a | `fund_performances` |
| FR-FDF-003 holdings ingest (where public) | Must | C3a | `fund_holdings` snapshots |
| FR-FDF-004 per-fund coverage metadata | Should | C3a+C2 | `coverage_metadata` (fund scope) + admin report |
| FR-FDF-005 anomaly alerts | Should | C3d | alert e-mail + log |
| FR-FDF-006 daily refresh unattended | Must | C3c | cron (TEFAS cadence) |
| FR-FDF-007..009 | Won't | — | fund UI/look-through out of scope |

**Matrix verdict:** all 104 non-Won't FRs map to at least one component; no orphan FRs. Won't-listed FRs (16) are BA decisions, not defects.

## 8. UX constraint satisfaction

### 8.1 The load-bearing mechanisms

| # | Mechanism | Serves |
|---|---|---|
| M-1 | **Honest-data envelope** — every data payload carries `asOf`, `stale`, and where applicable `adjusted`, per-figure `restated`, `coverage` | UXR-G-006..012; all data-bearing UXRs |
| M-2 | **TanStack Query + key invalidation** — mutations invalidate exactly the lists/pickers that must refresh without reload | Mutation matrix refresh column (§8.4) |
| M-3 | **zustand draft store persisted to sessionStorage** — criteria, DCF assumptions, filters, search, names, active stock-page section survive navigation + auth hop | UXR-G-021, UXR-G-024, UXR-SCR-010, UXR-VAL-013, FR-SCR-013, FR-ACC-010 |
| M-4 | **react-i18next instant switch** — language state outside data caches; URL + scroll preserved | UXR-G-013, UXR-G-014 |
| M-5 | **Per-section endpoints + independent Query instances** — stock-page sections and dashboard blocks load/fail/retry independently | SCR-005 §5, SCR-001 §5 |
| M-6 | **Debounced DCF compute endpoint** — outputs always mirror displayed inputs; last-valid-result retained on invalid input (client validation gates the call) | UXR-VAL-004, UXR-VAL-008 |
| M-7 | **DB uniqueness + `409 DUPLICATE_NAME`** — inline collision errors, no overwrite | UXR-G-029 |
| M-8 | **Radix/shadcn primitives** — focus traps, keyboard operability, visible focus, semantic tables | UXR-G-022, G-025..028 |
| M-9 | **`refetchOnWindowFocus` on session query** — verified-in-another-tab state applies to an open session immediately | UXR-ACC-006 |
| M-10 | **Rate limiting + lockout + generic errors** | UXR-ACC-008, UXR-ACC-012, RISK-ACC-001 |

### 8.2 Global rules UXR-G-001..030

| UXR(s) | Mechanism |
|---|---|
| G-001 loading on every screen-load | TanStack Query `isPending` + skeleton components per screen/block/section (M-5) |
| G-002 pending on async action | `isMutating` state disables + visually marks trigger |
| G-003 plain-language errors + retry | Error-code → i18n copy mapping (§10.2); retry re-invokes mutation; technical details logged, never rendered |
| G-004 input preserved on failure | Form state in React state/draft store (M-3); mutations never reset on reject |
| G-005 empty states | Explicit empty components per surface (screener zero-match w/ relax hint, list no-match, saved-items empty) |
| G-006 no-data w/ coverage boundary | M-1 `coverage` field → honest state component |
| G-007 as-of dates | M-1 `asOf` rendered on every data-bearing block |
| G-008 stale marking | M-1 `stale` → badge + as-of |
| G-009 restatement marks | M-1 per-figure `restated` → asterisk + footnote + warning component |
| G-010 adjusted-data disclaimer | M-1 `adjusted` → disclaimer component |
| G-011 cross-surface metric equality | single Metrics Engine + single DB (BR-MDF-009); same `derived_metrics` rows serve all endpoints |
| G-012 no real-time implication | EOD framing in copy; no auto-refresh timers; static until reload/navigation |
| G-013 instant language switch, context preserved | M-4: i18n state change re-renders; data caches, URL, scroll untouched |
| G-014 language persistence (device/account) | anonymous: localStorage; signed-in: PATCH preference + applied at sign-in; toggle-while-signed-in syncs account |
| G-015 plain language + units | i18n copy discipline; review gate |
| G-016 disclaimer on 7 research surfaces | shared i18n disclaimer component on SCR-001/002/003/004/005/006/011 |
| G-017 no advice language | copy policy + e2e assertions on key surfaces |
| G-018 inline validation w/ visible rules | client validators mirroring server contract; rules text i18n |
| G-019 disabled submit while invalid | form validity → disabled control |
| G-020 no duplicate submission | pending lock on triggers |
| G-021 state preserved across navigation | M-3 draft store (criteria/assumptions/filters/search/section) |
| G-022 destructive confirmations | Radix AlertDialog w/ consequence text (screens, scenarios, account) |
| G-023 anonymous save → sign-in prompt | prompt modal/route from save triggers |
| G-024 return to preserved work | `?returnUrl` + M-3 restore after auth success |
| G-025 narrow-width operability | Tailwind responsive layouts; tables degrade to prioritized columns (data retained) |
| G-026 keyboard operability | native/shadcn controls; focus order = reading order |
| G-027 visible + managed focus | focus-visible styling; focus placed/restored on route + dialog changes |
| G-028 programmatic labels in active language | labeled controls, semantic tables, aria labels from i18n |
| G-029 duplicate-name rejection | M-7: unique constraints → 409 → inline error, nothing overwritten |
| G-030 unverified-save gate | API 403 `EMAIL_NOT_VERIFIED` → verify-your-e-mail message + path; drafts preserved; unblocks on verify (M-9) |

### 8.3 Per-screen UXRs (98)

| UXR group | Mechanism |
|---|---|
| UXR-MOV-001..011 (SCR-001 blocks, as-of/stale, nav, inflation pair) | M-1 + `GET /api/v1/market/overview` (snapshot + macro in one payload, blocks render/fail independently as components); mover/sector links are SPA routes; inflation pair always both rendered (degrade note when independent measure missing — payload state, not absence) |
| UXR-MOV-012 sector period selector (Could) | snapshot period aggregates + selector; **last to implement** |
| UXR-SCR-001..007, 011, 019 (SCR-003 builder/run/results/empty/hidden-metrics/unverified gate) | criteria builder over `GET /api/v1/screener/metrics` (visible only → UXR-SCR-011); `POST /run` (AND, bounds, CAGR window); results table + counts + asOf; empty state; M-10 gate for UXR-SCR-019 |
| UXR-SCR-008..010 (save + preservation) | `POST /api/v1/me/screens` (403/409 handling); M-3 + M-7 |
| UXR-SCR-012..018 (SCR-004 list, re-run, dropped criterion, rename/delete/empty/anonymous) | `GET /me/screens`; re-run = run endpoint with stored criteria (response includes `droppedCriteria` when a hidden metric was skipped → UXR-SCR-015); rename `PATCH`, delete `DELETE` + confirmation; anonymous access → sign-in path |
| UXR-RES-001..007 (SCR-002 list/filter/search/prefill/header search) | `GET /api/v1/stocks` + `?sector=&q=`; dashboard links prefill via URL params; header search via `GET /api/v1/stocks/search` |
| UXR-RES-008..025 (SCR-005 seven sections + honesty marks + strip + DCF hand-off) | M-5 per-section endpoints; M-1 markers; strip = stock values + `sector_metric_medians` (peer counts, not-meaningful states); section state in M-3; DCF link routes to SCR-006 |
| UXR-VAL-001..011, 021 (SCR-006 calculator) | `GET /stocks/{s}/dcf` (baseline + price + immediate result); 8-param panel; M-6 debounced compute (point + sensitivity grid); M-1 flags on price/inputs; validation gating (UXR-VAL-008); unverified-save gate (021) |
| UXR-VAL-012..015 (save/load/picker) | `POST /me/scenarios`, picker = `GET /me/scenarios?stock=`, load restores params + confirm-over-discard (G-022 pattern) |
| UXR-VAL-016..020 (SCR-011 grouped list, rename/delete/anonymous/empty) | `GET /me/scenarios` grouped by stock; rename/delete w/ confirmations; M-2 invalidation of picker |
| UXR-ACC-001..007, 021 (SCR-007 register + verification landing + resend) | Identity flows; bilingual notice + consent checkbox; verification link → route with token → session gains verified state (M-9); resend endpoint |
| UXR-ACC-008..011 (SCR-008 sign in/out) | cookie auth; generic error; language pref applied; return-to-work; sign-out header control |
| UXR-ACC-012..015 (SCR-009 reset) | request → neutral confirmation; link → set-password; expiry handling + re-request |
| UXR-ACC-016..020 (SCR-010 settings) | language (immediate + persisted), password change, entry points, deletion w/ cascade + sign-out, verification status + resend |

**Coverage check:** all 128 UXRs (30 global + 98 per-screen) are mapped; none lacks a mechanism. *(SCR-012 note, 2026-10-06: the UX package has since added 28 admin UXRs — UXR-MDF-001..028 — which this table predates; they are served by the AD-13 admin surface, i.e. the `03` §8 admin endpoints plus the global mechanisms above, including UXR-MDF-020 via the v1.2 `GET /admin/descriptions` response sketch (`03` §8). Row-by-row mapping is deliberately not re-derived here — minimal-amendment posture; see §13.1.)*

### 8.4 Mutation matrix → invalidation contract

The UX matrix's "refresh without manual reload" column maps to TanStack Query key invalidation:

| Matrix mutation group | Invalidation on success |
|---|---|
| Save / rename / delete saved screen | invalidate `['me','screens']` + `['screen', id]`; active-screen display updates from mutation response |
| Save / rename / delete DCF scenario | invalidate `['me','scenarios']` + `['scenarios', stock]` (picker) |
| Register / verify / sign in / sign out | invalidate `['session']` (header auth state, gating), apply language preference client-side |
| Language toggle (signed-in) | optimistic local switch; PATCH; on failure revert + notice |
| Re-run screen / DCF compute | query results replaced by response (server-computed on demand; no persistence) |
| Password change / reset / account deletion | session invalidation; deletion → full client cache reset + sign-out state |

In-place rename updates use mutation-response data (no refetch of whole lists). All pending locks (UXR-G-020) are per-mutation `isMutating`.

## 9. NFR translation (business NFR → technical spec with numbers)

| NFR | Technical specification |
|---|---|
| NFR-MDF-001 freshness | EOD job starts 20:30 TRT each trading day; target completion 23:59 TRT; failure → retry ×3 (backoff 5/15/60 min) → alert e-mail; last-known-good served with `stale` flag. Macro: FX/gold daily by 09:00 next day; CPI within 24h of release; policy rate within 24h of CBRT decision. |
| NFR-MDF-002 retention | All fact tables append-only; no TTL/deletion jobs anywhere; 10Y ingest target where sources allow; DB sized for life-of-platform growth (§02 §6). |
| NFR-MDF-003 coverage transparency | `coverage_metadata` row per instrument×data-type; 100% of universe covered or explicitly recorded as gap; admin coverage report endpoint + ops summary; quarantine dismissals record the accepted gap with a note (BR-MDF-007); gaps render as honest no-data states (never silently dropped). |
| NFR-MDF-004 provenance | `source_ref` + `recorded_at` NOT NULL on every fact table; ingestion refuses facts without provenance (validation → quarantine). |
| NFR-MDF-005 public reproducibility | Monorepo + docker-compose + README (sources, model, cadence) + CI green badge; a fresh clone runs the full stack + seeded fixtures locally in ≤ 10 commands (documented quickstart). |
| NFR-MDF-006 cost | $0 data: adapters only for KAP/İşbank/TEFAS/TÜİK/ENAG/CBRT/free FX-gold. Infra ≤ ~€5/mo VPS + free tiers (B2, UptimeRobot, Brevo, GH Actions) + domain $0 (Student Pack year 1). |
| NFR-MOV-001 freshness | Snapshot stamped with trading date; served value always the latest completed snapshot; every macro value carries per-series as-of date. |
| NFR-MOV-002 bilingual completeness | i18n parity check in CI (missing TR or EN key fails the build); TR default verified by e2e. |
| NFR-MOV-003 public availability | Anonymous access to all research endpoints; availability target **99.0% monthly** (single VPS, ~7.2 h/mo budget — honest number for single-instance, no-HA); UptimeRobot 5-min checks → alert e-mail. |
| NFR-MOV-004 plain language | Copy review checklist per surface; units + as-of mandatory in labels; e2e asserts presence of units/as-of on macro strip. |
| NFR-MOV-005 cost | (as NFR-MDF-006) |
| NFR-MOV-006 regulatory | Disclaimer components on all 7 research surfaces (e2e-asserted); no recommendation vocabulary in i18n catalogs (grep check in CI as a backstop). |
| NFR-SCR-001/003 bilingual + consistency | i18n parity (CI); metric equality by construction (single Metrics Engine) + e2e spot-assertions (screener value = stock-page value for same stock/metric/date). |
| NFR-SCR-002 plain-language criteria | Criterion phrasing from metric catalog labels (TR/EN) w/ units; e2e asserts sample phrasing. |
| NFR-SCR-004 freshness honesty | Run responses always carry `asOf` (+`stale`); no client caching of results across days (query key includes asOf from context). |
| NFR-SCR-005 server-side persistence | Saved screens/scenarios in Postgres; device-independent by construction (server truth). |
| NFR-RES-001 bilingual completeness | Parity CI + description publish gate (both TR+EN required — DB CHECK). |
| NFR-RES-002 plain-language quality | builder review gate (workflow, FR-RES-020/021); no automated bypass. |
| NFR-RES-003 cross-surface consistency | (as NFR-SCR-003) |
| NFR-RES-004 freshness split | financial figures = EOD as-of; descriptions carry `last_reviewed_at` displayed. |
| NFR-RES-005 public availability | (as NFR-MOV-003) |
| NFR-RES-006 AI governance | AI runs only in C4 CLI (offline, builder-triggered); publication requires explicit builder action; drafts never served (status filter). |
| NFR-VAL-001 bilingual | parity CI incl. verdict templates. |
| NFR-VAL-002 plain-language verdict | verdict template = single sentence w/ percentage; builder validates during SC-006 research session. |
| NFR-VAL-003 price consistency | price from same `daily_prices` row as stock page (single source). |
| NFR-VAL-004 no black box | all 8 inputs visible+editable (BR-VAL-002); canonical-fact defaults labeled w/ source + as-of; no hidden coefficients in formula (§03 §7). |
| NFR-ACC-001 KVKK | collected fields: e-mail, password hash, language pref, consent records (+ user-owned content: screen criteria, scenario params); bilingual privacy notice versioned; deletion path hard-deletes; consent evidence retained anonymized; builder review (UC-ACC-005) before launch. |
| NFR-ACC-002 credential security | ASP.NET Identity defaults: PBKDF2 (≥100k iterations per .NET 10 defaults), per-user salt; lockout 10 failed / 15 min; generic sign-in errors; enumeration-neutral reset confirmation. |
| NFR-ACC-003 bilingual | parity CI. |
| NFR-ACC-004 retention | account data live while account active; deletion removes account + saved items; consent records retained (KVKK evidence) with `user_id` nulled + anonymized hash. |
| NFR-ACC-005 optional-account posture | all research endpoints anonymous; only `/me/*` mutations require auth; persistence additionally requires verified e-mail (UXR-G-030). |
| NFR-FDF-001 coverage transparency | per-fund `coverage_metadata`; measured during backfill; admin report. |
| NFR-FDF-002 retention | append-only fund tables; no deletion. |
| NFR-FDF-003 cost | TEFAS only, $0. |
| NFR-FDF-004 reproducibility | TEFAS adapter + docs in same repo/CI scope. |

**Architecture-level performance targets** (derived, not in brief — modest and honest for a 100-stock EOD app): API p95 ≤ 300 ms (cached GETs), ≤ 800 ms (screener run, DCF compute); dashboard first contentful render ≤ 3 s on 4G; Playwright e2e budget ≤ 5 min total.

## 10. Cross-cutting concerns

### 10.1 Authentication & authorization
- Cookie session (HttpOnly, Secure, SameSite=Lax) via ASP.NET Core Identity; CSRF: antiforgery token on unsafe `/api` requests (cookie+Lax alone is not sufficient for POSTs from subdomains).
- Roles: `user` (registered) and `builder` (admin role assigned to the builder's account via seeded claim); admin routes + publish actions require `builder`.
- E-mail verification (48h single-use token) gates persistence mutations (`403 EMAIL_NOT_VERIFIED`), not browsing (UXR-G-030). Reset tokens 2h single-use.
- Rate limits: `/api/v1/auth/*` 5 req/min/IP; global 600 req/min/IP; admin 60 req/min. Lockout 10/15 min.
- Future non-web client (post-V1, requires brief amendment): add `/api/v1/auth/token` issuing refresh tokens — documented extension point, deliberately not built (AD-07).

### 10.2 Error handling
- **Contract:** RFC 7807 ProblemDetails with stable machine `code`, params; **user-facing message text is rendered client-side from i18n** (codes → TR/EN copy). Raw exceptions never serialized to clients; Serilog captures them server-side with a correlation ID returned as `traceId` for support.
- Code catalog (initial): `VALIDATION_FAILED`, `INVALID_CREDENTIALS`, `EMAIL_TAKEN`, `EMAIL_NOT_VERIFIED`, `DUPLICATE_NAME`, `NOT_FOUND`, `RATE_LIMITED`, `LOCKED_OUT`, `TOKEN_EXPIRED`, `CRITERION_DROPPED` (informational field, not error), `DCF_NOT_COMPUTABLE` (e.g., discount rate ≤ terminal growth), `CONFLICT`.
- API 5xx → generic plain-language error + retry (UXR-G-003); e-mail dispatch failures on background flows → alert to builder (never silent).

### 10.3 Logging & observability
- Serilog structured JSON: request logs (method, route, status, ms, user id if any), job logs (job, run id, stats), alert events. Rolling files (14-day retention) + stdout (docker logs). Correlation IDs per request/job.
- `/health` endpoint (liveness + DB check) used by deploy smoke-check and UptimeRobot.
- Run ledger (`ingest_runs`) is the operational dashboard for pipeline health (admin view).

### 10.4 Configuration
- `appsettings.json` for non-secrets (schedules, feature flags like metric visibility defaults); env overrides for secrets: `ConnectionStrings__Default`, `Smtp__*`, `Evren__ApiKey`, `APP_URL`, `ALERT_EMAIL`. VPS `.env` (git-ignored) + committed `.env.example`. Secrets never in repo or images (public repo constraint).

### 10.5 Security posture (beyond auth)
- HTTPS-only (Caddy auto-TLS, HTTP→HTTPS redirect), HSTS, `X-Content-Type-Options: nosniff`, `frame-ancestors 'none'`, CSP `default-src 'self'` (SPA assets self-hosted; no third-party scripts — also good for the $0 posture).
- Password policy: min 10 chars (plain-language rules visible, UXR-ACC-003); no composition rules (NIST-alignment, plain-language friendly).
- Input validation everywhere ( FluentValidation-style server checks mirrored client-side); criteria/params payloads schema-validated (strict types, bounded numbers).
- Data egress: only outbound calls are adapters (source APIs), Brevo SMTP, evren API (C4), B2 (backups). No third-party analytics/trackers.

### 10.6 i18n discipline
- All user-facing strings (UI, verdicts, errors, emails) in `tr.json`/`en.json`; CI parity check; TR default; language resolved: account pref > localStorage > `tr`. E-mails are bilingual (both languages included in one e-mail) to avoid locale guessing server-side.

### 10.7 Honest-data payload contract (the envelope)
Every data-bearing response includes: `asOf` (ISO date), `stale` (bool); historical series add `adjusted` (bool); figures add `restated` (bool) where applicable; missing data returns 200 with `coverage: { availableFrom, boundaryNote }` or explicit `state: 'no-data' | 'preparing' | 'unavailable'` — never blank-as-zero (BR-RES-008/UXR-G-006).

### 10.8 Backup & disaster recovery
- Nightly `pg_dump -Fc` (compressed) → B2, 30-day retention, rclone over encrypted transport; restore drill documented in README (target: exercised once before launch, RTO 4h, RPO 24h).
- VPS loss = hours of recovery, not data loss beyond 24h — acceptable for this scale (documented consciously; matches the 99% availability target).

## 11. Architecture decision log (summary)

| AD | Decision | One-line rationale (full text in sections above) |
|---|---|---|
| AD-01 | .NET 10 LTS + ASP.NET Core Minimal APIs | support horizon covers launch; least ceremony at ~35 endpoints |
| AD-02 | PostgreSQL single database | free, SQL power for metrics, one engine for facts + accounts |
| AD-03 | React 19 + Vite SPA, TanStack Query, Tailwind + shadcn/ui | EOD read-heavy app; a11y primitives; fastest solo/AI iteration |
| AD-04 | One backend process (API + workers) in one container | 6 cron jobs don't justify orchestration; simplest deploy + reproducibility |
| AD-05 | In-process cron workers + DB run ledger (no Hangfire/external cron) | auditability + retries with zero extra infra |
| AD-06 | Metrics Engine in `/src/Core` shared by API + workers | BR-MDF-009 single-definition discipline, mechanically enforced |
| AD-07 | Cookie auth (Identity) now; `/auth/token` documented extension | safest browser default; mobile delayed (D-01) needs brief amendment before build |
| AD-08 | DCF computed server-side (pure endpoint) | single canonical formula; same result for any future client; sub-100ms server compute + 300ms debounce ≈ instant UX |
| AD-09 | Drafts in zustand + sessionStorage; list refresh via query invalidation | satisfies UXR-G-021/024 + mutation matrix without a state framework |
| AD-10 | Market P/E = cap-weighted aggregate (Σcap/Σearnings, loss-makers excluded, disclosed) | standard index convention; user decision D-05; sector strip stays median-based per OQ-UX-002 |
| AD-11 | Hetzner VPS + Caddy + docker-compose + GHCR + Actions CI/CD | cheapest robust option; portfolio artifact; user decisions D-02/D-06 |
| AD-12 | Public monorepo incl. `.pipeline/` planning docs | transparency for portfolio audience; contains no secrets (verified 2026-10-06) |
| AD-13 | Builder admin dashboard extension (v1.1): `GET /admin/summary` (pipeline health + freshness + content coverage one-glance), `quarantined_facts` store + review/dismiss endpoints, `GET /admin/stats` (aggregate-only counts) | builder instruction 2026-10-06; every piece traces to existing items (UC-MDF-001b, UC-MDF-004, UC-MOV-005, FR-MDF-012, BR-MDF-007, SC-008/OBJ-005) — no new FRs; the quarantine store closes a v1.0 gap (UC-MDF-001 alternate b mandated quarantine but gave it no home). Rejected: per-user data browsing, traffic/event analytics, log-viewer UI, feature-flag UI — no BA trace and several conflict with the KVKK-minimal / no-tracking posture (BR-ACC-002/008); logs remain on-box via `docker logs`. **v1.2 follow-up:** SCR-012's UXR-MDF-020 (KAP source references in the description editor) is confirmed via an additive response sketch on `GET /admin/descriptions` (`03` §8; change record §13.1) |

## 12. Flagged items & deviations (require builder awareness; none block)

| ID | Item | Status |
|---|---|---|
| FLG-01 | **Mobile delayed, not excluded** — brief §6 lists native mobile apps as a *permanent* exclusion; builder decision D-01 (2026-10-06) delays mobile to later versions. V1 remains compliant (no native app, responsive web only). **Building** one later requires amending the brief first. | User decision recorded; brief amendment owed if/when pursued |
| FLG-02 | **SPA without SSR/SEO** — accepted trade-off (AD-03); if organic search becomes a goal, add prerendering later (small, contained change). | Accepted; veto-able |
| FLG-03 | **UX-lane additions adopted into architecture** — scenario rename/delete (UXR-VAL-017/018), verification gating (UXR-G-030), resend-verification (UXR-ACC-021), duplicate-name rejection (UXR-G-029), deletion confirmations (UXR-G-022): all have mechanisms above; the BA package still lacks FR-level traces for the first three — brief-delta lines recommended (UX audit §3). | Adopted; brief-delta owed |
| FLG-04 | **Domain pending** (Student Pack, ETA ≈2026-10-09) — e-mail flows (FR-ACC-008/009) blocked end-to-end until domain + Brevo sender verified; local dev uses MailPit meanwhile. | Tracked; not blocking architecture |
| FLG-05 | **Brief-delta D1–D6 not yet pasted into the brief.** | Builder action item |
| FLG-06 | **Source OQs open by design** (OQ-MDF-001/002/005, OQ-MOV-001/002, OQ-SCR-003, OQ-FDF-002) — accommodated via adapter pattern + honest states; resolved during V0 per the reports' owners. | No action for architecture |
| FLG-07 | **Single-instance, no HA** (99.0% monthly target, RPO 24h) — honest to the cheap-hosting constraint; documented in NFR translation. | Accepted by constraint |

## 13. Change propagation & change record

Amendments to these documents must list affected FRs/UCs/UXRs, TCs, and tickets for re-validation — the same rule the UX package follows (`99-ux-audit.md` §6). The change record (§13.1) tracks every version. As of v1.2 the test-planner and planner have not yet derived artifacts, so no downstream re-validation is pending — they must derive from **v1.2**.

### 13.1 Change record

| Version | Date | Change | Affected items | Downstream re-validation |
|---|---|---|---|---|
| v1.0 | 2026-10-06 | Initial architecture (submitted for approval) | — | — (first version) |
| v1.1 | 2026-10-06 | Builder-directed **admin dashboard extension** (AD-13): `GET /api/v1/admin/summary` (ops one-glance), `quarantined_facts` table + `GET /admin/quarantine` + `POST /admin/quarantine/{id}/dismiss` (gives UC-MDF-001 alternate b's "quarantine" a storage home — a v1.0 gap), `GET /admin/stats` (aggregate-only SC-008/OBJ-005 measurement) | FR-MDF-012 mechanism (components C3a+C3d+C2+C1); UC-MDF-001/004 + UC-MOV-005 coverage rows (`03` §10); data model (`02` §3.1, §2, §7); admin API (`03` §8, §9) | None pending — test-planner/planner not yet run; derive from v1.1 |
| v1.2 | 2026-10-06 | **Admin descriptions payload — UXR-MDF-020 confirmation** (UX audit `99-ux-audit.md` §8 flag ruling 3, builder-approved 2026-10-06): response sketch added for `GET /api/v1/admin/descriptions` (`03` §8) — each queue entry explicitly serves its KAP source references (`sourceRefs`, projected from `business_descriptions.source_refs_json` resolved against `kap_disclosures`) alongside the editable texts. Additive response documentation only (versioning rule `03` §11): no new endpoints, no request changes, no schema change — `02` untouched (`source_refs_json` exists since v1.0). UXR-MDF-015/028 (run-ledger auto-refresh / manual refresh) verified client-side only — no architectural change (`/admin` 60 req/min accommodates polling, `03` §12) | UC-RES-004 (step 2 — review); UXR-MDF-020 (Should) — payload mechanism; admin API `03` §8 | None pending — test-planner/planner not yet run; derive from v1.2 |
