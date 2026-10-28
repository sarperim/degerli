# System Architecture — v1.5

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** architect · **Date:** 2026-10-06 (v1.0 → v1.1 → v1.2 same day)
**Status:** submitted for builder approval — v1.1 is a builder-directed amendment (admin dashboard extension); v1.2 is a small additive amendment (admin descriptions payload serves KAP source refs — UXR-MDF-020); v1.3 (2026-10-07) applies the test-package final-review mirror patches (DCF `debt`-param clarification F-VAL-1, rights-issue factor Q3, window-suffixed CAGR codes Q5); **v1.4 (2026-10-15) applies the builder's post-board correctness feedback** (12 verified issues: statement versioning, unit/currency normalization, adjusted per-share CAGRs, `OCF`-based FCF, TMS 29 regime flag, `accounting_profile` + financial-sector metric posture, sector-aware DCF, source replacements, real-payload test fidelity); **v1.5 (2026-10-17) records the builder-directed real-source ingestion contract** (real İş Yatırım/KAP/EVDS envelopes become first-class production parse targets; first live pull bounded to XU030; charset-faithful source client — §10.9, AD-14); change records in §13.1
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
| **C3** | **Data Platform** (in-process workers in the API container) | **C3a Ingestion**: source adapters (KAP primary; İşbank candidate for prices; TÜİK/ENAG/CBRT/FX/gold macro; TEFAS funds), validation, quarantine, idempotent upserts, backfill mode. **v1.5:** production adapters parse the real source envelopes (İş Yatırım `HGDG_*` prices, KAP XBRL statements, EVDS macro) alongside the canonical source-double envelope, and the production prices fetch loops per-ticker over the configured index's constituents (XU030 at launch) — real-shape contracts in §10.9. **C3b Metrics Engine**: all canonical metrics (18 screener + display metrics), CAGRs with window indication, market snapshot (breadth, movers, market P/E, market div yield), sector medians, DCF baseline generation. **C3c Scheduler**: cron-triggered jobs (Cronos) with a DB run ledger; retries with backoff. **C3d Alerting**: builder e-mails + log alerts on failures/staleness. | Serve HTTP (runs inside C2's process but no endpoints of its own); nothing user-triggered except admin-triggered jobs. |
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
- **Crash evidence (v1.5.26, TKT-foundation-045):** the API host installs two **passive** managed hooks — `AppDomain.UnhandledException` (the last managed code that runs for an unhandled exception) and `TaskScheduler.UnobservedTaskException` (observation only; `SetObserved` is deliberately not called, so the exception policy is unchanged) — which hand the fault to `src/Api/Diagnostics/CrashEvidenceWriter`. It writes one **bounded** JSON artifact (`logs/crash-evidence/crash-<UTC>-<pid>-<kind>.json`, path configurable through `Diagnostics:CrashEvidence:*`) carrying the exception type/message/stack (clipped at 4 000 / 16 000 chars), the last `LogTailLines` (default 200) lines of the newest rolling log read through a bounded end-of-file read, the process envelope (pid, runtime/app version, environment, OS/arch, machine) and the **exit path** (terminating vs non-terminating), with every configured secret plus credential shapes (`password=`, `api_key=`, `Bearer …`, `Basic …`, and the JSON-quoted shapes the repo's own `CompactJsonFormatter` log lines produce — `{"Password":"…"}`, `"apiKey": "…"`) masked by the same posture the KAP/EVDS client failures already use. The writer **never throws** — a diagnostics failure must not mask the original fault or alter the process's exit path — and the hooks are inert on a healthy request path (no request-path behaviour change). Every boot logs any artifact a **previous run** left behind (`Previous-run crash evidence found: …`) and states the native-abort posture honestly: **a SIGABRT (exit 134), heap/GC abort or external kill cannot run managed hooks**, so that class is covered only by the runtime dump knobs (`DOTNET_DbgEnableMiniDump`) — whose absence the same line reports rather than implying coverage the host does not have.

### 10.4 Configuration
- `appsettings.json` for non-secrets (schedules, feature flags like metric visibility defaults); env overrides for secrets: `ConnectionStrings__Default`, `Smtp__*`, `Evren__ApiKey`, `APP_URL`, `ALERT_EMAIL`. VPS `.env` (git-ignored) + committed `.env.example`. Secrets never in repo or images (public repo constraint).
- **Ingestion source bound & pacing (v1.5, §10.9.2):** `Ingestion:Prices:UniverseIndex` (non-secret, `appsettings.json`; the shipped value was flipped **`XU030` → `XU100`** by TKT-foundation-025 once the XU100 membership seed made the flip valid — the code-level template default stays `XU030`; the flip is config-only, no code) names the index whose current constituents the production prices fetch loops over; `Ingestion:Prices:RequestDelayMs` (non-secret; default 500) paces the sequential per-ticker requests. Both extend the existing `Ingestion:Prices` options section (the same section the prices adapter already binds).

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

### 10.9 Real-source envelope contracts (v1.5 — builder-directed)

**Decision (builder, 2026-10-17):** "Main functionality requires real data" — real data from now on. Production ingestion parses the **real** source envelopes — İş Yatırım EOD prices, KAP XBRL financial statements, TCMB EVDS macro. The canonical `{sourceRef, …}` envelope the WireMock source doubles serve **stops being the only production shape**; it remains the L2 golden-value contract (§10.9.7). The scope is deliberately **C3a-only**: adapters still emit the canonical payload models the jobs already validate and persist, so C5 storage, C3b metrics and the C2 API are untouched, and per-row provenance (`03` §1.2 — İş Yatırım primary, Yahoo contingency) is unchanged: `source_ref` stays `isyatirim` / `kap` / `evds`.

**Provenance of this section:** the escalation review of PR #82 (`.pipeline/plan/reviews/TKT-foundation-015-round-1.md`, findings DR-01..03) verified that the product has no real-shape parsing and that working decode logic (HGDG field map, Turkish-locale number parsing, code-page charset decoding, KAP XBRL concept map) currently lives only in the test-fixture project on the unmerged branch `TKT-foundation-015` (`fixtures/Degerli.Fixtures/RealSourceCatalog.cs`). That logic is the raw material to be **lifted into `src/`**; this section is its design authority.

#### 10.9.1 Prices — the real İş Yatırım EOD envelope (FR-MDF-001, FR-MDF-010)

The real endpoint is **per-ticker** (one symbol + a date range per request). The body is `{"ok":true,"errorCode":null,…,"value":[{…rows…}]}` — rows under `value`, each row one instrument-date:

| Source field | Canonical field | Rule |
|---|---|---|
| `HGDG_HS_KODU` | `symbol` | BIST ticker, stored as-is |
| `HGDG_TARIH` | `date` | `dd-MM-yyyy` (source-locale date format, **not** ISO) |
| `HGDG_KAPANIS` → fallback `HG_KAPANIS` | `close` | the source emits both spellings; primary first, alternate as fallback |
| `HGDG_MAX` → fallback `HG_MAX` | `high` | |
| `HGDG_MIN` → fallback `HG_MIN` | `low` | |
| `HGDG_HACIM` → fallback `HG_HACIM` | `volume` | JSON number, **scientific-notation-capable** (`1.3239396512E10`): parse as decimal, store `bigint` (round half-away-from-zero). **Canonical semantic: TRY turnover (₺)** — FLG-09 resolution below |
| — (no source field) | `open` | **Honest gap: NULL.** The source provides only AOF-weighted fields (`HGDG_AOF` = volume-weighted average price); open is not derivable and is **not invented from AOF**. No canonical metric consumes open (`mcap`, movers, DCF price all read close); the schema already allows NULL. Yahoo-contingency rows may carry open — a mixed NULL/open history across sources is accepted and honest |

**Volume semantics (FLG-09 — resolved by builder decision 2026-10-17):** the stored `volume` semantic is **TRY turnover (₺)**, matching the real primary source. (a) İş Yatırım rows store `HGDG_HACIM` as-is (₺ turnover — corroborated by `DOLAR_HACIM` × `DD_DEGER` in the captured body). (b) Yahoo-contingency rows store **NULL volume** — an honest gap: Yahoo's field is share count, a different unit, and deriving ₺ from shares × close was **rejected as computing** (the same posture as `open` above). (c) The production movers-eligibility threshold (BR-MOV-011; `Ingestion:MarketSnapshot:VolumeEligibilityThreshold`) is **recalibrated from the observed BIST 30 volume distribution during the live-activation ticket** — BR-MOV-011 always deferred the threshold to "set from the observed volume distribution" (`02` §6.3), and that moment is the first live pull; it remains one config value, not hard-coded. (d) FR-MOV-021's displayed "daily traded volume" on mover entries is ₺ turnover (the dashboard volume block, FR-MOV-005, aggregates the same ₺ semantic). (e) **Test-planner note:** the FU synthetic volumes **reinterpret as ₺ turnover with unchanged numeric values** — under the existing 1,000,000 threshold the golden eligibility outcomes are unchanged (LAMDA at 500,000 stays excluded), so **no golden re-derivation**; only the unit label changes.

Transport fields: `ok:false` or a non-null `errorCode` is a **source failure** (fetch-exception class), not a parse event. The **backfill path** (FR-MDF-010) uses the same mapping — per-ticker historical range → dated rows; the job records the achieved depth (BR-MDF-006 — never fabricate beyond the source).

**Envelope detection** (in the prices adapter, all three first-class): (1) Yahoo `chart` envelope → documented contingency (`03` §1.2); (2) real İş Yatırım envelope (`value` array + `HGDG_HS_KODU` rows) → production primary; (3) canonical double envelope → L2 tests. Keeping (3) parseable by the same production adapter preserves the existing L2 golden path (goldens untouched).

#### 10.9.2 Per-ticker fetch and the universe bound (FR-MDF-001/006/007, FR-MDF-010)

- **The loop lives in the prices job; the adapter is symbol-scoped.** The adapter fetches and parses **one symbol's** payload for a date/range (envelope detection stays inside the adapter); the job resolves the configured index's current constituents (`index_constituents`, `effective_to IS NULL`) and iterates. Rationale: this is the existing seam doctrine (an adapter fetches and parses one payload shape for one source; persistence, validation and quarantine stay in the job); per-symbol failure isolation (a failed ticker quarantines/alerts without aborting the run; run-ledger stats per symbol) is only expressible job-side; pacing is orchestration policy, not source translation. **Rejected:** an adapter-internal universe loop — couples universe policy into source translation, makes pacing/universe choice untestable at the job seam, and backfill would duplicate the loop.
- **Universe bound:** the production fetch loops over the current constituents of the index named by **`Ingestion:Prices:UniverseIndex`** (§10.4). Launch default **`XU030`**; **`XU100` later = a config flip, not new code** (XU030 ⊂ XU100, so the instruments already exist; the flip requires XU100 membership rows to be present — a later seed/sync, still no code change). **Flipped 2026-10-18 (TKT-foundation-025):** the shipped `Ingestion:Prices:UniverseIndex` is now **`XU100`** — the XU100 covered-universe seed (§10.9.3, TKT-foundation-022) supplies the membership rows, so prices now cover all 100 XU100 members; the initial run backfills the ~70 newly-covered members through the existing per-ticker backfill path. The code-level template default stays `XU030` (§13.1 v1.5.4).
- **Politeness posture:** strictly **sequential** per-ticker requests with a small inter-request delay (`Ingestion:Prices:RequestDelayMs`, default 500 ms); **no parallelism** against the undocumented public endpoint. No hard rate limit is known — the conservative posture is a choice, not a measured constraint.
- Backfill reuses the same per-ticker loop (historical range per symbol).

**Completeness gate — a partly-published day is never reported as success (amendment 2026-10-24, `chore(prices)`, FR-MDF-001/011, NFR-MDF-001).** İş Yatırım publishes a session **progressively**: a symbol's row appears some time after the close, so the *same* symbol legitimately answers EMPTY at 17:36 and full OHLCV at 20:42. The per-ticker loop treated "the source returned no row for this date" as a silent no-op — `SymbolRunStat(sym, 0, 0, 0, failed: false)`, indistinguishable from "unchanged" — and the run still reported `succeeded`. The dev-stack evidence (2026-10-23, run 243): `written 18, unchanged 71, quarantined 0`, status `succeeded`, with **11 of the 100 XU100 constituents recorded as nothing at all**; 40 minutes later **9 of those 11** had a real session at the source. The chain then published a whole-market aggregate over 9 of 100 symbols — real numbers, false picture. Three rules close it, and **the endpoint is deliberately unchanged** (`Common/Data.aspx/HisseTekil` is the only source with close + volume + OHLC; İş's chart feed is price-only): (1) **the honest empty is recorded** — a valid payload with no row for the requested date yields the explicit per-symbol reason `NO_ROW_FOR_DATE` in the run stats (`stats_json.symbols[].reason`) and in the log, never a silent zero; (2) **a bounded coverage sweep** — after the first pass the job reads the day's coverage of the bound index (`DailyPriceStore.GetDayCoverageAsync`, i.e. `PriceDayCoverage`/`IsComplete`, the same read the §10.9.16 repair gate uses) and, while incomplete, re-sweeps **only the symbols that answered empty** (a quarantined symbol or a transport failure is already terminal for the run — re-fetching would multiply `CONFLICTING_VALUE` noise or paper over a source-down the retry ladder owns), paced at the existing posture and bounded by `Ingestion:Prices:CoverageMaxPasses` (default 3 = first pass + two re-sweeps), `Ingestion:Prices:CoverageBudget` (default 25 min, deliberately under the 90-min `MaxRunDuration` attempt bound — no re-sweep is *started* once it is spent; a settle already in flight and its pass still finish) and `Ingestion:Prices:CoverageSettleDelay` (default 0 — no artificial wait; the in-run sweep is **best-effort only**: a re-sweep targets only the few symbols that answered empty (≈ 11 of 100 in the evidence), so the whole completeness effort with the shipped defaults is ≈ 1–2 minutes against the ≈ 40-minute progressive-publication window recorded above. A day still being published when the run ends therefore stays `partial` with its uncovered names, and the §10.9.16 morning repair gate is the real catch-up; raise this option to space the re-sweeps further at the cost of one settle per pass on the chain's wall clock). A symbol that was empty on pass 1 and has data on pass 2 is **written**, never skipped as "unchanged"; (3) **honest terminal status** — only a fully covered day is `succeeded`; a day with uncovered constituents is `partial` with the missing names in the stats and the summary line, while `failed` keeps its transport-level meaning (the retry ladder's signal). A genuinely empty symbol raises **no** alert (it is not a source failure) — but it can never be reported as success. An all-empty day (nothing published for the date at all) is `partial` too, and the §10.9.16 morning repair gate re-runs the day from the store's truth. **Non-goal, recorded:** the snapshot/metrics lane gains no coverage guard here — a partially covered day still produces a snapshot, it is simply no longer *silent*; and the scheduler binding is untouched.

#### 10.9.3 XU030 membership launch-seed (FR-MDF-007)

- The recorded launch state of the XU030 bound is a checked-in **membership launch-seed**: `index_constituents` rows (append-only, effective-dated, `source_ref` recorded) for the XU030 members at launch — reusing the existing TC-MDF-062 launch-seed machinery (the universe launch-seed/membership path; `02` §3.1). XU030 membership rides on instruments the universe seed already creates.
- **No new FR:** FR-MDF-007 already covers effective-dated membership change sets — the seed is the *recorded launch state*, not a new capability. Instruments outside the fetch bound are handled by the existing recorded-gap machinery (FR-MDF-011, BR-MDF-007).
- The index code is pinned as **`XU030`** (the real BIST code; the `02` §3.1 example was mirrored to `XU030` the same day — FLG-08 resolved).

**Amendment 2026-10-18 — XU100 covered-universe launch-seed (TKT-foundation-022, FR-MDF-007, BR-MDF-005).** The same recorded-launch-state mechanism is applied to the **covered universe**: a checked-in, date-stamped `Xu100LaunchSeed` (a sibling of `Xu030LaunchSeed` in `src/Ingestion/Universe/`, same shape — `IndexCode`/`SourceRef`/`AsOfDate`/`Symbols`/`VerificationNote`/`ApplyAsync`; no generalized refactor of the XU030 seed) writes the current BIST 100 membership to `index_constituents` append-only through the same TC-MDF-062 machinery and records the shared pre-launch gap once.

- **Why (the defect this closes):** the read surface (`src/Api/Stocks/StockListEndpoints.cs`, FR-RES-001) scopes the covered universe to current XU100 membership, but the real İş Yatırım universe snapshot publishes **instrument rows only — no index-membership rows** (§10.9.8), so the snapshot-derived cold-start XU100 block degrades to "no seed" and nothing in the real chain ever writes a real XU100 membership row: real instruments stored, only the fixture universe served. This seed is the recorded launch state that closes the last mile.
- **Provenance (FR-MDF-008):** `source_ref = bist://xu100-launch` — the rows trace to BIST's published XU100 composition, not to an İş Yatırım feed (deliberately diverging from the XU030 seed's `isyatirim://xu030-launch`). The list is **never** derived from the snapshot's instrument set (which is broader than the BIST 100). `AsOfDate` mirrors `Xu030LaunchSeed.AsOfDate` so both seeds describe one launch and the XU030 ⊆ XU100 invariant is clean at one `effective_from`. The membership list is verified against BIST's published composition for the launch index period, with the dated `VerificationNote` recording exactly where (BIST's own page where reachable; otherwise dated third-party aggregators recorded honestly — the 020 precedent). Real-world correctness remains a live-pull observation, not CI-verifiable.
- **Wiring (the decision this pins):** `UniverseSyncJob` applies the seed as a sibling step to the snapshot-derived cold-start block, gated on **(1) real-source sync** (`fetched.SourceRef == UniverseSourceAdapter.IsYatirimSource` — the canonical L2 stub carries its own ref, so the checked-in real seed never fires in the stub world and canonical goldens stay untouched, §10.9.7), **(2) cold-start posture** (`EffectiveDate is null` — a snapshot publishing an effective-dated change set hands membership to the FR-MDF-007 change-detection path), and **(3) once-only by the seed's own provenance marker** (`UniverseSyncStore.HasSeedAsync`, keyed on `bist://xu100-launch`). **Deliberately NOT gated on `!HasOpenConstituentsAsync("XU100")`:** the FU/dev world legitimately pre-populates XU100 with its fixture members (FLG-08) and the real seed must still fire *alongside* them — overlapped members land as `AlreadyPresent`, never duplicated. Unknown seed symbols are logged + quarantined + counted, never dropped (NFR-MDF-003).
- **Out of scope:** the read path (`StockListEndpoints.cs` is *right* — the defect was missing data, not wrong code) and `PricesJob.cs` (the XU030 cold-start hook is unchanged; the XU100 seed hangs off the universe job). Ongoing membership maintenance remains the FR-MDF-007 change-detection path; live-endpoint constituent discovery was evaluated and **explicitly deferred** (recorded follow-up, not a landing criterion).

#### 10.9.4 Charset at the source client (all adapters)

- `ISourceClient` honors the response's declared `Content-Type; charset=`: **windows-1254 / ISO-8859-9** (Turkish code pages) decode via the .NET code-pages provider (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`), **registered once at product startup** — the registration currently lives in the fixtures project and moves into `src/` with this amendment. No declared (or unknown) charset → UTF-8, the current behavior.
- Rationale: real İş Yatırım/KAP artifacts arrive in Turkish code pages; plain `ReadAsStringAsync` assumes UTF-8 and would mojibake Turkish text. **Rejected:** per-adapter charset sniffing — duplicates logic per adapter; the client is the one seam every adapter reads through, so the fix belongs there.

#### 10.9.5 Statements — the KAP XBRL fact envelope (FR-MDF-002)

The real KAP financial-statement shape is a **fact list**: per company, each fact carries a `concept` (XBRL tag), a `value` (Turkish-locale number string), and period/statement/version coordinates (`fiscalYear`, `periodType`, `periodEndDate`, `statementType`, `version`, `publishedAt`), plus envelope-level `unitScale`/`currency` that feed the v1.4 normalization (`02` §1.8/§3.1).

- **Concept map:** source tags — including inconsistent IFRS vs Turkish-taxonomy spellings for the same line — collapse onto **one canonical chart-of-accounts item code** (`02` §6.1). Examples: `ifrs-full:Revenue` / `tr:Hasilat` / `kap:NetSatisTutari` → `REV`; `ifrs-full:CostOfSales` → `COGS`; `ifrs-full:GrossProfit` → `GROSS_PROFIT`; `ifrs-full:ProfitLoss` → `NI`; `ifrs-full:Equity` → `TOTAL_EQUITY`; `ifrs-full:NumberOfShares` → `SHARES_DILUTED`; `ifrs-full:CashAndCashEquivalents` → `CASH`. Unmapped concepts pass through as `OTHER_*` (recorded, ignored by metrics — honest gap, `02` §6.1). The map is lifted from the fixtures into the product as one checked-in, single-place mapping.
- **Turkish-locale numbers:** values like `1.234.567,89` (`.` thousands separator, `,` decimal) parse **culture-free** (the host builds under `InvariantGlobalization`); the parser tolerates accounting parentheses, en-dash minus and a trailing `%` (observed in the wild).
- **Fact grouping:** facts group by (periodType, periodEndDate, statementType, version) into statements; the store assigns the integer version sequence per v1.4 (`02` §1.5).
- **Duplicate concepts — never silent latest-wins:** within one group, the same canonical item code appearing twice with **equal** values collapses (idempotent); with **unequal** values it is a validation event → `quarantined_facts` with both values retained (`CONFLICTING_VALUE` class) — never a silent overwrite. (This deliberately overturns the fixtures' latest-wins accumulator — DR-05; the fixture code is raw material, not contract.)
- `source_ref` stays **`kap`**.

#### 10.9.6 Macro — EVDS envelope verification (FR-MOV-014)

- A **bounded real EVDS payload capture is in scope of the follow-up wave**: the real response shape is verified against `MacroSourceAdapter` (which today parses its own `EvdsMacroPayload` canonical envelope) and the adapter is **hardened to parse the real shape**; the canonical envelope remains the L2 golden shape. The five-official-series set, the bounded 90-day value-date lookback and the API-key redaction posture are unchanged (existing adapter contract).
- `source_ref` stays **`evds`**.

#### 10.9.7 Test-double posture (mirror note for the test-planner)

- The FU canonical envelope remains the **L2 golden contract**: golden values untouched, still derived by hand from the synthetic fixture universe (FU §1) — **never** from real captures. The L2 doubles keep serving it through the symbol-scoped seam (goldens and asserted DB outcomes unchanged).
- A **real-source catalog** of captured raw bodies — charset-faithful (raw bytes + declared charset), **honestly labeled captured-vs-constructed** — is the **recorded exception to FU §1** ("no real symbols/values") and the **parsing-fidelity lane**: real envelopes assert parse shape/fidelity against the product adapters, never derived numbers. Redistribution terms are recorded with the catalog per RISK-MDF-001 (bounded, non-commercial, parsing-fidelity only).
- **Labeling rule:** hand-constructed bodies must never be labeled "captured verbatim" (the DR-03 doc-accuracy sub-item). Every catalog entry states its provenance.
- The catalog and decoders originate on branch `TKT-foundation-015` (PR #82, closed unmerged). The follow-up wave is: product-side lift (decoders, concept map, charset-provider registration into `src/`) + test-side mirror (FU §1/§10 exception recorded, a home for the TC-MDF-063..068 parsing cases, an EVDS capture case). Architecture records the posture here; the testing docs mirror it — **test-planner action**.

#### 10.9.8 Universe / index-levels — the real İş Yatırım envelopes (FR-MDF-006/007, FR-MDF-008)

**Discovery (live, read-only, 2026-10-18).** The `universe-sync` job's placeholder paths (`/universe`, `/index-levels`) are replaced by two real İş Yatırım resources on the same `www.isyatirim.com.tr` host as the verified prices endpoint. Both were probed and verified live, anonymously (no browser headers, no form digest). The 2026-10-17 probe's `Data.aspx/HisseEndeksler` → **401** was a red herring: the `Data.aspx` family returns 401 for an *unknown* `Data.aspx/<method>`, while a real method (`HisseTekil`) answers anonymously.

- **Universe resource — `POST …/_layouts/15/IsYatirim.Website/StockInfo/CompanyInfoAjax.aspx/GetHissePerformans`.** A legacy ASP.NET AJAX handler, **POST-only** (a GET mis-routes), so it does not ride the `ISourceClient` GET seam; the seam gains `PostJsonAsync` (a default interface implementation that throws `NotSupportedException`, so GET-only test doubles still satisfy the seam). Request body (JSON): `{"endeksKodu":"09","sektorKodu":"","exchange":""}` — `endeksKodu 09` = Ulusal-Tüm (every listed national instrument; ~630 rows / ~808 KB live, verified 2026-10-18).
- **Universe envelope shape:** a string-wrapped JSON payload `{"d":"{\"Sector\":null,\"Oran\":[ {…row…}, … ]}"}`. Each `Oran` row maps to one instrument: `HISSE_KODU` → `symbol` (stored as-is); `HISSE_TANIM` → `name` (the source pads it with trailing whitespace — **trimmed**; `listingDate` is an **honest NULL** — no source field exists); `AS_ALT_SEKTOR_KODU` → sector code, falling back to `AS_ALT_SEKTOR_TANIMI` (the sub-sector's Turkish label) because the source publishes no code today; `AS_SEKTOR_KODU` → parent code (2-level hierarchy, usually absent). The distinct sub-sector labels are reshaped into canonical `UniverseSector` reference rows — a faithful denormalization of the per-row classification the source already states, never an invented value. The resource is a full snapshot: **no** effective-dated change set and **no** `indices` reference rows.
- **Index-levels resource — `GET …/_Layouts/15/IsYatirim.Website/Common/ChartData.aspx/IndexHistoricalAll?period=1440&from=<yyyyMMddHHmmss>&to=<yyyyMMddHHmmss>&endeks=<code>`.** Returns `{"data":[[<epochMillis>,<close>],…],"timestamp":"…"}`; each pair is one trading day's index close. The chart timestamps are **midnight Europe/Istanbul**, so the calendar date is the epoch converted to **UTC+3** (Türkiye has observed permanent UTC+3 since 2016 — not DST-dependent). The body carries no index code, so the requested `endeks` value is stamped onto every fact. Verified live 2026-10-18 (nine XU030 closes, 2026-10-06..2026-10-16); **TKT-foundation-025** adds a matching nine-close **XU100** capture (same window, honestly labeled `isyatirim-index-levels-xu100`) to the real-source catalog, proving the requested code is stamped onto the decoded facts.
- **Config (`Ingestion:Universe`, §10.4):** `BaseUrl` = `https://www.isyatirim.com.tr`; `Method` = `POST`; `Path` = the `GetHissePerformans` route; `RequestBody` = the Ulusal-Tüm body; `IndexLevelsPath` = the templated `IndexHistoricalAll` route (`{from}`/`{to}`/`{index}`); `IndexLevelsIndices` = the per-index set the sync loops over (production default **`[XU100, XU030]`** — FR-MDF-006's "including BIST 100 and BIST 30"; the pre-025 single-value key `IndexLevelsIndex` is **removed** — no shipped config used it and the set replaces it; the code-level default is applied when the set is unconfigured, and a non-templated `IndexLevelsPath` — the canonical L2 double — is fetched once rather than per index); `IndexLevelsLookbackDays` = 30. The source client is a **keyed** `ISourceClient` (`UniverseSourceOptions.ClientKey`), separate from the prices/TEFAS/macro clients; its base address falls back `Universe:BaseUrl` → `Prices:BaseUrl` → `http://localhost`, so the canonical L2 harness composes over its own stubbed paths unchanged. `Method` **defaults to GET** only so bare L2 compositions (which serve the canonical double over GET) keep parsing; production sets `POST` explicitly.
- **Envelope detection** (both adapters, shape-based, never by config): the real envelopes above are decoded by `UniverseRealEnvelopeDecoder`; the canonical `sourceRef`/`instruments`/`levels` double (the L2 golden shape, §10.9.7) stays first-class. Every real payload is stamped `isyatirim` (FR-MDF-008).
- **Index-levels degradation (FR-MDF-006):** the index-levels resource is a *secondary* endpoint — its live unavailability must not abort the instrument/membership sync that already ran. **Extended to the per-index set (TKT-foundation-025):** the sync loops the configured indices (production `[XU100, XU030]`) sequentially; a failure on **one** index (fetch failure or unparseable payload) is **degraded per index** — the failing index is quarantined (`SOURCE_FETCH_FAILED`) and counted, one alert is raised, the run reaches `Partial`, and the **other index's levels are still stored**; a failure never aborts the loop, the instrument/membership sync, or the run. The launch-blocking chain (instruments → membership → prices) is never hostage to this second endpoint.

#### 10.9.9 Dev-stack data posture (TKT-foundation-023)

**Gap (user-reported 2026-10-18, live check of the running dev stack).** The dev stock list
served 116 instruments = 100 real + 16 fixture: the dev database was bootstrapped with the FU
fixture seed (`fixtures/`, `FixtureSeedEntry` — the demo bootstrap used before the real sources
worked), and the real XU100 launch-seed (022) then added its membership alongside the FU rows.
Both worlds legitimately coexist in `instruments`/`index_constituents` — the 022 wiring
deliberately lands FU XU100 members and the real `bist://xu100-launch` seed as `AlreadyPresent`
(§10.9.3) — so this is a **data condition, not a code defect**: `StockListEndpoints` (FR-RES-001)
serves exactly the covered universe it is given; the mixing is a property of *what was seeded*,
not of *what is served*.

**Decision (option A — the documented dev-reset path).** The real-only dev stack is a **fresh
database built by the real chain only**: `docker compose -f compose.dev.yml down -v` (or volume
removal) → `up` → `run --rm migrate` → admin-trigger `universe-sync`
(`POST /api/v1/admin/ingest/{job}/run`, as the seeded builder account) → prices backfill
(`backfillFrom`). The migration step runs only the *application* seed (`02` §8 — metric catalog,
macro series, builder account; deliberately not fixture data). The fixture seed stays exactly
what it is: the L2/L4 CI world (compose.ci) and an **opt-in** dev/demo bootstrap, documented as
such — never run against a database intended to browse real data. The README "Dev-stack data"
section documents both paths end to end (exact commands), and `FixtureSeedEntry.RunAsync` emits
an explicit warning line whenever it seeds — a log line, never a behavior change (exit code,
idempotency and the L4 contract are untouched).

**Rejected — fixture-exclusion switch.** A read-surface or job filter that hides FU rows. A code
path existing only to undo a seed: `StockListEndpoints` is *right* (the covered-universe scoping
is FR-RES-001/BR-MDF-005), and the mixing is a condition of what was seeded, not of what is
served. Hiding rows would also put a fixture-shaped branch on the production read path.

**Rejected — dev keeps both, only production is real.** This leaves the mixed 116-stock list as
the default dev experience — the exact gap reported — and lets fixture-anchored derived data (the
T-anchored market snapshot) masquerade as real on the browsing surface. Production is unaffected
either way (`compose.prod.yml` never runs the fixtures entrypoint; only `compose.ci.yml` does),
so there is no cost to making the dev default real-only.

**Live-pull observation (not CI-verifiable).** The reset path is verified by running it against a
live source: after `universe-sync` + prices backfill the stock list serves exactly the XU100
membership (no FU symbols), and `GET /api/v1/stocks/search?q=ASELS` finds `ASELS`. Price/statement
coverage on detail pages is bounded by what has landed (the prices backfill covers the configured
index — XU030 at launch — and KAP statements are activated separately); the honest no-data states
on not-yet-covered real stocks are FR-RES-016 working as pinned.

#### 10.9.10 Statements — company-scoped fetch and the statements bound (TKT-foundation-024, FR-MDF-002/008/010/011/012)

> **Superseded by replacement (orchestrator record, TKT-foundation-026, merged PR #99, 2026-10-18):**
> this section's *blocked live-discovery block* (the `www.kap.org.tr` website-proxy candidate, §13.1
> v1.5.3) is **superseded by the MKK VYK API source contract in §10.9.11** — the company-scoped loop,
> per-company isolation and the keyed `IKapSourceClient` are reused unchanged; only the **source** is
> rewired. TKT-foundation-024's open live criterion is **closed by replacement** (recorded in 024's
> ticket; no attempt fabricated).

**Why (the defect this closes).** The §10.9.5 KAP XBRL fact envelope has decoded in the product
since TKT-foundation-017 and `StatementsJob` persists versioned statements idempotently, but no
`Ingestion:Kap` / `Ingestion:Statements` sections existed in `src/Api/appsettings.json`: the KAP
base address fell back to `http://localhost` and the statements path to the placeholder
`/statements`, so a production `statements` run could never fetch a real payload and
`financial_statements` stayed empty for real instruments — starving every downstream consumer
(metrics-recompute → FR-RES-006/008/009/010/011, valuation/DCF, FR-MOV-006 market P/E).

**The statements resource is per-company.** The §10.9.5 envelope carries one `companyCode` per
payload, so unlike the prices/index-levels resources there is no multi-symbol list body; the fetch
seam is one company at a time. §10.9.2's seam doctrine therefore applies **verbatim** to
companies:

- **Company-scoped adapter; loop in the job.** `StatementsSourceAdapter.FetchCompanyAsync(symbol)`
  fetches and parses **one company's** payload; the `{symbol}` token in the configured path
  template is substituted in place (a template without the token gets the symbol appended as a
  `symbol` query parameter, so the fixed canonical L2 double route still resolves). Envelope
  detection stays inside the adapter — the real KAP XBRL fact envelope (§10.9.5) and the canonical
  `statements` double are both first-class. Persistence, validation and quarantine stay job-side,
  which is what makes per-company failure isolation and pacing expressible.
- **Identity guard (FR-MDF-008).** The canonical double is a single-company body; a payload whose
  company identity does not match the requested symbol is narrowed to an **empty statement set**
  (real envelope: skipped before decode by `companyCode`; canonical: `FilterBySymbol`), so a bound
  member can never be written under another company's identity or have another company's facts
  quarantined.
- **Universe bound:** the loop resolves the current constituents of the index named by
  **`Ingestion:Statements:UniverseIndex`** (`index_constituents`, `effective_to IS NULL`). Launch
  default **`XU030`**; **`XU100` later = a config flip, not new code**. This is its **own key,
  deliberately independent of `Ingestion:Prices:UniverseIndex`** (AD-14) so the two bounds flip
  separately as their pacing/coverage is verified.
- **Politeness posture:** strictly **sequential** per-company requests with a small inter-request
  delay (**`Ingestion:Statements:RequestDelayMs`**, default 500 ms); **no parallelism** against the
  public endpoint — identical to §10.9.2.
- **Per-company failure isolation and status.** A transport failure is a **source-down, not a
  parse/validation event**: the company is marked failed, **no quarantine row** is written, exactly
  one aggregate alert names the failed companies, and the loop continues. A per-company parse
  failure (`SourcePayloadException`) quarantines (`UNPARSEABLE_PAYLOAD`) and continues. Terminal
  status: `Failed` when every current constituent failed to fetch or parse (the retry ladder
  applies); `Partial` when some failed or some facts quarantined; otherwise `Succeeded`. The
  run-ledger row carries the per-company breakdown (`ingest_runs.stats_json.symbols`).
- **Empty bound.** Under the launch default bound (`XU030`), an empty resolution is a real
  cold-start/misconfiguration gap and is reported `Failed` (never a silent healthy success); a
  differently-named bound resolving empty degrades to `Succeeded` (its instruments may not exist
  yet). Identical posture to the prices job (§10.9.2).
- **Backfill (FR-MDF-010, FR-MDF-011).** Backfill reuses the same per-company loop and is a
  **nominal** distinction at this seam: the loop does not branch on `request.IsBackfill` because the
  per-company KAP resource returns the periods the source holds regardless, so an incremental and a
  backfill run perform identical per-company work. The achieved depth is what the source returned,
  and the per-instrument statements coverage depth is derived from the stored statements by the
  existing `CoverageRecorder` no-silent-gaps reconcile — never re-invented or fabricated beyond the
  source.

**Config (`Ingestion:Kap` / `Ingestion:Statements`, §10.4).** Shipped in `src/Api/appsettings.json`:
`Ingestion:Kap:BaseUrl = https://www.kap.org.tr`; `Ingestion:Statements = { Path, UniverseIndex =
XU030, RequestDelayMs = 500 }`.

**Live discovery — honestly blocked (2026-10-18, read-only probes; attempt record per the
018–023 honesty posture).** KAP's host `www.kap.org.tr` is reachable (HTTP 200) but has been
redesigned onto a Next.js front end; the former backend host `kapsitebackend.mkk.com.tr` no longer
resolves. Probing the candidate per-company route
`GET https://www.kap.org.tr/tr/api/company/items/{r}/{a}` returned **HTTP 500** for every parameter
combination tried and every other probed route returned **404**; no browser tooling was available
from this environment to reverse the front end's data calls. **No real KAP capture was obtained, so
none was fabricated** and no `RealSourceCatalog.cs` entry was added. The shipped
`Ingestion:Statements:Path` value is a **discovered-but-unverified candidate**: a live run against it
yields per-company `SOURCE_FETCH_FAILED` (honest `Partial`/`Failed`, zero fabricated rows, no
statements written) until the concrete route is re-verified live. **Live verification of the real
per-company resource is the orchestrator's post-merge criterion** (the 021 precedent), not a CI gate.

**Test-double posture (§10.9.7).** No golden value changed. The company-scoped seam needs the L2
harness to bind a **test-local single-member index** (`StatementBound`, index code `STMT`) carrying
exactly the symbol(s) each existing statements test serves, plus `RequestDelayMs = 0`; that
harness *binding* delta is the only permitted change to the existing statements-path goldens
(TC-MDF-065/067/071 and the versioning/normalization/accounting-profile lanes re-run green
unchanged). The new loop tests reuse the TC-MDF-072/073/074 lane shapes for companies.

#### 10.9.11 The KAP VYK API source contract — company mapping, list/detail envelopes, cursor and pacing (TKT-foundation-026, FR-MDF-002/008/010/011/012)

**Why (the defect this closes).** §10.9.10 wired the company-scoped statements loop but shipped a
**discovered-but-unverified** `Ingestion:Statements:Path` against `www.kap.org.tr`: KAP's Next.js
redesign had killed the former per-company route, every probe returned 500/404, and no real capture
could be obtained — so the loop could never fetch real financial statements. **The blocker was
resolved the proper way: MKK's official "KAP Data Dissemination Services" REST API (the "KAP VYK
API", an Apinizer gateway), with a free-plan API key obtained by the user.** This subsection records
the new source contract and **supersedes §10.9.10's blocked live-discovery block** (the loop, the
per-company isolation and the keyed `IKapSourceClient` are reused unchanged; only the **source** is
rewired).

**Gateway and auth (live-verified 2026-10-18).** `Ingestion:Kap:BaseUrl` targets the VYK gateway.
The **dev** gateway `https://apigwdev.mkk.com.tr/api/vyk` is spec-verified (live-reachable) and is
what ships; the **production** host (`apigw.mkk.com.tr`) did not resolve from this environment, so
pinning it is a **live-verification step recorded in the PR — never an assumed/verified guess** (the
018–025 honesty posture; the candidate is blocked until a live run pins it). Auth was **pinned
live**: an HTTP **Basic** credential, attached to the KAP `HttpClient` as a request header (never in
the URL) **iff** `Ingestion:Kap:ApiKey` (env `Ingestion__Kap__ApiKey`) or its paired
`Ingestion:Kap:ApiSecret` (env `Ingestion__Kap__ApiSecret`) is non-empty — the API **Key** is the
Basic *username*, the API **Secret** the *password* (the gateway returns HTTP 200 only for the pair;
a lone key, an `X-API-KEY` header and a raw `Authorization` value all 401). The key/secret are never
committed (`appsettings.json` ships empty defaults, mirroring `Ingestion:Macro:EvdsApiKey`) and every
configured secret is redacted from any message that could echo it (`KapSourceOptions.Redact`,
`KapHttpSourceClient`).

**Company mapping (structural pin).** The loop is company-scoped by BIST symbol; the gateway
identifies companies by `companyId`. One paced `GET /members` (Şirket Listesi) per run builds an
in-memory symbol→`companyId` map (`MemberInfo.stockCode` carries the ticker directly) reused for the
whole run (`VykCompanyDirectory`, lazy + cached). A bound symbol with no mapping is **marked failed
with the named reason `COMPANY_IDENTITY_UNRESOLVED`** — no rows written, no fabricated mapping,
counted in the run-ledger per-company stats, loop continues (FR-MDF-008/012). A `/members` transport
failure degrades honestly: the snapshot is `Failed`, every company is marked
`SOURCE_FETCH_FAILED`, one aggregate alert, no fabricated mapping.

**List + detail envelopes (live-verified 2026-10-18).** Statements are disclosures of class
**`FR`** (Finansal Raporlar): `GET /disclosures?disclosureIndex={cursor}&disclosureClass=FR&companyId={id}`
returns the `DisclosureInfoResponse` list; each new disclosure is fetched via
`GET /disclosureDetail/{disclosureIndex}?fileType=data` → `DisclosureByIdResponse`. **The gateway
does not return the OpenAPI's declared `flatData`** for `fileType=data` (live-verified: that member
is absent, and `fileType=html` returns `htmlMessages` instead) — the structured payload is the
XBRL-style **`presentation`** array, one entry per sub-report: `content.ContextList.Context[]` binds
context ids to periods (`Period.instant`, or `Period.startDate`/`endDate`) under keys
`CURR`/`PREV`/`CURR3`/`PREV3`, and `content.ReportItem` is a tree whose leaves carry a **bare IFRS
taxonomy `name`** and a `Values.Value[]` list of `{contextId, currency, rounding, value}`. The
`presentation` tree becomes a **first-class real envelope** alongside the §10.9.5 XBRL fact envelope
and the canonical double (envelope detection in the adapter): fiscal `year`/`period` map to the group
coordinates, each sub-report's root report-item name selects the statement type (`BalanceSheet`→`BS`,
`ProfitOrLoss`→`IS`, `CashFlows`→`CF`, `ChangesInEquity`→`EQ`), each leaf's **`CURR`** value collapses
onto `02` §6.1 through the shared `KapXbrlStatementDecoder.ConceptMap` (extended with the bare-name
spellings)/`StatementMapper` path, and the grouping + duplicate-concept rule carry over verbatim;
provenance stays `kap`. **The identity guard adapts:** a listed disclosure whose `companyId` does not
match the requested company is **skipped before decode** (empty statement set, no detail fetch, never
another company's facts quarantined). **Bounded live capture:** the shape is pinned by the
`RealSourceCatalog` `kap-vyk-disclosure-detail` entry (disclosure 1226255, live 2026-10-18, trimmed to
a representative real-report-item subset with real CURR/PREV values) — **no capture fabricated**; the
CI member/list doubles are shape-faithful constructed bodies. **Honest scope notes:** `consolidation`
(a detail field with no `02` §3.1 home) is **not persisted**; the attachment-file layer
(`/downloadAttachment/{id}` PDF/XBRL) is a **separate follow-up ticket** — never silently scoped in
(no PDF/XBRL-file parsing here).

**Pacing + throttle (structural pin).** The free plan allows ~6 calls/min, so the VYK source is
paced at ~10 s per call: the shipped `Ingestion:Statements:RequestDelayMs` flips **500 → 10000** and
**every** gateway call in the run (members, anchor, list, detail) is paced by a scoped
`KapRequestPacer` (on top of the loop's per-company pace, §10.9.10). An HTTP 429, or a
`VykErrorResponse`/Apinizer throttle fault, is a **retryable transport failure** through the
existing retry ladder with backoff — a source-down, not a parse event: no quarantine row, the
company marked failed, one aggregate alert, loop continues; a parse failure of a real VYK body
quarantines `UNPARSEABLE_PAYLOAD` and continues.

**Incremental cursor (structural pin).** The per-company incremental cursor is the **last-processed
`disclosureIndex`**, persisted in the **existing `coverage_metadata` table** (Scope `instrument`, a
`kap-disclosure-cursor` data type, the value in `Notes`) — **no schema migration**. The data type is
deliberately **outside `CoverageDataTypes.InstrumentScoped`**, so the no-silent-gaps reconcile never
clobbers the cursor row. A cursor-less company starts from the configurable
`Ingestion:Statements:EarlyDisclosureIndex` (the first live pass **is** the backfill — the achieved
depth is what the source returned, recorded honestly per FR-MDF-010/011); a re-run resumes from the
persisted cursor and writes nothing new (idempotent). `GET /lastDisclosureIndex` is the run's global
anchor (secondary; its unavailability does not fail the directory).

**Config (`Ingestion:Kap` / `Ingestion:Statements`, §10.4).** Shipped in `src/Api/appsettings.json`:
`Ingestion:Kap = { BaseUrl = https://apigwdev.mkk.com.tr/api/vyk, ApiKey = "", ApiSecret = "",
MembersPath = /members, LastDisclosureIndexPath = /lastDisclosureIndex }`;
`Ingestion:Statements = { Path = /disclosures, DetailPath = /disclosureDetail/{disclosureIndex},
DisclosureClass = FR, EarlyDisclosureIndex = 0, UniverseIndex = XU030, RequestDelayMs = 10000 }`.
`compose.dev.yml` passes `Ingestion__Kap__ApiKey` through to the api service; `.env.example`
documents the slot.

**Test-double posture (§10.9.7).** No golden changed. The VYK seam is exercised through the L2
harness's own stub routes (members/list/detail), with only the permitted deltas: the harness
*bindings* for the new routes, the shipped config values above (the 024 config-wiring test's two
shipped-value assertions amended), and new honestly-labeled `RealSourceCatalog` entries
(`kap-vyk-members`/`kap-vyk-disclosures` constructed, `kap-vyk-disclosure-detail` a bounded live
capture) for the TC-MDF-063 lane. Every existing statements-path golden (TC-MDF-065/067/071 + the
versioning/normalization/accounting-profile lanes) re-runs green unchanged.

**URI composition — the prefix-preserving base+path join (TKT-foundation-032, structural pin).**
The gateway base carries a **path prefix** (`https://apigwdev.mkk.com.tr/api/vyk`) and ships
**without** a trailing slash, while every request path option carries a **leading** slash
(`/members`, `/lastDisclosureIndex`, `/disclosures`, `/disclosureDetail/{disclosureIndex}`). .NET
`HttpClient` URI resolution treats a leading-slash relative URI as **root-relative**, so the
naive join silently drops `/api/vyk` and every real-gateway request answers **404** — the shipped
configuration was broken for any real deployment (live-verified 2026-10-19/20: the unprefixed URL
404s, the prefixed URL `…/api/vyk/members` returns 200 with the same HTTP Basic creds). **The join
is owned by the client seam, not by config discipline:** `KapHttpSourceClient` (a) normalizes its
base address to end with a directory separator and (b) trims the request path's leading slashes
before resolution (`KapUri.WithDirectorySeparator` / `KapUri.ToRelativePath`, shared with the
`IKapSourceClient` registration in `IngestionServiceCollectionExtensions`), so **both halves are
required** and any spelling — base with/without trailing slash × path with/without leading slash —
resolves under the configured prefix. The shipped `appsettings.json` values stay **unchanged**
(no trailing-slash config requirement); redaction and Basic-auth behavior are untouched (header,
never URL). **Test-double doctrine:** a root-bound double **cannot** catch this class — root-
relative and base-relative resolution coincide at the server root. Any double for a
prefix-carrying gateway **must bind under that prefix** (stubs at `/api/vyk/…`, base configured
with no trailing slash and the shipped leading-slash paths); `KapUriResolutionTests` holds the
regression plus the spelling matrix, and the root-bound VYK/canonical doubles stay byte-identical.

**Share counts — `GET /memberSecurities` and the `shares` lane (TKT-foundation-058, new v1.5.36; supersedes the TKT-foundation-033 goal).** Share counts were never in the disclosure XBRL layer (033's blocker, disproven 2026-10-22 on both legs: there is no separate production endpoint — `apigw.mkk.com.tr` 302-redirects to the test gateway — and the counts are not in `FR` disclosures at all). They live on **the gateway we already ingest from**: `GET /memberSecurities` (Şirket Menkul Kıymet Bilgileri on the VYK gateway, `Ingestion:Kap:MemberSecuritiesPath`, default `/memberSecurities`) returns **one flat body of every member's security lines** (live-probed 2026-10-22: 671 members / 1,062 lines), so it joins this section's seams unchanged: the shared keyed `IKapSourceClient`, HTTP Basic on the KAP `HttpClient`, `KapUri` prefix-preserving join, and a scoped `KapRequestPacer` on **every** call (paced by `Ingestion:Statements:RequestDelayMs`, shipped `10000` ms). Per line: `isin`, `isinDesc`, `borsaKodu`, `takasKodu`, `tertipGroup`, `capital`, `currentCapital`, `groupCode`, `groupCodeDesc`, `borsadaIslemeAcik` (the trading-open flag — the 2026-10-22 capture carried JSON booleans only; the decoder also tolerates the numeric `0`/`1` and the string `"true"/"false"/"1"/"0"` spellings, §10.9.5's honesty posture).
- **The lane (`src/Ingestion/Shares/`, job code `shares`).** One paced fetch per run (the 046 `/members` snapshot precedent — a whole-archive snapshot, never a per-instrument loop), decoded, then written to the **`share_counts`** store (`02` §3.1: `instrument_id` PK, `paid_in_capital numeric(18,4)`, `recorded_at`, `source_ref` — one row per instrument, **input-aware** in place per the 042/050 doctrine: an unchanged figure writes nothing and does not move `recorded_at`, a changed figure is repaired in place and counted). `source_ref = kap`. Admin-triggerable; **not** in the EOD or morning chain (share capital changes rarely — scheduled cadence is a later decision, recorded not scoped). **The column was renamed from the lying `shares_outstanding` by TKT-foundation-086 (v1.5.60) in a forward-only migration** — the 556 currency rows keep their values under an honest name; the entity property is `ShareCount.PaidInCapital`.
- **Matching + aggregation; the unit is resolved to money (USER DECISION D4, 2026-10-26; corrected by TKT-foundation-086, v1.5.60).** Lines match our instruments by **`borsaKodu` primary / `isin` fallback**, keyed on at least one **trading-open** line (`borsadaIslemeAcik`) — a closed-only member seeds no figure. `paid_in_capital` is the **sum of the member's `currentCapital` across all its matching security lines** (tertip aggregation A+B): the live shapes evidence it (ASELS A 2,421,818,181.816 closed + B 2,138,181,818.184 open = **4.56B**; THYAO C 0.01 + A 1,379,999,999.99 = **1.38B**; ISDMR A 20 + B 2,899,999,980 both open = **2.9B**; TUPRS = 1,926,795,598; GARAN 4.2B; AEFES 592,105,263). **The unit question the v1.5.36 text left open is now closed, and the answer is money:** 64 of 556 live rows carry **sub-unit precision**, which no share count can, so the value is **paid-in capital in TRY** — D4 verbatim: *"no `sharesOutstanding` claim from that lane."* The 065/081 evidence confirms the reading from the other side: the captured equity statement's monetary `Ödenmiş Sermaye` column (4,560,000 thousand TL) is the same figure as ASELS's 4.56B, and no committed capture carries an explicit share-count row. No fabricated division, no zeros, no nominal divisor.
- **Honest absence (031/052 doctrine).** An instrument with **no** security line (the 5 archive-absent members — GLRMK, PAHOL, TRALT, TRENJ, TRMET) or only closed lines gets **no row**, never a zero row; and a line missing `currentCapital` **on one of our matched instruments** is **skipped and counted**, with its **whole matched group withheld** (a partial tertip sum would be a fabricated figure) — a re-run that withholds a group **removes** any previously stored row for it, so a stale figure is never left being served; an instrument merely **absent** from a snapshot keeps its last stored row (last known wins — 042/050's "a stored row the current run no longer computes is left alone"). The skip count rides the run's `stats_json` and the run stays `succeeded` (a counted gap, not a permanently-lying freshness signal). Transport failure → `failed` run, no rows; a non-memberSecurities body → `UNPARSEABLE_PAYLOAD`.
- **The honest share count — the price-lane derivation (TKT-foundation-086, new v1.5.60; USER DECISION D5, 2026-10-26).** The count the public surface serves is derived from the **price lane's own payload**: İş Yatırım's daily EOD row carries `PD` (market capitalisation, TL) beside `HGDG_KAPANIS` (close), and a per-instrument row's count is `PD ÷ close`. The committed captures pin the arithmetic exactly — ASELS `1,714,560,000,000 ÷ 376 = 4,560,000,000` (its 07-10/08-10 rows `1,590,300,000,000 ÷ 348.75` agree), THYAO `400,890,000,000 ÷ 290.5 = 1,380,000,000`, GARAN `539,700,000,000 ÷ 128.5 = 4,200,000,000` — while GARAN's 06-10/07-10 rows (`4,199,999,902.306534 / 4,199,999,802.657694`), FROTO and EBEBK are **non-integral**, so the captures themselves exercise both outcomes. The store is the additive **`price_derived_share_counts`** (one row per instrument, PK `instrument_id`; `shares_outstanding bigint`, **both operands** `market_cap_raw`/`close_raw numeric(28,8)`, `price_date` = the fact's as-of, `recorded_at`, `source_ref = isyatirim`); storing the operands is what makes the arithmetic **auditable from the row** (FR-MDF-008).
- **The derivation rule (the honesty core).** A row is written **only** when `PD` is present and positive, the close is positive, and `PD ÷ close` is integral within the store's pinned tolerance (`0.0001`). A missing/non-positive `PD` is the named, counted skip **`PD_MISSING`**, a non-positive close **`CLOSE_INVALID`**, and a non-integral quotient **`NON_INTEGRAL_QUOTIENT`** — withheld and counted, **never rounded** into a guess (GARAN serves its 08-10 count or nothing, never 4,199,999,902), and the count is **never zero**. No nominal divisor and no `/memberSecurities` division exists in this lane — the 065 Option-B path stays forbidden. The row is input-aware (identical operands → `unchanged`, `recorded_at` unmoved; changed operands repair in place and are counted), **monotonic** (an older dated fact never regresses a newer row — counted as `stale`), and a withheld fact leaves any stored row alone (its own as-of is what makes a stale count honest). The prices job calls the store with the facts it **already fetched** — no new fetch, no new source, zero additional requests (`PD` rides the decoded fact additively; an envelope without it — the `HG_*` fallback shape, the canonical double, Yahoo — carries none: honest absence, never a zero).
- **Precedence (pinned, recorded for the day a statements decoder evidences a count).** A **statement-reported** share count (`SHARES_DILUTED`) **supersedes** the derived one; the derived lane supplies only where the statement is silent; both absent → the fact stays in `missingInputs` and **nothing is divided by a guess**. The DCF baseline job encodes exactly this order and records which source supplied the fact (`canonicalFactRefs.share_count.source = "statement" | "price-derived"`) with **that source's own as-of** (the statement's build date, or the derived row's `price_date`) — never the build date dressed up as the derived count's as-of. No statement decoder emits a count today (0 `SHARES_DILUTED` rows anywhere), so the derived lane is the live path.
- **Serving seam (corrected by TKT-foundation-086, v1.5.60; consumers are their own tickets).** `GET /api/v1/stocks/{symbol}/balance-sheet`'s `shareCount` block is now **two named facts, two units, two as-ofs** (`03` §3): **`paidInCapital`** (TRY — the renamed store's figure) with `paidInCapitalAsOf`, and **`sharesOutstanding`** (the derived count) with `sharesOutstandingAsOf` (the derived row's `price_date`). Each is honest `null` (with a `null` as-of) when its store has no row — a dash downstream, never `0`; the old ambiguous single `asOf` is gone. The statement-reported `lines.sharesOutstanding`/`lines.sharesDiluted` and BVPS stay **untouched** (the market-data facts are distinct fields, never a rewrite of the statement's own figures), and `lines.sharesOutstanding` keeps **count semantics** in the web surface (the 072 "paid-in capital" label revert, 065 consequence 2). **DCF baselines are no longer a pinned seam: the `share_count` fact consumes the derived count** (precedence above), which is what unblocks `fairValuePerShare`; per-share metrics (`MetricsEngine`) and multiples/market-P/E (`MarketSnapshotEngine`, BR-MOV-010's exclusion doctrine) remain **untouched consumers of their own tickets** — a count existing does not change their exclusion.
- **Config + test posture.** `Ingestion:Kap:MemberSecuritiesPath` defaults to `/memberSecurities` in code (no `appsettings.json` change in this ticket). No golden changed: the L2 double is `CannedSourceCatalog.MemberSecuritiesOk`, a **committed real-shape double derived from the 2026-10-22 live capture** (the ephemeral `/tmp` snapshot is never referenced) carrying the real multi-tertip/trading-flag shapes plus two **explicitly labeled derived anomalies** (no-`currentCapital` lines — the capture happened to have none, the contract requires the skip path exercised); every existing Kap/statement golden re-runs green unchanged.

#### 10.9.12 The TCMB EVDS 3.0 source contract — anonymous catalog + `/fe` values (TKT-foundation-027, FR-MOV-014)

**Why (the defect this closes).** §10.9.6 verified the macro envelope against the old **EVDS v2**
REST API (`evds2.tcmb.gov.tr/service/evds`) with the `TP.*` series identifiers it served. That host
now **blanket-redirects (302) to `evds3.tcmb.gov.tr`**, the v2 REST paths return an SPA shell / 404,
and the v2 adapter could never fetch a live value. This section **supersedes §10.9.6's live-source
wiring by replacement** — the macro adapter, jobs, store, freshness/alerting and the `evds`
provenance are reused unchanged; only the **source transport** and its series-code discovery are
rewired to the new platform. The owner-supplied `.env` key is **not** required for the new endpoint.

**The `/fe` values endpoint is anonymous (live-verified 2026-10-18).** A plain
`POST https://evds3.tcmb.gov.tr/igmevdsms-dis/fe` with `Content-Type: application/json` and the body
below returns **HTTP 200 + real data with no cookie, no key, no login** (re-verified at
implementation: `TP.DK.USD.S.YTL` 49.42 / `TP.DK.EUR.S.YTL` 55.41 on `16-10-2026`). The earlier 400s
were purely a wrong body shape, not an auth failure. **No credential travels in the v3 request**
(the `EvdsApiKey` setting is retained only as an optional redaction surface and for the legacy
canonical-double GET; empty default; never committed). The standing SEC-1 query-string-leak follow-up
therefore does **not** apply to the v3 lane: there is no key on the wire at all.

**Discovery (anonymous GETs).** Series codes are resolved, never assumed:
`GET /igmevdsms-dis/categories/withDatagroups/type=json` (the full catalog — confirms each target
datagroup still exists; 154 categories / 680+ datagroups) then
`GET /igmevdsms-dis/serieList/fe/type=json&code=<datagroup>` (resolves `SERIE_CODE` +
`DEFAULT_AGG_METHOD`). A datagroup or series absent from a live discovery response is **skipped**
(the job marks the series LATE; the last-known value keeps serving), never guessed.

**The request body (verbatim from the site's own `getSeriVerileri` + an owner-captured cURL).**
`series`/`aggregationTypes`/`formulas` are `-`-joined per selected series; dates are `dd-MM-yyyy`;
`frequency:"2"` = business day; `dateFormat:"0"`/`yon:"0"`/`sira:"0"` are numeric codes;
`ozelFormuller:[]` is an array:

```json
{"type":"json","series":"TP.DK.USD.S.YTL-TP.DK.EUR.S.YTL","aggregationTypes":"avg-avg",
 "formulas":"0-0","startDate":"15-10-2026","endDate":"16-10-2026","frequency":"2",
 "decimalSeperator":".","decimal":"2","dateFormat":"0","lang":"tr","yon":"0","sira":"0",
 "ozelFormuller":[],"groupSeperator":true,"isRaporSayfasi":false}
```

**The response shape.** `{"totalCount":N,"items":[{"Tarih":"dd-MM-yyyy","<CODE_underscored>":"<value>",
…,"UNIXTIME":{"$numberLong":"<epoch>"}}],"transposedItems":[…],"seriesNames":{…}}`. Two honest
gotchas the decoder handles: **(a)** column keys underscore the dotted identifier
(`TP.DK.USD.S.YTL` → `TP_DK_USD_S_YTL`), decoded by normalising `_`→`.`, so **both** spellings resolve
through the union map; **(b)** values are **strings carrying thousands-grouping commas** regardless
of `decimalSeperator` (`"6,622,000.00"`), so grouping is stripped before the invariant parse. Monthly
rows (`yyyy-MM`, CPI + policy rate) resolve to the month's first day.

**Series set (union mapping, not replacement — §10.9.7).** The five official series map to the
live-verified v3 identifiers, **alongside** the fixture-era identifiers the L2 canonical double keeps.
The **datagroup + serie pair must both match** the live serieList: `TP.ALTINPIYASA.KAP03` is
published by `bie_altinbistbul` (BIST gold, `İŞ GÜNÜ`, `last`), **not** by `bie_mkaltytl` — the
latter carries only the monthly Ankara Chamber of Jewelers series (`TP.MK.CUM/KUL/LON/RES.YTL`) and
publishes no `TP.ALTINPIYASA.*` identifier at all (live-verified 2026-10-19).

| Canonical | v3 identifier (live) | Datagroup | Aggregation |
|---|---|---|---|
| `USD_TRY` | `TP.DK.USD.S.YTL` (selling) | `bie_dkdovytl` | avg |
| `EUR_TRY` | `TP.DK.EUR.S.YTL` (selling) | `bie_dkdovytl` | avg |
| `GOLD` | `TP.ALTINPIYASA.KAP03` (USD/oz — matches the seeded unit) | `bie_altinbistbul` | last |
| `CBRT_REPO` | `TP.BISPOLFAIZ.TUR` | `bie_bispolfaiz` | last |
| `TUIK_CPI` | `TP.TUKFIY2025.GENEL` (index, 2025=100) → **computed YoY** | `bie_tukfiy2025` | avg |

`INDEP_CPI` stays admin-entered (ENAG is not on EVDS). Brent (`TP.BRENTPETROL.EUBP`) carries no V1
series, so it is not requested. **Code-pin resolutions recorded here (never guessed):**
**(1) CPI YoY** — the platform publishes the CPI only as an **index** (the `bie_tukfiy2025` datagroup
has 349 monthly index series and no published annual-change series), so the adapter fetches the index
over a window **derived from the output bound** — `output start − 12 months`
(`date − (90 days + 12 months)` ≈ 15 months, not a fixed day count) — and emits the canonical
**`YoY = (indexₘ/indexₘ₋₁₂ − 1) × 100`** for months inside the standard 90-day output window
(`EvdsCpiYoY`). Every emitted month's year-prior baseline is therefore inside the fetch by
construction; a fixed 400-day window did not reach it and emitted zero `TUIK_CPI` rows on a real run
(R2-F4). A deterministic derivation from two real index observations, never an invented value. **(2) `CBRT_REPO`** — resolved from the catalog's
"Merkez Bankaları Politika Faiz Oranı" datagroup as `TP.BISPOLFAIZ.TUR` (the plan's candidate list
did not name it; confirmed via the datagroup's serieList, not assumed).

**Two transports, one adapter.** When the additive `CatalogPath`/`SerieListPath` are configured
(`MacroSourceOptions.UsesV3Discovery`) the adapter drives v3 discovery + `POST /fe`; the FX/gold/repo
series ride the bounded **90-day value-date window** (DR-2) and the CPI index the derived wider window
above. **One values request per cadence, never a mixed one:** the platform collapses a response to the
*lowest common* frequency of the body's series (live-verified 2026-10-19 — adding the monthly repo to
the daily FX/gold request returned **4 monthly rows** instead of **65 business-day rows**, and the
decoder's `yyyy-MM` support would have stored monthly averages as daily observations), so the adapter
issues **three** requests — business-day (USD/EUR/GOLD), monthly (`CBRT_REPO`), and the CPI index —
and merges their decoded rows into one payload.
The **code-level defaults keep discovery OFF** (empty paths) so the **L2 golden lane** — the legacy
single-GET canonical `sourceRef`/`series` double — is unchanged; the shipped `appsettings.json`
enables discovery. Persistence, validation, `source_ref = evds`, the per-job cadence mapping and the
API-key redaction posture are all untouched.

**Config (`Ingestion:Macro`, §10.4).** Shipped in `src/Api/appsettings.json`:
`Ingestion:Macro = { BaseUrl = https://evds3.tcmb.gov.tr, Path = /igmevdsms-dis/fe,
CatalogPath = /igmevdsms-dis/categories/withDatagroups/type=json,
SerieListPath = /igmevdsms-dis/serieList/fe/type=json&code={code}, EvdsApiKey = "" }`.
`compose.dev.yml` passes the optional `Ingestion__Macro__EvdsApiKey` through to the api service
(empty default; never baked into an image); `.env.example` documents the v3 BaseUrl/Path + the
additive discovery routes and the now-optional key.

**Test-double posture (§10.9.7).** No golden changed. `RealSourceCatalog` gains four honestly-labeled
entries: `evds-v3-values` (constructed, POST), `evds-v3-capture` (a bounded **live capture**, POST),
`evds-v3-catalog` and `evds-v3-serie-list` (constructed, GET) — **no capture fabricated**. The
canonical double re-runs green through the same production adapter (legacy lane); the FU fixture
world keeps its `TP.*` codes via the union map.

**Freshness consumer note — per-series, publication-aware staleness (TKT-foundation-038, `02` §5.6).**
`MacroFreshnessService` is the *only* freshness consumer of this ingest (`GET /market/macro` and the
admin summary both read it), and its rule is **per series**, not per job: the v3 lane lands
`macro-cpi` **`partial`** honestly on every run (`INDEP_CPI` is admin-entered, ENAG is not on EVDS),
so a job-scoped "latest failure after the latest `succeeded` run" flag was true forever and marked
every monthly series `Bayat` while it served its latest published value (user-reported 2026-10-20;
the live ledger was 14 `failed` + 2 honest `partial` + zero `succeeded`). The amended semantics:
**(1)** failure-stale only when the owning job's latest `failed` run is newer than *that series' own*
latest `recorded_at` **and** newer than the job's latest `succeeded` run — stored data is the evidence
its ingest happened, so a `partial` run that wrote the series refreshes its clock, while a failure
that wrote nothing leaves it old; `INDEP_CPI` has no owning job and is never failure-stale.
**(2)** a `monthly` series' age window is measured from its **published value date** (`asOf`), stale
when `today > asOf + 65` days — the single documented constant
`MacroFreshnessService.MonthlyPublicationWindowDays`, immune to the monthly rows being re-recorded on
every run — while `daily` keeps its 1-day `recorded_at` tolerance and `per_release` keeps no window.
**(3)** a series with no value is `unavailable`, never `stale`, and the strip envelope aggregates
available series only. The source transport, the `evds` provenance and the jobs are untouched: this is
a serving-semantics fix, not an ingest change.

#### 10.9.13 Market snapshot — latest-stored-trading-day target selection (TKT-foundation-028, FR-MOV-001..006/010, NFR-MOV-001, BR-MOV-001/009; consumer-note amendment TKT-foundation-037, v1.5.13)

**Why (the defect this closes).** The `market-snapshot` job computed only for `request.Date`
(UTC today — `AdminOpsEndpoints`'s `target = DateOnly.FromDateTime(clock.GetUtcNow())`, no override;
the scheduler's EOD stream is trading-calendar gated and never fires on a weekend). On a
non-trading day the job honestly found no prices and wrote nothing, so `market_snapshots` stayed
empty even though the DB already holds real complete price history (the 025 XU100 backfill): the
weekend overview could not show Friday. At the time the read path served the latest snapshot row
with a weekday rule (`stale = SnapshotDate < LatestTradingDayOnOrBefore(today)`) — a rule the
v1.5.13 consumer-note amendment below later replaced with the same stored-prices resolution
(TKT-foundation-037); the gap this section closes was that no snapshot for the latest completed
trading day had ever been produced.

**Target-day selection = stored prices are the truth.** The day the formulas run over is the
**latest trading day ≤ `request.Date` that has stored prices for the covered universe** — the
maximum `daily_prices.price_date` ≤ `request.Date` over the **current XU100 membership**
(`index_constituents`, `effective_to IS NULL`), the same covered-universe filter
`MarketSnapshotEngine` uses. **Never** a weekday/holiday assumption: the `LatestTradingDayOnOrBefore`
helpers elsewhere are pure weekday-backing and `TradingCalendar.Holidays` ships empty, so a
calendar rule would fabricate a day the market never traded. Stored facts are the only authority.

- **Weekend/cold-start self-healing.** A Saturday/Sunday request resolves the preceding Friday, so
  the weekend overview serves the real Friday snapshot (`asOf` = Friday, `stale: false`); a missed
  EOD run or a cold start resolves the last day that actually has prices.
- **Trading-day behaviour unchanged.** When `request.Date` is itself a trading day with prices, the
  resolution is the requested day — identical to the pre-028 behaviour (row lands on that day).
- **Idempotency (a completed day is never *blindly* rewritten).** When the resolved trading day
  already has a `market_snapshots` row the job **recomputes it from the current facts and writes
  input-aware** (TKT-foundation-042, v1.5.18): identical values → the row is left untouched and the
  run reports an honest `unchanged: 1` (the pre-042 silent `written: 0, unchanged: 0` was itself a
  counting lie — the day *was* checked); different values (index levels or prices landing after the
  first compute) → the row is **overwritten in place** — still exactly one row per trading day
  (TC-MOV-005) — and the repair is counted in the run ledger. This is the **operational repair
  path**: a broken row (e.g. one computed before the day's index levels landed) is repaired by a
  plain re-run, never by SQL `DELETE`+re-insert. Days other than the resolved one are never
  addressed by the store at all, so **historical rows stay immutable**. Rejected alternative
  (recorded): an admin-only forced delete+recompute — it keeps SQL surgery as the only repair path.
- **Honest empty rule unchanged.** No stored price ≤ `request.Date` for the covered universe → the
  job writes no row (`Succeeded`, 0 written) and the overview keeps serving `state: unavailable`
  until a real day exists — no fabricated row.
- **No config flag.** The behaviour is simply "latest stored trading day"; an `appsettings` flag is
  rejected. The `prices` job is deliberately untouched (the snapshot needs only the latest trading
  day's prices, which already exist).

**Serving contract unchanged.** `GET /market/overview` (`03` §3) still serves the latest row + the
`stale` flag; this amendment changes only which day the pipeline materializes. An explicit
`request.Date` override (the admin trigger) is now interpreted as "on or before" rather than
"exactly".

**The input-aware recompute, extended to the derived-fact lanes (TKT-foundation-050, v1.5.34).**
The doctrine above is not snapshot-specific; it governs every derived-fact write. `metrics-recompute`
(`derived_metrics`, FR-MDF-013/014) and `medians` (`sector_metric_medians`, FR-RES-007) recompute the
target day from the current facts on every run and write input-aware at the engine/job boundary
(`MetricsEngine.PlanWriteAsync` / `SectorMedianEngine.PlanWriteAsync`): a computed row already equal
to its stored row is **not written** and is reported as an honest `unchanged` count — its
`computed_at` does not move; a row whose upstream facts changed (a restatement, a corrected line, a
late dividend for the metrics lane; a corrected upstream metric for the medians lane) is repaired
**in place** and counted as a rewrite, so a repair is a plain re-run rather than SQL surgery. Value
comparison happens in the stored column's own precision (`numeric(18,6)`) and never includes
`computed_at`, so an identical recompute of a high-precision value cannot masquerade as a perpetual
repair. Exactly one row per `(instrument, metric, as_of_date)` / `(sector, metric, as_of_date)` is
preserved (FR-MDF-015, NFR-MDF-002), rows of other dates are never addressed (historical days stay
immutable), and a stored row the current run no longer computes is left alone. The pre-050 write path
was a blind in-place overwrite: it reported the honest-looking `unchanged` count for rows it rewrote
and `written: 0` for a genuine repair — the same counting lie 042 removed from the snapshot lane. The
repair count is carried as the plan's `Rewritten` counter (the store-level precedent 042 shipped); the
distinct run-ledger `rewritten` stat remains TKT-foundation-044's `Jobs.cs` fold-in.

**Definition versioning — the formula-provenance stamp (TKT-foundation-070, v1.5.48).** The
input-aware recompute above answers *"did the facts change?"*; it could not answer *"which
definition produced this number?"*. `CanonicalMetrics` is the single definition of every metric
(BR-MDF-009), so editing it silently changed what the next recompute meant, and a formula change was
indistinguishable in the ledger and in the rows from a fact restatement — a same-day re-run rewrote
rows and called them an input repair, `computed_at` moved on write (never a definition stamp), and no
historical row could state the definition behind its value.

- **Single source of truth.** `CanonicalMetrics.Version` (an `int` constant, `/src/Core`) is the
  definition version. It is **bumped in the same change as any formula, input-assembly or
  not-meaningful-rule change** — there is no second place to update and no per-metric version: one
  definition, one number (BR-MDF-009).
- **Per-row stamp.** `derived_metrics.formula_version` and `sector_metric_medians.formula_version`
  (`int NULL`, `02` §3.1) hold the version that produced the row. The **stores** stamp it —
  `DerivedMetricStore.UpsertAsync` / `SectorMedianStore.UpsertAsync`, on the insert path *and* the
  repair path — because the stores are the only writers of those tables (the engines classify and
  return; they never insert).
- **Recompute rule.** A stored row whose stamp differs from `CanonicalMetrics.Version` is
  **rewritten even when its value is identical**, and the plan reports it distinctly
  (`MetricWritePlan.FormulaVersionRewritten` / `SectorMedianWritePlan.FormulaVersionRewritten`: the
  subset of `Rewritten` whose *values* were identical and only the definition moved). A row whose
  value also changed is a fact repair that additionally restamps. Same inputs **and** same version
  remain `unchanged` — the idempotency of v1.5.34 is preserved, not weakened.
- **Honest absence, never a guess.** `NULL` means "produced before versioning existed". The
  migration is additive and writes **no** row: every row present at deploy time was produced by the
  then-current definition (in production, exactly the shipped one), but no per-row record of which
  definition wrote a value exists, so a back-filled stamp would be a fabricated provenance claim.
  The first recompute of a day closes the gap from evidence (values unchanged, stamp written);
  historical days keep their `NULL` until someone actually recomputes them — no row is silently
  reinterpreted (`02` §8). A stored row for a metric the current definition no longer produces is
  never a repair target, so it keeps its `NULL` even after that day is recomputed.
- **Trigger provenance.** Who asked for a run is recorded too: `ingest_runs.stats_json.trigger` reads
  `scheduled` for a chain run (the scheduler is the only supplier of `IngestionRunContext`) and
  `admin` for the operator trigger (which calls the runner's bare overload). Additive and optional —
  `IngestResult`, jobs and counts are untouched by it — so an operator can tell a scheduled recompute
  from an admin-triggered one after the fact.
- **Exposure, recorded honestly.** The version is **DB-only**: no API/audit surface serves it, so a
  value's definition is queried (`SELECT formula_version …`) rather than read from a payload; the
  frozen `03` payload shapes are unchanged.
- **Recorded gap (flagged, not hidden).** The distinct `FormulaVersionRewritten` count is a plan-level
  (in-process) figure. It is **not** a separate `ingest_runs.stats_json` key, because the two job
  classes (`MetricsRecomputeJob`, `SectorMediansJob`) were outside this ticket's audited scope; the
  ledger's `written` carries the total repairs exactly as 044 established. Surfacing it as a ledger
  key is a small follow-up for the lane that owns those jobs.

**Consumer-note amendment (TKT-foundation-037, v1.5.13).** The doctrine reaches the **serve** side
too: the overview's display-freshness flag is no longer weekday-based. It was
`stale = snapshot.SnapshotDate < LatestTradingDayOnOrBefore(today)` — a pure weekday-backing helper —
so on any trading day before that day's EOD ingest (20:30 Istanbul) the previous session's row
(the freshest data that exists) was flagged `stale: true` ("Bayat"). The read path now resolves
`stale = snapshot.SnapshotDate < (latest stored trading day ≤ today with covered-universe prices)`
— the same `MarketSnapshotStore.ResolveLatestStoredTradingDayAsync` the job uses — so the flag is
true **only when a newer completed session's prices exist in the store and the served row lacks
them** (a genuinely missed `market-snapshot` run; the 028 self-healing gap). Tuesday before the EOD
→ Monday is the latest stored day → fresh; a completed EOD whose snapshot run failed → `stale: true`
(honest). The private weekday helper is removed from `MarketEndpoints`; the endpoint injects the
store (`Scoped`, already registered by `AddMarketDataIngestion`). No payload-shape change (`03` §3),
no schema change (`02` §3.2). The badge *copy* ("en güncel tamamlanmış seans" / EOD nature) is the
separate UX lane (TKT-foundation-039) — this amendment makes the flag truthful, not self-explanatory.

**Holiday extension — the calendar is data, and a holiday reads like a weekend (TKT-foundation-048, v1.5.44).**
The 037 rule above resolved the served row's freshness against the "latest stored trading day", but
"trading day" was still the **weekday** assumption: `ConfiguredTradingCalendar` shipped with
`TradingCalendar.Holidays: []` and `LatestTradingDayOnOrBefore` helpers only skipped Saturday/Sunday.
So a BIST holiday weekday was treated as a session that had produced no prices — the conservative
failure the doctrine accepts only when it is *unavoidable*. **(1) The calendar is configuration, not a
live source** (planner pin 1, rejected alternative recorded): `Ingestion:TradingCalendar:Holidays`
carries the published per-year closure dates as ISO `yyyy-MM-dd`, seeded from the official Borsa
İstanbul "Resmi Tatil Günleri" Pay Piyasası tables — 2025 in full and the 2026 closures still ahead of
the seeding date (2026-10-24, i.e. 2026-10-29). Only **full-closure weekdays** are seeded: a closure
falling on a Saturday/Sunday is already non-trading by the weekend rule, and an *arefe* (half day) is a
trading session, never a holiday. **(2) The rule is `weekend ∪ configured holiday`** (pin 2):
`IIngestionCalendar` gains `IsHoliday` and `LatestTradingDayOnOrBefore`, the latter walking the same
set back — a holiday Monday resolves to the preceding Friday, a closure plateau is walked past
entirely rather than half-entered, and the walk is bounded (a full year) so an unusable calendar fails
loudly instead of inventing a date. **(3) The serve side gates the stored candidate.** The overview
(`MarketEndpoints`) may no longer take the maximum stored `price_date` at face value: a row dated on a
non-trading date — a source that answers a closed date with the previous close, or a calendar-day
backfill — is **not a session**, so the date is skipped and `MarketSnapshotStore` is **re-asked below
it**. The answer stays a genuinely stored session (never fabricated), and a genuinely newer *trading*
session still proves the served row is behind: the calendar only discredits non-trading dates, so a
missed `market-snapshot` run stays `stale: true`. The store itself is untouched — the gating happens at
the in-scope caller and the store keeps its "stored prices are the truth" contract. **(4) Gated streams
explain themselves** (pin 3): a trading-day-gated stream — the EOD chain, the morning chain, and now
any per-job schedule that opts in with `Ingestion:Scheduler:JobSchedules[].TradingDaysOnly` (the
shipped `intraday-quotes` entry is `true`; the macro/fund streams stay ungated) — runs **no job** on a
configured holiday and writes **one** short run-ledger row per stream per holiday
(`job_code: "holiday-skip"`, `stats_json { gate: "trading-calendar", gateOutcome: "skipped", stream,
holiday }`, `succeeded`, `started_at = finished_at`); that is one row **per process lifetime** — the
dedupe marker is in memory, so a restart mid-holiday may re-record the same holiday (one extra row,
never a silent gap). The row is deliberately not a registered job code
(a `succeeded` row under a real code would let a freshness query mistake "the market was closed" for
"the job ran today"), it is written **instead of** an attempt — so no step timer is armed and no
`RUN_HUNG` can be declared (064's bounds are untouched and additive), and a weekend occurrence stays
silent as before: a Sunday needs no explanation, a holiday is what the calendar has to explain. The
amended `TC-MDF-011` holiday case is that change of contract, recorded rather than relaxed.
**(5) Honest limits, stated:** the list is static, so a newly declared or differently observed closure
is a configuration change, and until it lands that day reads `stale` — the conservative direction the
doctrine already accepts (a missed holiday shows *older* data as stale, never newer data as fresh);
past 2026 closures are not re-seeded because the seeding rule is "2025 + remaining 2026", which only
affects backfill/morning run-date resolution for already-elapsed dates; the calendar honours whatever
list is configured, so the weekend-silence contract assumes the seeding discipline of full-closure
weekdays only — a weekend-dated entry copied in from the raw table would count as a configured holiday
and write a `holiday-skip` row on a weekend (the config discipline is "seed weekdays only", and the
shipped-list test pins it); and the stock page's
`StockOverviewAndValuationEndpoints.FreshnessAsync` keeps its pre-048 weekday rule (`asOf <
weekday-today`), an independent semantic pinned by its own test lane and outside this ticket's
acceptance criteria — flagged, not silently changed.

#### 10.9.14 Market snapshot — price-derived vs share-derived aggregate decoupling (TKT-foundation-031, FR-MOV-001..006/021, NFR-MOV-001, BR-MOV-009/010/011)

**Why (the defect this closes).** `MarketSnapshotEngine` required a balance-sheet
`SHARES_DILUTED > 0` for **every** covered-universe constituent (`continue` guard) before it
entered the snapshot at all. The real dev/launch world holds complete prices and both index
levels but **no share-count facts yet**, so `constituents.Count == 0` → `ComputeAsync` returned
null → no `market_snapshots` row → the front page served `state: unavailable` even though
breadth, movers, total volume and both index moves were fully computable from the stored prices
alone. Dropping a member for a missing *share* fact silently discarded a *price* fact the
snapshot owns — the two were coupled by an implementation guard, not by a formula.

**The doctrine: the two blocks are decoupled.** A constituent **without a usable
`SHARES_DILUTED` (null or ≤ 0)** is no longer dropped. It carries **no market cap** and splits
by block:

| Block | Requirement (FR) | No-share member | Zero-shares world |
|---|---|---|---|
| Index levels + daily change | FR-MOV-001 | — (index facts, member-independent) | real levels served |
| Breadth (advancing/declining/unchanged) | FR-MOV-003 | **included** | real breadth |
| Top movers + volume eligibility | FR-MOV-004/021, BR-MOV-011 | **included** (volume/change are price facts) | real movers |
| Total traded volume | FR-MOV-005 | **included** | real total |
| Market P/E (Σmcap/ΣNI) | FR-MOV-006, BR-MOV-010, D-05 | **excluded and counted** | `null` |
| Market dividend yield (ΣDIV_TTM/Σmcap) | FR-MOV-006 | **excluded** (DPS × shares is not computable) | `null` |
| Cap-weighted sector performance | FR-MOV-002 | **excluded from the weights** | `{}` |

- **`market_pe_excluded_count` counts EVERY constituent not entering the P/E sum** — loss-makers,
  missing income **and** no-share members. The exclusion is reported, never silently dropped
  (BR-MOV-010 extended to no-share members).
- **Honest empty states, never fabricated readings.** `marketPe`/`marketDivYield` are `null` when
  no constituent qualifies; a sector with **no share-bearing member is omitted from the
  dictionary** (an explicit `0.0` would be a fabricated reading, and `{}` is the honest state).
  No substitute equal-weight / price-only sector formula is invented here — a UX-spec decision if
  ever wanted (the F-MOV-1 precedent).
- **The engine-level null return survives only for its original meanings:** no XU100 membership /
  no covered universe, no stored prices for the covered universe on the resolved day (and no
  member with a previous close to measure a change against). Missing share counts are **not**
  such a reason — the row is written and the overview serves `state: available` whenever the
  price-derived blocks are computable.
- **FU fixture world byte-identical.** Every FU constituent has a share count, so every branch
  above is a no-op there: TC-MOV-001..005 goldens, excluded counts, sector weights and the P/E
  value are unchanged. Only the no-shares / partial-shares real-world path is new.
- **Inputs unchanged (BR-MOV-009).** The snapshot still reads the canonical MDF facts only; no
  external source call, so no pacing constraint applies. `02` §3.2 needs no change — the
  snapshot stays one row per trading day and the P/E / dividend-yield columns were already
  nullable.

**Honest index-level empty state (TKT-foundation-042 extension, v1.5.18).** The **same honesty
class** applies to the index block, which was the last place a missing input still produced a
fabricated reading. `MarketSnapshotEngine.ReadIndexMovesAsync` fell back to `new IndexMove(0m, 0m)`
when the resolved day had no `index_levels` row yet (live dev-stack evidence 2026-10-20: the first
Tuesday snapshot, computed before `universe-sync` landed the day's closes, wrote
`xu100_level = 0.0000 / xu30_level = 0.0000` and the overview **served** those zeros — the UI would
have rendered 0,00). The doctrine now:

- **A missing index level is NULL, never 0.0000.** The engine returns a null move for an index with
  no stored level on the resolved day; `MarketSnapshotResult.Xu100Level/Xu100ChangePct/Xu30Level/
  Xu30ChangePct` are nullable and `02` §3.2's columns are nullable (they always were — no migration
  is required by this extension; existing rows keep their values).
- **The block is served only when it is honestly complete.** `GET /market/overview` serves
  `indices: null` when either index level is NULL (`03` §3 already contracted `indices: {...} |
  null`), which the Web renders as its existing honest Unavailable block — no screen change.
- **A missing index level never takes the row down**: breadth, movers, volume, P/E and sector
  performance are price-derived and keep serving `state: available`.
- **The repair path is §10.9.13's**: when the day's levels land later, the next `market-snapshot`
  run recomputes and repairs the row in place (the pre-042 no-op guard left the zeros in place and
  made SQL `DELETE` the only cure). FU fixture world unchanged (both levels always stored).

**Serving contract unchanged.** `03` §3's `GET /market/overview` payload shape is untouched
(`marketPe`, `marketDivYield` and `marketPeExcludedCount` nulls were already contracted as the
honest unavailable state). UI: no change — `MarketOverviewPage` already renders those nulls with
the honest unavailable treatment.

#### 10.9.15 The events families on the VYK gateway — dividends, corporate actions, disclosures (TKT-foundation-034, FR-MDF-003/004/005, FR-MOV-006 downstream)

**Why (the defect this closes).** TKT-foundation-026 rewired **`statements` only**; the
`dividends`, `corporate-actions` and `disclosures` adapters still fetched the placeholder
single-resource paths (`/dividends`, `/corporate-actions`, a `/disclosures` body that does not
exist as such). Every one of those routes 404s on the VYK gateway, so the three jobs could never
ingest a real row, and `§10.9.11`'s shared `IKapSourceClient` (v1.5.5's flagged consequence) made
that failure live. This subsection records the **verified** event-family routes and their decode
contracts. No family is blocked: all three are served by the gateway's two disclosure routes.

**The routes (live-verified 2026-10-20, dev gateway `https://apigwdev.mkk.com.tr/api/vyk`).**
Evidence: ~30 paced (≥12 s/call) probes plus real response captures kept outside the repo; the
OpenAPI alone was **not** trusted (its `disclosureTypes`/`disclosureClass` examples are ambiguous,
026's lesson).

| Family | List route | Family selector | Detail route |
|---|---|---|---|
| `corporate-actions` | `GET /disclosures?disclosureIndex={n}&companyId={id}` | listed row `disclosureType == "CA"` | `GET /disclosureDetail/{index}?fileType=data` |
| `dividends` | same | `disclosureType == "CA"`, then detail `eventType == "KP"` | same |
| `disclosures` | same | **every** listed type (the general surface) | same |

**What the gateway actually returns (pinned shapes, never invented).**

- **List rows** carry `disclosureIndex`, `disclosureType`, `disclosureClass`, `subReportIds`,
  `title`, `companyId`, `acceptedDataFileTypes` — and **no date**. `title` is the **company name**,
  *not* the disclosure subject (so the disclosures job must read the detail for its title/date/
  link). Rows are **ascending by index**, at most **50 per page**, starting at the requested index
  (a company-scoped page is often shorter — e.g. 26/39/40 rows for a whole company archive).
- **The `disclosureTypes` filter works** (verified: `?disclosureIndex=1160000&disclosureTypes=CA`
  → 50 all-`CA` rows), but a **filtered empty set answers HTTP 400
  `{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}`**, not `200 []`.
  **Same for an index past a company's last disclosure** (verified:
  `?companyId=1406&disclosureIndex=1231018` → 400 `ER005`). This decides two adapter rules:
  the family filter is applied **client-side on the listed row** (the unfiltered company list
  always returns that company's rows), and the per-company cursor is read **inclusively** (the
  walk re-requests from the last-seen index instead of `index + 1`), so a caught-up company
  re-reads its tail page rather than tripping `ER005`. A filtered list is therefore never used,
  and an honest empty is never turned into a source failure.
  - **Is index `0` itself a past-tail request? No (probed 2026-10-20, one paced call).**
    `GET /disclosures?disclosureIndex=0&companyId=1406` answers **HTTP 200** with the company's
    first page (39 rows, `1092405`…`1153433`, ascending) — the company's archive simply starts at
    its earliest index (`1092405`), so a first pass at `EarlyDisclosureIndex=0` proceeds normally.
    Evidence: the probe record reproduced on the ticket (timestamp `2026-10-20T14:38:03Z`).
  - **The client carries the status/body, and `400`+`ER005` = end-of-list (fix round 2).**
    `KapHttpSourceClient` now reads a bounded (≤2 KB), secret-redacted excerpt of a non-success
    body and carries it (with the HTTP status) on `SourceFetchException`; the success read stays
    **buffered** (`ResponseContentRead`), so a body stays inside `HttpClient.Timeout` (30 s, set in
    `IngestionServiceCollectionExtensions`) — a gateway that sends headers then stalls cannot hang
    the run, which has no job-level timeout. The adapters classify **only** a `400` on a **list**
    request whose body is the `ER005` envelope as "no disclosure at/after this index":
    that ends the company's walk with no exception and no rows (an honest empty), keeping the
    cursor at the last index actually emitted. Every other non-success — `404`, `401`, `429`, a
    `fault` envelope, a `400` with another code, transport, and **any** failure of the detail
    route (a detail `400`+`ER005` included) — remains a source failure. A company
    with zero disclosures therefore ends its first pass cleanly (succeeded run, 0 rows) instead of
    failing the whole family.
- **Detail bodies** carry `disclosureIndex`, `disclosureType`, `disclosureClass`,
  `subject{tr,en}`, `period{tr,en}`, `summary{tr,en}`, `time` (`dd.MM.yyyy HH:mm:ss`), `link`
  (`https://www.kap.org.tr/Bildirim/{index}`), `attachmentUrls[]`, `eventType`, `eventId`,
  `relatedStocks[]` and — **for `CA` bodies, unlike `FR` (026), `flatData` IS returned**:
  `flatData[0] = { id: "tr.com.mkk.kap.client.dto.KPY5xDTO", content: { object: { void: [ …named
  fields… ] } } }`.
- **`eventType` vocabulary observed** (every value below is from a real body): `KP` dividend
  (`KPY51DTO`, `subProcessName=NKP`, `useCaseMethodName=karPayi`, subject "Kar Payı Dağıtım
  İşlemlerine İlişkin Bildirim"), `SA` capital increase/decrease (`KPY52DTO`), `GAI` buy-back,
  `SPAF` debt issue, `IT` issue limit, `GK` general assembly (`KPY50DTO`).
- **Dividend body shape** (real capture: BİM disclosure `1138986`, 14.04.2023): `currency` is a
  keyed `{key,text}` list (`TRY`); `cashDividendList` rows (nested
  `object.void[] → object.void[]`) carry `exchCode`, `groupCode`, `installmentOrder`
  (`1`, `2`, … and `T` = total), `grossTry`, `netTry`, `grossPercentage`, `netPercentage`;
  `cashDividendPaymentDateList` rows carry `installmentOrder` and the date wrappers
  `advisedUsageDate.date.object.string` (**ex-date**), `recordDate`, `paymentDate`.

**Mapping onto the canonical contracts (downstream unchanged).** Every stored field is a real
source field; nothing is synthesized.

- `corporate-actions` → `CorporateActionFact`: `ActionType` = the gateway `eventType` code
  (`SA`, `GAI`, `SPAF`, `KP`, …) — the engine's `ToDatedAction` recognises only
  `split`/`bonus_issue`/`rights_issue`, so **no adjustment factor is invented** from an
  unrecognised code (honest, BR-MDF-010 untouched); `ActionDate` = the disclosure's publish date
  (`time`); `Terms` = the detail's `flatData[0].content.object` retained verbatim as `terms_json`
  (`NULL` when the gateway sent none — never an invented `{}`). **Overlap (documented, R9):** a
  `KP` dividend disclosure is a `CA` disclosure, so the same disclosure is stored in
  `corporate_actions` (`ActionType="KP"`) *and* decoded into `dividends` — the ticket's literal
  family rule; the two tables are different surfaces and are deliberately not de-duplicated.
- `dividends` → `DividendFact`: **one fact per cash instalment** (`installmentOrder != "T"`;
  the aggregate row is a summary, not a fact), `AmountPerShare` = `grossTry`,
  `ExDate` = the instalment's `advisedUsageDate`, `PayDate` = its `paymentDate` (nullable),
  `Currency` = the detail's `currency.key` (none when absent — never an assumed `TRY`). A row
  without an ex-date is left out rather than dated by guesswork; a row naming another
  `exchCode` is skipped (FR-MDF-008). Downstream consumer: FR-MOV-006's TTM DPS (unchanged).
- `disclosures` → `DisclosureFact`: `Id` = `disclosureIndex`, `DisclosureType` = the detail's
  `disclosureType`, `PublishDate` = `time`, `Title` = `subject.tr` (fallback `subject.en`),
  `SourceUrl` = `link`. Metadata only — the attachment/document layer stays a separate follow-up
  (026's scope note); `document_path` remains the configured-volume path.

**Where the loop lives (why the adapter, not the job).** The three job files are outside this
ticket's scope and ask `ISourceAdapter.FetchAsync(date)` for **one** payload, so the
company-scoped loop that §10.9.10/§10.9.11 place in the statements job lives in the adapters:
resolve `Ingestion:{Family}:UniverseIndex`'s current constituents
(`UniverseSyncStore.GetCurrentConstituentSymbolsAsync`), map each symbol through the run's
`/members` `VykCompanyDirectory`, walk that company's list from its cursor, fetch each new
detail, and aggregate into the one payload the job persists. The walk (list pagination, identity
guard, inclusive cursor, `ER005` honest-empty, stale-cursor reset, strict list-body parsing) is one
shared `VykDisclosureWalker` (fix round 2, R8): each adapter supplies only its family predicate
and detail decoder, so the three families cannot drift apart in their failure semantics. Pacing is
the shared scoped
`KapRequestPacer` (bound to `Ingestion:Statements:RequestDelayMs`) — **every** gateway call,
including `/members` and the anchor, is paced (shipped 10 000 ms; free plan ≈6 calls/min).
Provenance is `kap` on every row (FR-MDF-008).

**Failure posture (per-company isolation, honest).** A transport failure, an HTTP error
(`404`/`401`/`429`, or a `400` that is **not** `ER005`) or a gateway fault (`fault`/`code+message`
envelope) is a **`SourceFetchException`** — the job does not catch it, the runner records the run
`failed` in `ingest_runs` (the failure ledger) and no row is written; a structurally malformed body
is a **`SourcePayloadException`** (quarantine `UNPARSEABLE_PAYLOAD`); an empty bound index is a
failure, never an empty success. The **one** tolerated non-2xx is `400`+`ER005`, which is
end-of-list (above).
**A bound symbol the directory cannot map is quarantined per constituent, never a fail-stop**
(v1.5.22, TKT-foundation-046): the adapter's loop skips it, logs it with the named reason
`COMPANY_IDENTITY_UNRESOLVED`, records it on its `IVykIdentityIsolationReporter` channel and
continues the bounded walk; the job folds that list into the run's per-symbol stats
(`failed = true`, `written = 0`) and ends the run **`partial`** — the §10.9.10/§10.9.11
per-company isolation doctrine (FR-MDF-012) applied verbatim to the three families. Identity
isolation is **not** a parse event: it quarantines nothing, writes no row under a fabricated
identity and never advances the isolated symbol's cursor. **The pre-v1.5.22 posture — an
`CompanyIdentityUnresolvedException` escaping the adapter and failing the whole family run after an
hours-long paced walk — is the deviation this subsection corrects** (live-verified 2026-10-20: the
`dividends` and `corporate-actions` runs walked ~18.4 min and then surfaced HTTP 500 from
`GARAN` alone). The same doctrine applies to a transport failure inside one company's walk only as
far as §10.9.10 already allows: the family walk stays fail-fast for **list/company** transport,
because its cursors are persisted at the **end** of the loop (below) — a per-company transport
isolation there would advance past a company whose facts were never emitted. *(v1.5.40 narrows
this to the *list* route: a per-**detail** transport failure is isolated at the item, where the
cursor semantics are already recorded — see the next paragraph.)*

**Per-disclosure isolation and the bounded per-item fetch (v1.5.40, TKT-foundation-061).** The
doctrine above isolates a *constituent*; it did not isolate a *disclosure*, and the first truly
unattended EOD chain (2026-10-22) proved the gap on production-shaped data: step 5/9
`corporate-actions` **hung 90 minutes on attempts 1 and 2** (bounded only by the 041 watchdog,
which wrote the correct `RUN_HUNG` rows — 041 working as designed), and attempt 3 took a **fast
HTTP 500 from the gateway on `/disclosureDetail/1185709?fileType=data`** (`SourceFetchException`,
run 195). Because a detail failure was fail-fast, the run was recorded `failed` and the chain
fail-stopped **before `metrics-recompute` / `market-snapshot` / `medians`** — the day's close
never reached the snapshot and the site ran a day stale. The same job had completed a full
74-minute paced walk the same morning (302 rows written), so both faults are **per disclosure**
(one attachment id), not systemic.

The shared `VykDisclosureWalker` therefore isolates **one level down**, at the detail:
1. **Quarantine-and-continue.** A detail that the gateway cannot serve — an HTTP error, a **200
   fault envelope**, a transport failure — is recorded as a `VykDetailSkip` with the named reason
   **`DISCLOSURE_DETAIL_UNFETCHABLE`**, the family, the disclosure index, the failure kind
   (**`http` for a non-2xx status, `fault` for a 200 fault envelope, `transport` for a
   `HttpRequestException`, `timeout` for the bound**), the HTTP status when there was one, and
   **the gateway's error
   verbatim** (message + the bounded, redacted body excerpt); the walk logs it, continues with the
   next listed row, and the item's index still advances the cursor (it is *recorded*, never
   silently dropped — the quarantine row is the re-ingest handle). The per-item `try` covers the
   **decode** as well as the fetch: each adapter's `DecodeDetail` raises the fault-envelope
   `SourceFetchException` lazily, so it takes the same skip path. Each adapter exposes the run's
   skips on `IDisclosureIsolationReporter`; each job writes one `quarantined_facts` row per skip
   (payload = that evidence document) and folds the per-symbol skip count into the run's
   `symbols` stats (`quarantined`, `failed=false`, additive on 046's shape). The run terminates
   **`partial`** — **never `failed`, never a silent omission**.
2. **Bounded per-item fetch.** The hang class is bounded by
   `Ingestion:Kap:DetailFetchTimeoutMs` (shipped default
   `KapSourceOptions.DefaultDetailFetchTimeoutMs` = **180 000 ms — three minutes**, ~18× the
   shipped 10 s pace so a slow-but-healthy gateway is never falsely timed out). The bound starts
   **after** `KapRequestPacer` has paced the call, so politeness delay is never counted as a hang;
   it is applied both as a cancellation of the fetch and as a race against it (a client that
   ignores cancellation cannot stall the walk), and it composes with the run's
   `CancellationToken` — a **caller cancellation still propagates** and is never quarantined. A
   timed-out item takes the **identical** skip path as a 500 (failure kind `timeout`).
3. **The chain's step policy is untouched** (§10.9.16/§10.9.17): it fail-stops on `failed` only,
   so an honest `partial` step proceeds — which is precisely what unblocks `metrics-recompute` /
   `market-snapshot` / `medians` and lands the day's close.
4. **Unchanged on purpose:** a **list**-route failure is still fail-fast (`failed`), a malformed
   *detail body* is still `UNPARSEABLE_PAYLOAD`, and 046's identity isolation is untouched. Only
   the per-*detail* fetch **and decode** failure classes moved from run-fatal to per-item.

**Production-honesty pin (recorded with the same grant).** The answer to a per-item gateway error
is **quarantine-with-evidence** — never silent omission, and never a whole-run failure. A missing
attachment must never be filled with an invented row, an empty payload, or a fabricated skip
pretending the item did not exist; it is counted, named and inspectable.
**Statements joins the same walker (v1.5.42, TKT-foundation-063) — see the next paragraph.**

**Statements lane: one walk, stale-cursor recovery, and the honest zero-write status (v1.5.42,
TKT-foundation-063).** The statements lane had the identical unguarded per-detail fetch 061 fixed
in the shared walker, because it carried its **own duplicated walk** (`FetchVykCompanyAsync`). On
2026-10-23 it failed 22–24 of 30 covered companies every run and, on a zero-write run (run 212,
`written 0 / 24 failed / 6 quarantined → failed`), fail-stopped the EOD chain **before
`metrics-recompute` / `market-snapshot` / `medians`** — the same chain-loss class 061 closed for the
event families. Direct gateway probes (`GET /api/vyk/disclosures?disclosureIndex=…&disclosureClass=FR&companyId=…`)
answered **HTTP 400 `{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}`**: the
persisted per-company cursor pointed past the tail the gateway now serves.

1. **One walk, not two.** `StatementsSourceAdapter.FetchVykCompanyAsync` now calls the shared
   `VykDisclosureWalker` and its duplicated walk is deleted. The adapter supplies its list route
   (carrying `disclosureClass=FR` — the walker appends `disclosureIndex`/`companyId`), its detail
   route, a predicate accepting every listed row (the route is already the financial-report
   filter), the statement decode callback and the per-company cursor. **Unchanged on purpose:**
   the non-VYK canonical/XBRL single-resource path and its goldens; malformed detail bodies stay
   `UNPARSEABLE_PAYLOAD`; a list/company transport failure that is not ER005 stays a source
   failure. The statements cursor names the last index whose facts were **persisted**, so the
   walker's `replayPersistedIndex` re-decodes that one row on a re-run (honest `unchanged`
   idempotency) without a second walk or a changed cursor value.
2. **Stale-cursor recovery, recorded verbatim and persisted once.** A first request made from a
   persisted cursor that answers 400 `ER005` is the gateway saying "no disclosure at/after this
   index", never a source failure: the walk resets **once** from `EarlyDisclosureIndex`, re-walks,
   and reports the reset on `VykCompanyWalk.Reset` (`VykCursorReset`: symbol, from → to, the
   gateway's code and body **verbatim**). The statements job records it as a structured log event
   and persists the corrected state — the corrected cursor (last emitted index) when the re-walk
   emitted facts, or `KapDisclosureCursorStore.ClearAsync` when the re-walk was itself honest-empty
   — so the next run makes one list call and never repeats the same ER005. The reset is bounded to
   one attempt per company per run; a non-ER005 400/500 is never recovered as an honest empty.
3. **Per-company isolation + honest status.** The unit of isolation is the company, with 061's
   item-level semantics one level down: an unfetchable disclosure detail is an evidence-carrying
   skip (`DISCLOSURE_DETAIL_UNFETCHABLE` — disclosure index, failure kind `http`/`fault`/
   `transport`/`timeout`, status, the gateway's error verbatim) quarantined per 061, counted in the
   per-symbol stats, while the other companies' rows are written. Every constituent appears in
   `symbolStats`; nothing is silently dropped. A run whose writes are zero because companies were
   **honestly skipped** (stale-cursor reset, unfetchable detail, identity unresolved, quarantine)
   ends **`partial`** — never `failed`, never a chain fail-stop. Only a run where **every**
   constituent failed for a genuine source reason (directory/list transport, a non-ER005 status, or
   an unparseable payload) ends `failed`, preserving the retry ladder's meaning.
4. **Honesty doctrine (recorded with the grant):** never fabricate a statement, never silently
   drop a company; a skip is evidence + a named reason + a count.

**The disclosures lane completes: a bounded bootstrap window and a market-wide feed (v1.5.52,
TKT-foundation-078).** `disclosures` had never once finished a pass: `kap_disclosures` held zero
rows. The cause was throughput, not access — `FetchVykAsync` looped the bound constituents and,
per company, read the cursor; a cursor-less company fell back to `EarlyDisclosureIndex = "0"`,
i.e. the beginning of KAP's platform history. ASELS alone spans **1,092,199 → 1,231,017 = 138,818
index positions ≈ 39 pages**; at the mandated ≥12 s pace that is ≈8 min per company and ≈**4 h**
for one XU030 pass. Ledger proof: run **208** = `10:41:24 → 13:41:24` — **exactly 3 h 00 m**,
`written=0`, killed by the watchdog. (`ER005` is *not* the cause: the gateway answers it when an
index is at/above a company's latest disclosure — the end-of-list signal.)
1. **Bootstrap from the platform head's window, never from zero.** A company with **no** persisted
   cursor starts `Ingestion:Disclosures:BootstrapLookback` index positions below the
   `/lastDisclosureIndex` anchor the directory already fetched (shipped default **5,000**; the
   window is configuration, not a magic number in the walk). A company's own rows are *sparse* in
   index space (ASELS' 39-row page spanned 50,040 positions), so the window is normally one or two
   list calls. The seed is recorded as **provenance** on the run — a counted `bootstrapped N
   cursor(s) … BOOTSTRAPPED` log line plus a per-symbol stat (`written 0`, `failed false`, reason
   `BOOTSTRAPPED`) — never as "this company has no disclosures". A **persisted cursor always
   wins**: the incremental walk is unchanged (one page from the stored index, no re-walk, no
   reset — the 063 precedent), and a stale cursor's single ER005 reset now targets the **bounded
   window**, never index `0`. If the anchor is unavailable while a bootstrap is needed, the run
   fails loudly (`BOOTSTRAP_ANCHOR_UNAVAILABLE`) rather than silently walking from zero.
2. **The market-wide feed is the normal pass.** `Ingestion:Disclosures:Feed` selects the lane:
   **`market`** (the shipped default) pages `GET /disclosures?disclosureIndex={n}` with **no
   `companyId`** from one market cursor — recent disclosures across the market in a handful of
   pages — keeping only rows whose owning company is one of the bound constituents; a row outside
   the bound universe is **counted, logged (`OUTSIDE_BOUND_UNIVERSE`) and never fetched**, its
   index still advancing the cursor so it cannot be re-read forever. **`company`** keeps the
   bounded per-constituent walk for a first-seed/backfill. The market cursor is one
   `coverage_metadata` row (`scope = market`, no instrument, data type
   **`kap-disclosures-market-cursor`**, value in `Notes`) — outside `InstrumentScoped`, no
   migration. An honest-empty market pass persists **no** cursor, so the next run re-bootstraps
   from the then-current head instead of pinning a cursor that read nothing (and without a
   backwards reset, which would re-open the archive walk).
3. **The run is bounded — in work, and honestly in wall clock.** `Ingestion:Disclosures:MaxListPages`
   (default **24**) and `MaxDetailFetches` (default **200**) are **per-walk** ceilings. The pacing
   value actually in force is `Ingestion:Statements:RequestDelayMs` (**10 s**, the options type the
   shared `KapRequestPacer` binds; `Ingestion:Kap` carries no delay), so the **nominal** cost of one
   `market` walk is 24 × 10 s = 240 s of list pacing + 200 × 10 s = 2,000 s of detail pacing ≈
   **37.3 min**. That is an estimate, not a wall-clock bound: the number of disclosure details is
   capped but the *time* each may take is not — a detail that stalls is bounded only by the per-item
   `Ingestion:Kap:DetailFetchTimeoutMs` (**180 s**), and a list call only by the client's 30 s
   timeout. A work-only bound could therefore let a degraded gateway drive **200 × 180 s =
   36,000 s ≈ 10 h** (+ ≤ 12 min of list calls) out of a single walk, i.e. far past the
   **120-minute per-step bound** (§10.9.17, v1.5.43 — the 90-minute whole-attempt bound no longer
   exists). The walk therefore enforces its **own wall clock**: `Ingestion:Disclosures:MaxWalkDuration`
   (**default 90 min**, strictly below the 120-min `MaxStepDuration`, leaving ≈30 min for the job's
   store write). One clock is started per pass in `FetchVykAsync` and passed into the walk(s) — the
   market feed is one walk, the company feed shares it across the constituent loop — and the walker
   checks it before **every** list and detail request on the injected `TimeProvider` (no real
   sleeps). On expiry the walk self-truncates: it records `truncated`, persists the last index it
   *completed* (never the page's highest row, whose detail it did not pay for), persists every
   already-completed company's cursor, and the next run resumes exactly there. A deadline stop is an
   honest bounded pass (`succeeded`/`partial` with counts), **never** `failed`; the scheduler
   watchdog is now only the backstop for a single request that outlives its own per-item timeout —
   it no longer has to cancel the pass and discard every completed page (the run-208 `RUN_HUNG`
   signature: no counts, no cursor advance, the identical pass repeated next run). The worst case of
   a pass is therefore `min(work bound, MaxWalkDuration)`; `MaxWalkDuration ≤ 0` disables the
   deadline. The **shipped `market` feed** is one walk per run, so its page budget is also the
   run's. The `company` feed builds one walk per constituent but shares the pass deadline, so one
   run is bounded as a whole — the pre-deadline nominal of a full pass was constituents ×
   (pages + details) ≈ 37.3 min × 30 ≈ **18.7 h**, which is now a recorded, resumable multi-run
   **backfill**, never a single chain step. A budget- or deadline-truncated walk is **bounded, not
   broken**: it records `truncated`, persists the last completed index, and the next run resumes
   exactly there — no silent gap, no unbounded run.
   **Paging.** The market walk does **not** treat a short page as the end of the list: the live
   gateway serves 39–48-row pages with more rows above them (the 2026-10-24 probes), so it stops on
   "the page did not advance the cursor", on reaching `/lastDisclosureIndex`, on `ER005`, or at the
   budget — otherwise the shipped feed would advance ~48 positions per run instead of covering the
   window. The company walk keeps its short-page and non-advancing-page guards (pre-078 semantics).
   **Cursor boundary and reset.** A persisted cursor never depends on `/lastDisclosureIndex` (a
   secondary read, §10.9.11): the anchor is required only to compute a **bootstrap** window for a
   cursor-less company. A reset whose re-walk is itself honest-empty **clears** the cursor row (the
   063 statements precedent, `CoverageMetadata` removed), so the identical two-call reset cannot
   repeat; a persisted market cursor's own boundary row — like every page boundary — is skipped on
   resume, so an incremental run re-fetches no detail it already has.
4. **Honesty doctrine (unchanged, restated because it is load-bearing):** never fabricate a
   disclosure, a subject or a body; an unmapped identity is a named, counted skip
   (`COMPANY_IDENTITY_UNRESOLVED`); an unfetchable detail is quarantined with the gateway's body
   verbatim; a genuine gateway-down is still `failed`; an honest empty is a skip with a reason,
   never a synthesized row.

**Company mapping — comma-joined `stockCode` values (v1.5.22).** `MemberInfo.stockCode` is
**not** always a single ticker: 77 of the 1037 rows of the live dev-gateway `/members` (2026-10-20)
carry several, comma-joined — Türkiye Garanti Bankası is `"GARAN, TGB"`, plus `"ALBRK, ALK"`,
`"A1CAP, ACP"`, … — and the payload also carries literal duplicate member entries and 119 codeless
auditor-type members (`YK`/`FK`/`IGS`/`IGMS`). `VykCompanyDirectory` therefore splits the code on
commas and registers **every** trimmed part as its own key in the existing `OrdinalIgnoreCase` map
(v1.5.22 fixed the pre-existing single-key assignment, which made `BySymbol["GARAN"]` miss and
unresolved 14 of the 100 stored BIST-100 constituents — 9 purely by the comma). Duplicate handling
is **last-wins** with a logged warning when two member entries claim the same code with
**different** ids (identical duplicates are idempotent and silent) — deterministic, never silent.
Codeless members contribute no key and no fabricated mapping. The five constituents genuinely
absent from the dev frozen archive (GLRMK, PAHOL, TRALT, TRENJ, TRMET) stay unresolvable there
forever and are honestly skipped per the paragraph above; they resolve on production.
**A `200` list body must be a JSON array of objects carrying a numeric `disclosureIndex`**
(as a JSON string or a JSON number — a number is normalized to its invariant string, so the
acceptance check and the row model cannot disagree and silently drop an accepted row); a bare
object, a wrapper, a truncated body or any unexpected shape is a `SourcePayloadException`
(quarantine), never an empty success. Detail bodies must carry the fields the canonical contract
names — `eventType`+`time` for corporate actions, `disclosureIndex`+`disclosureType`+`time`+
`subject`+`link` for disclosures, a `flatData` terms document for a `KP` dividend — otherwise they
are quarantined rather than stored with null metadata.
**Cursors are persisted at the end of the adapter loop, before the job's store write.** A failure
inside the loop therefore never advances a cursor, but a store failure *after* the adapter returns
can skip the indexes the cursor already covers (accepted residual, recorded on the ticket): the
statements precedent persists per-company *after* its write, which the adapter cannot see. The
next run's stale-cursor reset (below) is the recovery path, not a claim that this window is closed.

**Incremental cursor (structural pin).** Per family per instrument, the last-seen
`disclosureIndex` is stored in the **existing `coverage_metadata`** table (Scope `instrument`,
data types **`kap-dividends-cursor`**, **`kap-corporate-actions-cursor`**,
**`kap-disclosures-cursor`**, the value in `Notes`) — no schema migration. These data types are
deliberately **outside** `CoverageDataTypes.InstrumentScoped`, so the no-silent-gaps reconcile
never clobbers them. The disclosures lane adds one **market-scope** cursor row
(`kap-disclosures-market-cursor`, Scope `market`, no instrument — v1.5.52). A cursor-less company
starts from `Ingestion:{Family}:EarlyDisclosureIndex` (the first pass **is** the backfill: achieved
depth is what the source returned, FR-MDF-010/011) — except the disclosures lane, whose
cursor-less company starts from the bounded head window (v1.5.52, above), and whose `company` feed
keeps `EarlyDisclosureIndex` only as the documented escape hatch (`BootstrapLookback ≤ 0`).
Two cursor-integrity rules (fix round 2): a **non-empty cursor that does not parse fails the run
loudly** (`SourceFetchException`, naming the value) instead of silently re-walking the whole
archive; and when the **first** list request of a company — the one made from a persisted cursor —
answers `400`+`ER005`, the cursor is treated as past a tail the gateway no longer serves and the
walk **resets once** to `EarlyDisclosureIndex`, so one stale row cannot poison every future run.
The reset is bounded to one attempt per company per run; since v1.5.42 it is also recorded verbatim
and its outcome persisted (the corrected cursor, or a cleared row when the re-walk is honest-empty
— see §10.9.15's statements paragraph). A persisted cursor never requires the
`/lastDisclosureIndex` anchor: it is a secondary read, and only a bootstrap window needs it
(v1.5.52).

**Config (`Ingestion:Dividends` / `Ingestion:CorporateActions` / `Ingestion:Disclosures`, §10.4).**
Shipped in `src/Api/appsettings.json`: each family declares `Path = /disclosures`,
`DetailPath = /disclosureDetail/{disclosureIndex}`, `EarlyDisclosureIndex = 0`,
`UniverseIndex = XU030` (its own bound key, AD-14); `Dividends`/`CorporateActions` add
`DisclosureType = CA` (`Dividends` also `DividendEventType = KP`); `Disclosures` ships
`DisclosureType = ""` (every type) and — since v1.5.52 — `Feed = market`,
`BootstrapLookback = 5000`, `MarketBootstrapLookback = 1000`, `MaxListPages = 24`,
`MaxDetailFetches = 200`, `MaxWalkDuration = 01:30:00` (all six documented in the options' XML docs
with their pages × pace arithmetic, the 180 s per-item caveat and the 120-min per-step ceiling the
90-min deadline sits under; code defaults identical). The code defaults are the same verified
values, so config and code cannot drift. The pacer keeps using
`Ingestion:Statements:RequestDelayMs` (shipped 10 s).

**Operator note — first-pass cost (flagged, not hidden).** The first live pass of each family is
its backfill, and `disclosures` is the expensive one: every listed disclosure needs its own detail
call (the list carries neither the title subject nor a date), so a full XU030 pass over the dev
archive is on the order of a thousand paced calls (~10 s each ⇒ hours on the free plan). One
consequence of the postures above: the walk advances cursors only at the **end** of its loop, and a
**list/company transport** failure repeats the run (and therefore the pass) from the start rather
than resuming mid-pass; an **identity-isolated** constituent (v1.5.22) and a **per-disclosure
detail skip** (v1.5.40) by contrast never block the run — the rest of the bound is ingested and
the run ends `partial` with the isolated symbols / skipped details counted. The
honest lever for the operator is therefore a narrower
`Ingestion:{Family}:UniverseIndex` for the first pass (each family has its own bound key, AD-14),
not a silent depth cap: no disclosure index is ever skipped past a company whose facts were not
emitted. **Since v1.5.52 the disclosures lane no longer pays this cost on a normal pass** — the
market feed reads only the newest disclosures from one cursor and the page/detail budgets bound a
pass at ≈45 min (above); the paragraph's "hours on the free plan" describes the pre-078
from-zero backfill, which the bootstrap window replaced.

**Test-double posture (§10.9.7).** The legacy single-resource branch is kept **verbatim** for
compositions with the VYK source disabled (`MembersPath` unset), so the canonical doubles —
including `EodChainTests`/`EodStaleServingTests`, which set the three canned `Path` values — are
byte-identical and the TC-MDF-003..006 goldens re-run unchanged (**no permitted delta**). The
verified routes are exercised by new L2 tests in `StatementsAndEventsIngestionTests` against
shape-faithful doubles of the verified routes (trimmed real captures: BİM `KP`, Tüpraş `SA`, and
the real list/detail metadata shapes) plus 404/fault honest-failure cases, the inclusive-cursor
resume, a config-wiring assertion (now comparing **every** family key against its code default),
and — fix round 2 — the `400`+`ER005` honest empty across all three families, a `200 []` empty
list, a stale-cursor reset, a `400` with a non-`ER005` code (still failed), a non-list `200` body
(quarantined), a detail-route `404` mid-walk — **amended by v1.5.40**: now isolated as a
`DISCLOSURE_DETAIL_UNFETCHABLE` skip with the sibling row written, one quarantine row carrying the
gateway's 404 verbatim, and the run `partial` (the pre-v1.5.40 expectation was failed/no rows/no
quarantine; it is the one pre-listed delta on the 034 lane) — a truncated detail body and a detail
missing its publish metadata (both still quarantined as `UNPARSEABLE_PAYLOAD`). The list doubles bind **both**
query parameters (`companyId`, `disclosureIndex`) and the tests assert the actual query, so a path
builder that dropped `companyId` would fail. **v1.5.22 (TKT-foundation-046)** adds the
identity-resolution lane: `VykCompanyDirectoryTests` (new, `TC-MDF-082`) drives the production
`VykCompanyDirectory` over a **committed real-shape `/members` double** (derived from the live
2026-10-20 capture — comma-joined codes incl. the exact `"GARAN, TGB"`, literal duplicate entries,
a same-code/different-id conflict and codeless auditor members; the ephemeral `/tmp` capture is not
referenced) and pins the per-ticker split, the codeless skip and last-wins-with-a-warning; the
family lane adds `TC-MDF-083` (an unmapped bound symbol is isolated, counted in the run's
per-symbol stats, never fetched, and the run ends `partial` — one theory case per family) and a
`TC-MDF-082` composition case (a `"GARAN, TGB"` constituent resolving end-to-end through the
`corporate-actions` walk). The TC-MDF-003..006 canonical goldens are untouched (VYK stays disabled
there); the only additive delta on the 034 lane is the new per-symbol skip stat, which is empty
when every symbol resolves. **v1.5.40 (TKT-foundation-061)** adds the per-disclosure lane:
`TC-MDF-094` (one detail answering HTTP 500, one theory case per family — the walk completes, the
skip is quarantined with the gateway's error verbatim and counted in `quarantined` + the per-symbol
stat, every fetchable row and the cursor are written, the run ends `partial`; plus the chain
composition case asserting `metrics-recompute`/`market-snapshot`/`medians` stay reachable after a
partial `corporate-actions` step) and `TC-MDF-095` (a detail that accepts and stalls is bounded by
the configured per-item timeout and takes the identical skip path with failure kind `timeout`; and
the guard half — the shipped bound stays ≥10× the shipped pace and a slow-but-answering detail is
never falsely timed out). Doubles only: the stall is a WireMock delay, never a live gateway call.

#### 10.9.16 Morning-cadence chain — EOD-repair safety net + incremental KAP freshness (TKT-foundation-040, FR-MDF-009/010/011/012, NFR-MDF-001, FR-MOV-001..006/021)

**Why (the gap this closes).** The EOD chain runs once, at 20:30 Europe/Istanbul, with a
5/15/60-minute retry ladder that is exhausted by ~21:50 (NFR-MDF-001). A failed night therefore
leaves the site serving the day before yesterday until the *next* 20:30 — and the served
`market/overview` block is exactly what the user opens in the morning. A live dev observation
(2026-10-19/20) recorded a scheduled chain that wrote `prices succeeded` and then produced no
statements/metrics/snapshot/medians rows in its wake; the Monday overview row existed only
because an admin trigger ran `market-snapshot` by hand. The root cause of that stall is **not**
claimed or investigated here — the point is that a morning pass repairs the outcome whatever
the cause.

**Decision (user-directed product change 2026-10-20: "we can take data morning and night").** The
scheduler grows a **second chain stream** on the existing engine — not a second scheduler, not a
new runner:

- **`Ingestion:Scheduler:MorningCron`** (shipped `0 30 8 * * *`, same `Ingestion:Scheduler:TimeZone`),
  trading-day gated exactly like the EOD stream (`TradingDaysOnly`): a non-trading occurrence is
  skipped silently with no ledger row.
- **The pinned `MorningIngestChain`** (the `EodIngestChain` sibling) is the stream's job list.
  An empty `MorningCron` creates no morning stream, so a host or test composition that configures
  nothing keeps the pre-040 single-stream behaviour byte-compatible; the shipped
  `appsettings.json` carries the cron, so production runs it.
- **`RunDateMode`:** the EOD stream runs for its **occurrence date**; the morning stream runs for
  the **previous trading day** (a first-class stream property, resolved once per occurrence by a
  calendar back-walk). Same retry ladder, same `RETRIES_EXHAUSTED` machinery, same fail-stop on a
  `Failed` job result — no new scheduling semantics.

**The honest constraint — BIST price data is EOD, so the morning pass is never newer market
prices.** A Tuesday 08:30 run targets **Monday's session**; Tuesday's session does not exist
until the ~18:00 close and belongs to the 20:30 chain. No log line, document or UI may imply
otherwise. The morning chain exists for **catch-up + KAP freshness**, nothing else.

**The composition (TKT-foundation-040 phase 1, completed by TKT-foundation-053):**
`prices (repair-gated) → statements (incremental) → dividends → corporate-actions →
disclosures → metrics-recompute → market-snapshot → sector-medians`.

- **The prices repair gate is stored-prices-as-truth (§10.9.13 doctrine).** Before invoking the
  `prices` job, the scheduler reads the day's coverage of the bound universe
  (`Ingestion:Prices:UniverseIndex`, the same current-membership filter the job fetches over) and
  **skips the job** when every current constituent already has a `daily_prices` row for the run
  date. A fully covered day is never re-fetched — a normal morning generates **zero** İş Yatırım
  calls and zero `CONFLICTING_VALUE` quarantine noise (the 74k-row lesson of
  TKT-foundation-035). An uncovered/partial day runs the job unchanged at the existing pacing
  (`Ingestion:Prices:RequestDelayMs`); an **empty** bound is never "complete", so a cold start
  still runs the job (and its launch-seed path). The gate reads the same `PriceDayCoverage`
  that the `prices` job itself now uses **inside** a run for its completeness sweep (§10.9.2
  amendment 2026-10-24): the uncovered subset of a job-level `partial` day is exactly what this
  gate re-runs. The gate writes one `ingest_runs` row
  (`status: succeeded`, `stats_json.gate: "prices-coverage"`, `gateOutcome: "skipped"`,
  `constituents`/`covered`) so a zero-cost morning is still auditable — the honest alternative to
  a silent hole in the chain ledger.
- **statements rides its existing per-company cursors (§10.9.11)** — the morning pass is
  incremental (~one paced list call per bound company), never a re-pull.
- **metrics-recompute / market-snapshot / sector-medians are idempotent by existing contract**
  (recompute from stored facts; snapshot is one-row-per-day and a completed day is a no-op). They
  close the loop so a failed night still yields a served `state: available` overview row by
  ~09:00, and the served row keeps its honest `stale` flag (Tuesday morning serves Monday).
- **macro is untouched:** `macro-daily` (08:00) and `macro-cpi` (09:30) already own the morning.

**Phase 2 — wired (TKT-foundation-053):** `dividends → corporate-actions → disclosures` are
morning-chain members, spliced between `statements` and `metrics-recompute` exactly as the EOD
order has them. The phase-1 rationale for leaving them out — "they fail fast on placeholder KAP
paths" — was true only before TKT-foundation-034 rewired the three families onto the verified VYK
gateway routes; what kept the wiring deferred afterwards was the **completability** question, and
TKT-foundation-046 answered it: each family lane now isolates an unresolvable constituent
(`COMPANY_IDENTITY_UNRESOLVED`, counted in the run's per-symbol stats), finishes `partial`, and
never kills the run — so an honest skip can no longer poison this chain's fail-stop. The honest
current statement is therefore: the families joined the morning pass **once 046 made the family
lanes completable**, not "once 034 landed". The morning pass stays **incremental, never a
full-universe first pull** — the families ride their per-company cursors (§10.9.11) and the
nightly EOD pass keeps them current, so a morning pass lists only what appeared overnight; 034's
~1k-call disclosures backfill belongs to the EOD chain/backfill. Every KAP call continues to ride
`KapRequestPacer` (≥12 s, 401-penalty-box aware) — unchanged. Fail-stop and the 5/15/60 retry
ladder are the same machinery as before: only a `failed` step stops the chain, a `partial` family
lane does not.

**Backward compatibility.** The EOD stream's construction, the per-job streams (`JobSchedules`),
the retry ladder and the alert machinery are untouched, and the **job runner's** run-ledger shape
is untouched — the only new ledger writer is the gate-skip row on the morning stream, which
carries its own `gate*` keys (`stats_json.gate: "prices-coverage"`, `gateOutcome: "skipped"`,
`constituents`/`covered`). `02-data-model.md` needs no change — the gate reads existing
`daily_prices`/`index_constituents`/`indices`.

#### 10.9.17 Chain-orchestration reliability — the hung-run watchdog, and `universe-sync` joins the EOD chain (TKT-foundation-041, FR-MDF-009/012, NFR-MDF-001, FR-MDF-006, FR-MOV-001..006/021)

**The defect: an unbounded await inside one chain step holds every stream, silently.** The
production loop (`IngestionSchedulerHostedService`) awaits one `TickAsync` to completion, a tick
runs each due stream's `RunAttemptAsync` in stream order, and an attempt runs its chain's jobs
**sequentially**. The run ledger is written by the **job runner at job completion**. So a chain
step whose await never completes does not fail the run — it freezes the scheduler: no further
wakes, **no retries on any other stream**, no exhausted alert, no ledger row, and a healthy
process that keeps serving requests. That is exactly the recorded signature of **2026-10-20**
(dev stack): the 20:30 Istanbul chain fired, `prices` (run 117) finished 17:37:03 UTC
`succeeded` (77/100 written), and then nothing — no `statements` row, no
metrics/snapshot/medians, no error, no alert; the fund retry **already due at 17:30:00.026 UTC**
(inside the same stream table, scheduled by the previous wake) never fired, which proves the tick
never returned rather than the loop dying; the in-flight step was still running when the process
was restarted at 20:15 UTC, so it never wrote a row. 2026-10-19 repeated it. The class is
structural: no job-level duration bound existed anywhere between the cron and the ledger.

**Doctrine: every scheduled attempt is bounded.** `Ingestion:Scheduler:MaxRunDuration`
(default `01:30:00` as shipped in v1.5.23; since v1.5.43 the shipped default is **4 hours**,
with every chain step additionally bounded on its own at **120 minutes** — see "The bound's
scope, corrected" below; `<= 0` disables each bound independently) bounds **one attempt**, not
one job — deliberately, so the class is closed regardless of *which* await hangs (a source adapter, the KAP pacer's delay, a connection-pool wait, a semaphore). On
expiry the watchdog, deterministic on the injected `TimeProvider`:

1. cancels the attempt (the hung await itself is not interruptible — that is the premise; the
   abandoned task keeps its own DI scope and cancellation source, which are handed to a
   continuation that disposes them when it finally ends, and its terminal exception is observed
   so it can never surface as an unobserved-task crash),
2. writes one honest run-ledger row: `job_code` = **the step that held the run**, `status =
   failed`, `started_at` = the attempt start, `finished_at` = the expiry, `error` naming the
   bound, and `stats_json` carrying `reasonCode: "RUN_HUNG"`, `hungJob`, `step`, `stepCount`,
   `runDate`, `attempt`, `retries`, `maxAttempts`, `maxRunDurationSeconds`,
   `maxStepDurationSeconds`, and — since v1.5.43 — `bound: "step" | "chain"` (which of the two
   bounds fired) — so the class is filterable from `GET /admin/ingest-runs`,
3. raises one `IngestionAlert` with `reasonCode: "RUN_HUNG"` (the existing C3d machinery and the
   builder e-mail seam — no new channel), naming the step and the bound,
4. hands the attempt to the **unchanged retry ladder** (5/15/60, `MaxAttempts`, then
   `RETRIES_EXHAUSTED` + fail-stop), scheduled **from the instant the attempt ended** — a bound
   of 90 minutes would otherwise schedule 85 already-overdue minutes.

Two edges are decided explicitly: an attempt that **completes as the deadline fires** is a
completed attempt (the watchdog re-checks completion before declaring a hang), so a run is never
both `succeeded` and `RUN_HUNG` and never retried on that race; and a **stopping host** unwinds
without a `RUN_HUNG` row or alert (a shutdown is not a hung run).

**The bound's scope, corrected: each step is bounded on its own, the attempt keeps a ceiling
(TKT-foundation-064, §13.1 v1.5.43).** The 90-minute attempt bound was **the wrong scope**, and
the recorded consequence was that a healthy chain could never complete. Measured on the dev
stack (2026-10-23): a healthy paced chain legitimately needs **~100–120 minutes** — prices
~5 min @3 s, statements ~13 min @12 s, dividends ~16 min, corporate-actions **58–74 min** at
≥12 s per item, then disclosures + metrics-recompute + market-snapshot + medians. Against that
budget a whole-attempt bound of 90 minutes expired **mid-step** every night: the Thursday
"corporate-actions hung" and the Friday "disclosures hung" were both the attempt bound expiring
while an individually-healthy step was in flight, and the `RUN_HUNG` rows 203/208 named whichever
step happened to be running (the 2026-10-23 chain then fail-stopped six times at `statements`:
runs 227/233/236/244). The operational answer — raising
`Ingestion__Scheduler__MaxRunDuration` to `04:00:00` by environment override — was a stopgap.
The watchdog therefore carries **two independent bounds**:

- **Per-step bound** — `Ingestion:Scheduler:MaxStepDuration`, code default **120 minutes**
  (≈1.6× the observed healthy 58–74 min corporate-actions walk, ≈10× the 12 s item pace). Each
  step (`runner.RunAsync(jobCode, …)` inside `ExecuteChainAsync`) is awaited under its **own**
  clock-driven deadline. A step that exceeds **its own** bound is genuinely hung: its progress is
  snapshotted before any cancellation (the 059 DR-3 rule), only that step is cancelled, the
  attempt fails, and the existing retry ladder takes over.
- **Whole-chain ceiling** — `Ingestion:Scheduler:MaxRunDuration`, code default **4 hours**
  (the operational value proven on 2026-10-23; ≈2× the healthy chain, still well ahead of the
  08:30 morning safety net of §10.9.16). It remains the outer race around the whole attempt, so a
  pathological sequence of individually-healthy steps is still bounded before morning.

`<= 0` disables **each** bound independently (documented; both zero restores the pre-041
unbounded await). **One honest terminal report per attempt** — the 041/059 semantics are
unchanged and shared by both bounds: exactly one `RUN_HUNG` run-ledger row and one alert name the
in-flight step, the report states **which bound fired** — `bound: "step" | "chain"` in
`stats_json`, `per-step bound <d>` / `whole-chain ceiling <d>` in the row's `error`, the alert
subject and the `Hung run detected` log line — and both configured bounds are recorded additively
(`maxStepDurationSeconds`, `maxRunDurationSeconds`). An operator can therefore tell a genuinely
stuck step from a chain that overran its ceiling **from the ledger alone**. The 059 edges hold
identically for both bounds: the snapshot is pre-cancel, a step that finishes successfully inside
the cancel window is the terminal truth (one row, no `RUN_HUNG`), a host stop unwinds with no
row/alert, the retry is measured from the attempt's actual end, and a cancellation that is not
ours still enters the ladder. A mis-typed bound (beyond the timer's ~49.7-day range) is a
failed attempt on the ladder (the DR-1 path), never a hot refire. `TickAsync`, stream
independence (v1.5.41/062) and the retry ladder are untouched.

**Edge semantics — corrected (TKT-foundation-059, a repair note, not a redesign).** The merged
watchdog's three edges were one window short each, and the two round-2 branches were prose-only;
the mechanism above is unchanged, only where its guards sit:

- **The attempt prologue is inside the failure-classifying `try`.** The ledger row's state, the
  DI scope, both linked cancellation sources and the deadline task are created *inside* the same
  `try` whose `catch` produces the attempt's failure value. Before the repair an exception in
  that prologue — most realistically a mis-typed `Ingestion:Scheduler:MaxRunDuration` beyond
  `Task.Delay`'s accepted range (~49.7 days) — escaped `RunAttemptAsync` with `Attempts` already
  bumped and `NextAttemptAt` still in the past, so the hosted loop re-fired the attempt
  **immediately and unboundedly**: a hot refire that never reached `RETRIES_EXHAUSTED`. Such a
  prologue failure is now attempt *n* on the ordinary ladder.
- **The completion re-check runs *after* the cancellation too.** Declaring the hang cancels the
  attempt and awaits the deadline source; a step that swallows the cancellation can still finish
  successfully inside that window. The watchdog therefore re-checks
  `IsCompletedSuccessfully` after the cancel and, when the attempt did finish, takes its outcome
  as the terminal truth — one row (the attempt's own), no `RUN_HUNG` row and no retry. "Never
  both `succeeded` and `RUN_HUNG`" holds across the whole cancel window, not only up to it.
- **The hung-step snapshot is taken *before* the cancellation.** A cancellation-swallowing step
  can return `Partial` and let the abandoned chain `Begin` the next step, so a snapshot taken
  after the cancel could name the *next* step in both `job_code` and the alert. The snapshot is
  hoisted above the cancel: the record names the state the run was actually in when the bound
  expired.

Both boundary branches (completes against the armed bound → a completed attempt; host stopping
at the bound → no `RUN_HUNG`) are now pinned by tests rather than prose (see §13.1 v1.5.37).

**The post-cancel re-check is a bounded settle window (TKT-foundation-085, §13.1 v1.5.59).**
§10.9.17's edge repair above made "never both `succeeded` and `RUN_HUNG`" hold across the cancel
window — but by a **single synchronous re-check**, and that re-check only saw a step's terminal
state when the step's continuation resumed **inline on the cancelling thread**. When the runtime
*queues* the continuation — the normal async case under load — the re-check observed a pending
task, wrote `RUN_HUNG`, and the queued continuation then wrote the step's own success row: **two
`ingest_runs` rows for one attempt, a spurious `RUN_HUNG` alert and a spurious retry**
(demonstrated on PR #141's review round 2, escalation E3, with an `await` hop inserted into the
swallowing double; it is also the flake class of `TC_MDF_079_DR_2`/`TC_MDF_113` on any head). The
defect is an **observation** race, not a write race — the step's terminal state exists, the
synchronous read simply cannot see a queued continuation.

The re-check is therefore an **interval**: after `attemptCts.Cancel()` (and the awaited deadline
source cancel) the hung path races the attempt's task against a **clock-driven settle delay**,
`Ingestion:Scheduler:PostCancelSettleWindow` — code default **5 s**, `<= 0` disables it (the
documented opt-out, mirroring the bounds' disable semantics; a value beyond `Task.Delay`'s
accepted range is an ordinary failed attempt on the ladder — the DR-1 path, never an escape out of
the tick). The delay runs on the injected `TimeProvider`, so the grace is deterministic under a
fake clock exactly like every other scheduling decision here; no real-time sleep bounds a test.

- **The attempt settles inside the window and completed successfully** (`IsCompletedSuccessfully`
  and no failure) → the terminal truth, which is the existing DR-2 rule now holding for the whole
  window instead of only the inline interleaving: one row (the step's own), **no** `RUN_HUNG`, no
  alert, no retry. A task that completes inside the window but carries a `Failure` still goes
  hung — "a chain that merely *ended* on our own cancellation is not a success" is unchanged.
- **The window elapses first** → the hung path is **unchanged** (064's bounds included): exactly
  one `RUN_HUNG` row + one alert naming the pre-cancel step and the bound that fired, the ladder
  takes over, `DisposeWithAbandonedWork` disposal unchanged. The grace is stated honestly as having
  been *given*: `stats_json.settleWindowSeconds` (additive; `0` when disabled) plus the window in
  the row's `error`, the alert subject/body and the `Hung run detected` line, and a `Settling …`
  warning when it is armed. The record's timestamps and the ladder's punctuality are the **bound's**
  instant, never the grace's — `finished_at` and the retry base are the attempt's end (so 064's
  "the retry is measured from the attempt's actual end" is not shifted by five seconds).
- **The host-stop path is untouched** — it is checked *before* the settle, so a stopping host still
  unwinds with no row and no alert (DR-5b).
- **064's bounds stay load-bearing and unweakened**: the window is a grace *before* the `RUN_HUNG`
  write, not a widening of either bound. A genuinely hung step is still cancelled at its own bound
  and named once; the ceiling still bites; `<= 0` still disables each bound independently.

**Residual, recorded honestly.** A cancellation-swallowing step that completes **after** the window
still lands its own row beside `RUN_HUNG`. In production the runner writes through the attempt's
token (`SaveChangesAsync(cancellationToken)`), so a token-cancelled EF write throws and
`RunCoreAsync` rethrows the `OperationCanceledException` unrecorded — the residual is reachable
only through cancellation-swallowing steps, and for those the `RUN_HUNG` row naming the step that
exceeded its bound *plus* the grace window is the honest attempt record. If it is ever observed
live, the recorded durable follow-up is the **single per-attempt claim** shared by the runner's
terminal row and the watchdog row (threaded through `IngestionRunContext` into
`IngestionJobRunner.RecordAsync`): it makes the row write exclusive, but it does not decide *truth*
— when the watchdog wins the claim a genuinely completed step is still recorded hung, its truthful
row is suppressed and the spurious alert/retry persist — so it is the fallback, not the design.

**Stream independence — the due instant, not the loop release, decides (TKT-foundation-062, a
repair note).** The bound above stops a hang from being *unbounded*; it did not stop an attempt
from *postponing other streams' due instants* while it ran. `TickAsync` executed the due streams
in stream order and `await`ed each attempt to its end, and the hosted loop is one logical loop:
inside that await it cannot compute `NextWake`, delay, or tick. So, live on 2026-10-22/23, the
EOD chain's attempt 3 failed at 21:52:29Z, `Retry 3 … scheduled at 2026-10-22T22:52:29+00:00`
was logged — and the retry was **not re-evaluated** while another attempt held the tick; the
next morning chain (05:30Z due) started at **12:11:24Z**, the exact instant the EOD attempt's
`RUN_HUNG` released the loop, ~6h41m late, with the armed EOD retry queued behind it. The
engine now keeps evaluating due-ness itself while attempts are in flight: `TickAsync` starts
every due stream's attempt concurrently, and — for as long as any of them runs — waits on
`next due instant` versus `attempts`, restarting the due scan on the injected clock at each
pass. A stream's armed retry and a cron occurrence therefore start **at their own instant for as
long as the in-flight attempt is at an await**; a step that blocks synchronously still holds the
pump thread and still postpones the other streams' due instants (the unbounded class under
"Scope of the guarantee"). An in-flight stream is itself neither a wake nor a due candidate (its
attempt owns it until it ends, then its success/retry decision applies). The remaining lateness
bound for an attempt is its own `MaxRunDuration`. The pump also makes two streams able to run the
**same job for the same run date** at once (an EOD retry for D in flight while the morning chain
resolves D): the recompute/snapshot/medians lanes are idempotent and input-aware
(v1.5.34/§10.9.16), the prices coverage gate remains the only cross-run guard, and no wrong
stored value was demonstrated. The `TickAsync` contract is unchanged — when it returns, the attempts it started have ended — so
the sequential-tick callers (the hosted loop, the fake-clock harnesses) need no change.

**Scope of the guarantee — awaits, not synchronous blocks.** The bound is an in-process timer
racing the chain's task, so it pre-empts **any await**: a source call, the KAP pacer's delay, a
connection-pool or semaphore wait. It cannot pre-empt synchronous work: the chain is invoked on
the loop thread, so a step that blocks before returning its task holds that thread exactly as an
unbounded await did, and no in-process timer can interrupt it. That residual class is **not
silent** — `Chain step {Step}/{Total} '{JobCode}' starting` is written before the step is
awaited, so the freeze is named in the log — but it is not bounded by this watchdog. The
recorded 2026-10-19/20 incident class was an await: the scheduler had no duration bound anywhere
between the cron and the ledger.

The stream therefore **recovers**: the next wake proceeds, other streams' due runs are not
starved (and, since TKT-foundation-062, not postponed by an in-flight attempt that is at an
await — see "Stream independence"
above), and a hung night retries within the same overnight window instead
of waiting for the 08:30 morning safety net (§10.9.16).

**Chain-step progress is logged, per job.** Every step logs
`Chain step {Step}/{Total} '{JobCode}' starting …` **before** its await and
`… finished … with {Status} (written/unchanged/quarantined/skipped)` after it, and the
scheduler's startup line now names the configured bound. The 2026-10-19/20 incident was
undiagnosable from logs precisely because nothing was written between "prices done" and the
process restart; the next occurrence is one grep.

**Exception honesty between steps.** A failed job keeps its existing path (the runner's failed
row, then the ladder). A cancellation that is **not** the host's stopping token — a job-level
timeout, a source adapter's own CTS — is now a **failed attempt**: it previously escaped
`TickAsync`, which meant no ladder, no retry log, and the loop re-firing the same overdue
attempt in a hot loop.

**`universe-sync` leads the EOD chain.** The pinned order (`EodIngestChain.JobCodes`) becomes
`universe-sync → prices → statements → dividends → corporate-actions → disclosures →
metrics-recompute → market-snapshot → sector-medians` (nine jobs). Rationale: the job had **no
schedule at all** (admin trigger / cold start only), so the day's official index levels and any
membership change were a manual repair — Tuesday's exact closes (XU100 11,797.70 / XU030
14,676.53) existed only because the orchestrator ran the job by hand after the stalled chain.
Leading the chain is what makes the levels feed the same night's `market-snapshot` index moves,
and `prices`' constituent resolution runs against freshly synced membership. The job is
idempotent, provenance-marked, ~3 cheap İş Yatırım calls, and its launch-seed logic is
once-only. Fail-stop semantics are unchanged (a `Failed` job stops the chain): on the FU world,
whose canonical snapshot carries one deliberately unclassified instrument, the step lands
`Partial` (a documented `MISSING_CLASSIFICATION` quarantine) and the chain continues. The
**morning** chain is intentionally untouched — whether it also leads with `universe-sync` is a
one-line composition decision on the 040 phase-2 lane, not a new stream.

**Backward compatibility.** No schema change (`02` unchanged — the watchdog writes the existing
`ingest_runs` columns), no API/payload change (`03` §9 only), no new dependency, no
configuration required: an empty scheduler config keeps the pre-041 chain shape *modulo* the one
leading job, and `MaxRunDuration <= 0` restores the unbounded await. Test-side only: the two
suites that drive the production default chain rather than an explicit `JobCodes` list
(`EodStaleServingTests`, `MorningChainTests`) gained the universe/index-levels source stubs and
the +1 ledger-row expectation; no assertion was weakened.
#### 10.9.18 Intraday refresh lane — 2-hour session sweeps, probe-gated source, `intraday_quotes` and the intraday/close price-state contract (TKT-foundation-047, FR-MDF-001 intraday mode, FR-MOV-001/002/003, FR-RES-005..018 supply, NFR-MDF-001/002, BR-MDF-009)

**Why (the gap this closes).** Every price the product serves is an **EOD** close: `daily_prices`
is written once by the 20:30 chain (plus the 08:30 repair), so during a session the site shows
yesterday's close and the user's 2026-10-22 requirement — *"it should update the data in hour or
2 hour periods then so data is updated, and at close it should display close"* — is unmet. This
subsection pins the backend lane and the **frozen display contract** the Web half
(TKT-foundation-049, parallel, disjoint files) builds against.

**Cadence — four sweeps, configuration only.** A new stream job `intraday-quotes` runs on the
existing C3c engine with `Ingestion:Scheduler:JobSchedules` cron `0 15 11,13,15,17 * * 1-5`
Europe/Istanbul — 11:15 / 13:15 / 15:15 / 17:15 TRT, the last sweep 45 minutes
before the 18:00 close. (The ticket's five-field `15 11,13,15,17 * * 1-5` is the same cadence;
the shipped six-field form is the scheduler's `CronFormat.IncludeSeconds` expression.) **The
day gate is the cron's `1-5` and nothing else:** `JobSchedules` entries register as per-job
streams with `TradingDaysOnly = false`, so a Turkish weekday holiday still costs one ~400-call
sweep until TKT-foundation-048 wires the calendar into this stream — the draft wording
"trading-day gated" described the cron, not an engine-level gate. The
two-hour interval is one constant: a 1-hour cadence is a config edit, not a ticket. At the
100-constituent launch bound with `Ingestion:Intraday:RequestDelayMs` = 500 (the prices lane's
politeness posture, §10.9.2) a sweep is 1–2 paced minutes; four sweeps ≈ 400 calls/day.

**Source — honest and probe-gated, never a code fork.** The production prices source is İş
Yatırım `HisseTekil`, whose envelope is **daily-bar shaped** (`HGDG_TARIH` dd-MM-yyyy,
`HGDG_KAPANIS` close, no sub-day timestamp, no last-price field) and whose publication is
progressive (§043 evidence: symbols appear only around 20:30). **Whether it serves a same-day
row mid-session is not verifiable from code or captures** — so the lane rides the *existing*
envelope detection (§10.9.1/§10.9.2: the adapter already parses both the İş `HGDG_*` body and
the Yahoo `.IS` chart) behind its own configuration section `Ingestion:Intraday`
(`BaseUrl`, `Path` with `{symbol}`/`{date}` tokens, `UniverseIndex`, `RequestDelayMs`) and its
own keyed `ISourceClient` — deliberately separate from `Ingestion:Prices` so repointing the
intraday lane at the Yahoo contingency (documented in `PricesSourceAdapter`) never moves the EOD
lane. **The first live gate is a paced single-symbol mid-session probe** (orchestrator,
post-merge): if İş serves a same-day row the sweep uses İş; if not, `BaseUrl`/`Path` repoint at
Yahoo's delayed quote. Either way the served `asOf` is the real observation instant — freshness
is never fabricated. A symbol with **no same-day row is counted** (`IngestResult.Skipped` + a
`NO_DATA` log event naming it) — 043's no-data doctrine applied from the start.

**Source wiring (probe outcome, 2026-10-23).** The probe settled the question: `HisseTekil`
lagged — at 17:35 UTC (after the 15:00 UTC close) it returned **no 2026-10-23 row** for
ASELS/THYAO — while İş Yatırım's **delayed chart feed**
`/_layouts/15/IsYatirim.Website/Common/ChartData.aspx/IntradayDelay?period=5&code={symbol}&last=108`
served the live session's 5-minute bars (`{"data":[[epochMs, price], …]}`, price only).
`Ingestion:Intraday:Path` therefore ships the chart feed; the EOD prices lane keeps
`HisseTekil`. The adapter detects the envelope by the `data` member (not configuration) and
reads the **last bar of the latest session present in the payload**, deriving the session date
from the bar's own epoch-millisecond stamp — a payload whose newest bar is an earlier session
is the honest no-data class for today, never yesterday's price stamped with today's date. The feed carries
no volume, so `day_volume` stays NULL (the FLG-09(b) honest gap); the source stamp stays
`isyatirim` (same publisher and host as the daily resource, so the stored `source_ref` names
the source, not the route). Probe values: ASELS 2026-10-23 15:00 UTC = 338.00, GARAN = 126.50,
ISCTR = 12.17, THYAO = 258.00.

**Storage — `intraday_quotes`, never `daily_prices`.** New table: `instrument_id`,
`session_date`, `fetched_at` (timestamptz, UTC), `last_price`, `day_volume` (nullable),
`source_ref`; one row per (instrument, session). A sweep **updates in place** (042's
input-aware-overwrite precedent applied per sweep): identical value → counted `unchanged` but
`fetched_at` advances (it records when the value was last *observed*); changed value → the row is
overwritten and counted `rewritten`; **prior sessions are immutable**. An intraday value is
**never** written to `daily_prices` and never becomes a close — the EOD chain remains the only
writer of closes. Yahoo's share-count `volume` stores **NULL** (FLG-09(b): the stored semantic
is ₺ turnover; a different unit is an honest gap, not a mislabel).

**The display contract — `priceState` (FROZEN seam with TKT-foundation-049).**
`GET /api/v1/market/overview` and `GET /api/v1/stocks/{symbol}/overview` gain
`priceState: { state: "intraday" | "close", lastPrice, prevClose, changePct, asOf, sessionDate }`,
or `null` when neither store has data:

- **During a session after ≥1 sweep** → `intraday`: the sweep's `last_price`, `prevClose` = the
  previous stored session's `daily_prices.close_raw` (Mondays vs Friday), `changePct` =
  `(lastPrice − prevClose)/prevClose` as a **decimal fraction** rounded to 6 dp (`03` §2/§4,
  036's unit contract), `asOf` = the sweep's `fetched_at`, `sessionDate` = the described session.
- **From the 18:00 close until the EOD chain lands (~20:30)** → **still `intraday`** with the
  last sweep's `asOf`. Planner-pinned over an 18:00 freeze: the close-in-formation must never
  masquerade as final; the honest reading is "last update HH:MM — not final".
- **Once the session's EOD close is stored** → `close`, sourced from `daily_prices`
  (`close_raw`), `asOf` = that row's `recorded_at`.
- **Weekends/holidays** → `close` of the last **completed stored** session (§10.9.13
  stored-prices-as-truth: a Saturday clock never fabricates a Saturday session). TKT-foundation-048
  extends the calendar to holidays.
- `state` is `intraday` exactly when the latest intraday session is **later** than the latest
  stored close session; otherwise `close`.

**Market-overview numerics are NULL in this lane.** The sweep stores instrument quotes only; no
index-level intraday level exists, and deriving one at the display seam would be a derived
display value (BR-MDF-009). The overview therefore serves `state`/`asOf`/`sessionDate` with
`lastPrice`/`prevClose`/`changePct` **NULL** — honest nulls, never a 0 reading — while the stock
page carries the full numeric block. If a market-level intraday level is later wanted it is an
aggregate (a Core computation) and its own change record.

**Accounting note.** `IngestResult` carries `written`/`unchanged`/`skipped`; the `rewritten`
count rides a run stat log line (`rewritten {Rewritten}`) because `src/Ingestion/Jobs/` is
outside this ticket's scope (043's lane). Folding it into the ledger stats is a later ticket.

**Backward compatibility.** The EOD chain, the morning chain (§10.9.16), the retry machinery and
the job runner are untouched; `daily_prices` semantics and every freshness rule (§10.9.13) are
unchanged — the contract is purely additive. The only shared surface is `src/Api/appsettings.json`
(the new section + one `JobSchedules` entry). The `intraday_quotes` schema is recorded here
(§13.1 v1.5.27, single-row append) and the migration in `src/Persistence/Migrations`; mirroring it
into `02-data-model.md`'s schema section is the architect's next documentation pass (this ticket's
doc scope is `01` only).

#### 10.9.19 The KAP public-site source contract — politeness, the ToS/robots gate, evidence class, identifier spaces, honest statuses and the bounded walk (TKT-foundation-079, FR-MDF-002/008/012, NFR-MDF-001)

**A website, not a contracted API — and a separate source.** The public site
(`https://www.kap.org.tr`) carries rich disclosure rows and the full XBRL-tagged financial
report as HTML, unauthenticated, verified live 2026-10-25 (`POST
/tr/api/disclosure/members/byCriteria`, `GET /tr/api/notification/export/excel/{disclosureIndex}`,
`POST /tr/api/search/combined` for identity; the evidence base is
`.pipeline/plan/kap-public-source-investigation.md`). It is **not** the VYK gateway of §10.9.11:
different base address, different client, different pacing, different options, different index
space. The lane therefore gets **its own source name and its own DI wiring**
(`Ingestion:KapPublic`, keyed `HttpClient` `"kap-public"`, `src/Ingestion/KapPublic/`) and the VYK
adapter is **untouched** — switching between them is a configuration selector, never code
removal. Nothing in this section amends §10.9.10/§10.9.11 or any VYK cursor.

**Evidence class — `kap-public`, never `kap` (FR-MDF-008).** A public-site HTML/JSON payload and
a VYK JSON payload are different evidence classes. Every fact this lane writes carries
`SourceName`/`SourceRef` = **`kap-public`**, so a stored row's provenance always answers *which
source produced it*. Re-stamping a public-site read as `kap` would silently merge two sources'
evidence and is a contract violation.

**Identifier spaces are three, and never mixed.** The lane touches (a) the public site's opaque
company oid — ASELS `4028e4a1413b7ef401413bc2251e0047` (from `search/combined`'s
`memberOrFunds` hit for keyword `ASELS`); (b) the public site's **own** `disclosureIndex` space —
ASELS 2026-06 FR = **`1643141`**, whose list window on 2026-01-01→2026-10-25 returned FR filings
`1643141`/`1598316`/`1561039` among ten rows; and (c) the VYK gateway's `companyId` (ASELS
**`866`**) and `disclosureIndex` space (head **`1231017`**). These are demonstrably different
spaces (`1643141 ≠ 1231017`). A public index is **never** written into a VYK cursor, never
compared to a VYK index, and never resolved as a VYK id; a mapping store (080) keeps all three
apart explicitly.

**Politeness is the contract, not an operational nicety.** The shipped mechanics:

- **Pacing** — `Ingestion:KapPublic:RequestDelayMs` = **3000 ms**, per lane, and every request
  **starts** at least that long after the previous request started (**start-to-start**, on the
  injected clock — a slow response never lets the next start come sooner). The client holds
  **one flight gate**: a second request cannot start while one is in flight, so the lane is
  **never parallel** against kap.org.tr, by construction rather than by convention.
- **A realistic, honest User-Agent** — browser-shaped (the edge expects that shape) with an
  honest self-identification token appended: `Mozilla/5.0 (X11; Linux x86_64) … Chrome/124.0
  Safari/537.36 Degerli/0.1` (**no parenthetical comment** — the parenthetical spelling is
  WAF-blocked with HTTP 666 on every route; see the v1.5.66 correction in §13.1 and the
  "code default" bullet below). The lane identifies itself; it does not impersonate a human
  visitor. Whether the edge accepts a bot-only UA was **not** probed — probing with a
  deliberately lying UA is not an acceptable politeness experiment.
- **Timeouts** — general (list/identity) request `RequestTimeoutMs` = **30,000 ms** (the list
  body is a few KiB: the captured ASELS FR window is 4,862 bytes); per-item detail
  `DetailFetchTimeoutMs` = **60,000 ms** (the full report is ~1.8 MB: 1,819,412 bytes captured
  for `1643141`). Pacing is not counted against a timeout — the walker paces first, then starts
  the item's clock.

**Boundedness — work caps *and* a wall clock, and a bounded pass resumes.** `MaxListPages` =
**40** (× 3 s = **120 s = 2 min** of list pacing) and `MaxDetailFetches` = **200** (× 3 s =
**600 s = 10 min**) give a **nominal ≈ 12 min** walk. That bounds *work*, not *time*: a stalled
detail is bounded per item at 60 s, so a degraded site could drive 200 × 60 s ≈ 3 h 20 min out of
one walk — past the **120-min per-step bound** (§10.9.17, v1.5.43). The walk therefore carries
its own deadline, `MaxWalkDuration` = **90 min**, strictly inside that bound, checked before
every list and detail request on the injected `TimeProvider` (a fake clock in L2, so no real
sleeps and deterministic tests). On expiry — or on either cap — the pass records `truncated`
with its reason code, stops at the last unit it **completed**, and the next run resumes exactly
there. A bounded pass is an honest bounded pass, never a failure and never a silent gap (the
078 `WalkBudget` semantics, §10.9.15). The mechanics (`KapPublicWalkBudget`,
`KapPublicRequestPacer`) land with this section; **080–084 must consume them — a walk that does
not hold a budget is a contract violation**, so the deadline obligation is inherited by the
downstream tickets rather than lost here.

**Honest statuses, each with exactly one named reason (NFR-MDF-001).** Four outcomes, closed
set — `Ok`; `HonestEmpty` (`HONEST_EMPTY_WINDOW`: a well-formed, empty window is a named skip,
never an error and never a backwards cursor); `Quarantined` (`UNPARSEABLE_PAYLOAD`: a 200 body
that fails **shape** parse is quarantined with the raw body verbatim — the export route declares
`application/vnd.ms-excel` while the body is HTML, so detection is by shape and never by declared
content type); `SourceDown` (`SOURCE_FETCH_FAILED` for a transport/non-success answer,
`SOURCE_REQUEST_TIMEOUT` for a per-item timeout) — the run is `failed`, nothing is fabricated.
Walk truncations add `WALK_PAGE_CAP`, `WALK_DETAIL_CAP`, `WALK_DEADLINE`. The refusal postures add
`AUTOMATED_ACCESS_FORBIDDEN` and `ACCESS_POSTURE_NOT_VERIFIED` (below). No fabricated row, no
synthesised value, no status without a reason.

**The ToS/robots gate (OQ-2) — no policy document is served; the operator proceeds on a recorded decision.** Two paced probes on 2026-10-25, recorded verbatim and distinguished:

- `GET https://www.kap.org.tr/robots.txt` → **HTTP 666**, **528 B**: the site's generic HTML
  access-error page for that path. It is **that path's error page — not a policy refusal
  statement**. Captured verbatim into `RealSourceCatalog` as **`kap-public-robots`** (status 666
  included).
- `GET https://kap.org.tr/robots.txt` → **HTTP 307** → `/en/robots.txt` → **HTTP 404** (the
  Next.js not-found page). Recorded from the orchestrator's probe record
  (`.pipeline/plan/run-report.md`, ~07:00 UTC 2026-10-25).

**Conclusion, stated plainly:** **KAP serves no `robots.txt` at all.** Under RFC 9309 an absent
(404) `robots.txt` states no rules, so **no automated-access rule is served or violated** — the
lane proceeds under its politeness contract. This is **not** an "Allowed" finding: no policy
document is served, and that absence is not read as consent. The decision is recorded, not
inferred: **user decision, 2026-10-25 (ratified proceeding on the recorded probe evidence)**, and
the posture is re-checked at V0 alongside the RISK-MDF-001 per-source terms pass.

The 666 body, verbatim:

```html
<html>
<head>
<meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
<meta http-equiv="Content-Type" content="text/html; charset=iso-8859-9" />
</head>
<body>
<div style="margin-left:30%;margin-top:10%">
<a href="https:/www.kap.org.tr"><img src="https://www.kap.org.tr/assets/images/errors/hata_500_transparent.png" style="border:none"/></a>
<font face=arial />
<p>Yasadiginiz bu erisim problemini "349891-PPE13" hata numarasi ile </p>
<p>kapdestek@mkk.com.tr adresine bildirebilirsiniz.</p>
</div>
</body>
</html>
```

**The judgment is expressed as configuration and behaviour, not prose.** There is no `User-agent`
line and no `Disallow` in the 666 answer (it is an error page), and the apex path serves no
policy document at all. `Ingestion:KapPublic:AutomatedAccess` is a three-valued recorded posture:

- `NotVerified` — the **code default** (`new KapPublicSourceOptions()`): the client refuses to
  issue **a single request** with `ACCESS_POSTURE_NOT_VERIFIED`. A posture nobody chose is not a
  permission.
- `Allowed` — the ratified value, **chosen explicitly in `appsettings.json`** (never inherited
  from a code default).
- `Forbidden` — a recorded prohibition: the client refuses with `AUTOMATED_ACCESS_FORBIDDEN`.

Both refusals throw `KapPublicAccessRefusedException` **before pacing and before the flight
gate**, so a posture can never be silently walked past (TC-MDF-104 (c)/(c2)). Had a prohibition
been served, this ticket would have stopped and surfaced to the user rather than shipping
`Allowed`.

**The OQ-1 market-wide probe — recorded verbatim, and it gates 084.** One paced `POST
byCriteria` with an **empty** `mkkMemberOidList` and an empty `disclosureClass` over
2026-10-24→2026-10-25 answered **HTTP 200 with 12 rows** — the market-wide shape **works**.
Committed as `kap-public-bycriteria-market-probe`. Capture record for this committed body: one
paced `POST /tr/api/disclosure/members/byCriteria` on 2026-10-25 (**~03:xx UTC**), with the
complete request `{fromDate: 2026-10-24, toDate: 2026-10-25, disclosureClass: "", subjectList: [],
mkkMemberOidList: [], inactiveMkkMemberOidList: [], bdkMemberOidList: [], fromSrc: false,
disclosureIndexList: []}`. It is a **later probe** than the investigation's 02:0x UTC market
probe (which printed newest `1687040` / stockCode `KT`); both are honest probes on different runs
and the two are **not reconciled into one canonical index**. Verbatim characteristics: classes
**DKB ×4, FR ×3, ODA ×3, DG ×2**; newest index **`1687047`** (24.10.2026 17:08:16, RODRG);
the production test entity **`KAP TEST A.Ş.` / `KTEST`** is present (indices `1687044`,
`1687036`), as are rows with **`stockCodes: null`** (BISTECH circuit-breaker notices). Two
consequences are pinned: 084's market-wide feed is implementable on this evidence, and **any
exclusion of `KTEST`/test rows or of null-`stockCodes` rows must be a named, recorded rule with
its count** — never a silent drop (TC-MDF-104 (ii)). An empty answer would have been committed
just as honestly; it did not occur.

**The configuration contract is pre-wired for the whole lane (079–084).** `Ingestion:KapPublic`
ships the full key set so later tickets never re-touch `appsettings.json` for a shared concern:
`BaseUrl`, `UserAgent`, `RequestDelayMs`, `ByCriteriaPath`, `ReportExportPath`,
`IdentitySearchPath`, `CompanyPagePath`, `RobotsPath`, `MaxListPages`, `MaxDetailFetches`,
`DetailFetchTimeoutMs`, `RequestTimeoutMs`, `MaxWalkDuration`, `AutomatedAccess` — plus the two
**inert** source selectors `Ingestion:Statements:Source` and `Ingestion:Disclosures:Source`, both
shipping `"vyk"`. A selector nobody reads yet is a default, not a switch: no lane flips silently,
and the shipped constants are pinned as config-equivalence assertions in L2 (TC-MDF-103 (i)).

**The committed double set — paced captures, honestly labeled.** Five named captures under the
`kap-public` source, captured 2026-10-25: `kap-public-robots` (verbatim, above);
`kap-public-bycriteria-asels-fr` (verbatim, ten rows — the FR filings `1643141`/`1598316`/
`1561039` plus their companion responsibility-statement/activity-report rows); the OQ-1 market
probe (verbatim); `kap-public-identity-ASELS` (verbatim, one `memberOrFunds` hit); and
`kap-public-report-1643141` — **bounded-trimmed, not byte-verbatim**, and labeled as such: the
live answer was 1,819,412 bytes / 731 tables / 1,467 rows, and the committed ~440 KB trim keeps
the header metadata and all four statement tables plus the **complete** cash-flow table
(`Nakit Akış Tablosu (Dolaylı Yöntem)`), which is 081's decode input. No credential appears in
any body. **No test in CI calls kap.org.tr** — the captures are the doubles.

**The stored company identity map — three identifier spaces, the resolution path and the honest
unmapped skip (TKT-foundation-080).** The `byCriteria` list is queried by the public site's
`mkkMemberOidList` (the company **oid**), so every lane above this one needs a ticker → oid
bridge. That bridge is **stored, never assumed**: `kap_public_company_map` (forward-only additive
migration, `02` §3.1/§8) holds **one row per instrument**, keyed by our `instrument_id`, and the
three identifier spaces live in **three space-labelled columns**:

| column | space | notes |
|---|---|---|
| `instrument_id` | **ours** (PK, FK → `instruments`) | un-mapped instrument ⇒ **no row** (honest absence, never a placeholder) |
| `kap_public_oid` | **the public site's** opaque company oid (ASELS `4028e4a1413b7ef401413bc2251e0047`) | CHECK `^[0-9a-f]{32}$` |
| `kap_public_oid_source`, `kap_public_oid_as_of` | provenance of the oid — **which body, which date** (FR-MDF-008) | the paced identity route plus its keyword, and the run's resolution date |
| `vyk_company_id` | **the VYK gateway's** numeric `companyId` (ASELS `866`) | nullable; CHECK `^[0-9]{1,12}$` |
| `vyk_company_id_source`, `vyk_company_id_as_of` | provenance of the VYK id, **separate** from the oid's | CHECK: id and its provenance are present or absent together |
| `recorded_at` | when the row was last written | an `unchanged` re-resolution does not move it |

**The isolation is enforced by the store, not only by code.** The oid column refuses a numeric
companyId and the VYK column refuses an oid (both CHECKs), so writing one space's identifier where
the other's belongs fails at the database; ASELS's oid and `866` coexist as **recorded facts about
different spaces**, never as translations of each other. In code the spaces are distinct types —
`PublicCompanyOid` (32 hex) and `VykCompanyId` (decimal digits) — with **no** conversion operator
between them, and only shape-checking parses construct them. A VYK id is recorded **only** when a
run holds that VYK-space evidence (its own source string and date); it is never derived from the
oid.

**The resolution path.** `KapPublicCompanyIdentityResolver` walks the bound universe **strictly
sequentially** on 079's paced, single-flight client: for each binding it reads
`POST /tr/api/search/combined` with `{"keyword":"<ticker>"}` (`KapPublicIdentityReader`, which
classifies the answer into the lane's honest statuses). A hit binds **only** when it is a
*company* hit (`searchType` `C`) whose `cmpOrFundCode` is the bound symbol and whose
`memberOrFundOid` parses as a public oid; a hit for another company is not a match. Then:

- **unmapped** — a bound symbol with no such hit is skipped, **named** and **counted**
  (`COMPANY_IDENTITY_UNRESOLVED`, the same 044 vocabulary word the VYK lane records, applied to
  this source), logged by name, and **never fetched**; the loop continues;
- **ambiguous** — two distinct oids for one symbol is `COMPANY_IDENTITY_AMBIGUOUS`: never resolved
  by picking one (that would be a guess);
- **source-down** — a transport failure, a timeout, or a body that is not the identity shape ends
  the pass as `source-down` with its reason (`SOURCE_FETCH_FAILED` / `SOURCE_REQUEST_TIMEOUT` /
  `UNPARSEABLE_PAYLOAD`): the run reports the failure and **stores nothing**, so no fabricated or
  partial mapping can exist. The VYK directory's failure posture (§10.9.11) is the precedent.

**Re-resolution is idempotent.** Identical resolution input leaves the rows `unchanged` (no write,
no timestamp move); a changed identity or a new evidence date repairs **the same row** in place —
still exactly one row per instrument, never a duplicate. Consumers read the store
(`GetPublicOidAsync` / `GetPublicOidsAsync`), never re-deriving a mapping per walk; this is what
082's statements walk and 084's disclosures feed consume.

**Wiring repair recorded with this section.** `KapPublicHttpSourceClient` normalizes its
`HttpClient`'s base in the constructor, and 079 landed that client over a **singleton** keyed
`HttpClient` — so the second run's client tried to re-set `BaseAddress` on an already-used
`HttpClient` and threw (its own message: *properties can only be modified before sending the
first request*). The keyed `HttpClient` is now **scoped** (one per run, the lifetime the scoped
pacer and client already implied): no shared mutable base, and every run after the first works. No
079 assertion changed (its suite re-runs green).

**Backward compatibility.** Additive only: one new options section, one new `src/Ingestion/
KapPublic/` seam, one new DI block, five catalog entries, one test file. `daily_prices`,
`financial_statements`, every VYK cursor and every freshness rule (§10.9.13) are unchanged, and
the VYK lanes' suites re-run green (`VykStatementsSourceTests`, `StatementsAndEventsIngestionTests`,
`VykCompanyDirectoryTests`, `CannedSourceCatalogTests`). **080's additive delta on top of the
above:** one new table + one forward-only migration (`kap_public_company_map`), three scoped
services (identity reader, mapping store, resolver) in the same `src/Ingestion/KapPublic/` seam,
one test file — plus the scoped-`HttpClient` wiring repair recorded above. No VYK code, cursor,
table or assertion is touched; `VykCompanyDirectoryTests` and `MemberSecuritiesIngestTests`
re-run green with it. Downstream lane: 080 identity mapping, 081 report decode, 082/083 statements
supply, 084 disclosures feed — all against this section, with §10.9.19 extended (not replaced) by
each.

**The FR report decoder — the excel-HTML tables onto the canonical statement model, cash flow
included (TKT-foundation-081).** `KapPublicReportDecoder.Decode(html, symbol)` reads one
`GET /tr/api/notification/export/excel/{disclosureIndex}` body into a
`StatementsPayload` **plus** the provenance and the honesty ledger the decode must be able to
account for (`KapPublicReport`: header, tables/columns, per-value lines, counted findings).
It is an **HTML decoder for this source only** — a different presentation from the VYK
presentation tree, a different label space, and therefore its own label→canonical map
(`KapPublicReportDecoder.LabelMap`, each entry naming the committed capture that supplied the
spelling). `StatementMapper` and `KapXbrlStatementDecoder` are **untouched**: both maps collapse
onto the same canonical chart-of-accounts codes (`02` §6.1 one-definition doctrine).

- **Shape, then parse.** `LooksLikeReport` is shape detection (`financial-table` +
  `taxonomy-field-title`), never the declared content type — the route lies
  `application/vnd.ms-excel` over HTML (§10.9.19's `UNPARSEABLE_PAYLOAD` rule reused verbatim).
  Parsing uses **AngleSharp 1.8.6** (a new `Degerli.Ingestion` package): the export nests the
  label panel and each value's `gwt-Label` inside the row's `<td>`s as **nested tables**, which
  defeats regex row matching (verified against the capture) and is exactly what a DOM parser is
  for. Hedge: the export still ships the old GWT CSS classes, so a KAP redesign of this export
  breaks the decoder — the 068 source-contract drift probes must cover this route (recorded
  follow-up, not this section's claim).
- **The raw integer is the read path.** Every value cell carries the undisplayed integer in its
  `title=""` attribute (`title="39468926"` beside the displayed `39.468.926`). The attribute is
  the value; the displayed text is a **cross-check only**, and a disagreement is a counted
  `DISPLAY_TEXT_MISMATCH` finding while the attribute still wins — never a silent override, and
  the decoder never "repairs" a value from the display text. This removes the Turkish
  number-format risk from the value path (it remains only for header dates and the presentation
  unit, both parsed culture-invariantly). A cell whose attribute is unreadable is a counted
  `VALUE_UNATTRIBUTED` drop — never `0`, never a fallback to the display text.
- **Column→period comes from the column headers, never from position.** Every table's value
  cells carry `col-order-class-N`; the table's own `context-header` cells are the headers, and
  `N − 4` is the header index (verified on all four captured tables: BS 2 headers/2 value
  columns, IS 4/4, CF 2/2). A header resolves to a period end (the last date it carries), an
  instant-vs-range duration, and its `Cari`/`Önceki` role. Headers are read into
  `KapPublicColumn` records and paired to value columns by **ordinal position**, guarded by a
  header-count/class-contiguity check (`COLUMN_HEADER_MISMATCH`): a table whose
  `col-order-class` values are not exactly the contiguous run from its first value column, or
  whose distinct count differs from its `context-header` count, is skipped **whole** rather than
  paired shifted.
- **Sub-period columns are named and skipped, never merged.** The captured IS carries four
  headers: two 6-month ranges (this report's own period) and two 3-month Q2 ranges ending on the
  **same dates**. The store's natural key `(periodType, periodEndDate, statementType)` cannot
  distinguish two durations ending on one date, so the 3-month columns are skipped whole and
  counted (`SUB_PERIOD_COLUMN` — 6 cells in the capture) rather than overwriting the year-to-date
  row. A table with **no** column headers (the captured `Özkaynaklar Değişim Tablosu`, an equity
  matrix whose column headers are not `context-header` cells) attributes nothing: every valued
  cell is an honest-null drop counted as `NO_COLUMN_HEADERS` (98 in the capture) and **no EQ
  statement row is emitted** — the honest gap, never a period defaulted onto the values.
- **Statement typing by the captured heading.** `Finansal Durum …`/`Bilanço` → `BS`,
  `Kar veya Zarar …`/`Gelir Tablosu` → `IS`, `Nakit Akış …` → `CF`, `Özkaynak …` → `EQ`; an
  unrecognised heading skips the table whole with `UNKNOWN_TABLE_HEADING`. The export's
  `tbl_general_role_*` class is recorded for traceability but is **not** the typing mechanism
  (it is a taxonomy-version artefact).
- **Section before label — the duplicate-label defence.** The cash-flow table repeats labels
  across its blocks and shares `Dönem Karı (Zararı)` with the income statement. Each row's
  section is resolved **first**, from the capture's own aggregate rows
  (`İŞLETME …`/`YATIRIM …`/`FİNANSMAN FAALİYETLERİNDEN NAKİT AKIŞLARI`, and the closing block
  `NAKİT VE NAKİT BENZERLERİ`/`ENFLASYON ETKİSİ`/`YABANCI PARA ÇEVRİM FARKLARININ ETKİSİNDEN
  ÖNCE …`) → `CF_OPERATING`/`CF_INVESTING`/`CF_FINANCING`/`CF_SUMMARY`. The label map is then
  applied **within the section's statement**, and an unmapped label keeps its honest passthrough
  code `OTHER_{section}_{Slug(label)}` (which `StatementMapper.Map` passes through unchanged and
  metrics ignore). Section scoping is what keeps two legitimately repeated labels two lines
  instead of a "duplicate".
- **Duplicate rule — the TC-MDF-065 doctrine, applied to this source.** Within one statement row
  `(periodType, periodEndDate, statementType)`, a second value for the same code with an **equal**
  value collapses idempotently (one payload line, one provenance line, no finding); with an
  **unequal** value it is a counted `DUPLICATE_LABEL_CONFLICT` (one per column) that retains
  **both** values verbatim in the finding, keeps the **first** value in the row and records the
  code in `ConflictingItemCodes` so the store quarantines the row (`CONFLICTING_VALUE`, 02 §1 /
  DR-05 overturned) — never a silent latest-wins.
- **Period type is surfaced, never inferred downstream (user decision 2026-10-25: annual FCF +
  labelled interims).** `Periyot` maps only from the attested pairs —
  `1 → 3 Aylık`, `2 → 6 Aylık`, `3 → 9 Aylık`, `4 → Yıllık` (attested by the committed
  `byCriteria` captures' own `year`/`period`/`ruleType` rows; a non-FR filing in the same capture
  carries a `period` that is **not** a financial period and is not read as an attestation);
  anything else is **null** plus a counted `PERIOD_TYPE_UNKNOWN`. The store's `PeriodType` fails
  safe: only an attested annual period writes `FY`, everything else (interim **and** unknown)
  writes `Q`, so the DCF baseline's FY filter can never consume a half-year. The declared period
  is also cross-checked against the durations the report's own column headers declare; a
  disagreement (e.g. a 6-month report self-declaring `Periyot:4`) is counted
  (`PERIOD_DURATION_MISMATCH`) and fails safe to `Q` for **every** statement row, instant
  balance-sheet columns included. Nothing is annualized here; the interim's own
  `periodEndDate`/`fiscalYear` are its own.
- **Units are the store's vocabulary, not a re-derivation.** `Sunum Para Birimi`
  (`1.000 TL`) → `UnitScale = thousands`, `Currency = TRY`; an unrecognised unit leaves both null
  (counted `PRESENTATION_UNIT_UNRECOGNISED`) so the store refuses the payload rather than scaling
  wrongly (02 §3.1, TC-MDF-055).
- **Cash flow decodes, and FCF computes through the existing formula.** The captured indirect
  CF table yields one `CF` statement row per column (current and comparative, each with its own
  period end): `OCF` from the statement's own operating total `İŞLETME FAALİYETLERİNDEN NAKİT
  AKIŞLARI` — the figure **after** tax paid and other operating payments (user decision
  2026-10-26, DR-2: KAP reports this row *as* operating cash flow); `CAPEX` from the combined
  `Maddi ve Maddi Olmayan Duran Varlıkların Alımından Kaynaklanan Nakit Çıkışları` row (its
  tangible/intangible children are **not** mapped — the combined row is the CAPEX definition, and
  double-counting it would inflate |CAPEX|), plus `NI`, `DEPR_AMORT`, `ΔWC` and closing `CASH`
  where the capture evidences them. The before-tax operating subtotal
  `Faaliyetlerden Elde Edilen Nakit Akışları` is deliberately **not** mapped onto `OCF`; it stays
  a decoded, labelled `OTHER_CF_OPERATING_*` companion so both measures remain visible instead of
  being equated.
  FCF is `CanonicalMetrics`'s unchanged `OCF − |CAPEX|` with the NULL-if-missing rule preserved,
  and the store applies the unit scale. The captured goldens: OCF `15.206.440` / `18.013.334`,
  CAPEX `−33.746.150` / `−17.190.837`, and the capture's own cash ladder cross-validates the
  column→period alignment (current opening cash `34.251.653` = the BS prior column's cash,
  current closing `39.468.926` = the BS current column's).
- **Honest ledger, closed reason set.** `NO_COLUMN_HEADERS`, `COLUMN_UNATTRIBUTED`,
  `COLUMN_HEADER_MISMATCH`, `VALUE_UNATTRIBUTED`, `LABEL_UNATTRIBUTED`, `DISPLAY_TEXT_MISMATCH`,
  `DUPLICATE_LABEL_CONFLICT`, `HEADER_FIELD_MISSING`, `PRESENTATION_UNIT_UNRECOGNISED`,
  `UNKNOWN_TABLE_HEADING`, `SUB_PERIOD_COLUMN`, `PERIOD_TYPE_UNKNOWN`,
  `PERIOD_DURATION_MISMATCH`, `PERIOD_SEMANTICS_UNDETERMINED`, `NO_CANONICAL_LINES`,
  `DUPLICATE_LABEL_DISAMBIGUATED`, `BLOCK_UNDETERMINED`. Every dropped
  cell, skipped column and disagreed label is named and counted with a bounded verbatim excerpt
  (the detail is clipped at
  `MaxExcerptChars`, the 079/TC-MDF-104 precedent); nothing is zero-filled and nothing is silently
  overwritten. A statement that decodes lines but maps **none** to a canonical code is counted
  `NO_CANONICAL_LINES`, so a label-spelling drift surfaces as a named finding instead of a clean
  ledger with an empty metric.
- **Recorded gaps (flagged, not papered over).** `fin_line_items` has no label/table provenance
  column, so the "which table, which label, which column, which cell" answer lives on
  `KapPublicReport.Lines` at decode time; persisting it per row is an additive schema change and
  its own ticket. Evidenced share-count lines (`pay adedi`) are **never** consumed, divided or
  relabelled here — the finding routes to TKT-foundation-065's option decision. The decoder writes
  no row itself: 082/083 own the walk and the store call.

**IS/BS coverage, the debt/cash core and the repeatable-passthrough rule (TKT-foundation-088).**
The decoder above shipped with the cash-flow deliverable, and its first live walk exposed the
gap this subsection closes: 62 statements across 7 companies came back **60 CF / 2 IS / 0 BS**,
which is why `GET /stocks/{symbol}/dcf` answered `state: "missing_inputs"` naming
`['debt','cash']`. The diagnosis, recorded on the ticket and reproduced here because it corrects
the premise: the captured live reports were **not** undecodable — they decode 146–174
balance-sheet lines and 66–94 income-statement lines — but only **2–6** of those lines mapped to a
canonical code, and the statement row was then **quarantined whole** by `DUPLICATE_LABEL_CONFLICT`
on `OTHER_*` passthrough codes (the balance sheet legitimately repeats `Ticari Alacaklar`,
`Peşin Ödenmiş Giderler`, `Finansal Yatırımlar`, `Türev Araçlar` and the `ARA TOPLAM` sub-total in
its current and non-current blocks; the income statement repeats `Ana Ortaklık Payları` across its
profit-allocation and comprehensive-income sections). Two changes follow.

- **The label map's coverage, evidenced capture by capture.** The map is extended only with
  spellings a **committed** body carries, and each entry names that body in
  `KapPublicLabelRule.Provenance` (the provenance set is `KapPublicReportDecoder.CaptureNames`;
  a spelling outside it is un-evidenced by construction). The income statement gains its own
  sub-total ladder — `ESAS FAALİYET KARI (ZARARI)` → `EBIT`, `Genel Yönetim Giderleri` → `SGA`,
  `Finansman Giderleri` → `FIN_EXP`, `SÜRDÜRÜLEN FAALİYETLER VERGİ ÖNCESİ KARI (ZARARI)` →
  `PRETAX_INC`, `Sürdürülen Faaliyetler Vergi (Gideri) Geliri` → `TAX_EXP`, `DÖNEM KARI (ZARARI)`
  → `NI` (from the ASELSAN 2025 Yıllık body) — and the balance sheet gains the **debt/cash core**
  `Kısa Vadeli Borçlanmalar` → `ST_DEBT`, `Uzun Vadeli Borçlanmalar` → `LT_DEBT` (financial
  borrowings only, from the TÜPRAŞ 2025 Yıllık body) plus `TOPLAM ÖZKAYNAKLAR` →
  `TOTAL_EQUITY`, `TOPLAM DÖNEN VARLIKLAR` → `CUR_ASSETS`, `TOPLAM KISA VADELİ YÜKÜMLÜLÜKLER` →
  `CUR_LIAB`. Every target reuses the canonical assignment the VYK concept map already makes for
  the equivalent IFRS element (`02` §6.1 one-definition doctrine, never forked). **Trade payables
  and every operating liability are deliberately not debt**, and a capture carrying only a
  combined debt figure would be a recorded finding routed to the planner, never a guessed split.
- **A repeated passthrough label is two real lines, not a conflict — the rule is scoped to
  `OTHER_*` (user-approved 2026-10-27).** Within one statement row, an unequal second value for an
  `OTHER_*` passthrough code is now kept beside the first under a counted, deterministic
  `{code}_{n}` (ordinal from 2), with one `DUPLICATE_LABEL_DISAMBIGUATED` finding per kept line
  naming the label and the kept code verbatim; the row is **not** quarantined and nothing is
  overwritten. Equal values still collapse idempotently, **canonical** codes keep the rule above
  unchanged (unequal → counted `DUPLICATE_LABEL_CONFLICT` → `ConflictingItemCodes` →
  `CONFLICTING_VALUE` quarantine), and a first-occurrence passthrough code stays byte-identical so
  v1.5.55's 1643141 goldens (OCF/CAPEX/FCF and every line count) hold unamended. Metrics ignore
  `OTHER_*` either way, so the suffix invents no figure.
- **The recorded, routed gap (not guessed here).** The current portion of long-term borrowings
  (`Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları`, 12.863.610 thousand TL in TÜPRAŞ's FY2025
  capture, beside `Kısa Vadeli Borçlanmalar` 8.957.100) is a **separate presentation line**: a
  1:1 concept map cannot merge two reported lines into one canonical code without either
  overwriting a value or manufacturing an unreported sum, so it stays an `OTHER_BS_*` companion and
  the DCF's `debt` (= `ST_DEBT` + `LT_DEBT`) is the two borrowings lines only. Aggregating
  presentation lines into a canonical code is a modelling decision and is routed to the
  planner/user.

**The per-share DCF consumer completes, the financials surface serves the public-site periods, and the derived lanes describe the latest stored session (TKT-foundation-089).** With
v1.5.62's IS/BS decode landed, the consumer chain that was blocked by it is verified end to end
and one real gap in the derived lanes is repaired. The verification is a **differential** over the
committed TÜPRAŞ `1554106` capture (test plan §29, TC-MDF-118): with its IS/BS rows present, one
`dcf-baselines` pass produces `missingInputs: []` with `base_fcf`, `debt` (= the balance sheet's
`ST_DEBT` + `LT_DEBT`), `cash` (= `CASH`) and `share_count` (086's price-derived lane) all in
`params`, each statement fact's `canonicalFactRefs` entry naming `source: "statement"` with the
baseline's as-of (and the count naming `price-derived` with its `asOf` set to the row's
`price_date`), so
`GET /stocks/{symbol}/dcf` no longer answers `missing_inputs` and `POST …/dcf/compute` returns a
`fairValuePerShare`; with the same capture's IS/BS rows withheld (the live pre-088 shape, 60 CF /
2 IS / 0 BS) the state is again `missing_inputs` naming `['cash','debt']` and revenue is null —
the recorded red reproduced as the control. `GET /stocks/{symbol}/financials` serves
`revenue`/`netIncome` for the public-site periods from those statements; `ebitda` stays honestly
**null** where the IS row carries neither `EBITDA` nor `EBIT` + `DEPR_AMORT` (the existing
`Line(EBITDA) ?? Sum(EBIT, DEPR_AMORT)` seam's own honest branch — no line is added and no label
work is done here).

- **The repair: the derived lanes resolve their day by §10.9.13's rule (§10.9.13 applied, not
  amended).** `metrics-recompute` and `medians` used `request.Date` blindly, so a request for a day
  whose covered universe owned no stored prices wrote **nothing** and reported a green
  `written: 0, unchanged: 0` — indistinguishable from "everything was already current", and
  exactly the 2026-10-26 live reading that kept TKT-foundation-083's criterion open. Both lanes now
  resolve their day through the **one** implementation of the stored-prices-as-truth rule,
  `MarketSnapshotStore.ResolveLatestStoredTradingDayAsync` (exposed as a static entry beside the
  instance method, so the engines
  `MetricsEngine.ResolveComputeDayAsync`/`SectorMedianEngine.ResolveComputeDayAsync` delegate to it
  rather than re-deriving a day of their own): the target is the latest trading day ≤
  `request.Date` with stored prices in the covered universe. The resolver never consults the trading
  calendar (weekend or holiday) — stored prices are the truth — and fabricates no day; with no
  stored price at all there is genuinely no session to describe, so the pass writes nothing and
  claims nothing. The jobs' constructors, the DI
  composition and the test fixture harness are unchanged, so no consumer outside the lanes moved.
- **The routed decisions this consumer consumes as stored, never resolving them.** 088's **DR-2**
  (`Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları` stays `OTHER_BS_*`) means the served `debt`
  excludes that current portion — a partial borrowings figure asserted as the stored
  `ST_DEBT` + `LT_DEBT`, never widened here; 088's **DR-1** (`TOTAL_EQUITY` carries the
  NCI-inclusive total) is read exactly as stored, unfiltered. Both remain open on the planner/user's
  desk.

**Cumulative-vs-period (YTD) column semantics — the declared-duration rule, per-line span provenance, and the fail-safe when nothing attests a period (TKT-foundation-090).** A
Turkish interim FR filing's *main* column is **cumulative (year-to-date)**, and this subsection
pins what the decoder does with that fact — determined from the committed real bodies, not
assumed (test plan §30, TC-MDF-119).

- **The determination: the decoder stores the declared cumulative figure, not the discrete
  three-month cell.** The MİGROS 2026-06 interim body (`1648240`) declares `Periyot 2` →
  `6 Aylık`, and its income-statement and cash-flow main columns are verbatim
  `Cari Dönem 01.01.2026 - 30.06.2026` — a six-month cumulative range. The income-statement (and
  other-comprehensive-income) table carries the discrete quarter as a **separate** column,
  `Cari Dönem 3 Aylık 01.04.2026 - 30.06.2026`, which the decoder already names and skips as
  `SUB_PERIOD_COLUMN` (v1.5.55); the **cash-flow table of that filing has only the two
  declared-range columns** (current and prior) and no quarterly sibling, so the CF figure's span is
  attested by its own declared range. The decoder decodes the column whose **declared duration is
  the report's own**; it never substitutes the three-month cell for a cumulative one. The body's
  own cells are decisive: the decoded `REV` is the six-month `241.298.039` and `NI` the six-month
  `1.078.256`, not the three-month `124.399.203` / `−644.936`. The interim row is therefore stored
  `Q` at the filing's own `periodEndDate` carrying the cumulative figure — the honest shape, and
  the reason the DCF's `PeriodType == "FY"` filter can never reach it (TC-MDF-119 (iii)). What the
  store and the served payload *cannot* say is that the figure spans six months: the span does not
  leave the decode (see the provenance bullet below), so a served `Q` + `2026-06-30` still reads as
  a quarter on the display surface — a gap routed to the display/consumer ticket (057's
  `financialPeriodLabel`), not solved by this ticket.
- **Annual bodies are twelve-month columns, so the FY basis is untouched.** TÜPRAŞ `1554106`'s CF
  column declares `01.01.2025 - 31.12.2025` and its cells give `49.464.734 − |19.118.339| =
  30.346.395` thousand TL (₺30,35bn, the recorded FY2025 FCF); ASELSAN `1561039` gives `48.901.969 −
  |38.168.467| = 10.733.502`. Both type `FY` because a twelve-month duration attests them; v1.5.55's
  `SUB_PERIOD_COLUMN`/cumulative semantics and every annual golden are unchanged by this ticket.
- **The span travels with the decode result — not with the store or the served payload.**
  `KapPublicLine` gains `PeriodStartDate` (nullable) and `DurationMonths` (nullable), populated from
  the **resolved column** it was decoded under. The filing's own declared span is therefore
  preserved on the decode result and available to the decode's own consumers and tests; an instant
  (balance-sheet) line carries `null`/`null`, and `null` means "the filing did not attest one" —
  honest absence, never a default. It is **not** carried further: `Group.ToFact()` builds
  `StatementFact` from `(PeriodType, PeriodEndDate, …)` and drops both fields, the
  `FinancialStatement` entity has no span field, and `StockFundamentalsEndpoints.FinancialsAsync`
  emits only `periodType`/`fiscalYear`/`periodEndDate`. No stored or served artifact therefore
  carries the span, and the served label remains the store's `FY`/`Q` vocabulary — 057's
  `financialPeriodLabel` renders `Q` + `2026-06-30` as "2026 Q2". Closing that served-label gap
  (expose the declared span through the payload/store, or label interims by their dated range) is
  routed to a display/consumer ticket; it is not implemented here.
- **The fail-safe: a period no column attests is named, counted, and never typed `FY`.** A report
  that self-declares a period while **no** column declares a readable date range has undetermined
  semantics. The decoder raises exactly **one** counted `PERIOD_SEMANTICS_UNDETERMINED` finding per
  report and types **every** statement row `Q` — for both the annual-declared and the interim-declared
  variant, because an interim's `Q` must rest on evidence too, not merely on the absence of danger.
  This closes a real defect: the pre-ticket decoder accepted the declaration silently and typed the
  unattested instant (balance-sheet) rows `FY`, the shape that could put a half-year `debt`/`cash`
  into a DCF annual basis. Its **incidence is unmeasured**: no captured report lacks declared
  ranges, so the case is a mutation of a committed body, not an observed filing. The trade-off is
  the safe direction but should be named: in such a report the **2025-12-31 annual comparative**
  balance sheet now types `Q` too, so it drops out of the FY-only `BalanceSheetAsync` read — an
  annual figure hidden from the baseline rather than a half-year let into it. Flow tables whose
  columns cannot be attributed at all still invent nothing (their cells stay `COLUMN_UNATTRIBUTED`
  drops), and the unattested balance sheet is still stored — honestly as an interim. Never a guess,
  never a silent default.
- **Repair is a recorded re-walk, never a silent rewrite (FR-MDF-015).** Where the fix changes what
  should be stored, already-written rows are **not** rewritten in place: the route is an explicit
  cursor reset plus a re-walk for the affected companies (the v1.5.56/v1.31 condition), executed as
  a recorded step — historical immutability and the versioning rules stand.

**The statements lane's public-site mode — configuration, the date-windowed walk, its own cursors, isolation, and the two-source coexistence write rule (TKT-foundation-082).** The
`statements` job gains a second body: `Ingestion:Statements:Source` selects `vyk` (the shipped
value and the code default — switching back is a configuration change, never a rewrite) or
`kap-public`. An unrecognised value fails the run **by name** (`INVALID_SOURCE_SELECTION`) with
**zero** requests: a typo must not silently run the other source under the right-looking
provenance. The VYK adapter, its cursor (`kap-disclosure-cursor`) and its routes are untouched.

- **The list is date-windowed, not index-paged.** One company's reports come from
  `POST /tr/api/disclosure/members/byCriteria` over `fromDate..toDate`, filtered by
  `disclosureClass: FR` and the **stored** public oid (080's map — never the symbol's spelling,
  never the VYK `companyId`). The selection inside that class is the site's own
  `disclosureCategory: FR` **alone** (v1.5.56's pin; the `disclosureType` is *not* a second
  condition — the two coincide on the committed capture, and requiring both would silently drop a
  real report whose type differs, which a lane whose contract is "every skip is named and counted"
  cannot do). `KapPublicLookbackMonths` (shipped 24) is the bootstrap range and
  `KapPublicWindowDays` (shipped 365) the window width; windows iterate **oldest → newest** so the
  date watermark advances monotonically.
- **Truncation is window-scoped, not global.** Within one window candidates are ordered
  newest-first, so a budget spent inside a window keeps what a consumer needs now. Across windows
  the order is oldest-window-first, so a multi-window bootstrap that is bounded part-way loses the
  **newest windows** until a later pass — it is never a statement that the newer periods do not
  exist, and it never moves the watermark backwards.
- **Boundedness is politeness, and the budget is per *pass*.** One `KapPublicWalkBudget` (page
  ceiling, detail ceiling, and a 90-minute deadline strictly inside the 120-minute per-step bound —
  all from `Ingestion:KapPublic`) is created **once per run and shared by every company**, so the
  caps bound the run instead of multiplying per company. Every request is admitted before it is
  issued through 079's client unchanged (≥3 s pacing, one flight gate, honest UA, posture gate); 082
  adds no request path of its own. On truncation the walk records `truncated` with its reason
  (`WALK_PAGE_CAP`/`WALK_DETAIL_CAP`/`WALK_DEADLINE`), persists the last **completed** report and the
  next pass resumes exactly there.
- **The universe is walked least-recently-walked first.** Because the budget is run-sticky, a fixed
  universe order would let the same head companies spend it every pass and starve the tail forever.
  The run orders companies by their persisted watermark (never-walked first, then oldest first), so
  whoever a pass could not start is at the front of the next one. A company the spent budget never
  started is named under its **own** reason, `WALK_NOT_STARTED` — distinct from the truncation codes
  of a company that was walked and then bounded, because "not attempted" and "attempted and cut" are
  different facts.
- **Its own cursors, in their own spaces.** `kap-public-statements-cursor` (instrument-scoped
  `coverage_metadata`) holds the watermark (the inclusive last covered day) plus the report indices
  completed since it; a window walked in two passes **accumulates** that seen set (replacing it
  would re-fetch the first pass's completions), which means the row grows for the lane's lifetime —
  a stated size cost, not a correctness one. `kap-public-statements-depth` holds the **provenance of
  depth**: `requestedFrom`/`requestedTo` against `achievedFrom`/`achievedTo`, the bootstrap flag and
  any truncation. A resumed pass updates the achieved half but **keeps the configured request** the
  bootstrap recorded (only a real bootstrap writes `requestedFrom`), so "asked for 24 months, walked
  6" can never be rewritten as "asked for 6 and got it". Both data types are outside the coverage
  recorder's owned set and distinct from the VYK cursor, so a public index (`1643141`) never enters
  the VYK cursor and a VYK index (`1231017`) never enters the public one.
- **Isolation and the honest statuses.** An unmapped company is named, counted
  (`COMPANY_IDENTITY_UNRESOLVED`) and never fetched. A report the source cannot deliver — or
  delivers in a shape that is not a report — is a named, counted quarantine carrying the source's
  own answer verbatim (`DISCLOSURE_DETAIL_UNFETCHABLE`, `UNPARSEABLE_PAYLOAD`) while the company's
  other reports are still written. An all-isolated zero-write run is `partial`, never `failed`. A
  **list** source-down is different: without an index there is no walk, so the company is `failed`
  and nothing is quarantined for it — and a list failure in a **later** window still reports the
  counts of the windows it already completed (the walk returns its partial result with the named
  failure; a `failed` company that wrote rows makes the run `partial`, never `failed`-with-zero and
  never a complete walk).
- **Two-source coexistence — the write rule; the read preference is deferred.** The natural key in
  `financial_statements` does not include the source and `version` is the restatement sequence, so a
  cross-source fact would otherwise collapse into a silent idempotent `unchanged` (or be forced into
  a false `restated` row). For a period the **other** source already covers, this lane therefore
  **does not write**, retains the incumbent row exactly as it is (not overwritten, not deleted, not
  relabelled, as-of queryable with its lines), and surfaces the overlap as a **named, counted**
  `SOURCE_OVERLAP_RETAINED` finding whose evidence names both provenances, the period and the
  disclosure index. Coverage is checked on the store's **exact** key **and** on the calendar period
  (same period end + statement type under any period type): VYK defaults to `FY` at 12-31 while the
  decoder fails safe to `Q`, so the second answer is a named, counted
  `SOURCE_PERIOD_TYPE_DISAGREEMENT` finding — one real period is never written twice on a
  period-type spelling. The rule is **per natural key, not per report**: the same report's uncovered
  periods are still written. Re-ingesting a withheld fact after `source_ref` joins the natural key
  (083's migration, TC-MDF-111) needs an explicit **cursor reset** or 083's recorded re-pin: the lane
  has already marked the report completed, so a later pass by itself will not re-fetch it. The
  **read** preference for a coexisting period belongs to that same migration and
  `StockFundamentalsEndpoints.Pick` (TC-MDF-111), not here.

**Quarantine recovery — the transport-failure backlog is replayed once, and the tombstone is
persisted (TKT-foundation-092, the 063 stale-cursor-reset doctrine one level down).** A report whose
**detail** failed in **transport** is quarantined (`DISCLOSURE_DETAIL_UNFETCHABLE`) **and** marked
completed: it enters the seen set, its window closes and the watermark moves past it, so no ordinary
pass can ever list it again — the live 2026-10-26 runs (302/303) left **~180** such quarantines
(AKBNK alone 102) and silently bounded those companies' history by a transient outage. The repair is
bounded, persisted and recorded, never a retry ladder:

- **What is replayable is decided by what the read carried, not by the status word.** Only a
  **transport failure** — `SOURCE_FETCH_FAILED`/`SOURCE_REQUEST_TIMEOUT` with **no body ever
  received** (`KapPublicFetchResult.RawBody` absent; the live runs' own `bodyReceived: false`
  signature) — registers a replay. A **non-success status carrying a body** (the source *did*
  answer, verbatim excerpt retained) and a **shape verdict** (`UNPARSEABLE_PAYLOAD`) are **terminal**:
  re-fetching either would be a retry ladder against a source that already spoke, or ~900 KB spent to
  re-learn the same answer. If shape-quarantine replay is ever wanted it is its own recorded decision.
  The predicate is one line — `IsTransportFailure` = `IsSourceDown && string.IsNullOrEmpty(RawBody)` —
  and it carries one **recorded edge, not a silent assumption**: a **status answer carrying no readable
  body** (a non-success status whose body is zero-length, or whose body read failed with
  `IOException`/`HttpRequestException`) is indistinguishable *at the read* from a transport failure and
  is therefore treated as one. That is inside the ruled class, it is bounded to one extra request per
  index (the tombstone still fires, so no ladder can form), and it is written down here so a later
  reader does not re-open the zero-length-body case as a defect: tightening the predicate is a
  semantics change that needs its own recorded decision, not a correction.
- **The state lives in the lane's own cursor row, and no migration is needed.** `KapPublicStatementCursor`
  gains two additive index arrays inside `kap-public-statements-cursor`'s `coverage_metadata.notes`
  JSON — `quarantined` (backlog still owing its one replay) and `retried` (the tombstone: indices
  whose replay is spent) — written **always**, even empty. An absent array reads as an empty one, so a
  pre-092 row keeps its position and simply has no backlog; it is never reinterpreted as "all
  retried". The window close (`CloseWindow`) advances the watermark **and carries both markers
  forward**, because a position change is never a reason to forget a debt.
- **It rides the ordinary pass, with the pass's own rules.** The backlog is drained at the **start**
  of a walk, **before** the window loop, through 079's paced single-flight client, admitted by the
  same `KapPublicWalkBudget` — a replay costs a **detail fetch** exactly like any other, so the detail
  ceiling bounds it (a replay needs no list page, so the page ceiling is untouched). A bounded replay
  sets `truncated` with `WALK_DETAIL_CAP`, leaves position and backlog exactly where they were, and
  the next pass resumes the backlog. The backlog is finite and strictly shrinking (every attempt
  spends a tombstone), so coverage can be deferred for at most as many passes as the backlog needs —
  bounded by the number of indices, never unbounded.
- **Every attempt lands in exactly one named outcome (NFR-MDF-001).** A replay that succeeds writes the
  report's facts under `kap-public` and is counted **recovered**; one that fails again (or answers a
  non-report body) is re-quarantined **terminally** with its evidence, its one replay spent, and is
  counted **still-quarantined**. The walk result carries `ReplaysAttempted`/`Recovered`/
  `StillQuarantined`, and three log lines carry them — the walker's per-symbol summary
  (`KapPublicStatementsWalk.cs:597-616`), the job's per-company replay line, emitted only when a replay
  was attempted (`StatementsJob.cs:650-656`, guarded at `:648`), and the run's completion line
  (`StatementsJob.cs:879-895`, `quarantine-replays N (recovered R, still-quarantined S)`). They are
  **not** a stored ledger or stats field: `IngestResult` and `SymbolRunStat` are unchanged by this
  ticket, so the counts are observable in the log and nowhere else. Each recovered index's own line
  states what the write did (`quarantined → recovered ({Inserted} inserted, {Unchanged} unchanged,
  {OverlapRetained} overlap-retained)`), so a recovery whose report was entirely retained under the 083
  coexistence rule — recovered, `0` inserted, overlap-retained `> 0` — is never described as "written".
  No third attempt ever fires for the same index, in this process or a later one: the tombstone is
  persisted. The **explicit cursor reset remains the escape hatch** for anything beyond the one
  automatic replay (the v1.5.56/v1.31 condition, unchanged).
- **The quarantine ledger is append-only: one row per attempt, never deduped or superseded (recorded
  rule, TKT-foundation-092 review round 1).** A replay that fails again writes a **second**
  `DISCLOSURE_DETAIL_UNFETCHABLE` row for the *same* disclosure index. That is deliberate: the audit
  trail keeps the first quarantine and the spent replay side by side, and nothing rewrites history to
  make one index look like one event. A reader who sees two rows for one index is seeing the designed
  shape, not a double-write defect — the dedupe instrument is the cursor's `retried` tombstone (which
  bounds *attempts*), never a deletion of evidence (BR-MDF-007's "rows are never deleted").
- **A recovered index leaves its original quarantine row standing.** Recovery touches the cursor's
  `quarantined`/`retried` markers and the statement store — **nothing** deletes the earlier quarantine
  row and nothing annotates it with a recovery status (the verbatim evidence stands as recorded). That
  the report was later recovered is read from the run's completion log line and from the stored facts,
  never from a rewritten quarantine row, which is exactly the "the quarantine evidence itself is
  unchanged" non-goal this ticket pinned.

**The statement store's natural key gains the source, and one pinned preference decides which
source's fact is the current one (TKT-foundation-083, FR-MDF-008/011/013/014/015, FR-MOV-006/
BR-MOV-010).** v1.5.56 recorded the coexistence **write** rule and deferred the **read** side
because the store could not hold two sources' facts for one period: the natural key was
`(instrument, period_type, period_end_date, statement_type)` and `version` was the restatement
sequence (`02` §3.1), so a cross-source fact could only be a silent `unchanged` swallow, a
forbidden in-place overwrite, or a false `restated` declaration.

- **One forward-only migration, no fact rewritten.** The unique index and the store's key become
  `(instrument, source_ref, period_type, period_end_date, statement_type)` + `version`
  (`FinancialStatementStore`, `DegerliDbContext`, `20261026130253_StatementSourceInNaturalKey`).
  Dropping and recreating the index **rewrites no row**: every stored statement keeps its values,
  its `version`, its `restatement_date` and its `source_ref`. `version` stays the restatement
  sequence **within one source's own filing series** — a fact from another source is not a
  restatement of this one, so the public-site row for a period the incumbent also holds is a
  version-0 fact, never version 1.
- **The preference is pinned, and it is a property of the source.** One implementation —
  `StatementSourcePreference` (`src/Ingestion/Statements/`) — is the only definition:
  rank `kap-public` < `kap` < any other recorded class (a fixture/backfill provenance), then the
  highest `version` within that source, then the row id. Lower rank wins; the ordering is **total**
  and therefore order-independent — never a silent latest-write, never a tie-break by arrival. The
  rule is **public-site current wins for the periods it covers; the incumbent's rows are retained
  and stay as-of queryable** — the served payload selects one row *on read*; nothing is deleted or
  relabelled to make a choice. The two labelled classes are the **evidence-class names actually
  stamped on stored rows** — `kap` by `KapXbrlStatementDecoder` (the VYK path) and `kap-public` by
  the public lane — and they are matched as names, never by prefix: a canonical-double/backfill
  body's own `sourceRef` (e.g. the §10.9.7 test envelope's `kap://statements/<symbol>`, which
  `StatementsSourceAdapter` carries through verbatim) is a *distinct recorded class* and therefore
  sits in the fallback bucket beside a fixture provenance. Two fallback-class sources holding one
  key are then separated by `version` alone — the restatement rule, not the source rule.
- **Whole statement, never per line.** The winner is chosen per `(period, statement type)` and all
  of its lines are served from that one row. Mixing two sources' lines would serve a statement no
  source ever filed — and it is exactly what would fabricate a per-share figure: a current filing
  that carries no share count must leave market P/E and the per-share metrics honestly `NULL`
  (FR-MOV-006/BR-MOV-010, the 065 Option-A posture) instead of borrowing an older filing's count.
  A balance-sheet period the current source does not cover **is** the incumbent's fact (preference
  ii), and then the incumbent's count is what that fact carries — the rule is per statement type,
  because statement types are separate natural keys.
- **Every consumer goes through the one rule.** `StockFundamentalsEndpoints.Pick` (served
  financials and balance sheet), `MetricsEngine`, `MarketSnapshotEngine` and
  `DcfBaselineGenerationJob` select through `StatementSourcePreference`; a local
  `OrderByDescending(version).First()` would resolve two same-version sources by row order, which
  the migration makes reachable for the first time.
- **Coverage depth is per source.** `coverage_metadata` gains the family
  `statements:{sourceRef}` (`statements:kap`, `statements:kap-public`), one achieved range per
  (instrument, evidence class), written by the no-silent-gaps reconcile beside the aggregate
  `statements` row. The per-source types sit **outside** `CoverageDataTypes.InstrumentScoped`: a
  missing `statements:kap-public` row means "this source wrote nothing here", never "this source
  found no statements" (the public lane's own `kap-public-statements-depth` keeps that second
  answer), so honest absence is not turned into a gap claim. A company holding only 2023 incumbent
  facts now reads as 2023-deep per source, never as "current".
- **What does not change.** v1.5.56's **write** rule stands: the lane still withholds an overlapped
  fact and names it `SOURCE_OVERLAP_RETAINED` (the incumbent row untouched, no false `restated`).
  With the source in the key that withheld fact can now coexist, but re-ingesting it needs an
  explicit cursor reset — the lane already marked the report completed. Known residue recorded
  honestly: `KapPublicStatementTargets.GetForeignPeriodsAsync`'s doc comment still describes the
  pre-migration "at most one provenance answers per key" shape (behaviour unaffected — the overlap
  question is asked of *foreign* sources and the first match is named), and the admin coverage
  endpoint renders the `InstrumentScoped` set only, so per-source depth is visible in
  `coverage_metadata` and not yet in `GET /admin/coverage` (an admin-surface change, outside this
  ticket).

**The identity-population invocation — one registered job, admin-triggerable, never a chain step
(TKT-foundation-087).** The identity map above is a *stored* fact, so it needs a production caller;
without one it stays empty and every downstream public-site read is honestly isolated into
uselessness. The recorded blocker was exactly that: `KapPublicCompanyIdentityResolver.ResolveAsync`
had **no caller outside tests** (080/082's live-criterion notes), `kap_public_company_map` never
populated, and a live `Source = kap-public` statements walk isolated every XU030 company as
`COMPANY_IDENTITY_UNRESOLVED` and wrote nothing.

- **The surface is a registered `IIngestJob`, not a new endpoint** (§10.9.17's per-job convention):
  `KapPublicIdentitySyncJob` in `src/Ingestion/KapPublic/`, job code **`kap-public-identity`**, one
  `services.AddScoped<IIngestJob, …>()` line beside its lane peers in the composition root and one
  line in `03` §8's job-code catalog. That single registration is the whole invocation surface —
  the runner, the run ledger (`ingest_runs`), the admin summary's every-registered-job list
  (UXR-MDF-003) and the admin trigger `POST /api/v1/admin/ingest/{job}/run` (UC-MDF-002) all
  discover jobs through it. No new route, no new config key, no new request path.
- **The bind is the statements lane's own universe.** `Ingestion:Statements:UniverseIndex`'s current
  constituents (shipped `XU030`), resolved to instrument ids through
  `KapPublicStatementTargets.GetInstrumentIdsAsync`. Never a hardcoded symbol list: the identity map
  is only useful for the universe the walk will actually walk, and widening it is a config value.
- **One paced pass, and politeness is structural.** Every request is 079's client's (≥3 s
  configurable pacing, one flight gate, strictly sequential, honest UA, the ratified `Allowed`
  posture gate); the job adds no `HttpClient`, no pacer and no parallelism of its own
  (082's identical pin), and issues exactly one identity search per bound symbol.
- **Honest statuses, mapped — never re-interpreted.** The resolver's `Ok` carries its counts through
  the ledger: inserted → `written`, repaired-in-place → `rewritten` (a repair is not a fresh write),
  `unchanged` → unchanged (as itself), and each unresolved symbol is **named** in the per-symbol stats under
  the 044 vocabulary (`COMPANY_IDENTITY_UNRESOLVED`/`AMBIGUOUS`) and **counted** in `skipped` — so a
  pass that mapped only part of its bound reads `partial`, never a green run hiding a company with
  no identity. The resolver's `SourceDown` is `failed`, naming its own reason and the symbol it
  stopped at, with **nothing stored** (080's contract). A re-run is idempotent (`unchanged`,
  `recorded_at` unmoved).
- **An empty bound is the honest named empty** (`EMPTY_BOUND_UNIVERSE` with the index code): zero
  requests, zero writes, and a status that claims nothing was resolved. It is not a failure — no
  source failed, the cold start belongs to the EOD/morning chain, and the statements lane names the
  same gap on its own walk — and it is never a fabricated resolution zero.
- **Scheduling posture (the §10.9.11 shares-lane precedent):** admin-triggerable, **not** a step of
  `EodIngestChain`/`MorningIngestChain`. A company's public oid changes rarely, so a cadence is a
  recorded later decision, and the byte-unchanged chains are asserted so by test.
- **Doubles only, zero live requests.** The identity answer in CI is the committed
  `kap-public-identity-ASELS` capture served by the WireMock double; the pass itself is exercised
  through the production job runner. Nothing in this subsection adds a source, a table, a migration,
  a dependency or a VYK-touching change.

**Comma-coded identity matching — `cmpOrFundCode` is a token list, and an unresolved ambiguity stays
unresolved (TKT-foundation-091, FR-MDF-002/008/012).** The identity resolution rule above was an
**exact-equality** rule (`hit.CmpOrFundCode == symbol`), and the first live `kap-public-identity`
pass (run 301, 2026-10-27) showed what that costs: **22 of 30 XU030 companies mapped, 8 unresolved**
— BIMAS, ENKAI, GARAN, ISCTR, KRDMD, SISE, VAKBN, YKBNK — each isolated as
`COMPANY_IDENTITY_UNRESOLVED` and therefore never walked, never statement-fed, never DCF-fed. The
verified cause is **not** a missing company: the site serves several of these companies' codes
**comma-joined** in one field (`GARAN` → `cmpOrFundCode` `"garan,tgb"`, with several candidate rows),
the same convention TKT-foundation-046 fixed for the VYK directory's `stockCode`.

- **Candidacy is a token rule, not an equality rule.** A company hit (`searchType` `C`) is a
  candidate when the bound symbol is one of the **comma-split tokens** of its `cmpOrFundCode`
  (each part trimmed, compared `OrdinalIgnoreCase`; an empty token never matches, and a code that
  merely *contains* the symbol inside a longer token is not a match). The exact-equality path is the
  **degenerate case** of that rule — a single-token code behaves exactly as before — which is why
  080's ASELS cases stand unamended. Only company hits are ever candidates: a **fund** hit spelling
  the same token never binds a stock instrument and never creates an ambiguity.
- **Several distinct company candidates sharing the token is an honest ambiguity — deliberately
  *not* 046's last-wins.** `COMPANY_IDENTITY_AMBIGUOUS` is named, counted, and **stores nothing**;
  the refusal names the candidates it declined to choose between. The divergence is intentional and
  recorded: the VYK list is a **directory of record** (one row per member, so a tie is a data defect
  to break deterministically and log), whereas this source's answer is a **search result list** in
  which several real companies legitimately carry the token — there, a first-wins/last-wins rule
  would be a guess wearing a rule's clothes. No tie-break is applied on this lane: not arrival order,
  not title proximity, not "the one whose code starts with the symbol". **If live captures ever
  evidence a tie-break the source itself supports** (e.g. exactly one candidate whose token list is
  *led* by the symbol, or a `searchType`/`actionKey` discrimination the endpoint actually makes),
  that finding is recorded and routed to the user before it is implemented — never smuggled in as a
  resolution.
- **The map is authored against committed bodies, and the bodies say what they are.** The two
  comma-coded answers the rule is pinned on (`kap-public-identity-garan-comma-coded`,
  `-isctr-comma-coded`) are **constructed**, not captured: this ticket issued **zero** live requests
  (the lane's politeness directive), so no verbatim answer for those symbols exists in the
  repository. Their provenance is `Constructed` — enforced by the catalog's own label rule, not by
  prose — and each label states which facts the run-301 observation carries (**the comma-joined
  code and that several candidate rows exist** — 087's record, and for GARAN only; its record says
  nothing per-symbol for ISCTR) and which are **constructed for the branch shape** (the oids, the
  second candidate and the token it shares, the fund row, the row order). The frame is the
  committed ASELS answer's exact shape, so the reader decodes them through production code with no special case. **Which of the eight symbols actually
  resolve versus land ambiguous is unmeasured** — that is the orchestrator's live criterion, and
  ambiguity is a legitimate outcome to record, not a failure to force.
- **Politeness, pacing and the VYK lanes are untouched.** The pass still makes exactly one paced,
  single-flight identity request per bound symbol through 079's client; no request path, no schedule,
  no quarantine and no decoder behaviour changes (088/090 own the decoder; 092 owns quarantine
  recovery).
- **The User-Agent default — the WAF finding, and the corrected code default (landed).** The
  087 live wave established that this lane's original code-default UA carried a parenthetical
  comment (`…DegerliBot/0.1 (kap.org.tr public-site reader; polite, paced, single-flight)`) which
  the site's WAF answers with **HTTP 666 on every route**, while the same string without the
  comment (`…Chrome/124.0 Safari/537.36 Degerli/0.1` — honest self-identification, browser-shaped,
  no explanatory parenthetical comment) passes (HTTP 200 on `search/combined` and `byCriteria`, probe-verified one
  request at a time). **The correction is landed by this ticket's lane (orchestrator decision
  2026-10-27, v1.5.66):** `KapPublicSourceOptions.DefaultUserAgent` is now the comment-free
  passing string, the shipped `src/Api/appsettings.json` value is aligned to it (the configured
  value wins over the default, so without that alignment the code fix is inert), and the two
  assertions that pinned the old token in `KapPublicSourceTests` (`TC-MDF-103(a)`/`(d)`) now pin
  the corrected default — still config-equivalence pins, pinning the *corrected* value, plus the
  blocked-**marker** pins (`DoesNotContain("DegerliBot")`, `DoesNotContain("public-site reader")`)
  so the blocked spelling cannot silently return. The pin is the marker, never punctuation: the
  corrected string legitimately contains `(X11; Linux x86_64)` and `(KHTML, like Gecko)`, so a
  `DoesNotContain("(")` pin would fail on the *correct* value (test plan v1.41 records the
  correction; the shipped `TC_MDF_103_a` asserts `Contains("Mozilla/5.0")`,
  `Contains("Degerli/0.1")` and those two negatives). This required
  amending the ticket's `scope:` (the two files were omitted from the planner's list) — recorded
  on the ticket, not a silent widening. The politeness posture (≥3 s, single-flight, honest
  token) is unchanged by it. The stale dev-stack config override is now redundant; its removal
  is an orchestrator ops step, not a code change here.

**The statements-view label map: the industrial IS/BS/CF row set, the row-scope dimension, and the Konsolide/Solo flag (TKT-foundation-093; this subsection subsumes the planner's dated 2026-10-28 design ruling it replaces, whose mechanism it lands unchanged).** §10.9.19's decoder shipped with the debt/cash core and the income ladder (v1.5.62), which left the statements view the user specified (recorded verbatim on TKT-foundation-095) short of the taxonomy families it renders. The gap was never the data — every one of those rows already decodes as a **valued `OTHER_*` passthrough** — it was the canonical mapping, and one structural obstacle that a flat map cannot express.

- **The obstacle, stated exactly.** The label-rule key was `(StatementType, Label)` and only the cash-flow statement resolved a section before mapping; BS/IS rows fell to `DefaultSection`. But the balance sheet legitimately repeats captions **across its current/non-current blocks with different values** (`Ticari Alacaklar` ×2, `Ticari Borçlar` ×2, `Kiralama İşlemlerinden Borçlar` per block pair, `Ertelenmiş Gelirler (Müşteri Sözleşmelerinden Doğan Yükümlülüklerin Dışında Kalanlar)` ×2), and the income statement repeats `Ana Ortaklık Payları` across its profit-or-loss and other-comprehensive-income parts. A 1:1 map would either overwrite one value with the other or raise `DUPLICATE_LABEL_CONFLICT` and quarantine the statement row — the v1.5.62 failure mode returning through the front door. **The rejected alternative, with its consequence (recorded):** routing the repeated families to `OTHER_*` would leave the served canonical block mostly empty, since the sheet *is* organized in current/non-current blocks and the view structurally requires the ST/LT dimension — rejected.
- **The row-scope dimension — additive by construction.** The rule key gains a **row scope**, resolved **before** label mapping exactly as the CF section already was: the balance sheet's current/non-current block from the capture's own `DÖNEN VARLIKLAR` / `DURAN VARLIKLAR` / `KISA VADELİ YÜKÜMLÜLÜKLER` / `UZUN VADELİ YÜKÜMLÜLÜKLER` anchors; the income statement's part from its own `Kar veya Zararda Yeniden Sınıflandırılmayacaklar` anchor (a standalone `Diğer Kapsamlı Gelir Tablosu` starts in OCI, and a combined statement flips at that anchor); the cash-flow statement keeps the block its section already carries. The closed vocabulary is `KapPublicScopes` (`CURRENT`/`NONCURRENT`/`PL`/`OCI`) beside `KapPublicSections` for CF. The map is carried as **two projections**: `LabelMap` (unqualified — still a function of `(statement, label)`, the invariant TC-MDF-107 (i)/117 (f) pin) and `ScopedLabelMap` (one entry per `(statement, label, scope)`). **A rule without the scope qualifier matches in every scope**, so every pre-093 entry is unchanged and every landed golden holds; the dimension materializes in **stored data as distinct canonical codes**, so the served payload and any consumer keep a flat `code → figure` map with no block logic of their own.
- **The honest fail-safe, never a guess.** A row whose scope cannot be determined (before any anchor, or inside the equity block `ÖZKAYNAKLAR`, which has no current/non-current split) matches **no** scope-qualified entry even where one exists for its caption: it keeps its section-scoped `OTHER_{section}_{Slug}` passthrough and the miss is a named, counted **`BLOCK_UNDETERMINED`** finding — emitted **once per row** (not per cell), naming the row order, the statement and the caption — which joins §10.9.19's closed reason set (now seventeen codes). The fail-safe's incidence on real filings is **unmeasured** (all five committed bodies resolve every block from their own anchors, zero `BLOCK_UNDETERMINED`), so its case is a mutation of a committed body; the direction it errs in is the honest one — a labelled passthrough rather than a fabricated block.
- **The evidence rule is unchanged, and generalized.** Both projections stand only on **committed** bodies: every entry names its capture in `KapPublicLabelRule.Provenance` (the closed set `KapPublicReportDecoder.CaptureNames`, now five), the body must carry the exact spelling, and every target must be in `StatementMapper.CanonicalCodes`. With THYAO `1565996` (2025 Yıllık, `1.000.000 TL`, committed as a bounded trim from the already-fetched body — **no new live request**) the industrial row set is evidenced end to end; the chart extension those entries target is `02` §6.1's, **42 additive codes**, never a rename.
- **The Konsolide/Solo flag.** `KapPublicReport.Consolidated` derives from the header-table field `Finansal Tablo Niteliği`, already read into `KapPublicReportHeader.ConsolidationType`, through one named function (`KapPublicReportDecoder.ConsolidatedFor`): `Konsolide` → `true`, `Solo` → `false`, absent **or any other value** → `null`, with the declared string retained verbatim either way. It is **never** inferred from the `<h1>` company heading and **never** from the presence of minority interests — a filing that says `Bireysel` is decoded as "declared, not one of the two words we understand", which is honest null, not a guess. Decoding and carrying it is this ticket's; **persisting and serving it belong to the statements lane's next slice** (094), which is why the value lives on the decode result and the store schema is untouched.
- **Fences (unchanged, restated because the new codes sit close to them).** `ST_DEBT`/`LT_DEBT` remain exactly the two borrowings lines: `ST_LEASE_LIAB`/`LT_LEASE_LIAB` are the lease family, the current portion of long-term borrowings stays `OTHER_BS_*`, and the DCF's `debt` is **not** widened (DR-2 stands). `TOTAL_EQUITY` remains the NCI-inclusive total as stored; `EQUITY_PARENT` and `MINORITY_INT` are decoded beside it, never subtracted from it (DR-1 stands). No share-count line is mapped, no cumulative/period semantics change (090 stands), and no VYK/XBRL decode, equity-matrix decode, serving, metric or UI behaviour moves. The `TC-MDF-107(b)` invariant (every rule target ∈ `StatementMapper.CanonicalCodes`) is unaffected — it asserts membership, not registry size — and the cases that enumerate the registry or a canonical surface grew **additively**, recorded in test plan v1.45's re-pin round (user-approved 2026-10-28), never weakened.

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
| AD-14 | **Real-source ingestion contract (v1.5):** production adapters parse the real İş Yatırım `HGDG_*` / KAP XBRL / EVDS envelopes (§10.9); first live pull bounded to XU030 via `Ingestion:Prices:UniverseIndex` (XU100 = config flip); per-ticker loop in the job, symbol-scoped adapter; charset-faithful `ISourceClient` (code-pages provider moves from fixtures into the product) | builder instruction 2026-10-17 ("main functionality requires real data"); every piece traces to existing items — FR-MDF-001/010 (prices + backfill), FR-MDF-002 (statements), FR-MDF-006/007 (index bound + membership seed), FR-MOV-014 (macro), on top of the v1.4 OQ-MDF-001 price-source resolution — **no new FRs**; downstream (C5 storage, C3b metrics, C2 API) deliberately untouched (adapters still emit the canonical payloads the jobs persist). Rejected: canonical-envelope-only production parsing (the L2 double's shape is a test artifact, not a source contract — DR-01, PR #82 escalation); adapter-internal universe loop and parallel fetching (§10.9.2) |

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
| FLG-08 | **Index-code example inconsistency (v1.5):** `02` §3.1's `indices` example writes `XU30`, while the real BIST index code pinned by v1.5 (the `Ingestion:Prices:UniverseIndex` default and the launch-seed target, §10.9.2/§10.9.3) is **`XU030`**. Flagged, not silently edited (`02` is v1.3-approved; architecture owns only this doc). **Resolved 2026-10-17 (builder-directed mirror):** the one-line `02` §3.1 edit is applied — the example now reads `XU030`. | Resolved (2026-10-17) — `02` §3.1 mirror applied |
| FLG-09 | **`daily_prices.volume` unit semantics unpinned across sources (v1.5):** the real İş Yatırım `HGDG_HACIM` is the source's reported trading volume — in the captured body it reconciles with `DOLAR_HACIM` × `DD_DEGER` (the USD rate), i.e. TRY-denominated turnover — while the Yahoo contingency's chart `volume` is share count. Mixed-source rows would carry inconsistent units unless pinned. The planner/builder must pin (a) the stored unit, (b) the BR-MOV-011 movers-threshold unit, and (c) the FR-MOV-021 mover-volume display unit before the prices-adapter ticket is sliced. Not resolved here: deriving share count (÷ AOF) would invent a value, violating the honest-gap posture applied to `open` (§10.9.1). **Resolved by builder decision 2026-10-17:** the stored semantic is **TRY turnover (₺)** — İş Yatırım rows store `HGDG_HACIM` as-is; Yahoo-contingency rows store NULL volume (honest gap — different unit; deriving ₺ from shares × close rejected as computing); the movers threshold (`Ingestion:MarketSnapshot:VolumeEligibilityThreshold`, BR-MOV-011) is recalibrated from the observed BIST 30 distribution during the live-activation ticket; FR-MOV-021 mover volume displays ₺ turnover; FU volumes reinterpret as ₺ with unchanged values (no golden churn). Recorded in §10.9.1 (volume-semantics note). | Resolved (builder, 2026-10-17) — §10.9.1 |

## 13. Change propagation & change record

Amendments to these documents must list affected FRs/UCs/UXRs, TCs, and tickets for re-validation — the same rule the UX package follows (`99-ux-audit.md` §6). The change record (§13.1) tracks every version. As of v1.2 the test-planner and planner have not yet derived artifacts, so no downstream re-validation is pending — they must derive from **v1.2**.

### 13.1 Change record
| Version | Date | Change | Affected items | Downstream re-validation |
|---|---|---|---|---|
| v1.0 | 2026-10-06 | Initial architecture (submitted for approval) | — | — (first version) |
| v1.1 | 2026-10-06 | Builder-directed **admin dashboard extension** (AD-13): `GET /api/v1/admin/summary` (ops one-glance), `quarantined_facts` table + `GET /admin/quarantine` + `POST /admin/quarantine/{id}/dismiss` (gives UC-MDF-001 alternate b's "quarantine" a storage home — a v1.0 gap), `GET /admin/stats` (aggregate-only SC-008/OBJ-005 measurement) | FR-MDF-012 mechanism (components C3a+C3d+C2+C1); UC-MDF-001/004 + UC-MOV-005 coverage rows (`03` §10); data model (`02` §3.1, §2, §7); admin API (`03` §8, §9) | None pending — test-planner/planner not yet run; derive from v1.1 |
| v1.2 | 2026-10-06 | **Admin descriptions payload — UXR-MDF-020 confirmation** (UX audit `99-ux-audit.md` §8 flag ruling 3, builder-approved 2026-10-06): response sketch added for `GET /api/v1/admin/descriptions` (`03` §8) — each queue entry explicitly serves its KAP source references (`sourceRefs`, projected from `business_descriptions.source_refs_json` resolved against `kap_disclosures`) alongside the editable texts. Additive response documentation only (versioning rule `03` §11): no new endpoints, no request changes, no schema change — `02` untouched (`source_refs_json` exists since v1.0). UXR-MDF-015/028 (run-ledger auto-refresh / manual refresh) verified client-side only — no architectural change (`/admin` 60 req/min accommodates polling, `03` §12) | UC-RES-004 (step 2 — review); UXR-MDF-020 (Should) — payload mechanism; admin API `03` §8 | None pending — test-planner/planner not yet run; derive from v1.2 |
| v1.3 | 2026-10-07 | **Builder final-review mirror patches** (from the approved test package, test-planner directed; builder-approved 2026-10-07): (1) **F-VAL-1** — DCF debt parameter clarified to **total debt (ST+LT)**; field renamed `net_debt` → `debt` (`02` §6.4, `03` §5 example); (2) **Q3** — rights-issue (TERP) adjustment factor defined on `corporate_actions` (`02` §3.1); (3) **Q5** — windowed CAGRs stored as **window-suffixed metric codes** (`rev_cagr_3y/5y/10y` &c; 26 codes), `02` §6.2 note + §7 volume corrected (~6.5M rows) | FR-VAL-003/005/006/010 (`02` §6.4); FR-MDF-018 + BR-MDF-010 (`02` §3.1 adjustments); FR-MDF-013/014/018 (`02` §3.1/§6.2 metric storage) | None pending — the test package (FU v1.3, MDF v1.2) is the fixture/golden source of truth; implementation derives from v1.3 |
| v1.4 | 2026-10-15 | **Builder post-board correctness feedback applied** (12 verified issues, delivery planner; builder-approved 2026-10-15 — architecture `02` v1.3, `03` v1.4, testing plans v1.4/MOV v1.2/VAL v1.2/FDF v1.1 + FU v1.4; test-planner confirmation pending): (1) `financial_statements.version` → integer sequence (0=as-reported, 1..n restatements; a period may be restated repeatedly) — `02` §1.5/§3.1/§5.7; (2) statement unit-scale + reporting-currency normalization to raw single-unit TRY with `source_unit_scale`/`source_currency` provenance + period-end FX conversion (new MDF→MOV read edge) — `02` §1.8/§3.1/§5.9; (3) corporate-action-adjusted EPS/DPS series for `eps_cagr_*`/`div_cagr_*` — `02` §6.2; (4) `OCF` added to the chart of accounts; `FCF = OCF − \|CAPEX\|` (replaces the indirect reconstruction) — `02` §6.1/§6.2; (5) TMS 29 `regime_break` flag on spanning CAGRs — `02` §3.1/§6.2; (6) `instruments.accounting_profile` + financial-sector metric NULL posture + sector-aware DCF verdict (`DCF_NOT_COMPUTABLE` reason `financial_sector`) — `02` §3.1/§6.2/§6.4, `03` §5/§7; (7) manual independent-CPI admin entry — `03` §8; (8) metric `regimeBreak`/basis flags on served payloads — `03` §4/§5; (9) per-row price-source provenance (İş Yatırım primary) — `03` §1.2. Data-source resolutions: price source = İş Yatırım public endpoints (Yahoo `.IS` contingency) resolving OQ-MDF-001; macro consolidation via TCMB EVDS resolving OQ-MOV-005; TEFAS ticker-level holdings descoped to a recorded coverage gap (OQ-FDF-002/BR-FDF-004) | FR-MDF-002/003/010/013/014/018/019 (schema, normalization, metrics); FR-MDF-007 (constituents seed posture); FR-MOV-014 (macro ingest + manual entry); FR-FDF-003/004 (holdings descope); FR-VAL-001/003 (sector DCF); UXR-resolved display markers (SCR-005/006/012 — ux-designer flow-back open) | **Re-validation (planner):** regenerates affected tickets as a post-board fix wave — TKT-mdf-013..022, TKT-mov-008/009, TKT-val-010, TKT-fdf-003, TKT-foundation-015; three UI-alignment tickets deferred until the ux-designer adds the SCR-005/006/012 markers. Completed work affected by these changes is reworked by builder direction (the feedback authorizes it) |
| v1.5 | 2026-10-17 | **Builder-directed real-source ingestion contract** (builder instruction 2026-10-17 — "main functionality requires real data"; resolves the PR #82 / TKT-foundation-015 round-1 escalation findings DR-01..03 by design authority): (1) the real İş Yatırım EOD `HGDG_*` envelope becomes a first-class production parse target in the prices adapter (field map with `HG_*` fallbacks, `dd-MM-yyyy` dates, scientific-notation volume, honest NULL `open` — never invented from AOF; same mapping on backfill) — §10.9.1; (2) production prices fetch is per-ticker over the configured index's current constituents — loop in the job, symbol-scoped adapter, strictly sequential + paced; bounded to **XU030** at launch via `Ingestion:Prices:UniverseIndex` + `Ingestion:Prices:RequestDelayMs` (XU100 = config flip, §10.4) — §10.9.2; (3) XU030 membership launch-seed reusing the TC-MDF-062 machinery (no new FR) — §10.9.3; (4) `ISourceClient` honors declared response charsets (windows-1254/ISO-8859-9 via the code-pages provider, registered in product startup — moved out of fixtures) — §10.9.4; (5) the KAP XBRL fact envelope becomes a first-class production parse target (concept map collapsing IFRS/TR tag variance onto `02` §6.1 item codes, Turkish-locale numbers, period/statement/version grouping, duplicate-concept rule: equal collapses / unequal quarantines — never silent latest-wins) — §10.9.5; (6) real EVDS envelope verified/hardened against a bounded capture — §10.9.6; (7) test-double posture: FU canonical envelope stays the L2 golden contract (goldens untouched); a charset-faithful, honestly-labeled real-source catalog is the recorded FU §1 exception and parsing-fidelity lane — §10.9.7. (8) the stored `volume` semantic is pinned to **TRY turnover (₺)** — İş Yatırım rows store `HGDG_HACIM` as-is; Yahoo-contingency rows store NULL volume (honest gap, different unit); the movers threshold (`Ingestion:MarketSnapshot:VolumeEligibilityThreshold`) is recalibrated from the observed BIST 30 distribution at live activation; FU volumes reinterpret as ₺ unchanged (no golden churn) — §10.9.1 (FLG-09 builder resolution, 2026-10-17). AD-14 records the decision; no new FRs; downstream (C5 storage, C3b metrics, C2 API) deliberately untouched | FR-MDF-001/010 (prices mechanism refined — real envelope + per-ticker loop); FR-MDF-002 (statements mechanism refined — real envelope); FR-MDF-006/007 (index bound + launch seed); FR-MOV-014 (EVDS hardening); FR-MDF-008 (provenance posture unchanged: `isyatirim`/`kap`/`evds`); `03` §1.2 per-row price-source provenance unchanged; FR-MOV-021/FR-MOV-005 + BR-MOV-011 (volume semantics: ₺ turnover canonical — FLG-09 resolution) | **Re-validation:** test-planner mirrors §10.9.7 (record the FU §1/§10 exception; give TC-MDF-063..068 from PR #82 a recorded home; add an EVDS capture case — DR-02's routing); planner slices the follow-up wave (lift decoders/concept-map/charset registration from branch `TKT-foundation-015` into `src/`; symbol-scoped adapter fetch + job loop; XU030 seed; EVDS bounded capture); PR #82 stays closed-unmerged (raw material only); FLG-08 resolved same day (one-line `02` §3.1 mirror applied: `XU30` → `XU030`); FLG-09 resolved by builder decision 2026-10-17 (stored `volume` = TRY turnover ₺ canonical; Yahoo rows NULL volume; movers threshold `Ingestion:MarketSnapshot:VolumeEligibilityThreshold` recalibrated from the observed BIST 30 distribution at live activation; FU volumes reinterpret as ₺ unchanged — no golden churn — §10.9.1) |
| v1.5.1 | 2026-10-18 | **XU100 covered-universe launch-seed** (TKT-foundation-022, §10.9.3 amendment): a checked-in, date-stamped `Xu100LaunchSeed` (sibling of `Xu030LaunchSeed`) writes the current BIST 100 membership to `index_constituents` append-only, closing the real-universe read-path gap — the real İş Yatırım universe snapshot publishes instrument rows only, so no real XU100 membership was ever written and the covered-universe read surface (FR-RES-001) served only the fixture universe. Provenance `bist://xu100-launch`; wired into `UniverseSyncJob` gated on real-source + cold-start + once-only by the seed's own provenance marker; deliberate coexistence with FU XU100 members (AlreadyPresent); XU030 ⊆ XU100 invariant asserted; no new FR (the FR-MDF-007 recorded launch state). Read path and `PricesJob` unchanged. | FR-RES-001 (read surface unblocked); FR-MDF-007 (recorded launch state — no new FR); FR-MDF-006/008/011; BR-MDF-005/007; NFR-MDF-003 | Tests carrying the TKT-foundation-022 tag in LiveActivation/UniverseRealEnvelope/UniverseSync/StockListEndpoints; universe-lane TC derivation remains a test-planner follow-up (flagged by 021) |
| v1.5.2 | 2026-10-18 | **Real-only dev-stack data posture** (TKT-foundation-023, new §10.9.9): pins the dev data-world decision after the user-reported mixed 116-stock dev list (100 real + 16 FU fixture, a *data condition*, not a read-path defect). **Option A (recorded):** the real-only dev stack is a fresh database built by the real chain only — `down -v` → `up` → `run --rm migrate` → admin-trigger `universe-sync` → prices backfill — documented end to end in README "Dev-stack data"; the FU fixture seed stays the L2/L4 CI world and an opt-in dev/demo bootstrap. §10.9.3's deliberate FU/real coexistence in CI is the *reason* a reset path (not an exclusion switch) is the fix. Rejected: fixture-exclusion switch (a read-path filter existing only to undo a seed; `StockListEndpoints` is right — FR-RES-001) and "dev keeps both, only production is real" (leaves the reported mixed list as the dev default and lets T-anchored derived data pose as real). `FixtureSeedEntry.RunAsync` gains one honesty warning line on the seed path (log only — exit code, idempotency, L4 contract unchanged). | FR-RES-001 (dev browsing experience fixed by data posture, not read-path change); FR-MDF-006/007/011; FU §1 (fixture universe kept intact as the CI world); NFR-MDF-005 (reproducible documented bootstrap); `02` §8 (application seed the migrate step runs — unchanged) | No TCs needed (docs + one log line; foundation convention — explicit criteria). README + §10.9.9 + `FixtureSeedEntry` only; nothing under `src/Api`/`src/Ingestion`/`tests/` changes. Live criterion (018–022 honesty posture): the orchestrator runs the documented reset path on a fresh dev volume post-merge and verifies the stock list serves exactly the real covered universe |
| v1.5.3 | 2026-10-18 | **KAP statements company-scoped wiring** (TKT-foundation-024, new §10.9.10): the `statements` job is wired to a real per-company KAP resource — the §10.9.5 envelope carries one `companyCode` per payload, so §10.9.2's seam doctrine applies verbatim to companies: company-scoped `StatementsSourceAdapter.FetchCompanyAsync` (`{symbol}`-templated path), the loop stays in the job over the bound index's current constituents, strictly sequential + paced (`Ingestion:Statements:RequestDelayMs`, default 500 ms), per-company failure isolation (a transport failure marks the company failed, writes **no** quarantine, raises one aggregate alert, continues the loop; a parse failure quarantines `UNPARSEABLE_PAYLOAD` and continues; terminal `Failed`/`Partial`/`Succeeded`). Its **own bound key `Ingestion:Statements:UniverseIndex`, default `XU030`, independent of `Ingestion:Prices:UniverseIndex`** (AD-14). Ships `Ingestion:Kap:BaseUrl = https://www.kap.org.tr` + an `Ingestion:Statements` section in `src/Api/appsettings.json`. **Live discovery honestly blocked** (2026-10-18): `www.kap.org.tr` is behind a Next.js front end, the former `kapsitebackend.mkk.com.tr` backend no longer resolves, and the candidate route returned 500/404 on every probe — **no capture fabricated**, no `RealSourceCatalog` entry; the shipped `Path` is a discovered-but-unverified candidate and live verification is the orchestrator's post-merge criterion. No golden churn: the company seam needed only an L2 harness *binding* delta (test-local `StatementBound` index `STMT`). | FR-MDF-002 (activated); FR-MDF-008 (per-company identity guard + provenance); FR-MDF-010/011 (backfill depth via existing `CoverageRecorder`); FR-MDF-012 (per-company isolation); unblocks FR-RES-006/008/009/010/011, FR-VAL-001, FR-MOV-006; AD-14 | Existing statements-path goldens (TC-MDF-065/067/071 + versioning/normalization/accounting-profile lanes) re-run green unchanged; new loop tests reuse TC-MDF-072/073/074 lane shapes for companies; statements-loop TC derivation remains a test-planner follow-up (flagged by 021) |
| v1.5.4 | 2026-10-18 | **XU100 price coverage + index levels for both BIST indexes** (TKT-foundation-025): closes the two remaining gaps from the user's 2026-10-18 live check. (1) **Prices bound flipped** `XU030` → `XU100` in the shipped `src/Api/appsettings.json` (the §10.9.2 "XU100 later = a config flip, not new code"; valid now that the XU100 membership seed of §10.9.3 v1.5.1 supplies the rows) — the initial run backfills the ~70 newly-covered members through the existing per-ticker backfill path; pacing stays the pinned sequential `RequestDelayMs` 500 ms posture. (2) **Index levels for both BIST indexes** (FR-MDF-006 "including BIST 100 and BIST 30"): `Ingestion:Universe:IndexLevelsIndex` becomes the set **`IndexLevelsIndices`** (production default `[XU100, XU030]`; the pre-025 single-value key is removed — no shipped config used it), the sync loops the set sequentially through the templated route, and degradation is **per index** — a failing index quarantines `SOURCE_FETCH_FAILED` + alerts + `Partial` while the other index's levels still store (§10.9.8). (3) **`MarketSnapshotEngine` BIST 30 spelling fix**: the index-move lookup resolves both the real `XU030` and the FU `XU30` spellings, so a real-only DB never silently degrades the BIST 30 move to (0, 0) (FLG-08); the FU world has no `XU030` row, so every existing golden re-runs unchanged (§10.9.7). No new FRs. | FR-MDF-001/010 (prices coverage + backfill over the covered universe); FR-MDF-006 (both BIST indexes' levels); FR-MDF-008 (provenance on synced levels); FR-MOV-001 (both index moves non-degenerate); FR-RES-016/BR-RES-008 (price-less members' honest no-data posture deliberately unchanged — the fix is coverage, never a read-path change); BR-MDF-005 (covered universe = BIST 100); AD-14 | Test-plan delta: TC-MDF-075's "no override → production default" expectation amended `XU030` → `XU100` (planner-applied, test-planner confirmation pending; L2 harness bindings + goldens untouched); new universe-lane tests in `UniverseSyncTests`/`UniverseRealEnvelopeTests` (multi-index loop, per-index degradation) and a `MarketSnapshotTests` both-spellings test carry the `TKT_foundation_025` tag; TC-MDF-063 re-runs green with the new honestly-labeled XU100 capture; FU goldens (TC-MOV-001, EodChainTests, MarketEndpointsTests) re-run unchanged. Live criteria (orchestrator post-merge, the 021/023/024 precedent): a real prices run at `UniverseIndex=XU100` covers all current XU100 members + backfills; the index-levels sync stores real XU100 **and** XU030 levels; the snapshot recomputes over the real universe (market P/E / sector medians wait on 024's statements) |
| v1.5.5 | 2026-10-18 | **KAP statements source rewired to MKK's official "KAP VYK API"** (TKT-foundation-026, new §10.9.11): supersedes 024's blocked website-proxy live criterion **by replacement** — the company-scoped `StatementsJob` loop, per-company isolation, envelope detection and keyed `IKapSourceClient` of v1.5.3 are reused unchanged; only the **source** is rewired to MKK's Apinizer gateway. The dev gateway **`https://apigwdev.mkk.com.tr/api/vyk`** is spec-verified and ships; the production host (`apigw.mkk.com.tr`) did not resolve from this environment and is **pinned by live verification, never assumed**. Auth = HTTP **Basic** on the KAP `HttpClient`, attached **iff** `Ingestion:Kap:ApiKey`/`ApiSecret` is present (env `Ingestion__Kap__ApiKey`/`Ingestion__Kap__ApiSecret`; `appsettings.json` ships empty defaults, the `EvdsApiKey` precedent; **live-verified**: the API Key is the Basic username, the API Secret the password), never in the URL, always redacted. Company mapping is a paced `GET /members` (`stockCode`→`companyId`, lazy+cached); a bound symbol with no mapping is marked failed with the named reason **`COMPANY_IDENTITY_UNRESOLVED`** (no rows, no fabricated mapping, counted in per-company stats, loop continues); a `/members` transport failure degrades honestly (snapshot `Failed`, every company `SOURCE_FETCH_FAILED`, one aggregate alert). Statements are class **`FR`** disclosures: `GET /disclosures?disclosureIndex={cursor}&disclosureClass=FR&companyId={id}` then `GET /disclosureDetail/{disclosureIndex}?fileType=data`; the detail's XBRL-style **`presentation`** tree (live-verified — the OpenAPI's declared `flatData` is **not** returned) joins the §10.9.5 decode doctrine (first-class real envelope, each sub-report's root name selects `BS`/`IS`/`CF`/`EQ`, each leaf's `CURR`-context value collapses onto `02` §6.1 via the shared concept map extended with the bare-name spellings, grouping + duplicate-concept rule verbatim, provenance `kap`), the identity guard skips a mismatched-`companyId` disclosure **before decode**. The per-company incremental cursor is the last-processed `disclosureIndex`, persisted in the existing `coverage_metadata` (`kap-disclosure-cursor`, outside `InstrumentScoped` — no migration, no reconcile clobber); a cursor-less company starts from `Ingestion:Statements:EarlyDisclosureIndex`. Pacing flips `Ingestion:Statements:RequestDelayMs` **500 → 10000** and every gateway call is paced by a scoped `KapRequestPacer`; an HTTP 429 / Apinizer throttle fault is a retryable transport failure (existing retry ladder, no quarantine), a real-body parse failure quarantines `UNPARSEABLE_PAYLOAD`. **Honest shape notes:** the `presentation` shape is pinned by a bounded **live capture** (`RealSourceCatalog` `kap-vyk-disclosure-detail`, disclosure 1226255, 2026-10-18, real CURR/PREV values — **no capture fabricated**); the member/list CI doubles are shape-faithful constructed bodies; `consolidation` (detail field with no `02` §3.1 home) is **not persisted** (flagged); the attachment-file layer (PDF/XBRL) is a **separate follow-up ticket**, never silently scoped in. | FR-MDF-002 (per-instrument per-period statement ingest activated live); FR-MDF-008 (provenance `kap` + company identity guard); FR-MDF-010 (backfill via `EarlyDisclosureIndex` + achieved depth recorded honestly); FR-MDF-011 (coverage metadata downstream); FR-MDF-012 (per-company isolation, unmapped-symbol named reason); unblocks FR-RES-006/008/009/010/011, FR-VAL-001, FR-MOV-006; BR-MDF-006; AD-14 | No statements-live TC exists (the 021/024 test-planner follow-up flags stand) — explicit CI criteria per the foundation convention: a 12-case suite in `tests/Api.IntegrationTests/VykStatementsSourceTests.cs` carrying the `TKT_foundation_026` tag (config wiring + Basic-auth iff credential + redaction; members mapping + real-shape detail decode/ingest; unmapped-symbol named reason; members transport failure; identity guard pre-decode skip; duplicate-concept rule; cursor persist/resume/idempotency; pacing across all VYK calls; throttle 200-fault + 429; the bounded capture through the production decoders). `RealSourceCatalog` gains two constructed VYK list entries + one bounded captured detail entry; canonical goldens untouched; the 024 config-wiring test's two shipped-value assertions updated minimally in place (BaseUrl + `/disclosures`). Live criteria (orchestrator post-merge): the credential pair now works against the dev gateway (HTTP 200 verified 2026-10-18) — a live run must pin the **production** BaseUrl (`apigw.mkk.com.tr` did not resolve at implementation time) before the statements-source live criterion closes; the dev-gateway list/detail decode is already capture-verified. Known consequence to flag: `DisclosuresSourceAdapter` shares `IKapSourceClient`, so the disclosures job now also targets the VYK gateway (out of scope; its live activation was already blocked). |
| v1.5.6 | 2026-10-18 | **TCMB EVDS macro source migrated to the EVDS 3.0 platform** (TKT-foundation-027, new §10.9.12): supersedes §10.9.6's live-source wiring **by replacement** — `evds2.tcmb.gov.tr` now 302-blanket-redirects to **`evds3.tcmb.gov.tr`** and the v2 REST paths + some v2 `TP.*` datagroup codes are gone. The macro adapter, jobs, store, `MacroFreshnessService`, alerting and `source_ref = evds` are reused unchanged; only the **source transport** is rewired. The v3 values endpoint **`POST https://evds3.tcmb.gov.tr/igmevdsms-dis/fe` is ANONYMOUS** (live-verified 2026-10-18: HTTP 200 + real data with no key/cookie/login — the earlier 400s were a wrong body shape, not auth), so **no credential travels**; the `EvdsApiKey` setting is retained only as an optional redaction surface + the legacy canonical-double GET (empty default; never committed) and the SEC-1 query-string-leak follow-up does **not** apply to v3. Series codes are **discovered** (anonymous catalog `GET …/categories/withDatagroups/type=json` + `GET …/serieList/fe/type=json&code=<datagroup>`) and resolved to the live-verified identifiers — a datagroup/series absent from discovery is **skipped** (job LATE), never guessed. The request body is pinned verbatim from the site's own `getSeriVerileri` (hyphen-joined `series`/`aggregationTypes`/`formulas`, `frequency:"2"`, `dateFormat:"0"`/`yon:"0"`/`sira:"0"`, `ozelFormuller:[]`); the response underscores column keys (`TP_DK_USD_S_YTL`) and serialises values as **strings with thousands-grouping commas** — the decoder normalises `_`→`.` and strips grouping. **Code-pin resolutions recorded (never guessed):** (1) **CPI YoY** — the platform publishes the CPI only as an **index** (`bie_tukfiy2025` has 349 monthly index series, no published annual-change series), so the adapter fetches `TP.TUKFIY2025.GENEL` over a window derived from the output bound (`output start − 12 months`) and emits deterministic `YoY = (indexₘ/indexₘ₋₁₂ − 1) × 100` (`EvdsCpiYoY`) for months in the 90-day output window; (2) **`CBRT_REPO` = `TP.BISPOLFAIZ.TUR`** (from the "Merkez Bankaları Politika Faiz Oranı" datagroup — the plan's candidate list didn't name it; confirmed via serieList). Values are requested **one request per cadence** (business-day FX/gold, monthly repo, CPI index) because the platform collapses a mixed-frequency body to its lowest common frequency (live-verified 2026-10-19: 4 monthly rows vs 65 business-day rows). Series map (union, not replacement): `USD_TRY`←`TP.DK.USD.S.YTL`, `EUR_TRY`←`TP.DK.EUR.S.YTL`, `GOLD`←`TP.ALTINPIYASA.KAP03` (USD/oz, matches the seeded unit; published by `bie_altinbistbul`, live-verified 2026-10-19), `CBRT_REPO`←`TP.BISPOLFAIZ.TUR`, `TUIK_CPI`←CPI index→YoY; `INDEP_CPI` stays admin-entered; the fixture-era identifiers stay mapped so the L2 canonical double parses unchanged (discovery **off** by code default; the shipped `appsettings.json` enables it). `Ingestion:Macro` ships `BaseUrl=https://evds3.tcmb.gov.tr`, `Path=/igmevdsms-dis/fe`, additive `CatalogPath`/`SerieListPath`, `EvdsApiKey=""`; `compose.dev.yml` passes the optional key through; `.env.example` documents the v3 wiring. `RealSourceCatalog` gains four honestly-labeled entries (`evds-v3-values`/`evds-v3-catalog`/`evds-v3-serie-list` constructed, `evds-v3-capture` a bounded **live capture** — **no capture fabricated**). | FR-MOV-014 (macro ingest live-activated on the new platform); FR-MOV-007 (macro strip incl. USD/TRY unblocked); FR-MOV-016 (last-known/stale when v3 is down — LATE-on-missing-series posture preserved); FR-MOV-009 (per-series as-of); FR-MDF-008 (provenance `evds` + key-never-logged posture re-verified); OQ-MOV-005 re-resolved; AD-14; §10.4 (config) | No macro TC covers a live v3 shape yet (the real-envelope lane anticipated the v2 capture; TC-MOV-028's posture transfers). Explicit CI criteria per the foundation convention: the adapter suite + the real-envelope/ingest suite in `tests/Api.IntegrationTests/MacroEvdsAdapterTests.cs` and a deployment-wiring test in `LiveActivationTests.cs`, all carrying the `TKT_foundation_027` tag (v3 discovery + `/fe` body shape; underscored/comma decode → canonical; CPI YoY; missing-datagroup skip; provenance stamp + malformed quarantine; key-never-on-wire + redaction; appsettings + `.env.example` + `compose.dev.yml` wiring; real v3 envelope decode/ingest into `macro_values` with `source_ref=evds`; bounded 90-day window; bounded live capture; canonical double re-parses unchanged). TC-MOV-026 canonical goldens untouched (legacy lane unchanged). Live criterion (orchestrator post-merge): the anonymous `POST /fe` returns real values for all five series (USD/TRY + EUR/TRY verified live 2026-10-18) and a macro run stores them |
| v1.5.7 | 2026-10-18 | **Market snapshot targets the latest stored trading day** (TKT-foundation-028, new §10.9.13): the `market-snapshot` job's date selection changes from "always `request.Date` (UTC today)" to **"the latest trading day ≤ `request.Date` that has stored prices for the covered universe"** — the max `daily_prices.price_date` ≤ `request.Date` over the current XU100 membership (`index_constituents`, `effective_to IS NULL`), the same covered-universe filter the engine uses; **stored prices are the truth, never a weekday/holiday rule** (`LatestTradingDayOnOrBefore` helpers are pure weekday-backing and `TradingCalendar.Holidays` ships empty). This closes the weekend/cold-start gap (the overview must show Friday on Saturday/Sunday) and self-heals a missed EOD run. Trading-day behaviour unchanged (requested day resolves exactly); **idempotent no-op** when the resolved day already has a `market_snapshots` row (a completed day is never recomputed/rewritten — the TC-MOV-005 one-row-per-day contract); the honest empty rule is unchanged (no stored price ≤ `request.Date` → `Succeeded`/0 written, no fabricated row, overview `state: unavailable`). **No `appsettings.json` change** — a config flag is rejected while the behaviour is simply "latest stored trading day"; the `prices` job is deliberately untouched. The serving contract of `03` §3 (latest-row + `stale`) is the unchanged consumer. | FR-MOV-001..006 (equity blocks materialized for the latest completed day); FR-MOV-010/NFR-MOV-001 (freshness: the dashboard reflects the latest completed trading day — weekend/cold-start case); BR-MOV-001 (latest-completed-trading-day posture); BR-MOV-009 (formulas unchanged — only which day they run over); `03` §3 (`GET /market/overview` latest-row + stale — unchanged consumer); `02` §3.2 (`market_snapshots` one-row-per-trading-day idempotency — unchanged) | New L2 date-selection tests in `MarketSnapshotTests` (`TKT_foundation_028` tag: Sat→Fri pick + `GET /market/overview` `asOf`/`stale:false`; already-snapshotted → no-op; trading-day → as-now; no-prices → no row + overview `unavailable`); existing TC-MOV-001..005 goldens and the EOD chain (`EodChainTests`) re-run green unchanged (fixture anchor T is a trading day with prices, so the selection resolves T exactly); TC-MOV-012 payload `asOf`/`stale` unaffected. Live criterion (orchestrator post-merge, the 021/023/024 precedent): an admin-triggered `market-snapshot` run on a Saturday records a real row at **2026-10-16 (Friday)** from the stored real prices and `GET /api/v1/market/overview` serves it (`asOf 2026-10-16`, `stale:false`); a trading-day run records the run day as now — attempt/result recorded on the ticket, never fabricated |
| v1.5.8 | 2026-10-18 | **Data-source provenance disclosure** (TKT-foundation-029): an honest per-data-class source disclosure is added to the product — a site-wide `DataSourceDisclaimer` footer element in the shell (`AppShell.tsx`, every route, not only research routes; a plain text block, **not** a second `complementary` landmark, so it never collides with the informational-only `Disclaimer` of FR-MOV-020/UXR-G-016) plus one tiny anonymous read endpoint `GET /api/v1/meta/data-sources` returning the per-class source catalog (`id, class, source, state: live\|unavailable, nature`) from a **small static product-side catalog** (`src/Api/Metadata`). Honesty is server-owned (§10.7): whether a class is live changes as the real-source work lands, so liveness lives in the server payload, not a build-time web bundle. The disclosed classes are exactly the AD-14 real sources (no new source): İş Yatırım public EOD endpoints (prices / index levels / universe) **live**; MKK "KAP Data Dissemination Services" (statements, via 026) and TCMB EVDS (macro, pending 027) honestly **unavailable** until their live activation. Every class states its EOD/as-of nature (not real-time) and the free public-source caveat; a class with no live values renders the honest `unavailable` marker — never a claim of a live source, never fabricated live data. No DB, no migration, no new model; `fixtures/Degerli.Fixtures/RealSourceCatalog.cs` stays the test-only parsing-fidelity lane and is never referenced by product code. | FR-MDF-008 (class-level provenance of the per-fact lineage); FR-MOV-020 (+ FR-SCR-016/FR-RES-017/FR-VAL-008 covered in one place by the site-wide footer); FR-MOV-009 (as-of nature); §10.7 honest-state (`state: unavailable`, never blank-as-zero); §10.6 i18n (tr/en catalogs + required parity); §10.9/AD-14 (the disclosed classes); §3 component C1 (footer shell surface); `03` §3 (new public read endpoint under the §11 additive rule), §12/§13 | Explicit foundation criteria (no TC covers a provenance disclosure; the XC disclaimer sweep TC-XC-006 covers only the informational-only component): component tests `DataSourceDisclaimer.test.tsx` (shell-footer render, TR default/EN toggle, per-class source + as-of nature, honest `unavailable` marker, no live claim for a fixture-only class, no duplicate landmark, a11y clean), backend `DataSourcesEndpointsTests` (anonymous 200, no DB, honest per-class catalog), i18n parity green; existing `Disclaimer` + TC-XC-006 unaffected. **Flow-back (UXR-G-016 disclaimer lane):** the ux-designer records this surface/placement in the UX spec per the flow-back standard. |
| v1.5.9 | 2026-10-20 | **Market snapshot: price-derived vs share-derived aggregate decoupling** (TKT-foundation-031, new §10.9.14; user-authorized product change 2026-10-19/20 — "make something so it will be showing live data in the morning"): the `market-snapshot` engine no longer requires a balance-sheet `SHARES_DILUTED > 0` for every covered-universe constituent. A member without a usable share count carries no market cap and splits by block: it **is included** in breadth (FR-MOV-003), top movers + volume eligibility (FR-MOV-004/021, BR-MOV-011) and total volume (FR-MOV-005); it **is excluded and counted** from market P/E (FR-MOV-006, BR-MOV-010, D-05 — `market_pe_excluded_count` now counts every non-P/E constituent: loss-makers, missing income and no-share members) and from the market dividend yield (ΣDIV_TTM/Σmcap over share-bearing members only — `null` when none); and it carries no cap weight in cap-weighted sector performance (FR-MOV-002), where a sector with no share-bearing member is **omitted** from the dictionary (`{}` in a zero-shares world — an explicit `0.0` would be fabricated). The engine-level null return survives only for its original meanings (no XU100 membership / no stored prices for the covered universe on the resolved day); the row is written and the overview serves `state: available` whenever the price-derived blocks are computable. **FU fixture world byte-identical** (every FU constituent has shares, so every branch is a no-op); `02` §3.2 and `03` §3 unchanged (nullable P/E columns + null serving already contracted); UI unchanged (`MarketOverviewPage` already renders the honest unavailable valuation state). No pacing constraint — stored facts only, no external source call. | FR-MOV-001..006, 021 (price-derived blocks materialize with zero share data); NFR-MOV-001; BR-MOV-009 (canonical facts only — unchanged inputs); BR-MOV-010 (P/E exclusion doctrine extended to no-share members); BR-MOV-011; `03` §3 (payload shape unchanged — nulls already contracted); `02` §3.2 (one row per trading day, nullable P/E columns — unchanged) | New L2 cases in `MarketSnapshotTests` (`TKT_foundation_031` tag): zero-shares world → one row written + `GET /market/overview` `state: available` with real indices/breadth/movers/volumeTotal, `marketPe: null`, `marketPeExcludedCount` = constituent count, `marketDivYield: null`, `sectorPerformance: {}`; partial-shares world → P/E, div yield and sector weights over the share-bearing subset only, no-share members still in breadth/movers/volume and in the exclusion count; documented as TC-MOV-029/030 in the MOV test plan. TC-MOV-001..005 goldens, `MarketEndpointsTests`, `EodChainTests`, the `MarketAggregatesTests` unit cases and the L4 overview spec re-run green unchanged (no permitted golden delta — the fix branches only when shares are absent). Live criterion (orchestrator post-merge, the 028 precedent): an admin-triggered `market-snapshot` run on the real dev stack records `WRITTEN=1` at `snapshot_date=2026-10-19` and `GET /api/v1/market/overview` serves `state: available`, `asOf=2026-10-19`, `stale=false`, real XU100 11,430.14 / XU030 14,221.62 and real breadth/movers/volume with `marketPe: null`, `marketDivYield: null`, `sectorPerformance: {}` — attempt/result recorded on the ticket, never fabricated. |
| v1.5.10 | 2026-10-20 | **KAP VYK URI composition fixed at the client seam** (TKT-foundation-032, §10.9.11 amendment): the shipped VYK base `https://apigwdev.mkk.com.tr/api/vyk` (no trailing slash) plus its leading-slash path options (`/members`, `/lastDisclosureIndex`, `/disclosures`, `/disclosureDetail/{disclosureIndex}`) made .NET resolve every request **root-relative**, silently dropping the `/api/vyk` prefix — every real-gateway call 404'd (live-verified 2026-10-19/20: the unprefixed URL 404s, `…/api/vyk/members` returns 200 with the same HTTP Basic pair); CI could not catch it because the L2 doubles bind at the server root, where root-relative and base-relative resolution coincide. Fix is **code-side, not config discipline**: `KapHttpSourceClient` normalizes the base to a directory separator **and** trims the path's leading slashes (`KapUri`, shared with the `IKapSourceClient` registration), so every spelling resolves under the configured prefix; `appsettings.json` values stay unchanged, redaction/Basic-auth untouched (header, never URL). **Doctrine:** a double for a prefix-carrying gateway must bind **under the prefix**; root-bound doubles cannot catch this class. | FR-MDF-002..005 (statements/dividends/actions/disclosures read through this client); FR-MDF-008 (provenance carries the correctly-resolved URL); §10.9.11; TKT-foundation-026 heritage; unblocks TKT-foundation-033/034 | Explicit foundation criteria (no TC lane — a post-merge defect fix, the 030 precedent): new `KapUriResolutionTests` (prefixed double + the exact shipped spellings, red→green; the base×path spelling matrix; the full VYK company fetch across all four routes) carries the `TKT_foundation_032` tag; `VykStatementsSourceTests`, `StatementsAndEventsIngestionTests`, `RealSourceStatementsTests`, the statements loop/normalization/versioning lanes and the canned-catalog contract tests re-run green unchanged (root base + normalized path = the same root routes); no `appsettings.json` diff; CI green required. Live criterion (orchestrator post-merge, paced ≥12 s/call): the dev stack rebuilt from merged `dev` **without** the interim `/tmp/opencode/dev-overrides.yml` env overrides answers HTTP 200 on one paced KAP VYK statements fetch. |
| v1.5.11 | 2026-10-20 | **The three event families rewired from placeholder KAP paths to the verified VYK gateway routes** (TKT-foundation-034, new §10.9.15): `dividends`, `corporate-actions` and `disclosures` still fetched `/dividends` / `/corporate-actions` / a non-existent `/disclosures` single-resource body, so all three 404'd on the gateway and could never ingest a real row (026 rewired `statements` only). **Investigation-first, live-verified 2026-10-20 (~30 paced probes + real captures; never guessed from the ambiguous OpenAPI):** all three families are served by `GET /disclosures?disclosureIndex={n}&companyId={id}` (ascending, ≤50/page, rows carry `disclosureIndex`/`disclosureType`/`disclosureClass`/`title`=<i>company name</i>/`companyId`, **no date**) plus `GET /disclosureDetail/{index}?fileType=data` (real `subject{tr,en}`, `time` `dd.MM.yyyy HH:mm:ss`, `link`, `eventType`, `eventId`, and — unlike 026's `FR` bodies — **`flatData` IS returned**: `content.object.void[]` with `currency{key}`, `cashDividendList` (`exchCode`/`installmentOrder` incl. `T`/`grossTry`/`netTry`) and `cashDividendPaymentDateList` (`advisedUsageDate`/`recordDate`/`paymentDate`)). `corporate-actions` = listed `disclosureType=CA`; `dividends` = `CA` + detail `eventType=KP` (observed vocabulary `KP`/`SA`/`GAI`/`SPAF`/`IT`/`GK`); `disclosures` = every listed type. **No family is blocked.** Two live-verified gateway quirks decide the adapter rules: a *filtered* empty list and an index past a company's last disclosure both answer **HTTP 400 `ER005`** (not `200 []`), so the family filter is applied **client-side on the listed row** and the per-company cursor is read **inclusively** (re-request from the last-seen index). **Fix round 2 makes that honest-empty rule real:** `KapHttpSourceClient` now carries the HTTP status and a bounded, redacted body excerpt on `SourceFetchException` (the success read stays **buffered**, so its body remains inside the client's 30 s `HttpClient.Timeout` — a gateway that sends headers then stalls cannot hang a run that has no job-level timeout); only **`400` on the list route whose body is the `ER005` envelope** is classified as end-of-list (no exception, no rows, cursor kept at the last emitted index — a zero-disclosure company's first pass succeeds), while `404` (whatever its body, an `ER005` one included)/`401`/`429`/fault/non-`ER005` `400`/transport and any detail-route failure still fail the run; a non-list `200` body is an `UNPARSEABLE_PAYLOAD` quarantine, never an empty success; a numeric (non-string) `disclosureIndex` is normalized to its invariant string rather than silently dropped after the parse accepted it; a persisted cursor whose first request answers `400`+`ER005` is reset **once** to `EarlyDisclosureIndex` (logged) so one stale row cannot poison every future run; a non-empty cursor that does not parse fails loudly. **Index-0 probe (one paced call, 2026-10-20):** `GET /disclosures?disclosureIndex=0&companyId=1406` → **200** with the company's first page (39 rows, `1092405`…`1153433`), so a first pass at `EarlyDisclosureIndex=0` proceeds normally (earliest served index `1092405`). Because the job files are outside the ticket, the company-scoped loop of §10.9.10/§10.9.11 lives **in the adapters** (universe → `/members` → per-company list/detail), paced by the shared `KapRequestPacer`; per-family per-instrument cursors (`kap-dividends-cursor`, `kap-corporate-actions-cursor`, `kap-disclosures-cursor`) reuse `coverage_metadata` outside `InstrumentScoped` (no migration) and persist **at the end of the adapter loop, before the job's store write** (a failed fetch never advances a cursor; a post-fetch store failure can skip those indexes — accepted residual, ticket-noted, recovered by the stale-cursor reset); failure posture is **fail-fast and honest** (transport/404/401/429/`400`-not-`ER005`/fault → `SourceFetchException` → run recorded `failed`, `400`+`ER005` → end-of-list honest empty, malformed or non-list body → `UNPARSEABLE_PAYLOAD`, unmapped symbol → `COMPANY_IDENTITY_UNRESOLVED`, empty bound → failure). Mapping keeps every canonical contract: `ActionType` = the gateway `eventType` code (no adjustment factor invented), `ActionDate` = publish date, `Terms` = the verbatim flatData terms document; one dividend fact per instalment (the `T` row is skipped), `AmountPerShare` = `grossTry`, `ExDate` = `advisedUsageDate`; disclosure `Title` = `subject.tr`, `SourceUrl` = `link`, `PublishDate` = `time`. Config: `Ingestion:{Dividends,CorporateActions,Disclosures}` ship `Path=/disclosures`, `DetailPath=/disclosureDetail/{disclosureIndex}`, `UniverseIndex=XU030`, `EarlyDisclosureIndex=0`, `DisclosureType=CA` (empty for disclosures), `DividendEventType=KP`; code defaults identical. The legacy single-resource branch is kept verbatim for VYK-disabled compositions, so no golden changes. | FR-MDF-003/004/005 (all three jobs activated on a real source); FR-MDF-008 (provenance `kap`, identity guard, cross-`exchCode` guard); FR-MDF-010/011 (backfill via `EarlyDisclosureIndex` + honest achieved depth); FR-MDF-012 (malformed → quarantine, transport → failure ledger); FR-MOV-006 (TTM DPS now has a real dividend supply); §10.9.11 (shared client/cursor/pacing seams reused), §10.9.10 (company-scoped loop doctrine); AD-14 | No golden delta: `StatementsAndEventsIngestionTests` TC-MDF-003..006 re-run byte-identical on their canonical doubles (VYK stays disabled there), and `EodChainTests`/`EodStaleServingTests` are unchanged. New L2 tests carrying the `TKT_foundation_034` tag cover the three verified routes against shape-faithful doubles built from real captures (BİM `KP` 1138986, Tüpraş `SA` 1138796), 404 + Apinizer-fault honest failures (failed ledger, no rows, no quarantine), inclusive-cursor resume/idempotency, and the shipped config ↔ code-default agreement for **every** family key. Fix round 2 adds: `400`+`ER005` honest empty across all three families, `200 []`, stale-cursor reset, `400` with another code (still failed), `404` with an `ER005` body (still failed), non-list `200` body (quarantined), a numeric `disclosureIndex` row (ingested, cursor advanced), an unparseable persisted cursor (loud failure, zero list calls), detail-route `404` mid-walk (failed, no rows, no quarantine), truncated detail body and detail missing publish metadata (both quarantined); the list doubles bind `companyId`+`disclosureIndex` and the tests assert the actual query. `VykStatementsSourceTests`, `KapUriResolutionTests`, `EodChainTests`, `EodStaleServingTests`, `AdminEndpointsTests`, `CannedSourceCatalogTests` re-run green. Live criteria (orchestrator post-merge, paced ≥12 s/call, 401-penalty-box aware): one paced run per family against the reachable dev gateway ingests real rows with `provenance=kap`, or records the honest result — attempt and outcome on the ticket, never fabricated. |
| v1.5.12 | 2026-10-20 | **Percentage unit contract pinned; Web display ×100 defect fixed** (TKT-foundation-036, `03` §2/§4 amendment): the API has always served every ratio/percentage metric as a **decimal fraction** (`Core/Metrics/MarketAggregates.cs` documents `ChangePct` "0.05 = +5%"; every API golden pins fractions — `MarketEndpointsTests` xu100 `0.02`, gainer ETA `0.10`, sectors `0.00253/0.022409/0.041726`; the live dev stack served AGHOL `0.014735`). The **Web** layer disagreed: `formatSignedPercent` rendered its input verbatim, so every SCR-001 change on the home screen displayed **100× too small** (AGHOL "+0,01%" for +1,47%; BIST 100 "−0,02%" for −2,49%) — a fabricated number under §10.7. **Contract decision (planner-made, recorded): the API stays fraction-unit; the fix is Web-side.** The formatter becomes the unit boundary (`style: 'percent'`-equivalent ×100 + `signDisplay: 'always'`, so `0.014735` → `+1,47%`), `contracts.ts` pins the fractions on `IndexLevel`/`MoverEntry`/`SectorPerformance`, the MSW doubles are realigned to the API golden units, and `03` §2 gains the explicit rule "all ratio/percentage metrics — request bounds and response values — travel as decimal fractions (0.02 = +2%)" with §4's illustrative examples corrected from percent notation to fractions (`roe: 0.182`, `minValue: 0.15`) — the percent-notation examples were the same trap class. No API, engine, schema or golden change. | FR-MOV-001/002/004/021 (signed changes rendered truthfully); FR-MOV-006 (div-yield correct-path regression guard); UXR-MOV-001/003/004 (signed, non-colour-only); NFR-MOV-004; `01` §10.7; `03` §2/§4 | TC-MOV-031 added (L3 — fraction payload renders the correct % on every percentage surface: indices, movers incl. `aria-label`, sector entries, with `marketDivYield`/`formatPercent` regression guards) and TC-MOV-016's L4 sign-only assertion strengthened to known-fixture **values** (`index-xu100` "+2,00%", mover ETA "+10,00%") — delivery-planner-applied, test-planner confirmation pending (the v1.2/v1.3 precedent). `tests/e2e` + Web unit delta only; backend goldens untouched (re-run unchanged). |
| v1.5.13 | 2026-10-20 | **Overview display freshness is stored-prices-based** (TKT-foundation-037, §10.9.13 consumer-note amendment): `GET /api/v1/market/overview` computed `stale` with the weekday rule `snapshot.SnapshotDate < LatestTradingDayOnOrBefore(today)` (a pure weekday-backing helper; `TradingCalendar.Holidays` ships empty), so on **every trading day before that day's EOD ingest (20:30 Istanbul)** the previous session's row — structurally the freshest data that exists — was flagged `stale: true` (the user-reported 2026-10-20 "Bayat" badge on Monday's row, Tuesday morning; the same divergence recorded verbatim on TKT-foundation-031's live verification, where the pinned criterion was `stale=false`). The serve path now joins §10.9.13's doctrine: `stale = snapshot.SnapshotDate < (latest stored trading day ≤ today with covered-universe prices)`, resolved through the existing `MarketSnapshotStore.ResolveLatestStoredTradingDayAsync` (DI-injected; `Scoped`, already registered by `AddMarketDataIngestion` — no `Program.cs` change). **Honest direction preserved:** `stale: true` only when a NEWER completed session's prices exist in the store and the served row lacks them (a genuinely missed `market-snapshot` run — the 028 self-healing gap); Tuesday before EOD → Monday is the latest stored day → fresh; a completed EOD whose snapshot run failed → stale. The endpoint's private weekday helper is removed (no remaining caller). **No payload-shape change** (`03` §3: latest row + `asOf`/`stale` envelope), **no schema change** (`02` §3.2), no config change. The badge *copy* ("en güncel tamamlanmış seans" / EOD nature) is deliberately NOT here — it is the TKT-foundation-039 Web lane (finding B3) that makes the truthful flag self-explanatory. | FR-MOV-010 (as-of + stale marking is stored-prices truth); FR-MOV-016 (last-known-good only when genuinely trailing); NFR-MOV-001 (dashboard reflects the latest completed trading day); BR-MOV-001; UXR-MOV-010 (the flag made truthful — rendering unchanged); §10.9.13 (doctrine extended write → serve); `03` §3 (contract unchanged); `02` §3.2 (unchanged) | New **TC-MOV-032** in `.pipeline/testing/market-overview.md` §2 Group C, implemented in `tests/Api.IntegrationTests/MarketOverviewFreshnessTests.cs` (`TKT_foundation_037`-ticket tag): Monday snapshot over Monday-only prices on a Tuesday clock → `stale: false`/`asOf` Monday (red on the weekday rule); the same world plus the newer stored session the snapshot lacks → `asOf` Monday + `stale: true`; Tuesday snapshot over Tuesday prices → `stale: false`. FU/golden stability: `MarketEndpointsTests` (TC-MOV-001..005 goldens, TC-MOV-012/014/015 envelope), `EodChainTests`, `EodStaleServingTests` (TC-MDF-013/014) and `MarketSnapshotTests` re-run green unchanged — **no permitted golden delta** (the FU anchor T is a trading day with prices, so the resolution is a no-op there). CI green (Backend + E2E) required. Live criterion (orchestrator post-merge, read-only): on the dev stack (Tuesday, Monday snapshot row 2026-10-19, no Tuesday prices) the endpoint serves `state: available`, `asOf: 2026-10-19`, **`stale: false`** and the SPA row shows no Bayat badge; after the 20:30 Istanbul EOD chain lands Tuesday's row: `asOf: 2026-10-20`, `stale: false` — both recorded on the ticket, never fabricated |
| v1.5.14 | 2026-10-20 | **Macro freshness is per series and publication-frequency aware** (TKT-foundation-038, §10.9.12 freshness note + `02` §5.6 amendment; post-merge defect fix, user-reported morning check 2026-10-20): TÜİK TÜFE and TCMB Politika Faizi served `Bayat` while serving their latest published values. `MacroFreshnessService` had three compounding defects, all fixed at the serving seam only (no ingest, adapter, transport, schema or API-shape change): **(1) the failure clock was job-scoped and ignored `partial` runs** — every non-daily series mapped to `MacroCpiJob`, whose live ledger is 14 `failed` + 2 honest `partial` + **zero `succeeded`** (ENAG/`INDEP_CPI` is admin-entered and never present, so the CPI job lands `partial` honestly), so `lastSuccess = null` made `failureStale` true forever. It is now **per series**: failure-stale only when the owning job's latest `failed` run is newer than *that series' own* latest `recorded_at` **and** newer than the job's latest `succeeded` run — the stored rows are the evidence the series' ingest happened (a `partial` run that wrote it refreshes the clock; a failure that wrote nothing leaves it old, so staleness stays honest); `INDEP_CPI` (admin-entered, `per_release`, no owning job) is never failure-stale. **(2) the monthly age rule was vacuous** — a flat 31-day tolerance from `recorded_at` can never catch a missed monthly publication because the CPI job re-records the monthly rows on every run. A `monthly` series is now **publication-aware and measured from the published value date**: stale when `today > asOf + 65 days`, the single documented constant `MacroFreshnessService.MonthlyPublicationWindowDays` (covers TÜİK's M+1-early publication and the CBRT's ≤2-month meeting cycle; changing the window is that one constant, never a semantics change). `daily` keeps its 1-day `recorded_at` tolerance (the rule that works) and `per_release` keeps no age window. **(3) the strip envelope counted unavailable series** — a series with no stored value is `unavailable`, never `stale`, and the macro response's top-level `stale` now aggregates available series only. Honesty posture unchanged: a genuinely stale series still shows stale (daily lane, FU GOLD, TC-MOV-024 construction all unchanged) — the fix is precision, not suppression. | FR-MOV-010 (as-of + stale marking), FR-MOV-016 (last-known/stale only when genuinely trailing), FR-MOV-009 (per-series as-of), NFR-MOV-001; §10.7 honest-state (a false `Bayat` on fresh data is as dishonest as a false `fresh`); `02` §5.6 (cadence tolerances) / §3.2 (`macro_series.cadence` → freshness tolerance pointer); UC-MOV-001 alternate a, UC-MOV-003; AD-14 sources untouched | New **TC-MOV-033** in `.pipeline/testing/market-overview.md` (delivery-planner-applied, test-planner confirmation pending — the v1.2/v1.3 precedent) and its L2 realisation in `tests/Api.IntegrationTests/MacroFreshnessSemanticsTests.cs`, tagged `TC_MOV_033` (live ledger shape: `partial`-written series fresh; failure newer than the series' rows stale; monthly `asOf − 40` fresh vs `asOf − 70` stale under the shipped 65-day window; absent `INDEP_CPI` `unavailable` + strip envelope not poisoned). Regressions re-run green: `MacroIngestionTests` (TC-MOV-006/007/008/009/010/011/026 and the revision path), `FixtureUniverseNowAnchoredTests` (the 030 Sunday proof — daily `USD_TRY` fresh / `GOLD` stale construction unchanged), `MarketEndpointsTests` (TC-MOV-012/013/014 macro strip goldens), `AdminEndpointsTests` (admin summary reads the same service); no golden delta on the daily lane. TC-MOV-009/026's harness clock advances one day before the failing run so the failure genuinely postdates FU §7's T-evening `recorded_at` seed stamp — precondition realism for per-series semantics, assertions unchanged (recorded on the ticket). Live criterion (orchestrator post-merge, read-only): `GET /api/v1/market/macro` on the dev stack serves `TUIK_CPI` **`stale: false`** (Sep, latest published), `CBRT_REPO` **`stale: false`** (Aug), USD/EUR/GOLD fresh as now, `INDEP_CPI` `state: unavailable` without marking the strip stale — recorded as-is, never fabricated |
| v1.5.15 | 2026-10-20 | **Morning-cadence ingest chain** (TKT-foundation-040, new §10.9.16; user-directed product change 2026-10-20 — "we can take data morning and night"): the C3c scheduler grows a **second chain stream** on the *same* engine — `Ingestion:Scheduler:MorningCron` (shipped `0 30 8 * * *`, Europe/Istanbul, trading-day gated) running the pinned **`MorningIngestChain`** with a new stream property **`RunDateMode`**: the EOD stream runs for its occurrence date, the morning stream for the **previous trading day** (calendar back-walk). **Honest constraint, encoded:** BIST price data is EOD — a Tuesday 08:30 pass targets Monday's session and is *catch-up + KAP freshness, never newer market prices*. Phase 1 composition `prices (repair-gated) → statements (incremental) → metrics-recompute → market-snapshot → sector-medians`; the **prices repair gate is stored-prices-as-truth** (§10.9.13 doctrine): when every current constituent of the bound universe already has a `daily_prices` row for the run date the `prices` job is **not invoked** (zero İş Yatırım calls, zero `CONFLICTING_VALUE` noise) and one `ingest_runs` row records `gate: prices-coverage` / `gateOutcome: skipped`; an uncovered/partial day runs the job at the existing pacing, an empty bound never counts as complete (cold start still seeds/fetches). Retry ladder + `RETRIES_EXHAUSTED` + fail-stop unchanged (same stream machinery). **Phase 2 is gated on TKT-foundation-034 / PR #106:** `dividends → corporate-actions → disclosures` join between statements and metrics-recompute (pinned in `MorningIngestChain.Phase2JobCodes`) only once the VYK rewire + per-family cursors land — today they fail fast on placeholder KAP paths and would poison the chain's fail-stop; the morning pass is always incremental, never a full-universe first pull, and every KAP call keeps the ≥12 s `KapRequestPacer`. Macro untouched (macro-daily 08:00 / macro-cpi 09:30 own the morning). Empty `MorningCron` creates no stream, so a composition that configures nothing stays pre-040 byte-compatible; the shipped `appsettings.json` carries the cron. No schema change (`02` unchanged — the gate reads existing `daily_prices`/`index_constituents`). | FR-MDF-009 (a second cadence on the same engine — retries/ledger unchanged); FR-MDF-010/011 (the gate's coverage read — a completed day is never re-fetched); FR-MDF-012 (fail-stop preserved); NFR-MDF-001 (morning pass near-zero cost; run-ledger budget); FR-MOV-001..006/021 + NFR-MOV-001 (the overview row exists and is fresh by morning); BR-MOV-001/009; UC-MDF-001 alternates (failed-run recovery); `03` §9 (operations row) | Explicit foundation criteria (no TC lane when the ticket was sliced; test-planner confirmation pending): `tests/Api.IntegrationTests/MorningChainTests.cs` carries **TC-MDF-077** (morning chain fires at 08:30 for the previous trading day; a non-trading occurrence is skipped silently; a failed attempt retries 5/15/60 and raises `RETRIES_EXHAUSTED` after `MaxAttempts`) and **TC-MDF-078** (covered day → prices gate SKIPPED with zero price-source requests + snapshot no-op + zero quarantine; failed night → prices repaired, metrics/snapshot/medians written, `GET /market/overview` `state: available`, `asOf` = the repaired day); both mirrored into `.pipeline/testing/market-data-foundation.md` §9. **Backward compat:** `SchedulerTests`, `EodChainTests`, `FundSchedulingCoverageTests` and the per-job-stream behaviour re-run green unchanged (no delta — the EOD stream construction is untouched). Live criteria (orchestrator post-merge, never fabricated; KAP ≥12 s, İş Yatırım ≥3 s): (a) after a fully succeeded night the 08:30 pass records the prices gate SKIPPED (zero İş Yatırım calls), statements incremental, snapshot no-op, chain `succeeded`; (b) a failed night is repaired by the next 08:30 pass and `GET /api/v1/market/overview` serves the repaired day by ~09:00. **Phase 2 (post-034):** one morning pass picks up overnight-published disclosures incrementally. |
| v1.5.16 | 2026-10-20 | **Honest-freshness UX copy: the truthful flags explain their own semantics** (TKT-foundation-039, finding B3 of the user's 2026-10-20 morning check): after v1.5.13 (037, stored-prices freshness) and the macro publication-aware freshness lane (038), the remaining gap is comprehension — a `Bayat` badge that can still appear honestly was being read as "the platform has no access to data". This is a **Web copy/semantics layer** change only: `StaleBadge` (`components/honest-data/markers.tsx`) carries its catalog explanation (`honestData.staleExplanation`) as both `title` and accessible name — "last known-good value; a newer completed session or publication has not arrived", never "this number is wrong"; the SCR-001 equity as-of line (`MarketOverviewPage.tsx`) states the completed-session semantics (`screens.marketOverview.asOfExplanation`); the macro strip (`MacroStrip.tsx`) renders a per-series publication-cadence note beside each as-of date (`marketOverview.macro.cadence.{monthly,perRelease,daily}`), mirroring the seeded `macro_series.cadence` column so a monthly series reads as its latest **published** value and a daily series as its latest completed day. All copy lives in the `tr`/`en` catalogs. **No** data-flow, formatter (036's fraction scaling is byte-identical on render), layout-landmark, contract or schema change; no new endpoint. **Flow-back (recorded):** final wording is a UX-spec decision — the ux-designer records the approved copy in the SCR-001 spec (UXR-MOV-010/UXR-MOV-008/009) and re-validation applies only if it changes structure/behavior. | FR-MOV-010/016 (as-of + stale marking, the user-comprehension half); FR-MOV-020 (EOD-nature disclosure, consistent with the TKT-foundation-029 footer); NFR-MOV-001; UXR-MOV-008/009/010; §10.7 honest-state (copy, not data); §10.6 i18n (tr/en parity); §10.9.13 consumer note (the semantics 037/038 own) | No new TC lane — explicit foundation criteria (the 030/036 precedent; screen-alignment style): WCAG-relevant copy asserted from the catalogs in `markers.test.tsx` (badge explanation present in TR + EN, distinct from the bare label, programmatically determinable — the badge-without-explanation regression is impossible), `MarketOverviewPage.test.tsx` (as-of explanation TR + EN) and `MacroStrip.test.tsx` (cadence note per series + EN) — tagged TC-MOV-016/TC-MOV-024; i18n parity, advice-vocabulary gate, eslint/tsc and the full Web unit suite green. Existing `tests/e2e` specs are unaffected (additive copy; badge selectors are data-slot based). Live criterion (orchestrator post-merge, browser): the overview row and macro strip show their semantics copy on the dev stack, and any visible badge carries the explanation — recorded on the ticket, never fabricated |
| v1.5.17 | 2026-10-21 | **Honest-freshness copy is conditional on the truthful flag** (TKT-foundation-039, review round 1 DR-1/DR-2/DR-3; amends v1.5.16): v1.5.16's explanatory copy rendered **unconditionally**, so a stale row displayed the recency claim *beside* the badge that denies it — the macro strip's per-series cadence note ("latest completed day / latest published value") and the SCR-001 `screens.marketOverview.asOfExplanation` ("most recently completed session") both appeared on `stale: true`. Now the recency claim is **suppressed whenever the sibling stale flag is true** (`series.stale` per macro series; `envelope.stale` for the overview as-of line), leaving the badge's own explanation ("last known-good value; a newer completed session or publication has not arrived") as the honest statement; the as-of date itself still renders. No new copy/keys — conditionality only. Also: an unmapped macro series code renders **no** cadence note (the `?? 'daily'` default is removed; a false recency claim is never the fallback, DR-2), and `StaleBadge` carries `role="note"` so its author-supplied accessible name is legal on a `role=generic` `<span>` where naming is prohibited (DR-3; `note` is not a landmark). No data-flow, formatter (036 byte-identical), contract, schema or catalog-shape change. | FR-MOV-010/016; FR-MOV-020; NFR-MOV-001; UXR-MOV-008/009/010; §10.7 honest-state; §10.9.13 consumer note (037/038 semantics); WCAG name/role/value (DR-3) | `markers.test.tsx` (badge explanation TR + EN, now under `role="note"`), `MarketOverviewPage.test.tsx` (stale envelope: badge present, `market-asof-explanation` **absent**, TR + EN), `MacroStrip.test.tsx` (stale series: badge present, `macro-cadence` **absent**, fresh siblings keep it, TR + EN) — tagged TC-MOV-016/TC-MOV-024; existing fresh-state TR + EN assertions unchanged. i18n parity (655 keys), tsc, eslint and the three touched Web suites green. Existing `tests/e2e` selectors are data-slot based and unaffected; final wording remains the ux-designer flow-back |
| v1.5.18 | 2026-10-21 | **Market snapshot: repairable same-day recompute + honest index levels** (TKT-foundation-042, §10.9.13 amended / §10.9.14 extended; post-merge defect fix from the dev-stack operational evidence of 2026-10-20 evening — both defects live-verified by the orchestrator). **(B) The resolved-day guard is input-aware.** A same-day `market-snapshot` re-run with CHANGED inputs returned `written: 0, unchanged: 0, quarantined: 0` and left the existing broken row untouched (`xu100_level = 0.0000`); the only operational repair was a SQL `DELETE` of the row plus a re-run (run 124). The job now recomputes the resolved day from the current facts on every run and the store decides: identical values → the row is untouched and the run reports the honest `unchanged: 1` (the pre-042 `0` was a counting lie — the day *was* checked); changed values → the row is **overwritten in place** — still exactly one row per trading day (TC-MOV-005) — and the run counts the repair (the pre-042 silent `0/0` becomes a real `written: 1`; a dedicated `rewritten` stat needs `IngestResult`/the run-ledger stats seam and is a follow-up scope item, flagged on the ticket). Days other than the resolved one are never addressed, so historical rows stay immutable. Rejected alternative (recorded): an admin-only forced delete+recompute — it keeps SQL surgery as the only repair path. **(C) Missing index levels no longer fabricate 0.0000.** `MarketSnapshotEngine.ReadIndexMovesAsync` fell back to `new IndexMove(0m, 0m)` when the day had no `index_levels` row yet, and the engine copied that zero into the snapshot columns, which the overview SERVED (`xu100_level = 0.0000`, rendered 0,00). Missing levels now yield **null** moves: `MarketSnapshotResult`'s four index fields are nullable, `02` §3.2's columns are nullable (**no migration is required — they always were**; existing rows keep their values), and `GET /market/overview` serves `indices: null` when either level is NULL (`03` §3 already contracted `{...} \| null`; the Web already renders the honest Unavailable block — no screen change). The price-derived blocks keep serving `state: available` (§10.9.14), and the row is repaired in place once the levels land (§10.9.13). **FU fixture world byte-identical**: both levels are always stored and the recompute is value-identical, so TC-MOV-001..005 / TC-MOV-029/030 and the `MarketEndpointsTests` payload goldens re-run unchanged — the single permitted delta is the 028 no-op test's expectation (a value-identical re-run now reports `unchanged: 1`). | FR-MOV-001 (index levels + change — the honest-null path); FR-MOV-010/NFR-MOV-001 (the served row must be truthful); FR-MOV-003/004/005/021 (price-derived blocks unaffected by a missing index fact); BR-MOV-009 (a recompute over changed canonical facts is still canonical); BR-MOV-001; TC-MOV-005 (one row per day — preserved, semantics amended); TC-MOV-012 (payload asOf/state/indices); `02` §3.2 (nullable index-level columns + recompute semantics); `03` §3 (additive: `indices: null` already contracted); §10.7 honest-state (0.0000 is a fabricated value); the 031/M-1 honesty class | New L2 cases in `MarketSnapshotTests` tagged `TC_MOV_034`/`TC_MOV_035` (delivery-planner-applied, test-planner confirmation pending — the v1.2/v1.3 precedent): prices with NO index levels → the row's four index columns are NULL (red before: 0.0000), the price-derived blocks are real, and `GET /market/overview` serves `state: available` + `indices: null`; after the levels land the next run repairs the row with the real figures. Same-day repair → a value-identical re-run reports `unchanged: 1` (red before: 0) and a re-run over changed inputs repairs the row in place (corrected values, served corrected, still exactly one row for the day) while a historical day's row is byte-identical afterwards (red before: `written: 0, unchanged: 0`, row untouched). Regressions re-run green: the whole `MarketSnapshotTests` class (incl. the 028 no-op test's input-aware delta), `MarketEndpointsTests` (TC-MOV-012/013/014/015 goldens), `MarketOverviewFreshnessTests` (TC-MOV-032), `EodChainTests` and `Core.UnitTests` (54/54) — Backend focused run: 32/32 integration + 54/54 unit. CI green required. Live criterion (orchestrator post-merge, read-only, stored facts only): on the dev stack the existing 2026-10-20 row (written via the SQL-surgery repair) still serves correctly and a re-run reports `unchanged: 1`; a deliberately stale-inputs scenario is repaired by a plain re-run — attempt/result recorded on the ticket, never fabricated |

| v1.5.22 | 2026-10-21 | **VYK company-identity resolution: the comma-joined `stockCode` key and the family lanes' fail-stop** (TKT-foundation-046, §10.9.15 amended; §10.9.10/§10.9.11 unchanged): two live-verified dev-stack defects made all three KAP family jobs unable to complete a run. (1) `VykCompanyDirectory.FetchMembersAsync` registered a member's **whole** `stockCode` string as one dictionary key, so Türkiye Garanti Bankası's `"GARAN, TGB"` produced the literal key `"GARAN, TGB"` and `BySymbol["GARAN"]` missed — 77 of the 1037 dev-gateway members are comma-coded, unresolving 14 of the 100 stored constituents (9 purely by the comma: ALBRK, GARAN, HALKB, ISCTR, ISMEN, KRDMD, TSKB, VAKBN, YKBNK; 5 genuinely absent from the dev frozen archive: GLRMK, PAHOL, TRALT, TRENJ, TRMET). The map now splits on commas and registers every trimmed part as its own key (existing `OrdinalIgnoreCase`), **last-wins** on duplicates with a logged warning when two entries claim one code with different ids (identical duplicates idempotent/silent), codeless auditor-type members still skipped. (2) The three family adapters let `CompanyIdentityUnresolvedException` escape the paced company loop, so `dividends` (run 125) and `corporate-actions` (run 126) walked ~18.4 minutes on 2026-10-20 and then failed the whole run with HTTP 500 on `GARAN` alone. The **planner-pinned design decision** replaces fail-stop with **per-constituent quarantine-and-continue** — the §10.9.10/§10.9.11 statements-lane isolation doctrine (FR-MDF-012) applied verbatim: the adapter skips the unmapped symbol, logs it with the named reason `COMPANY_IDENTITY_UNRESOLVED`, reports it on the new `IVykIdentityIsolationReporter` channel, and the job folds it into the run's `symbols` stats (`failed=true`, `written=0`), ending the run **`partial`** — never a whole-run 500, no quarantine row (identity is not a parse event), no cursor advance, no fabricated identity. The rejected alternative (keep fail-stop + an `Ingestion:{Family}:UniverseOverride` to trim the universe) is recorded as fabricating reliability by operator action and hiding unresolvable members. Transport failures inside a company walk stay fail-fast (cursors persist at loop end). The runner-level `error`-column honesty stays TKT-foundation-044's scope (`Jobs.cs` untouched here). | FR-MDF-003/004/005 (the three families can complete a run at all); FR-MDF-008 (identity reason, no fabricated mapping); FR-MDF-012 (per-company isolation — the pinned decision); FR-MDF-010/011 (skipped symbols counted, never hidden); FR-MOV-006 (downstream dividend supply); §10.9.10/§10.9.11 (doctrine aligned to, unchanged); TKT-foundation-034's per-family live criteria (unblocked); AD-14 | L2 doubles only, never live sources. New `VykCompanyDirectoryTests` (**TC-MDF-082**) drives the production directory over a **committed real-shape `/members` double** derived from the live 2026-10-20 capture (comma codes incl. `"GARAN, TGB"`/`"ALBRK, ALK"`/`"A1CAP, ACP"`, literal duplicate entries, a same-code/different-id conflict, codeless `YK`/`FK`/`IGS` members; the ephemeral `/tmp` snapshot is not referenced): per-ticker resolution (red: only the joined key exists), codeless skip (empty map key absent) and last-wins + logged conflict (red: no warning, joined key wins). `StatementsAndEventsIngestionTests` gains **TC-MDF-083** (one theory case each for `dividends`/`corporate-actions`/`disclosures`: a bound carrying an unmapped constituent — GLRMK's absent-member class — completes the loop, writes the resolvable constituent's rows **and** cursor, names the skip in the ledger `symbols` stats with `COMPANY_IDENTITY_UNRESOLVED` logged, quarantines nothing, and ends `partial`; red today: the exception kills the run at the first absent symbol) and a `TC-MDF-082` composition case (`"GARAN, TGB"` resolving end-to-end through the `corporate-actions` walk). **No golden delta:** TC-MDF-003..006 canonical doubles re-run green unchanged (VYK stays disabled there) and the `StatementsAndEventsIngestionTests` 034 lane re-runs green with the additive stat shape only; `VykStatementsSourceTests` + `StatementsLoopTests` re-run green unchanged (the doctrine copied, not changed). Live criterion (orchestrator post-merge, one paced run per family, ≥12 s/call): each family completes its walk end-to-end with ≥1 real `provenance=kap` row where the archive has one, GARAN resolving, the 5 absent members honestly skipped and counted, and zero HTTP 500s — attempt and outcome recorded on the ticket. |
| v1.5.23 | 2026-10-21 | **Chain-orchestration reliability: hung-run watchdog + `universe-sync` leads the EOD chain** (TKT-foundation-041, new §10.9.17; post-merge defect + design-gap fix, planner-sliced from the second consecutive silent stall): the 20:30 Istanbul EOD chain stalled **after** `prices` on both 2026-10-19 and 2026-10-20 — `prices` finished 17:37:03 UTC `succeeded`, then no `statements`/metrics/snapshot/medians ledger row, no error, no alert, process serving; the fund retry already due at 17:30:00.026 UTC never fired, proving the tick never returned. Root cause is structural: the hosted loop awaits one `TickAsync`, a tick runs the chain's jobs sequentially, and the ledger is written at **job completion** — so any await that never completes inside a step freezes the whole scheduler invisibly. The fix bounds **every attempt** (`Ingestion:Scheduler:MaxRunDuration`, default 90 min, `<= 0` disables) by racing a clock-driven deadline: on expiry the attempt is cancelled (abandoned task's scope/CTS handed to a continuation, exception observed), one honest `ingest_runs` row is written with `job_code` = the hung step and `stats_json.reasonCode = "RUN_HUNG"` (+ `hungJob`/`step`/`stepCount`/`runDate`/`attempt`/`retries`/`maxRunDurationSeconds`), one `RUN_HUNG` `IngestionAlert` fires through the existing C3d/mail seam, and the unchanged retry ladder takes over — measured from the instant the attempt **ended**, so a bounded night retries inside the overnight window instead of waiting for the 08:30 safety net. Per-job `Chain step {Step}/{Total} '{JobCode}' starting/finished` logging makes a stalled chain legible, and a cancellation that is **not** the host's stopping token becomes a failed attempt (it previously escaped the tick: no ladder, no retry log, hot refire); a run finishing as the deadline fires counts as finished (never `succeeded` + `RUN_HUNG` for one attempt) and a stopping host never raises a spurious `RUN_HUNG`. The bound watches **awaits**: a synchronous block inside a step cannot be pre-empted in-process, though its `Chain step … starting` line still names it (§10.9.17 "Scope of the guarantee"). **Design gap closed:** `universe-sync` — which had **no schedule** (admin trigger/cold-start only) and made Tuesday's index levels a manual repair — is **prepended** to `EodIngestChain` (`universe-sync → prices → statements → dividends → corporate-actions → disclosures → metrics-recompute → market-snapshot → sector-medians`), so the night's own levels feed that night's snapshot; fail-stop and `Partial` semantics unchanged; the morning chain is deliberately untouched (040 phase-2 lane). No schema/API-shape/config-required change; no new dependency. | FR-MDF-009 (the scheduler/retry machinery that failed to engage), FR-MDF-012 (failure isolation — a silent stall is its worst case), NFR-MDF-001 (the ledger must never have a silent hole), FR-MDF-006 (index levels), FR-MOV-001..006/021 + NFR-MOV-001 (the overview row existing at all); UC-MDF-001 alternates (failed-run recovery); §10.9.13 (chain doctrine), §10.9.16 (morning safety net — complementary, neither replaces the other); `03` §9 (EOD chain row) | **TC-MDF-079** (new, `tests/Api.IntegrationTests/EodStallWatchdogTests.cs`): a doubled post-`prices` chain step that never completes — pre-fix the tick never returns (no `RUN_HUNG` row, no alert, and the second stream due after the stall instant never fires), post-fix the watchdog bounds it within `MaxRunDuration`, records the `RUN_HUNG` row + alert naming the step, the loop's next wake fires and the stream recovers via the retry ladder; plus a throwing step (ladder + honest runner row) and a self-cancelling step (ladder, never an escaping cancellation). **TC-MDF-080** (`EodChainTests.cs`): `universe-sync` is the chain's first job (ledger row, idempotent re-run: no duplicate levels, unchanged membership) and a world with **no** stored index levels still ends with the snapshot serving that day's real levels — no manual run. Chain-order golden updated (one leading job); `SchedulerTests`, `MorningChainTests`, `EodStaleServingTests`, `MacroIngestionTests`, `UniverseSyncTests` re-run green (the two default-chain suites gained the universe stubs and the +1 row, no assertion weakened); both TCs mirrored into `.pipeline/testing/market-data-foundation.md` §9 (delivery-planner-applied, test-planner confirmation pending). Live criterion (orchestrator post-merge, never fabricated): the next 20:30 Istanbul chain completes end-to-end on its own with `universe-sync → … → market-snapshot` rows and the overview serving the day with real index levels, zero manual repair; if it stalls, `RUN_HUNG` fires verbatim. |
| v1.5.27 | 2026-10-22 | **Intraday refresh lane — 2-hour session sweeps, probe-gated source, `intraday_quotes` and the frozen `priceState` contract** (TKT-foundation-047, new §10.9.18; user-directed requirement 2026-10-22, verbatim: *"it should update the data in hour or 2 hour periods then so data is updated, and at close it should display close"*). A new stream job **`intraday-quotes`** on the existing C3c engine (`Ingestion:Scheduler:JobSchedules` cron `0 15 11,13,15,17 * * 1-5` Europe/Istanbul — 11:15/13:15/15:15/17:15 TRT, the six-field form of the ticket's `15 11,13,15,17 * * 1-5`; `1-5` cron-gated — per-job streams run with `TradingDaysOnly = false`, so holiday gating is TKT-foundation-048's scope, not this lane's; a 1-hour cadence is a config edit) runs a paced per-symbol sweep over the covered universe (own `Ingestion:Intraday` section mirroring `Ingestion:Prices` — `BaseUrl`, `{symbol}`/`{date}` `Path`, `UniverseIndex`, `RequestDelayMs` 500 ≈ 400 calls/day at XU100) and writes the **new `intraday_quotes` table** (`instrument_id`, `session_date`, `fetched_at` UTC, `last_price`, `day_volume` nullable, `source_ref`; one row per instrument × session, **updated in place** per sweep — identical → `unchanged` with `fetched_at` advanced, changed → overwrite + `rewritten` stat, prior sessions immutable). **An intraday value is never written to `daily_prices` and never becomes a close** — the EOD chain stays the only writer of closes. **Source is probe-gated, never a code fork:** İş Yatırım `HisseTekil`'s envelope is daily-bar shaped (no sub-day timestamp, no last-price field) and its same-day mid-session row is unverifiable from code/captures, so the lane rides the existing §10.9.1 envelope detection (İş `HGDG_*` **and** the Yahoo `.IS` chart contingency) behind configuration only; a symbol with no same-day row is **counted** (`IngestResult.Skipped` + a named `NO_DATA` event — 043's doctrine), and Yahoo's share-count `volume` stores **NULL** (FLG-09(b)). **The display contract (frozen seam with TKT-foundation-049, Web half, disjoint files):** `GET /api/v1/market/overview` and `GET /api/v1/stocks/{symbol}/overview` gain `priceState: { state: "intraday" | "close", lastPrice, prevClose, changePct, asOf, sessionDate }` (or `null`): `intraday` during the session after ≥1 sweep (sweep's price, `prevClose` = the previous stored session's close — Mondays vs Friday, `changePct` a decimal fraction to 6 dp per `03` §2/§4, `asOf` = the sweep's `fetched_at`), **still `intraday` with its last `asOf` from the 18:00 close until the EOD chain lands** (the close-in-formation must never masquerade as final — planner-pinned over an 18:00 freeze), `close` from `daily_prices` once the session's EOD close is stored (`asOf` = that row's `recorded_at`), and the last **completed stored** session at weekends/holidays (§10.9.13 stored-prices-as-truth; 048 extends the calendar). On the **market overview** the three numerics are honestly **NULL** — the lane stores instrument quotes only and no index-level intraday level exists (deriving one at the seam would be a derived display value, BR-MDF-009); the stock page carries the full numeric block. `rewritten` rides a run stat log line (`rewritten {Rewritten}`) because `src/Ingestion/Jobs/` is out of this ticket's scope. **Backward compatible:** EOD chain, morning chain (§10.9.16), retry machinery, freshness rules (037/039/§10.9.13) and the job runner untouched; `src/Api/appsettings.json` gains one `JobSchedules` entry + the `Ingestion:Intraday` section. | FR-MDF-001 (the new intraday ingest mode); FR-MOV-001/002/003 (overview price/index display); FR-RES-005..018 lane (stock-page price header supply); NFR-MDF-001 (≈400 paced calls/day, run-ledger budget); NFR-MDF-002 (in-place today, prior sessions immutable); BR-MDF-009 (no derived display values — hence the overview's NULL numerics); `03` §2/§4 (`changePct` decimal fraction); §10.9.1/§10.9.2 (envelope detection + Yahoo contingency reused, prices lane untouched); §10.9.13 (stored-prices freshness); AD-14 | L2 doubles only, never live sources. New `tests/Api.IntegrationTests/IntradayQuotesTests.cs` carries **TC-MDF-084** (a paced 3-symbol sweep over the İş same-day row / the Yahoo delayed-quote chart / an İş envelope without a session row: two `intraday_quotes` rows with the sweep's `fetched_at` and the session date, Yahoo `day_volume` NULL, the no-data symbol counted `skipped` and named in a `NO_DATA` log event, **zero** `daily_prices` rows for the session, one request per symbol and a paced run time; plus the shipped cron and `Ingestion:Intraday` config constants) and **TC-MDF-086** (re-sweep is in place: identical → `unchanged` + advanced `fetched_at`, changed → overwrite + `rewritten` stat, exactly one row per instrument × session, the prior session's row byte-identical). `MarketOverviewFreshnessTests` gains four **TC-MDF-085** cases (`intraday` mid-session with NULL market numerics; still `intraday` at 19:00 TRT with the last sweep's `asOf`; `close` after the EOD lands with `asOf` = the close row's `recorded_at`; Saturday serves Tuesday's close) and `StockOverviewAndValuationEndpointsTests` three (`intraday` with `lastPrice`/`prevClose`/`changePct` vs the previous stored close; the 19:00 world keeps the sweep's `asOf`; `close` = the stored `close_raw` with its `changePct`). Mirrored into `.pipeline/testing/market-data-foundation.md` §12 (v1.11 — the record number after TKT-foundation-054's v1.9 and TKT-foundation-050's v1.10 landed on `dev` first). **No golden delta:** `PriceIngestionTests`, `SchedulerTests`, `MarketEndpointsTests`, `MigrationsAndSeedTests`, `MorningChainTests` and the freshness lane re-run green unchanged (the contract is additive). `02-data-model.md`'s schema section needs the `intraday_quotes` table mirrored on the architect's next pass (this ticket's doc scope is `01`). Live criteria (orchestrator post-merge, paced, honest, recorded verbatim on the ticket): (a) one paced single-symbol `HisseTekil` probe mid-session recording whether a same-day row exists and its semantics — the İş-vs-Yahoo source choice follows the outcome and is recorded; (b) one full paced sweep completing with per-symbol accounting (100/100 or honest no-data counts) and rows visible in `intraday_quotes`; (c) the API serving `state: intraday` with the correct `asOf`; (d) after the 20:30 EOD chain, `state: close` from `daily_prices`. |
| v1.5.29 | 2026-10-22 | **Web display of the intraday/close/weekend/holiday price states on SCR-001 and SCR-005** (TKT-foundation-049 — the Web half of P-1, built against the frozen contract of TKT-foundation-047 item 4 / §10.9.x; user requirement 2026-10-22 "at close it should display close"). The Web layer becomes a **renderer of the served `priceState`** and nothing more: `PriceState` is added to `lib/api/contracts.ts` exactly as frozen (`state: "intraday" \| "close"`, `lastPrice`, `prevClose`, `changePct`, `asOf`, `sessionDate`) with the three numerics **`number | null`**, carried as an **optional, nullable** member (`priceState?: PriceState \| null`) of `MarketOverviewResponse` (SCR-001) and `StockOverviewResponse` (SCR-005's header — the same payload the Overview section already reads, so one query key/cache entry and no header-vs-section disagreement). Presentation semantics (per §10.9.18): a shared `screens/price-state/PriceStateHeader` renders the served `lastPrice` + `changePct` (036's fraction formatter, byte-identical) **only where the producer serves them** — on the **market overview** the three numerics are honestly **NULL** (no index-level intraday level exists), so SCR-001 renders the state/as-of label only: intraday = "son güncelleme HH:MM — kesinleşmedi / last update — not final" from `asOf` in the **BIST session clock** (Europe/Istanbul, explicit so a reader abroad sees the sweep's local time), close = "seans kapanışı / session close" dated by `sessionDate` — with **no value, no change and no direction arrow**, never a fabricated `0,00` / `+0,00%`; the **stock page** carries the full numeric block, where `prevClose`/`changePct` are nullable at cold start (no previous stored close) and the change/arrow is then omitted entirely, again never `+0,00%`. The **18:00→EOD-landing window stays `intraday` with its last `asOf`** — the UI never freezes or relabels it as a close (planner-pinned in 047). **Never fabricated:** no client-side `changePct` computation, no client-side state inference, no 18:00 freeze; a payload without `priceState` degrades to the existing 037/039 EOD display (as-of date + stale flag, no price value) on SCR-005 and to the unchanged SCR-001 envelope header, so a missing value is never inferred or blank-as-zero. **Copy coherence:** 039's SCR-001 recency claim (`screens.marketOverview.asOfExplanation`, "closing values … intraday prices are not shown") is suppressed while the served state is `intraday`, exactly as it already was for `stale: true` — the header never contradicts itself (039 review-round-1 DR-1 doctrine extended to the intraday lane); 039/036 copy and formatters are untouched, and every new label lives in the `tr`/`en` catalogs (`priceState.*`). Styling/copy remain minimal-unopinionated: the ux-designer pins the final SCR-001/SCR-005 intraday-state presentation entries (routing item recorded on the ticket, not a blocker). No API, engine, schema, config or endpoint change — the Web consumes the contract 047 serves. | FR-MOV-001/002/003 (overview price/index display renders the served state), FR-RES-005..018 lane (SCR-005 header lane), FR-MOV-020/FR-RES-017 (the "not final" language is the research-surface disclaimers' sibling), FR-MOV-010/FR-MOV-016 + NFR-MOV-001 (as-of/stale semantics preserved and extended, never contradicted); §10.7 honest-state; §10.6 i18n (tr/en parity); the version numbers between v1.5.22 and v1.5.29 belong to the same wave's other tickets (soft reservations; single-row append, last-lander rebases), 049 pinned at v1.5.29 per the planner allocation | No TC lane exists for the intraday display (the ticket's acceptance is explicit, screen-alignment style — the 030/036/039 precedent): the new Vitest cases carry the `TKT-foundation-049` tag and extend **TC-MOV-016** (SCR-001 price header: intraday with its as-of time + "not final", the 18:00→EOD window never relabelled close, the dated close, TR/EN, the served-change-proves-no-client-computation double, the `priceState`-absent EOD degradation, the intraday claim suppression) and **TC-RES-005** (SCR-005 header: the same three states with the symbol, TR/EN, the EOD fallback); MSW doubles serve each real-shape state — the instrument shape (`PRICE_STATE_INTRADAY` / `_AFTER_CLOSE` / `_CLOSE`, plus a cold-start `changePct: null` double) and the market shape `GET /market/overview` actually emits (`MARKET_PRICE_STATE_INTRADAY` / `_AFTER_CLOSE` / `_CLOSE`, numerics **null**), so SCR-001 is asserted to render **no** value/change node and no `0,00`/`+0,00%` text while the full-numeric value/change assertions live on SCR-005 where the producer serves them. Existing `MarketOverviewPage.test.tsx`, the SCR-005 suites (`StockPage`, `Valuation`, `SectionMarkers`), `MacroStrip` and the honest-data markers re-run green with **no delta** (the shared fixtures stay without `priceState`, which is what keeps the degradation path covered); i18n parity (660 keys), advice-vocabulary, `tsc`, eslint and the full Web unit suite (313/313) green. Live criterion (orchestrator post-merge, the ticket's own): on the dev stack during a session with ≥1 047 sweep landed both screens show the intraday state with the correct asOf, and after the EOD chain both show the close; console/network clean — attempt and result recorded on the ticket, never fabricated |
| v1.5.32 | 2026-10-22 | **Dividends availability is a server signal, and the section has three honest states** (TKT-foundation-052, `03` §3 dividends payload — additive; finding of the user's 2026-10-20 live check on SCR-005 §7): the dividends endpoint served the never-paid state whenever the `dividends` table held **zero rows for the instrument**, and the section rendered "Bu şirket hiç temettü ödemedi." That is an unbacked negative claim — a zero-row table is equally the shape of "the dividends lane has not covered this instrument yet", which is exactly the state the dev archive was in (zero rows, no completed run). The payload now carries **`dividendsAvailable`** — the lane's own coverage/run state, never the row count — and the section renders three distinct states: **(a)** signal absent/`false` → the neutral "Temettü verisi yok / No dividend data" state (no claim about the company); **(b)** signal `true` with zero rows → the existing never-paid wording, now **backed**; **(c)** rows → the existing dated history. The signal is raised on evidence only: the per-instrument family cursor (`coverage_metadata`, `scope=instrument`, `data_type=kap-dividends-cursor`, persisted by the adapter after a completed VYK walk — §10.9.11/§10.9.15), or the latest terminal `dividends` run in the ledger (`succeeded` = every resolvable constituent walked; `partial` counts **unless** the run's per-symbol stats name this instrument failed, i.e. the 046 identity isolation — an isolated symbol was never walked and keeps the neutral state). Unreadable run stats and an **absent** signal both degrade to the neutral state: absence of evidence is never rendered as evidence of absence. No schema change (`02` unchanged — the cursor rows already exist), no ingestion change (034/046/053's lane untouched), no new endpoint. | FR-RES-012 (dividends display lane), FR-MDF-010/011 (honest achieved depth — availability before claims), FR-MDF-012 (an isolated constituent's data is honestly absent); §10.7 honest-state (a fabricated absence is the same defect class as a fabricated zero); §10.6 i18n (tr/en parity); SCR-005 §7; UXR-RES-019/021 (the section's honest empty surface) | New **TC-RES-037** (L2, `StockFundamentalsEndpointsTests`, `TKT_foundation_052` tag): zero rows with no dividends run → `dividendsAvailable: false` (red before: the property did not exist); zero rows after a `succeeded` run → `true` with the state/no-rows/no-TTM shape; zero rows after a `partial` run that names the symbol failed → `false`; populated ALFA → `true` with the history and TTM intact. New **TC-RES-038** (L3, `Dividends.test.tsx`): the three states render their distinct catalog copy in TR and EN (neverPaid only in the backed state; no fabricated TTM 0 in either empty state) plus the degradation case — a payload **without** `dividendsAvailable` renders the neutral state and never the claim (red before: 3/5 failed, the unbacked claim rendered). **No golden delta:** the existing `StockFundamentalsEndpointsTests` class re-runs green unchanged (17/17 — the LAMDA never-paid test's `state: no-data`/coverage assertions hold; the new signal is additive), the SCR-005 suites (`StockPage`, `Valuation`, `SectionMarkers`) and the full Web unit suite (324/324) re-run green, i18n parity (662 keys), advice-vocabulary, `tsc`, eslint and `dotnet build` (0 warnings) clean. Copy flow-back: the neutral-state wording is a ux-designer routing item (recorded on the ticket, not a blocker). Live criterion (orchestrator post-merge): a stock with no dividends coverage shows the neutral state, and after a completed dividends run a genuinely-zero company shows the backed wording — recorded verbatim, never fabricated. |
| v1.5.33 | 2026-10-22 | **Morning chain phase 2: the three KAP event families join the 08:30 pass** (TKT-foundation-053, §10.9.16 amended — the flip TKT-foundation-040 pinned): `MorningIngestChain.JobCodes` is now the eight-job composition `prices (repair-gated) → statements (incremental) → dividends → corporate-actions → disclosures → metrics-recompute → market-snapshot → sector-medians`, i.e. the three family lanes sit between `statements` and `metrics-recompute` exactly where the EOD chain orders them. Nothing else about the morning stream changes: run date is still the **previous trading day**, the stored-prices repair gate still skips a fully-covered day, retry ladder (`5/15/60`) + `RETRIES_EXHAUSTED` + fail-stop (`failed` only, `partial` continues) are the unchanged stream machinery, and every KAP call keeps `KapRequestPacer` (≥12 s). The morning pass is **incremental, never a full-universe first pull** — the families ride their per-company cursors (§10.9.11) that the nightly EOD pass keeps current. **Stale-rationale repair (040 review DR3-01):** §10.9.16 carried "phase 2 is gated on TKT-foundation-034 — the families fail fast on placeholder KAP paths", which stopped being true when 034 rewired the families onto the verified VYK routes (v1.5.11); the real remaining blocker was **completability**, and 046 (v1.5.22) settled it by replacing the families' fail-stop with per-constituent identity isolation (`COMPANY_IDENTITY_UNRESOLVED` counted, run ends `partial`). The section now states that timeline honestly — the families joined *once 046 made the lane completable*, not "once 034 landed". No schema (`02` unchanged), no API-shape, and no config change (`appsettings.json` already ships `MorningCron`; the flip is code-side only). | FR-MDF-003/004/005 (dividends/actions/disclosures now in the daily morning cadence); FR-MDF-009 (second cadence on the same engine — ladder/ledger unchanged); FR-MDF-012 (a family lane's honest skips are isolated, never fail-stop); FR-MOV-006 (morning-fresh TTM DPS supply); FR-MDF-016 (pipeline health — the chain completes); NFR-MDF-001 (near-zero-cost normal morning preserved by the repair gate + cursors); §10.9.15 (the families' completable posture this wiring relies on); §10.9.16 | `tests/Api.IntegrationTests/MorningChainTests.cs` — new **TC-MDF-091**: the morning pass writes the eight ledger rows in the pinned order; each family lane ends `partial` with the 046 per-symbol stat shape (`symbol`/`failed`/`written`) naming the isolated `COMPANY_IDENTITY_UNRESOLVED` constituent; the pass stays non-failed (metrics recomputed, snapshot written, medians recomputed, zero quarantine, zero alerts, no fact under the unresolvable identity). The suite's chain-order golden moves to the eight-job list (the three TC-MDF-077/078 tests keep their semantics: scheduling, non-trading skip, retry ladder, repair gate, covered/partial/failed-night worlds). Regressions re-run green: `SchedulerTests`, `EodChainTests`, `EodStallWatchdogTests` (041's watchdog baseline on the shared scheduling files), `MorningChainTests` 7/7. **Live criterion (orchestrator post-merge, never fabricated; KAP ≥12 s):** the next 08:30 Istanbul morning pass on the dev stack completes end-to-end with the three family rows present in the ledger (honest skips counted, zero HTTP 500s) and metrics recomputed on the fresh event data. |
| v1.5.34 | 2026-10-22 | **Derived-fact lanes: the input-aware same-day recompute** (TKT-foundation-050, §10.9.13 extended by reference — the TKT-foundation-042/v1.5.18 pattern applied to `metrics-recompute` and `medians`). Operational evidence on the dev stack (2026-10-21, ledger runs 164/166): both jobs were a **blind in-place overwrite with blind counting** — every recompute rewrote every row and moved `computed_at` while reporting the existing rows as `unchanged` (416 metric rows / 15 median rows), and a re-run after an upstream fact changed reported `written: 0` while overwriting the rows anyway; the literal `written: 0, unchanged: 0` reading belongs to the empty-recompute case (no stored prices for the target day, §10.9.13's target-day lane), not to a same-day re-run with stored prices. Root shape: the write path recomputed and rewrote every row on every run (moving `computed_at`), reported all existing rows as `unchanged` regardless of whether their values changed, and therefore had no notion of a repair. The two jobs now recompute the target day from the current facts and write **input-aware** at the engine/job boundary (`MetricsEngine.PlanWriteAsync` / `SectorMedianEngine.PlanWriteAsync`, the row-level counterpart of the snapshot store's `HasSameValues`/`Rewritten` precedent): a computed row already equal to its stored row is **not written** and is reported as the honest `unchanged: N`; a row whose upstream facts changed (a restatement, a corrected line, a late dividend for `derived_metrics`; a corrected upstream metric for `sector_metric_medians`) is repaired **in place** and counted as a rewrite (`MetricWritePlan.Rewritten` / `SectorMedianWritePlan.Rewritten`, folded into the run's `written` exactly as 042 folds a snapshot repair). Comparison is over the input-derived values only — never `computed_at` — and in the stored column's precision (`numeric(18,6)`), so a high-precision recompute cannot masquerade as a perpetual repair. Exactly one row per `(instrument, metric, as_of_date)` / `(sector, metric, as_of_date)` is preserved (FR-MDF-015, NFR-MDF-002); other dates are never addressed, so historical days stay immutable; a stored row the current run no longer computes is left alone. **No computation-semantics change** (`MetricsEngine.ComputeAsync`/`SectorMedianEngine.ComputeAsync` are untouched — the FU §11 goldens are byte-identical and TC-MDF-026/031/033/034 re-run green), **no schema change**, **no config change**. The distinct run-ledger `rewritten` stat stays TKT-foundation-044's `Jobs.cs` fold-in (042's recorded scope ruling; `Jobs.cs` is not touched here). | FR-MDF-013/014 (recompute mechanics + honest run accounting); FR-MDF-015/NFR-MDF-002 (append-only, one row per key, historical immutability); FR-MDF-010/011 (honest achieved depth / run accounting — the 042 honesty class); FR-RES-007/SD-002 (`sector_metric_medians` supply); BR-MDF-009 (identical inputs → identical outputs — now actually exploited); §10.9.13 (doctrine extended); `02` §3.1/§3.4 (storage semantics unchanged) | New L2 cases in `tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs` (**TC-MDF-088**, delivery-planner-applied, test-planner confirmation pending — the v1.2/v1.3 precedent): identical same-day re-run of both jobs → `written: 0, unchanged: N`, no row written (`computed_at` frozen), one row per key, historical day byte-identical (red before: 4/4 failed — every row rewritten, `computed_at` moved); a re-run after an upstream fact changes → the affected rows repaired in place and counted as written, the untouched rows keep their `computed_at`, one row per key, historical day byte-identical (red before: `written: 0` while the rows were overwritten — the silent `0/0` class). **No golden delta:** `MetricsEngineTests` (TC-MDF-026/027/031/032/033/034/058 and the FCF lane), `MarketSnapshotTests` (the 042 lane), `EodChainTests`, `MorningChainTests`, `MarketEndpointsTests`, `MarketOverviewFreshnessTests`, the screener/regime/restatement/valuation consumers and `Core.UnitTests` re-run green unchanged — full backend suites locally 450/450 integration + 54/54 unit. CI green required. Live criterion (orchestrator post-merge, read-only, stored facts only): the next manual/scheduled same-day re-run of both jobs on the dev stack shows counted `unchanged`/`rewritten` stats in the ledger row — never `0/0` silence — attempt and result recorded on the ticket, never fabricated |
| v1.5.35 | 2026-10-22 | **Financials period labels are period-type-aware** (TKT-foundation-057, Web-only; SCR-005 §3, FR-RES-017 honesty lane; user-directed 2026-10-22): the stock page's financials table rendered **every** period as `FY{{year}}` (`stockPage.financials.period`) while the API has always served `periodType` (`FY` \| `Q`) with `periodEndDate` — the very fields the table already keys its rows on. Live on the dev stack, AEFES's Q1-2023 revenue row (24.5B) sat labelled "FY2023" beside FY2022 (90.5B), readable as a fabricated −73% collapse. `StockSections.tsx` now derives the label from the payload: `FY` → the fiscal-year label unchanged; `Q` → the quarter from the period-end month (Jan–Mar → Q1, Apr–Jun → Q2, Jul–Sep → Q3, Oct–Dec → Q4) with that period's own year; absent or unexpected `periodType`, or an unparseable `periodEndDate` → the period-end date itself (`stockPage.financials.periodEnding`), never a fabricated quarter and never an implied fiscal year. Both catalogs gain `periodQuarter` (TR `Ç{{quarter}} {{year}}` / EN `Q{{quarter}} {{year}}`) and `periodEnding`, parity-checked. **Companion half — the which-stock header (acceptance ii), landed under the user's 2026-10-22 scope amendment (034 Option-A precedent):** the header must name the stock (symbol + official company name) and no payload SCR-005 loads carried a name (`03` §3's overview served the description only; `Instrument.Name` is language-neutral and was projected only by `GET /stocks` and `GET /stocks/search`). Rather than fabricate a Web-side lookup, `GET /api/v1/stocks/{symbol}/overview` now also serves the instrument identity — the canonical uppercase `symbol` and the language-neutral `name` (`Instrument.Name`), on both the `published` and the `preparing` payloads, with a blank/missing name served as `null` (never an empty string dressed up as an identity). This is **additive only** per `03` §9's in-V1 rule — no field removed or renamed, no other endpoint touched, no schema or config change; `03` §3's overview row records the shape. The SPA header renders `symbol — name` (`stockPage.identity.withName`, e.g. "AEFES — Anadolu Efes") from the **same overview query key the description section already uses** (no extra request) and degrades to the symbol alone while loading, on failure, or when the name is `null`. | FR-RES-008 (financials served as-is), FR-RES-017 (the label is a claim about the data), FR-RES-005–018 lane; honesty lineage 036/039/052 | `src/Web/src/screens/stock-page/FinancialsPeriodLabel.test.tsx` — 6 cases: Q1 derived from `periodEndDate` in TR and EN (no "FY2023" row beside FY2022), Q2/Q3/Q4 month derivation, FY label retained, honest degradation for an absent and for an unexpected `periodType` (MSW `QUARTER_FINANCIALS_FIXTURE` = the live AEFES shape); `StockIdentity.test.tsx` 5 cases: symbol + name from the shared overview payload, canonical symbol over route casing, symbol-alone degradation for a `null` name and for an absent `name` field, and a one-request proof; API side `StockOverviewAndValuationEndpointsTests` gains `TKT_foundation_057_overview_serves_the_instrument_identity` + `..._serves_a_blank_name_as_null` (doubles only), each red without the projection and green with it, class 8/8. Regressions re-run green: `StockPage`, `SectionMarkers`, `Valuation`, `section-nav` (6 files / 38 tests); `check:i18n` parity 658 keys, `tsc --noEmit` and `eslint` clean. Live criterion (orchestrator post-merge): on the dev stack AEFES's financials table shows the Q1-2023 row as a quarter beside FY2022 (no implied −73% collapse) **and** the header reads "AEFES — Anadolu Efes", console/network clean. |
| v1.5.36 | 2026-10-22 | **Share-count store from VYK `/memberSecurities` — the zero-shares world ends** (TKT-foundation-058, §10.9.11 extended; **supersedes the TKT-foundation-033 goal** — the source was never the disclosure XBRL layer, it is the gateway's `/memberSecurities`, live-probed 2026-10-22 at 671 members / 1,062 security lines with `currentCapital` populated on 1,061). New `shares` ingestion lane (`src/Ingestion/Shares/`, admin-triggerable, **not** in either chain): one paced `/memberSecurities` snapshot per run through the existing keyed `IKapSourceClient`/`KapRequestPacer`/`KapUri` seams (the pacer's shared `Ingestion:Statements:RequestDelayMs` interval, shipped 10 s; the 046 `/members` precedent), decoded tolerantly (`borsadaIslemeAcik` accepts JSON booleans, the numeric `0`/`1` and the string `"true"/"false"/"1"/"0"` spellings — the 2026-10-22 capture itself carried booleans only; a line missing `currentCapital` is skipped and counted) and written to the new **`share_counts`** store (`02` §3.1 additive: `instrument_id` PK, `shares_outstanding`, `recorded_at`, `source_ref = kap`) — one row per instrument, **input-aware** per the 042/050 doctrine (unchanged → `unchanged`, no `recorded_at` movement; changed → repaired in place and counted). Matching is **`borsaKodu` primary / `isin` fallback**, keyed on at least one **trading-open** line; the count is the **sum of `currentCapital` across the member's matching tertip lines** (the evidenced hypothesis: ASELS A+B = 4.56B, THYAO C+A = 1.38B, ISDMR A+B = 2.9B on the live shapes) — and the **unit remains unresolved, never assumed**: every line in the committed double carries `capital == currentCapital`, so a matching served value cannot discriminate a share count from paid-in TL; the stored value is treated as a count only after the ticket's post-merge ASELS/THYAO/TUPRS/GARAN/AEFES served-vs-known-real table passes, and a refuted rule is recorded on the ticket. **Honest absence (031/052 doctrine):** no line, closed-only lines, or a group with an unreadable line → **no row**, never a zero (a re-run that withholds a group also removes a previously stored row for it); the skipped lines are counted in the run stats and the run stays `succeeded`. Transport → `failed` run, non-memberSecurities body → `UNPARSEABLE_PAYLOAD`. **Serving seam:** `GET /api/v1/stocks/{symbol}/balance-sheet` gains an **additive** `shareCount { sharesOutstanding, asOf }` block (`03` §3) — honest `null` when absent — leaving `lines.sharesOutstanding`/`sharesDiluted`/BVPS byte-identical (the statement's own figures are never rewritten). Downstream consumers (per-share metrics, multiples/market-P/E per BR-MOV-010, DCF baselines) are **pinned seams only** in their own tickets. No `appsettings.json` change (`Ingestion:Kap:MemberSecuritiesPath` defaults to `/memberSecurities` in code); no schema change beyond the additive table + migration. | FR-MDF-002/003 (the new gateway endpoint in the ingest lane); FR-MDF-008 (`source_ref`); FR-MDF-010/011 (skips counted, honest absence); FR-MOV-006 lane (market P/E / per-share supply — seam only); BR-MOV-010; FR-RES-0xx financials lane (per-share display supply — seam only); FR-VAL-001 lane (DCF baseline supply — seam only); §10.9.11 (extended); `02` §3.1 (additive table) | New L2 cases, doubles only (**TC-MDF-092** in `tests/Api.IntegrationTests/MemberSecuritiesIngestTests.cs`, 10 cases: one row per matched instrument with tertip aggregation — ASELS 4.56B / THYAO 1.38B / TUPRS 1,926,795,598 / GARAN 4.2B / AEFES 592,105,263 / ISDMR 2.9B / DRVFM via the ISIN fallback; `borsaKodu`-primary decoy never matched; unmatched/closed-only absent, no zero row; missing `currentCapital` skipped+counted with the group withheld (DRVE/NOCAP bound so the withheld branch is genuinely exercised) and a previously stored row for a withheld group removed; input-aware re-run; the trading-open flag's string `"1"`/`"0"` and numeric `1`/`0` spellings honoured (an unmatched member's unreadable line is not a skip); the single-object member body decoded as **one** member; one paced call per run; failed/quarantined failure posture. **TC-MDF-093** in `StockFundamentalsEndpointsTests`: a seeded count serves `shareCount.sharesOutstanding` + `asOf` while the statement's figures stay at their goldens, and an instrument without a count serves JSON `null`s). Delivery-planner-applied, test-planner confirmation pending (the v1.2/v1.3 precedent). **No golden delta:** `StockFundamentalsEndpointsTests` 18/18, `CannedSourceCatalogTests`/`VykCompanyDirectoryTests`/`VykStatementsSourceTests` 27/27 re-run green (the catalog double is additive and the catalog contract test covers it automatically). Red→green recorded in the PR body (7 failed / 1 passed before, 8/8 after). **Live criterion (orchestrator post-merge, paced by the shared 10 s pacer interval, honest, recorded verbatim on the ticket):** one `/memberSecurities` fetch + `shares` run on the dev stack populates real counts, and the unit/aggregation cross-validation table (ASELS/THYAO/TUPRS/GARAN/AEFES served vs known real counts) is recorded on the ticket before the values are treated as counts. |
| v1.5.37 | 2026-10-23 | **Hung-run watchdog edge semantics corrected — the four `RunAttemptAsync` boundary defects from 041's round-3 review** (TKT-foundation-059, §10.9.17 repair note; no redesign, no schema/API/config change, no new dependency). The merged watchdog (v1.5.23) bounded the primary hang correctly but was one window short on three edges, and left its two round-2 boundary branches prose-only. **(1) Prologue into the failure-classifying `try`:** the attempt's state/scope/CTS/deadline creation now sits inside the same `try` whose `catch` yields the attempt's failure value — an exception there (most realistically a mis-typed `Ingestion:Scheduler:MaxRunDuration` beyond `Task.Delay`'s ~49.7-day accepted range) previously escaped `RunAttemptAsync` with `Attempts` already bumped and `NextAttemptAt` still past, so the hosted loop re-fired the attempt immediately and unboundedly instead of walking the ladder to `RETRIES_EXHAUSTED`. **(2) Post-cancel `IsCompletedSuccessfully` re-check:** the "never both `succeeded` and `RUN_HUNG`" guard now also runs after `attemptCts.Cancel()` + the awaited deadline-source cancel, so an attempt that finishes successfully inside that window is recorded once, by its own row, with no `RUN_HUNG` row/retry — the guard covers the whole cancel window, not just its entry. **(3) Snapshot hoisted above the cancel:** the hung-step snapshot is taken when the completion re-check fails, before any cancellation — a cancellation-swallowing step that returns `Partial` and lets the chain `Begin` the next step can no longer rename the step in `job_code`/`hungJob`/the alert. **(4) Boundary coverage:** both round-2 branches are pinned by tests instead of prose — a run completing against the armed bound is one completed attempt (no `RUN_HUNG`), and a stopping host at the bound unwinds with no row, no alert and no Hung-run-detected log line. Primary doctrine unchanged: a chain step awaiting forever is bounded, exactly one row + one alert name the caught step, and the ladder resumes measured from the attempt end. | FR-MDF-016 (pipeline health — the watchdog's honesty), FR-MDF-009 (scheduler/retry machinery), NFR-MDF-001 (no silent ledger hole); §10.9.17 (edges corrected, mechanism unchanged); TKT-foundation-041 heritage (§13.1 v1.5.23) | `tests/Api.IntegrationTests/SchedulerTests.cs` — five new L2 cases, doubles only, fake clock, one per defect: **TC-MDF-079 DR-1** (60-day `MaxRunDuration` → four ladder attempts, `RETRIES_EXHAUSTED` "failed after 4 attempts", zero ledger rows, next wake = tomorrow's cron; red before: `ArgumentOutOfRangeException` escaped `TickAsync`), **DR-2** (a cancellation-swallowing step finishing in the cancel window → exactly one row, the succeeded one; red before: 2 rows — success + `RUN_HUNG`), **DR-3** (swallow-to-`Partial` then an uninterruptible hang → `RUN_HUNG` names step 1 of 2; red before: named step 2), **DR-5(a)** (completes against the armed bound → one completed row, no `RUN_HUNG`, no retry) and **DR-5(b)** (host stopping at the bound → zero rows, zero alerts, no Hung-run-detected log; red under branch-removal mutation). The 041 suite re-runs green unchanged: `SchedulerTests` (8), `EodStallWatchdogTests` (4), `EodChainTests` (2), `MorningChainTests` (7), `EodStaleServingTests` (2), `FundSchedulingCoverageTests` (6), `MacroIngestionTests` (12) — 41/41, no assertion weakened. Live criterion (orchestrator, never fabricated): the next 20:30 Istanbul EOD chain under the repaired watchdog shows no spurious `RUN_HUNG`/success double row for one attempt and no hot-refire signature in the ledger; observed behaviour recorded verbatim on the ticket. |
| v1.5.39 | 2026-10-22 | **SCR-005 UX remainder: the daily-movement block joins the identity anchor, and the sector comparison states its honest empty** (TKT-foundation-055, Web-only; user findings 2026-10-20, planner-pinned minimal shapes of 2026-10-22): **(b) daily movement** — the landed `priceState` contract (047/049) was already rendered by the page's price header, but as a separate block below the description, so the day's `lastPrice`/`changePct`/state label did not read as part of the stock's identity (057's `SYMBOL — Name` anchor). `StockPage` now nests `StockPriceHeader` inside the same `stock-page-identity-anchor` unit as `StockIdentity` (above the page description): one query key, no extra request, no client-side computation — the header renders the served `lastPrice`, `changePct` (with its direction arrow and text label) and the state's honest label (intraday `Son güncelleme {{time}} — kesinleşmedi` / close `Seans kapanışı — {{date}}`), and degrades to the 049 EOD envelope (as-of date + stale flag, no price) when the payload carries no `priceState`. **(c) sector comparison honest empty** — in the zero-shares world (share-count-derived multiples blocked until 058 lands) the vs.-sector strip served every entry with no own value, no median and `peerCount: 0`, and the renderer turned that absence into a wall of per-row "Anlamsız" verdicts and an "0 emsal" median basis. `VsSectorStrip` now treats a row as a **comparison** only where a sector median is served: when no entry has one, the block renders a single honest empty state (`stockPage.valuation.strip.noSectorDataTitle` — TR "Bu sektör için yeterli veri yok" / EN "Not enough data for this sector", the 052 doctrine) instead of the rows; within a populated strip, "Anlamsız" renders only when the server says `notMeaningful` — which under the served contract is exactly the null-own-value case (`notMeaningful = value is null`, unconditionally; BR-RES-008 "explicitly not-meaningful, never 0"), so no separate "uncomputed" payload exists to present as a dash — and a peer count of 0 never renders as a median basis. The block populates by itself as 058's counts land — no dependency, no schema/API/config change, no change to the frozen `priceState` contract. | FR-RES-005–018 lane (SCR-005 header + valuation section); FR-RES-017 (research-surface honest marking — the empty states); UXR-RES-013/024/025 (strip comparison, peer count, not-meaningful); UXR-RES-021/022 (as-of and honest no-data); 052 honesty doctrine; §10.9.x `priceState` contract (consumed, unchanged) | `src/Web/src/screens/stock-page/StockMovementBlock.test.tsx` — 8 cases against MSW `priceState` doubles: intraday price/change/label inside the identity anchor, identity+movement as one unit ahead of the description, the 18:00→EOD window staying intraday, the completed close dated by `sessionDate`, cold start with no change at all (never `+0,00%`), the served `changePct` over an inconsistent double (no client computation), the EOD degradation when `priceState` is missing, and the EN labels; `src/Web/src/screens/stock-page/SectorComparison.test.tsx` — 5 new cases (`TC-RES-025` extension): the no-peers payload renders the honest empty once in TR and EN (no per-row "Anlamsız", no "emsal", no metric rows), a computable comparison keeps its per-row not-meaningful verdict, a metric whose same-day peer rows are all null never renders a zero peer count as a basis while the block stays populated, and the section integration case; plus the populated strip regression. Red before (historical measurement at the pre-fix head `37486a3`, before the impossible "uncomputed own value" case was dropped): 13/15 failed for the right reason (missing anchor testid / missing empty state / fabricated verdicts). Regressions re-run green: stock-page + price-state suites 10 files / 66 tests, full Web unit suite 57 files / 350 tests, `check:i18n` parity 666 keys, `check:advice` clean, `tsc --noEmit` and `eslint` clean. No backend change. Live criterion (orchestrator post-merge): on the dev stack the stock page header shows the movement block beside the identity anchor and the sector comparison shows the honest empty (no "Anlamsız"/"0 emsal" wall), console/network clean. |
| v1.5.40 | 2026-10-23 | **Event-family per-disclosure resilience: one un-fetchable KAP attachment is an honest partial, not a dead chain** (TKT-foundation-061, §10.9.15 extended; §10.9.16/§10.9.17 cited). Evidence — the first truly unattended EOD chain (2026-10-22): step 5/9 `corporate-actions` hung 90 min on attempts 1–2 (the 041 watchdog bounded each and wrote the correct `RUN_HUNG` rows — 041 working as designed; `IngestionScheduler.cs` untouched here), then attempt 3 took a fast **HTTP 500 on `/disclosureDetail/1185709?fileType=data`** (run 195) and, because a detail failure was fail-fast, the run recorded `failed` and the chain fail-stopped **before `metrics-recompute`/`market-snapshot`/`medians`** — Thursday's close never reached the snapshot. The same job had walked a full 74 min that morning (302 rows), so the fault is **per disclosure**, not systemic. The fix is 046's per-constituent doctrine applied one level down, in the **shared** `VykDisclosureWalker` (the only `disclosureDetail` fetch in the three families; the adapters cannot express per-item isolation): **(1)** a detail the gateway cannot serve (HTTP error, fault envelope, transport) is recorded as a `VykDetailSkip` — reason `DISCLOSURE_DETAIL_UNFETCHABLE`, the disclosure index, failure kind (`http`/`fault`/`transport`/`timeout` — a 200 fault envelope is `fault`, never `transport`), status and **the gateway's error verbatim**; the per-item `try` covers the raw append and the **decode** as well as the fetch, so an adapter's lazily-raised fault-envelope `SourceFetchException` takes the same skip path (a malformed detail body stays `UNPARSEABLE_PAYLOAD`) — the walk logs it and continues, the index still advances the cursor (recorded, never silently dropped), each adapter exposes the run's skips on `IDisclosureIsolationReporter`, each job writes one `quarantined_facts` row per skip and folds the per-symbol skip count into `ingest_runs.stats_json` (`symbols[].quarantined`, additive on 046's shape), and the run ends **`partial`** — never `failed`, never a silent omission; **(2)** the hang class is bounded by a **per-item fetch timeout** (`Ingestion:Kap:DetailFetchTimeoutMs`, shipped default `KapSourceOptions.DefaultDetailFetchTimeoutMs` = 180 000 ms ≈ 18× the shipped 10 s pace) started **after** the pacer, applied as both cancellation and a race (a client that ignores cancellation cannot stall the walk) and composing with the run's `CancellationToken` (caller cancellation still propagates, never quarantined); a timed-out item takes the identical skip path; **(3)** the chain's step policy is untouched — it fail-stops on `failed` only, so the partial step proceeds and the snapshot steps run. **Unchanged on purpose:** list-route failures stay fail-fast, malformed detail bodies stay `UNPARSEABLE_PAYLOAD`, 046's identity isolation is untouched, and the 034 canonical goldens (VYK disabled) are byte-identical. **Production-honesty pin (recorded with the grant):** the answer to a per-item gateway error is **quarantine-with-evidence** — never silent omission, never a whole-run failure. **Recorded follow-up (not this ticket's scope):** `StatementsSourceAdapter.cs:291` carries the identical unguarded per-detail fetch and needs its own ticket. | FR-MDF-003/004/005 (the three family ingests — resilience), FR-MDF-010/011 (honest achieved depth — skips counted, never hidden), FR-MDF-012 (family isolation, extended to the item), FR-MDF-016 (pipeline health — the chain completes); §10.9.15 (extended), §10.9.16/§10.9.17 (the chain/watchdog interplay); 046 heritage (per-constituent), 041 heritage (the watchdog) | New **TC-MDF-094** (L2, `StatementsAndEventsIngestionTests`, `TKT_foundation_061` lane): one detail answering HTTP 500, one theory case per family — the walk completes, the skip is quarantined with the gateway's error verbatim and counted, every fetchable row and the cursor are written, the run is `partial`; plus the chain-composition case (`metrics-recompute`/`market-snapshot`/`medians` reachable after a partial `corporate-actions` step). New **TC-MDF-095** (L2): a stalled detail (WireMock delay, 60 s) is bounded by a configured 500 ms bound and takes the identical skip path with failure kind `timeout`; the guard half asserts the shipped bound ≥10× the shipped pace and that a slow-but-answering detail (2 s) is never falsely timed out. **One pre-listed delta:** 034's detail-404 case moves from failed/no-rows/no-quarantine to isolated-skip/partial (the posture this row replaces; its honesty intent — never an empty, never a silent drop — is preserved and strengthened). **No other golden delta:** `StatementsAndEventsIngestionTests` 39/39, `VykCompanyDirectoryTests`, `EodChainTests`, `MorningChainTests`, `SchedulerTests`, `EodStallWatchdogTests`, `MarketSnapshotTests`, `MetricsRecomputeIdempotencyTests` re-run green; `dotnet build` 0 warnings. Red→green: 6 failed / 1 passed pre-fix, 7/7 after. Live criterion (orchestrator, never fabricated): the Friday 17:30 UTC chain completes with `corporate-actions` honestly `partial` (the un-fetchable attachment skipped with evidence and counted) and `metrics-recompute`/`market-snapshot`/`medians` all run; the 2026-10-22 close is recovered via the admin-triggered chain re-run. |
| v1.5.41 | 2026-10-23 | **Scheduler stream independence — a due instant fires at its own instant, not at the loop's release** (TKT-foundation-062, §10.9.17 repair note; diagnosis-first, no redesign, no schema/API/config change, no new dependency). **Recorded defect (live, dev stack):** the EOD chain's attempt 3 failed 2026-10-22T21:52:29Z and logged `Retry 3 for 2026-10-22 scheduled at 2026-10-22T22:52:29+00:00`; the retry then fired ~11h49m late (attempt 4 at 10:41:24Z) and the Friday 05:30Z morning chain never fired at its instant — it started at **12:11:24Z**, the exact instant the EOD attempt's `RUN_HUNG` released the loop (~6h41m late), with the armed EOD retry queued behind it. **Diagnosed mechanism (hypothesis 3 of the ticket, pinned on the fake clock before any code change; hypotheses 1 and 2 falsified):** `TickAsync` executed the due streams sequentially and `await`ed each `RunAttemptAsync` to its end, and the hosted loop is one logical loop — inside that await it cannot compute `NextWake`, delay, or tick, so *no other stream's due instant is evaluated at all* while an attempt is in flight; on release the starved occurrence is consumed with `now` as its attempt start (fires at the release instant) and the retry ladder and the morning safety net collapse into one serial queue. **Repair:** `TickAsync` starts every due stream's attempt concurrently and, while any is in flight, waits on *next due instant* versus *attempts* on the injected clock, re-scanning due-ness at each pass — so an armed retry and a cron occurrence each start at their own instant for as long as the other stream's in-flight attempt is at an await; a step that blocks synchronously still holds the pump thread and still postpones the other streams' due instants (the unbounded class under "Scope of the guarantee"). A stream with an attempt in flight is neither a wake nor a due candidate (`NextWake`/due scan skip it; the flag is cleared in a `finally` on every ending — success, ladder, exhausted, host stop), which is what prevents a re-fire of the same attempt. The `TickAsync` contract is unchanged (when it returns, the attempts it started have ended), so the hosted loop and every fake-clock harness keep working as-is, and the sequential-await semantics 059's boundary tests pin are preserved within a stream. Honest limit, flagged not fixed: the *duration* of that night's hold (~11h49m) exceeds one 90-min watchdog bound, so it needed an attempt that outlived its own bound — the documented synchronous-block class (§10.9.17 "Scope of the guarantee"), which this ticket neither bounds nor explains; what it repairs and pins is the starvation interaction. | FR-MDF-016 (pipeline health — schedules fire when due), FR-MDF-009 (scheduler/retry machinery), NFR-MDF-001 (the ledger must never have a silent hole); §10.9.17 (stream independence added; 041's watchdog and 059's edge repairs untouched), §10.9.16 (the morning chain that was starved); TKT-foundation-041 heritage, TKT-foundation-059 (landed base, no rework) | `tests/Api.IntegrationTests/SchedulerTests.cs` — two new L2 cases, doubles only, fake clock (new `BuildStarvationHost`: the real engine, the `FakeTimeProvider`, the 059 `RecordingJobRunner` double, optional morning stream): **TC-MDF-081** (a) *retry punctuality* — EOD attempt 1 fails at 20:30 (retry armed 20:35) while a per-job stream's attempt holds the tick from 20:31: the retry's ledger row is `started_at` **20:35**, not the release instant (red before: never re-evaluated, `TimeoutException` at the 20:35 wait); (b) *morning-chain independence* — the EOD retry attempt itself hangs from 20:35 and the 08:30 Istanbul (05:30Z) morning chain fires at 05:30Z for the previous trading day on attempt 1 while it is in flight (red before: `TimeoutException` at the 05:30Z wait — the live 12:11:24Z shape). Existing suites re-run green and unrelaxed: `SchedulerTests` (incl. 059's DR-1/DR-2/DR-3/DR-5a/DR-5b and TC-MDF-011/012), `EodStallWatchdogTests` (4), `EodChainTests`, `MorningChainTests`, `EodStaleServingTests`, `FundSchedulingCoverageTests`, `MacroIngestionTests`, `BackfillAndCoverageTests` — 45/45. Live criteria (orchestrator, honest, recorded verbatim on the ticket): on the dev stack a scheduled retry fires within its ladder tolerance of its scheduled-at time, and the next morning chain fires on schedule even with a retry in flight. |
| v1.5.42 | 2026-10-23 | **Statements lane: per-company isolation + stale-disclosure-cursor recovery — ER005 is not a source failure** (TKT-foundation-063, §10.9.15 extended; §10.9.11 cursor note updated; §10.9.16/§10.9.17 cited). **Live evidence (dev stack, 2026-10-23):** `statements` failed 22–24 of 30 covered companies every run (11:00Z written 5/failed 22 → partial; 12:24Z written 2/failed 23; **run 212 14:06Z written 0 / 24 failed / 6 quarantined → failed**, identical at 14:17Z), and the zero-write `failed` fail-stopped the EOD chain **before `metrics-recompute` / `market-snapshot` / `medians`** — the chain-loss class 061 closed for the event families. Direct gateway probes on two independently sampled company paths answered **HTTP 400 `{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}`**: the persisted per-company cursor pointed past the tail the gateway now serves, and the adapter's own walk treated that 400 as `SourceFetchException` → `failed` per company. **Fix (planner-pinned, no redesign):** (1) **one walk, not two** — `StatementsSourceAdapter` now uses the shared `VykDisclosureWalker` (061) and its duplicated walk is deleted; the adapter supplies the `disclosureClass=FR` list route, the detail route, an accept-all family predicate (the route is already the FR filter), the statement decode callback and the cursor; the non-VYK canonical/XBRL path and its goldens, malformed-detail `UNPARSEABLE_PAYLOAD` and non-ER005 list failures are unchanged; the statements cursor (last **persisted** index) re-decodes its own row through the walker's `replayPersistedIndex`, preserving the 026/024 idempotency golden. (2) **stale-cursor recovery recorded and persisted once** — a first request from a persisted cursor answering 400 `ER005` resets **once** from `EarlyDisclosureIndex`; `VykCompanyWalk.Reset` (`VykCursorReset`: symbol, from → to, gateway code + body verbatim) is recorded as a structured log event and the corrected state persisted — the corrected cursor when the re-walk emitted facts, or `KapDisclosureCursorStore.ClearAsync` when the re-walk was honest-empty — so the next run makes one list call and repeats no ER005; a non-ER005 400/500 is never recovered. (3) **per-company isolation + honest status** — an unfetchable statements detail is a `DISCLOSURE_DETAIL_UNFETCHABLE` quarantine with evidence (index, `http`/`fault`/`transport`/`timeout`, status, error verbatim) counted in `symbols` stats while other companies write; a zero-write run of honest skips (reset / unfetchable detail / identity unresolved / quarantine) ends **`partial`**, never `failed`; only an all-constituent genuine source-down (or all-unparseable) world ends `failed`, so the retry ladder stays meaningful. Identity isolation remains the 046 shape (`failed=true` per symbol) but no longer fail-stops an all-unresolved run. **Honesty pin:** never fabricate a statement, never silently drop a company; a skip is evidence + a named reason + a count. | FR-MDF-002 (statements ingest), FR-MDF-008 (identity/provenance), FR-MDF-010/011 (skips counted, never hidden), FR-MDF-012 (isolation doctrine), FR-MDF-016 (pipeline health — the chain completes); §10.9.15 (extended), §10.9.11 (cursor note), §10.9.16/§10.9.17 (the chains this keeps reachable); 061 heritage (shared walker + per-item evidence), 046 heritage (per-constituent isolation) | L2 doubles only, never live sources. `tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs` carries **TC-MDF-096** (three cases: an HTTP-500 detail / a socket-reset transport detail → the item quarantined with the gateway's error verbatim, the sibling company written, run `partial`; an all-isolated zero-write world → `partial` with one evidence-carrying skip per company; and an all-companies list-500 world → still `failed`) and **TC-MDF-097** (a persisted cursor past the tail answering `400 ER005` → exactly one reset from `EarlyDisclosureIndex`, the re-walked facts written, the reset recorded verbatim in the structured log, the corrected cursor persisted, a second run one list call with no reset, and a non-ER005 500 on another company still `failed`; plus the honest-empty re-walk → the cursor row cleared and the next run starting from the early index). Red→green: 5 failed / 1 passed on the pre-fix behaviour (the invariant source-down case passing), 6/6 green after; `StatementsAndEventsIngestionTests` (45 incl. 061's family cases), `VykStatementsSourceTests`, `VykCompanyDirectoryTests`, `StatementsLoopTests`, `RealSourceStatementsTests`, `EodChainTests`, `MorningChainTests`, `SchedulerTests`, `EodStallWatchdogTests`, `MarketSnapshotTests`, `MetricsRecomputeIdempotencyTests`, `EodStaleServingTests` re-run green; `dotnet build` clean. Live criterion (orchestrator post-merge, honest, recorded verbatim): one paced statements run over the 30 covered companies makes zero ER005-caused `failed` companies (recovered or isolated with evidence), ends an honest `partial` where gaps remain, and the EOD chain reaches `metrics-recompute` / `market-snapshot` / `medians`; a second consecutive run shows no repeated stale reset and no ER005 on the same company. |
| v1.5.52 | 2026-10-24 | **The disclosures lane completes: a bounded head-window bootstrap, a market-wide feed, and a walk whose cost is bounded (and honestly stated)** (TKT-foundation-078, §10.9.15 extended; §10.9.11 cursor note updated; §10.9.16/§10.9.17 cited). **Live evidence (dev stack + ledger, 2026-10-24 02:30–02:50 UTC):** `kap_disclosures` had never held a row and no pass had ever finished — the gateway was fully reachable and credentialed (`/members` 1,037 companies, ASELS `866`; `/lastDisclosureIndex` `1231017`; `?companyId=866&disclosureIndex=0` → 39 real rows 1092199→1142913, paging confirmed) — the defect was **throughput**: `FetchVykAsync` looped the bound constituents and a cursor-less company fell back to `EarlyDisclosureIndex = "0"`, re-reading the whole archive (ASELS ≈ 138,818 positions ≈ 39 pages ≈ 8 min per company ⇒ ≈ 4 h for one XU030 pass); ledger run **208** = `10:41:24 → 13:41:24`, exactly 3 h 00 m, `written=0`, killed by the watchdog. `ER005` is the gateway's end-of-list answer (an index at/above a company's latest disclosure), not the cause. **Fix (no redesign of the honesty rules):** (1) **bootstrap from the platform head's window, never zero** — a cursor-less company starts `Ingestion:Disclosures:BootstrapLookback` positions below the directory's `lastDisclosureIndex` (shipped **5,000**, configuration with its pages × pace arithmetic in the option doc; a company's rows are sparse in index space, so the window is usually one or two list calls); the seed is recorded as **provenance** (`bootstrapped N cursor(s) … BOOTSTRAPPED` log + per-symbol stat with `written 0`/`failed false`, never "no disclosures"); a **persisted cursor wins** (incremental walk unchanged, one page, no re-walk, no reset — the 063 precedent) and a stale cursor's single ER005 reset now targets the **bounded window**, never 0; a missing anchor while a bootstrap is needed fails loudly (`BOOTSTRAP_ANCHOR_UNAVAILABLE`) instead of walking from zero. (2) **the market-wide feed is the normal pass** — `Ingestion:Disclosures:Feed = market` (shipped) pages `GET /disclosures?disclosureIndex={n}` with **no** `companyId` from one market cursor, keeps only rows owned by bound constituents, and counts+logs (`OUTSIDE_BOUND_UNIVERSE`) the rest without fetching them (their index still advances the cursor, so they cannot be re-read forever); `company` keeps the bounded per-constituent walk for first-seed/backfill. The market cursor is one `coverage_metadata` row (`scope = market`, data type `kap-disclosures-market-cursor`, outside `InstrumentScoped`, no migration); an honest-empty market pass persists **no** cursor so the next run re-bootstraps from the then-current head (never a backwards reset). (3) **the run is bounded in work** — `MaxListPages = 24` and `MaxDetailFetches = 200` are **per-walk** ceilings: at the pacing actually in force (`Ingestion:Statements:RequestDelayMs` = **10 s**; `Ingestion:Kap` carries no delay) one `market` walk is nominally 24 × 10 s + 200 × 10 s ≈ **37.3 min**, and the shipped `market` feed is one walk per run. That nominal is not the wall-clock bound: a detail that stalls is bounded per item at **180 s** (`Ingestion:Kap:DetailFetchTimeoutMs`), so a work-only bound could let a degraded gateway drive 200 × 180 s ≈ **10 h** out of one walk — far past the **120-min per-step bound** (§10.9.17, v1.5.43; the 90-min whole-attempt bound no longer exists). The walk therefore enforces its own wall clock: `Ingestion:Disclosures:MaxWalkDuration` = **90 min**, strictly inside the per-step bound, one clock per pass checked before every list/detail request on the injected `TimeProvider`. On expiry it records `truncated`, persists the last index it **completed** (and every completed company's cursor), and the next run resumes exactly there; the scheduler watchdog is only the backstop for a single request that outlives its own per-item timeout. The `company` feed shares that one deadline across its constituent loop, so a full pass (the pre-deadline nominal constituents × (pages + details) ≈ 18.7 h) is a recorded, resumable multi-run **backfill**, never one chain step. A budget- or deadline-truncated walk records `truncated`, persists the last completed index and resumes exactly there (no silent gap). **Four correctness repairs in the same lane:** the market walk terminates on "the page did not advance" + reaching `/lastDisclosureIndex`, **never** on a short page (the live gateway serves 39–48-row pages with rows above them, so a row-count stop would advance ~48 positions per run and miss the "within ~50 of the head" criterion); a persisted cursor no longer touches the anchor at all (the anchor is needed only to compute a bootstrap window, so a directory without `/lastDisclosureIndex` no longer fails an incremental run); a reset whose re-walk is honest-empty **clears** the cursor row (the 063 precedent) instead of repeating the identical two-call reset every run; and the cursor's own boundary row is skipped on resume, so an incremental run re-fetches no detail it already has. Honesty rules unchanged and load-bearing: no fabricated disclosure/subject/body, unmapped identity = named counted skip (`COMPANY_IDENTITY_UNRESOLVED`), unfetchable detail = quarantine with the gateway body verbatim (`DISCLOSURE_DETAIL_UNFETCHABLE`), genuine gateway-down = `failed`, honest empty = a skip with a reason. | FR-MDF-005 (the disclosures lane activated: recent KAP data and the 051 cash-flow/statement supply), FR-MDF-010/011 (achieved depth honest — bootstrap provenance, budget truncation, counted out-of-bound rows), FR-MDF-012 (isolation unchanged), FR-MDF-016 (pipeline health — the pass completes and cannot fail-stop the chain by timing out), §10.9.15 (extended), §10.9.11 (market cursor), §10.9.16/§10.9.17 (chain step bound), AD-14 | L2 doubles only, never a live gateway call (`tests/Api.IntegrationTests/StatementsAndEventsIngestionTests.cs`, `TKT_foundation_078` lane): **TC-MDF-099** (a huge-range company — 2,000 dense rows far below the head, the run-208 shape — completes inside the page budget, pages only the head window, writes its two in-window disclosures, persists the last read index and records the bootstrap as provenance; plus the window-is-configuration case with a 200-position window starting at `1230817`, and the shipped default constants 5,000/1,000/24/200); **TC-MDF-100** (a persisted cursor is one incremental page with no reset and no re-bootstrap — the 063 no-regression half; a stale cursor past the tail resets **once** to the bounded window, recorded, corrected cursor persisted, next run one page; a reset whose re-walk is honest-empty **clears** the row so the two-call reset cannot repeat; and a persisted cursor still walks incrementally when `/lastDisclosureIndex` is down — the anchor is a secondary read and only a bootstrap needs it); **TC-MDF-101** (the market feed pages the market list with no `companyId`, writes only bound rows, counts the out-of-bound ones without a detail fetch, advances and resumes its own cursor without re-fetching its boundary row; ER005 is an honest empty that persists no cursor; a market list 500 is still `failed` with no rows/quarantine; a `MaxListPages = 1` pass truncates, persists the last completed index `1230066` and resumes exactly there; and a **30-row page followed by 20 rows at higher indexes** is paged past, not treated as end-of-list — the live gateway's short-page shape; plus the round-2 wall-clock cases — a fake clock crossing a 10-min `MaxWalkDuration` truncates after four details, persists the last completed index and resumes there, and a non-positive bound disables the deadline; the shipped-config guard pins `MaxWalkDuration < MaxStepDuration` and config ↔ code equality). **No golden delta:** `StatementsAndEventsIngestionTests` re-runs green (incl. 034/046/061/063 families — the only changed assertions are the 078 lane's own), `VykCompanyDirectoryTests`, `VykStatementsSourceTests`, `EodChainTests`, `MorningChainTests`, `SchedulerTests`, `EodStallWatchdogTests`, `MarketSnapshotTests`, `MetricsRecomputeIdempotencyTests`, `EodStaleServingTests` re-run green; `dotnet build` 0 warnings. Red→green: with the pre-078 from-zero start restored, 3 failed / 5 passed (the huge-range company writes 0 rows instead of 2 — the from-zero walk exhausts its page budget before reaching the recent window). Review-round red evidence at the pre-round head: **7 failed / 4 passed** (099_a, 099_b, 100_b unchanged; plus the reset-clears-cursor case leaving the stale row, the anchor-down incremental case throwing, the short-page case writing 30 of 50 rows, and the market walk's row-count stop), **11/11 green** after. Live criterion (orchestrator post-merge): trigger `disclosures` on the merged dev stack — `kap_disclosures` > 0 rows, newest stored index within ~50 of `lastDisclosureIndex`, the run completes inside its step bound, and a second run is incremental with no re-walk. |
| v1.5.25 | 2026-10-23 | **A failed run names its reason: the fund lane's instant failures are a never-wired source, and the ledger records why a run failed** (TKT-foundation-044, §10.9.5 runner seam extended; §10.9.1 source-failure classes cited; no schema change — `ingest_runs.error` already exists, the defect was that it was left NULL). **Recorded defect (live, dev stack, runs 220–224 on 2026-10-23 and on every scheduled attempt since 2026-10-18):** `fund-nav` / `fund-performance` / `fund-holdings` failed **instantly** (4–30 ms — a transport refusal, not a timeout) with `stats_json {"mode":"incremental","symbols":[],"written":0,…}` and an **EMPTY `error` column**. **Root cause (diagnosed with doubles only, no live TEFAS call):** `Ingestion:Funds` is configured **nowhere** — no `appsettings.json` section, no compose/`.env` passthrough (verified in the running dev container: no `INGESTION__FUNDS__*` variable, `grep -c Funds appsettings.json` → 0, and `curl http://localhost/funds/universe` inside the container → `Failed to connect … port 80 after 0 ms`). `TefasSourceOptions.BaseUrl` is therefore empty, the keyed TEFAS client falls back to the placeholder `http://localhost`, the universe GET is refused in ~0.4 ms, and the `HttpRequestException` propagates out of the adapter to the runner's catch-all. The planner's hypotheses settle as: **(a) refuted** — `symbols: []` is *structural* for the fund lane (fund jobs have no per-symbol loop and never fill `IngestResult.SymbolStats`), the job never reaches universe resolution, and no empty-bound failure exists in this lane, so the empty array is not evidence of an empty universe; **(b) refuted** — no endpoint was ever configured to go down; the `/funds/universe` path is a placeholder default never wired to a real TEFAS resource (the §10.9.1 TKT-foundation-021 class, and OQ-FDF-002 — TEFAS programmatic access — remains unverified); **(c) confirmed**, more precisely *never wired* rather than regressed. **Fix (a) — the reason survives the failure:** `IngestResult` gains `FailureReason` (reason code + bounded detail) and the runner's `RecordAsync` always writes `ingest_runs.error` for a failed row — the thrown failure's classified reason first (`IngestFailureReason.Describe`: an `IReasonCodedFailure` names itself, then `SourcePayloadException` → `UNPARSEABLE_PAYLOAD`, `SourceFetchException`/`HttpRequestException` → `SOURCE_FETCH_FAILED`, else `FAILED_UNSPECIFIED`), then the job-reported `FailureReason`, then a defensive `FAILED_UNSPECIFIED` **with a warning** so no failed row can ever again be written with an empty reason (FR-MDF-009). The detail is single-lined and bounded (`MaxLength` 300, `…`-truncated); redaction stays where it already lives (adapters redact their own credentials before throwing). The fund lane's two job-reported failure paths (unparseable payload, missing provenance) now name their reason too. **(b) — an unconfigured source says so:** `TefasSourceAdapter` refuses to fetch when no base address is configured and throws `TefasSourceNotConfiguredException` (`SOURCE_NOT_CONFIGURED`, naming `Ingestion:Funds:BaseUrl`) **before** any request reaches the wire — the dev-stack row now reads a reason an operator can act on instead of an anonymous connection refusal. **Honest limit (recorded, not fabricated):** this ticket does **not** invent a TEFAS endpoint — the fund lane ships as **honest-failing with a named reason** until OQ-FDF-002 is resolved and a verified base address is configured (a config change outside this ticket's audited scope: `src/Api/appsettings.json` + live TEFAS discovery). **(c) — the 042 fold-in:** the runner stats seam gains the distinct `rewritten` count (`IngestResult.Rewritten` → `stats_json.rewritten`), so a same-day repair is reported as a repair rather than as a fresh `written: 1`. | FR-FDF-001/002 (the fund ingest lane), FR-MDF-009 (run-ledger completeness — the error field is part of the honest record), FR-MDF-012 (failure isolation), FR-MDF-010/011 (honest run accounting, the 042 fold-in), NFR-MDF-001, §10.9.1 (source-failure classes — a placeholder route is a config fault, not a source-down), §10.9.5 (runner seam extended) | `tests/Api.IntegrationTests/FundIngestionTests.cs` — **TC-FDF-014** (a–e) and **TC-FDF-015** (delivery-planner-applied, test-planner confirmation pending — the 030/v1.4 precedent): the loopback-refusal world reproduces the dev signature (fast failure, `symbols: []`, `written: 0`) and the row now carries `SOURCE_FETCH_FAILED: … 127.0.0.1:1`; the HTTP-500 world carries `SOURCE_FETCH_FAILED: … HTTP 500 … /canned-sources/tefas-universe`; the **unconfigured** world carries `SOURCE_NOT_CONFIGURED: … 'Ingestion:Funds:BaseUrl' …` with **zero** requests on the wire; the unparseable/missing-provenance worlds carry `UNPARSEABLE_PAYLOAD` / `MISSING_PROVENANCE` (quarantine unchanged); the thrown-reason detail is bounded, single-lined and `…`-truncated; and a same-day repair reports `rewritten: 1` with `written: 0` and no `error`. `FundSchedulingCoverageTests` carries the scheduler-path half (**TC-FDF-014 (f)**): after the 5/15/60 ladder, all four `fund-nav` rows carry `SOURCE_FETCH_FAILED … HTTP 500`. Red→green recorded in the PR body: **5 failed / 0 passed** on the pre-fix behaviour (`Error = null` on all four ladder rows — the exact dev defect), **7/7** after. **No golden delta:** `FundIngestionTests` (TC-FDF-001/002/003/004/009/011) + `FundSchedulingCoverageTests` (TC-FDF-005/006/007/008) + `FundHoldingsGapTests` re-run green — 20/20 — and the full backend integration suite re-runs green (the runner's added `stats_json` key is additive; `error` is written only for `failed` rows). **Recorded follow-up (not this ticket's scope, flagged):** the TEFAS base address itself must be wired to a verified endpoint (OQ-FDF-002) before the fund lane can succeed — until then its ledger rows read `SOURCE_NOT_CONFIGURED`, which is the honest state. Live criterion (orchestrator post-merge, honest, recorded verbatim on the ticket): the next 19:00–19:10 Istanbul fund-job window writes ledger rows that **name their reason** (the source is still unwired), never an empty `error`. |
| v1.5.26 | 2026-10-24 | **Crash-evidence capture — an abort is never again undiagnosable** (TKT-foundation-045; §10.3 posture amended; no schema/API-shape/request-path/config-required change, no new dependency). **The event, recorded honestly:** the dev stack's API process exited with **code 134 (SIGABRT) at 2026-10-20T20:12:53Z** during routine log-file churn; the last app-log lines were benign watch artifacts, there was **no stack, no unhandled-exception entry, no artifact** — the log file wrote nothing. Root cause is **not** claimed and **not** investigated to a conclusion (hypotheses unchanged: `dotnet watch`/hot-reload interaction, a runtime abort, an environment-level kill); the deliverable is that the next occurrence leaves evidence by construction. **Investigation attempt (item c), and what it could not recover — recorded, not fabricated:** the dev stack runs locally (`degerli-dev-api-1`) and the API is the `dotnet watch` child inside the container (confirmed live 2026-10-24: "dotnet watch ⌚ Polling file watcher is enabled", hot-reload/ENC restart prompts); the container that aborted **no longer exists** — the running container was created 2026-10-23T21:42:08Z, so the 2026-10-20 container logs and any in-container file were discarded by this wave's force-recreates; `docker events --since 168h` retains nothing for that container; `dmesg` is unreadable in this environment, so the OOM/external-kill branch could neither be confirmed nor excluded. The abort is therefore **still undiagnosed, by evidence loss — exactly the hole this ticket closes.** **Mechanism (built):** two passive managed hooks (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` — observation only, `SetObserved` never called) in the new `src/Api/Diagnostics/` components (`CrashEvidenceHooks`), installed from `Program.cs` right after the host is built so the **merged** configuration decides the paths; the writer (`CrashEvidenceWriter`) lands one bounded JSON artifact (`logs/crash-evidence/crash-<UTC>-<pid>-<kind>.json`) with the exception type/message/stack clipped at 4 000/16 000 chars, the last `LogTailLines` (200) lines of the newest rolling log via a bounded end-of-file read (512 KB cap), the process envelope (pid, runtime version, app version, environment, OS, arch, machine) and an explicit **exit path** string (terminating — "signals, hosted-service shutdown and the graceful exit path do not run"; non-terminating; unobserved-task "process continues, policy untouched"); every textual field passes `CrashEvidenceRedactor` (configured secrets — `ConnectionStrings:Default`, `Ingestion:Kap:ApiKey/ApiSecret`, `Ingestion:Macro:EvdsApiKey`, `Smtp:Password`, `Auth:UserRefHashKey`, `Evren:ApiKey` — plus `password=`/`api_key=`/`token=` shapes, their JSON-quoted forms (`{"Password":"…"}`, `"apiKey": "…"`), `Bearer …`, `Basic …`), the KAP/EVDS client-failure posture applied to the crash surface; concurrency-safe landing (each capture writes its own unique temp file and a no-overwrite rename arbitrates the name — a capture that loses the same-second race takes the next `-N` suffix instead of dropping its artifact, the attempt count is bounded and the temp is always cleaned up, so two faults in one second both leave an artifact with no `.tmp` debris); the writer **cannot throw** (returns `null`) and the hooks cannot propagate a sink fault, so diagnostics failure never masks the original fault. Every boot runs `CrashEvidenceStartupReport`: one Information line per artifact a **previous run** left behind (newest first, max 5 + a summary) and one honest **native-abort posture** line — dumps `enabled (DOTNET_DbgEnableMiniDump=…)` when the knob is set, otherwise **"not configured … a native abort (SIGABRT / exit 134) cannot run managed hooks and will leave no dump"**. **Execution modes (item iii):** capture is host-level, so it is identical under `dotnet watch` (the watch child *is* the host; an abort of that child runs the hooks) and in the composed product container (§INT-006 posture); the **honest limit** is stated in the artifact's own `captureNote` — nothing managed can run on a native abort — and the second honest limit is durability: the artifact lives on the container filesystem, so a `--force-recreate` (the dev-stack habit in this wave) discards it; a bind-mounted/host evidence path is the operational fix. **Flagged, out of this ticket's file scope (orchestrator action):** (1) the runtime dump knobs belong to the process environment — `DOTNET_DbgEnableMiniDump=1`, `DOTNET_DbgMiniDumpType=2`, `DOTNET_DbgMiniDumpName=…/crash-evidence/dump.%p` — and must be added to `compose.dev.yml`'s `x-api-environment` and to `compose.prod.yml` (both outside `src/Api/Program.cs`, `src/Api/Diagnostics/`, this doc and `CrashEvidenceTests.cs`), plus a durable evidence volume for the compose stacks; until then the startup line truthfully reports "not configured". | NFR-MDF-001/operational honesty (a crash with no evidence is an unauditable event — now auditable); §10.3 (logging & observability, amended); §10.4 (config doctrine — paths and bounds are config, secrets are collected only to be redacted); §3 C1/C2 (the API host that died silently); the INT-006 deploy precedent (crash-relevant hardening); foundation explicit-criteria convention (030 precedent) | `tests/Api.IntegrationTests/CrashEvidenceTests.cs` — **14 L2 cases, doubles/injected-seam only, no process kill, no database**, all tagged `TKT_foundation_045`: unhandled exception → artifact with every field an operator needs (kind/source/exitPath/capturedAt/exception/process/log tail/captureNote) and the collision-safe `crash-<UTC>-<pid>-<kind>` name; terminating vs non-terminating recorded from the real event args; unobserved task exception recorded, non-terminating, `args.Observed` still false (no behaviour change); log tail bounded to 25 of 500 lines with `truncated: true`; oversized message/stack clipped (artifact < 100 KB); a writer whose directory is a file returns `null` and does not throw; a throwing sink never propagates out of either hook; redaction holds for a configured secret, `password=`, `api_key=`, `Bearer` and `Basic` across message, stack and log tail, and for the JSON-quoted `CompactJsonFormatter` shapes (`{"Password":"hunter2"}`, `"ApiKey":"…"`) the real tail actually carries; eight concurrent captures on one fixed clock all land distinct artifacts with distinct content and no `.tmp` debris; `Install()` is proven to subscribe both process-level events (invocation-list proof), a second `Install()` adds no duplicate and `Dispose()` removes them; the **real host booted** (`WebApplicationFactory<Program>`) reports a previous-run artifact by name, stays silent and writes nothing on a healthy boot, and reports the native-abort posture matching the ambient dump knob, with both posture branches (`enabled` / `not configured`, including an explicitly-set-but-inert value) pinned deterministically through the report's injected environment lookup. Red→green: red as not-implemented (the `Degerli.Api.Diagnostics` types did not exist; the test project failed to compile against them), 14/14 green after (11 at first landing; the review round added the concurrency, `Install()` and posture-branch cases). Targeted regressions re-run green and unrelaxed: `HealthEndpointTests`, `InternalErrorTests`, `DataEnvelopeTests`, `Foundation014MailDispatcher*`, `SchedulerTests` — 25/25; `dotnet build Degerli.sln` 0 warnings / 0 errors. No TC lane exists for this lane (foundation explicit-criteria convention); no existing test was modified. **Live criterion (orchestrator post-merge, honest — operator action, never a test):** on the dev stack, a deliberately-triggered abort (`kill -ABRT` the API process) leaves a recoverable artifact (or, with the dump knob wired, a dump) and the revived host's startup line names it — the record goes on the ticket verbatim; any future spontaneous abort now leaves evidence by construction, and its diagnosis is a follow-up on that evidence, never this row's claim. |
| v1.5.43 | 2026-10-24 | **Watchdog scope corrected: each chain step is bounded on its own (120 min), the whole attempt keeps a 4-hour ceiling** (TKT-foundation-064, §10.9.17 extended; 041/059/062 semantics preserved; no schema/API change, no new dependency). **Recorded defect (dev stack, 2026-10-23):** `IngestionSchedulerOptions.MaxRunDuration` (90 min) bounded the **whole chain attempt** — one deadline raced against all of `ExecuteChainAsync` — while a healthy paced chain legitimately needs **~100–120 min** (prices ~5 min @3 s, statements ~13 min @12 s, dividends ~16 min, corporate-actions **58–74 min** at ≥12 s/item, then disclosures + metrics-recompute + market-snapshot + medians). A healthy chain could therefore **never** complete: Thursday's "corporate-actions hung" and Friday's "disclosures hung" were both the attempt bound expiring while an individually-healthy step was in flight, and `RUN_HUNG` rows 203/208 named whichever step happened to be running; on 2026-10-23 the chain fail-stopped six times at `statements` (runs 227/233/236/244). The operational stopgap — `Ingestion__Scheduler__MaxRunDuration=04:00:00` via environment override — is not the design. **Fix (two independent bounds):** (1) **per-step bound** `Ingestion:Scheduler:MaxStepDuration`, code default **120 min** (≈1.6× the observed healthy 58–74 min walk, ≈10× the 12 s item pace) — every step awaited under its own clock-driven deadline; a step exceeding **its own** bound is genuinely hung, its progress is snapshotted before any cancellation (059 DR-3), only that step is cancelled, the attempt fails and the ladder takes over; (2) **whole-chain ceiling** `MaxRunDuration`, code default raised **90 min → 4 h** (the operational value proven today, ≈2× the healthy chain, still ahead of the 08:30 safety net of §10.9.16) as the outer race; `<= 0` disables each bound independently. **One honest report per event:** exactly one `RUN_HUNG` row + one alert name the in-flight step and say **which bound fired** — `stats_json.bound: "step" | "chain"` plus both configured bounds additively (`maxStepDurationSeconds`, `maxRunDurationSeconds`) and `per-step bound <d>` / `whole-chain ceiling <d>` wording in the row's `error`, the alert subject and the `Hung run detected` line — so the two classes are distinguishable **from the ledger alone**. All 059 edges hold unchanged for both bounds (pre-cancel snapshot, successful finish inside the cancel window is the terminal truth, host stop writes nothing, retry measured from the attempt's actual end, unexpected cancellation still enters the ladder); `TickAsync`, stream independence (v1.5.41) and the retry ladder are untouched, and a mis-typed bound is still the DR-1 failed-attempt path, never a hot refire. | FR-MDF-009 (scheduler/retry machinery), FR-MDF-016 (pipeline health — healthy schedules must complete; the watchdog's honesty), NFR-MDF-001 (no silent ledger hole); §10.9.17 (extended: "The bound's scope, corrected"), §10.9.16 (the ceiling stays ahead of the morning safety net); 041 heritage (v1.5.23), 059 (v1.5.37), 062 (v1.5.41) — all preserved | **TC-MDF-098** (L2, `tests/Api.IntegrationTests/EodStallWatchdogTests.cs`, doubles only, fake clock, no real `Task.Delay`): (constant) shipped defaults are 120 min step / 4 h chain and both keys bind from configuration; (a) a chain of 40 min + 60 min (~100 min) completes **succeeded** under the shipped defaults with zero `RUN_HUNG`, each step's own row recording its duration, the slow step's 60 min asserted inside its 120-min bound — **red before: the 90-min whole-attempt bound fired mid-step**; (b) a never-completing doubled step under a 10-min step bound (4 h ceiling) is cancelled at **its own** bound — exactly one `RUN_HUNG` row naming it with `bound: "step"` and `maxStepDurationSeconds`, one alert naming it and saying `per-step`, the retry measured from the attempt's end, the next attempt's healthy step `succeeded` with no second `RUN_HUNG` — **red before: the tick never returned**; (c) individually-healthy steps whose total exceeds a 10-min `MaxRunDuration` produce exactly one `RUN_HUNG` naming the in-flight step with `bound: "chain"` and `maxRunDurationSeconds` — **red before: no `bound` field**; plus a supporting case that a mis-typed `MaxStepDuration` (60 days) is four ladder attempts + `RETRIES_EXHAUSTED`, zero ledger rows and no hot refire. Non-regression re-run green and unrelaxed: `SchedulerTests` (incl. TC-MDF-079's 10-min `MaxRunDuration`, 059 DR-1/2/3/5a/5b, 062's TC-MDF-081) and `EodStallWatchdogTests` (18/18), plus `EodChainTests`, `MorningChainTests`, `EodStaleServingTests`, `FundSchedulingCoverageTests`, `MacroIngestionTests`, `BackfillAndCoverageTests`, `MetricsRecomputeIdempotencyTests`, `MarketSnapshotTests` (49/49). **One harness-only delta, no assertion changed:** TC-MDF-081(b) raises `MaxStepDuration` to 12 h alongside its existing 12 h `MaxRunDuration` so its premise — a retry attempt still in flight at 05:30Z — survives the new per-step bound, exactly as that test already raised the attempt bound for the same reason. Live criterion (orchestrator post-merge, honest, recorded verbatim on the ticket): with the `04:00:00` environment override removed, a scheduled EOD chain completes end-to-end with **no `RUN_HUNG`** and per-step durations recorded, the shipped 120 min/4 h defaults confirmed in effect. |
| v1.5.44 | 2026-10-24 | **BIST holiday calendar: a holiday reads fresh like a weekend, and a gated stream explains its skip** (TKT-foundation-048, §10.9.13 extended — the 037 stored-trading-day rule gains the holiday half; §10.9.16/§10.9.17 cited, 064's bounds untouched and additive; no schema change, no API-shape change, no new dependency). **Requirement (user, 2026-10-22):** "at weekends it should display it too and also holidays it should display it too" — weekends already read fresh through 037's stored-trading-day rule; the missing piece was the holiday calendar, so a holiday Monday rendered `stale: true` on Tuesday morning. **The seam already existed and was empty:** `TradingCalendar.Holidays: []`, consumed by `MarketSnapshotStore` and wired through `IngestionServiceCollectionExtensions` — this row populates and extends it, it does not create an abstraction. **(1) Source = static configuration (planner pin 1; the live-calendar alternative is rejected and recorded):** `Ingestion:TradingCalendar:Holidays` is seeded from the official Borsa İstanbul "Resmi Tatil Günleri" Pay Piyasası tables — **2025 complete**: 01-01, 03-31, 04-01, 04-23, 05-01, 05-19, 06-06, 06-09, 07-15, 10-29; **remaining 2026** (seeding date 2026-10-24): 10-29. **Only full-closure weekdays are seeded**: weekend-dated closures are already non-trading (2025-03-29/30, 2025-06-07/08, 2025-08-30, 2026-03-21/22, 2026-05-30, 2026-08-30 excluded) and *arefe* half-days are trading sessions (2026-10-28, 2025-10-28, 2025-06-05 excluded). A malformed entry fails at composition — a typo must never quietly widen the calendar. **(2) Rule = `weekend ∪ configured holiday` (pin 2):** `IIngestionCalendar` gains `IsHoliday(DateOnly)` and `LatestTradingDayOnOrBefore(DateOnly)`, the walk bounded to a year so an unusable calendar throws instead of inventing a date; a holiday Monday resolves to the preceding Friday, and a closure plateau (Kurban 2026-05-27..29 + the weekend) resolves to the 05-26 arefe that traded. **(3) Serve-side gating, in scope:** `MarketEndpoints` no longer takes the maximum stored `price_date` at face value — a stored row dated on a non-trading date (a source echoing the previous close on a closed day, or a calendar-day backfill) is not a session, so the date is discredited and the store is **re-asked below it** (`ResolveLatestStoredTradingDayAsync` is reused unchanged, `MarketSnapshotStore` untouched: stored prices remain the truth and the answer stays a genuinely stored session). The gate discredits only non-trading dates, so a genuinely newer **trading** session still yields `stale: true` — the missed-run signal is preserved; and if the re-ask walks off the end of a store that holds only non-trading dates, the discredited stored maximum is returned, so the row still reads `stale: true` rather than flipping fresh. **(4) Gated streams are never silent (pin 3):** a trading-day-gated stream writes **one** short run-ledger row per stream per configured holiday (per process lifetime — the dedupe marker is in memory, so a restart mid-holiday may re-record it; an extra row, never a silent gap) — `job_code: "holiday-skip"`, `status: succeeded`, `started_at = finished_at`, `stats_json { gate: "trading-calendar", gateOutcome: "skipped", stream: "eod-chain" | "morning-chain" | "job:<code>", holiday: "yyyy-MM-dd", tradingDaysOnly: true }` — and runs no job (zero source calls, zero facts). The row is written **instead of** an attempt, so no step timer is armed, no `RUN_HUNG` can be declared and the retry ladder is not involved (064's per-step/ceiling bounds and 041/059/062 semantics preserved, purely additive). A **weekend** occurrence stays silent: the pre-048 weekend contract is unchanged, and the row exists for the non-obvious closure. Per-job streams opt in through the new `Ingestion:Scheduler:JobSchedules[].TradingDaysOnly` (default `false`; the shipped `intraday-quotes` entry is `true`, the macro/fund entries stay ungated because their data is published every day) — 047's sweep no longer sweeps a closed market. **Honest limits (recorded, not fabricated):** the list is static, so a newly declared or differently observed closure is a configuration change and until it lands that day reads **`stale`** — the conservative failure direction the doctrine already accepts (never newer-data-as-fresh); past-2026 closures are not re-seeded (the seeding pin is "2025 + remaining 2026"), which affects only backfill/morning run-date resolution for elapsed dates; and `StockOverviewAndValuationEndpoints.FreshnessAsync` keeps its own pre-048 weekday rule (`asOf < weekday-today`), a separate semantic pinned by its own lane and outside this ticket's acceptance — flagged for a follow-up, not silently changed. **One recorded test-contract amendment:** `SchedulerTests.TC_MDF_011_Holiday_calendar_is_config` asserted a *completely* empty ledger on a holiday (the silent-skip posture pin 3 replaces); it now asserts the single `holiday-skip` row and "no job ran" — the supersession is stated in the case's own doc comment. | FR-MDF-009 (scheduler/trading-day eligibility), FR-MDF-010/011 (honest achieved depth/freshness — the holiday extension), FR-MOV-001/002 (overview display freshness), FR-MOV-010/016 (last-known-good, never a false badge), NFR-MDF-001 (no silent ledger hole); §10.9.13 (extended with the holiday rule), §10.9.16 (the morning chain is gated too), §10.9.17 (bounds untouched, skip is not an attempt), §10.9.18 (holidays serve the last completed session); 037 heritage (stored-trading-day rule), 047 heritage (the intraday sweep), 064 heritage (untouched bounds) | L2 doubles only, never live sources. `tests/Api.IntegrationTests/TradingCalendarTests.cs` — new **TC-MDF-087** calendar lane (9 cases, no container): `weekend ∪ holiday` non-trading with its weekday neighbours trading (the arefe 2026-10-28 trades); holidays are configuration and an empty list is the pure weekday calendar; last trading day across a weekend is Friday; a **holiday Monday resolves to Friday**; a `holiday plateau` + weekend resolves to the 2026-05-26 arefe (and the first day after it resolves to itself); the June 2025 Fri–Mon closure cluster walks back to 2025-06-05; the **shipped `appsettings.json` list equals the publication** exactly (weekdays only, no duplicates, arefes excluded) and composes through the production options shape; the shipped `intraday-quotes` entry carries `TradingDaysOnly: true` while the macro/fund entries do not; a malformed date throws naming the bad value. `MarketOverviewFreshnessTests.cs` — three **TC-MDF-087** freshness cases: a `daily_prices` row dated on a configured holiday does not make Friday's served row stale (**red before: `stale: true`**), a holiday Monday with only Friday covered reads fresh on the Tuesday clock (the honest holiday world, regression pin), and the same calendar does **not** hide a missed trading session (`stale: true`). `EodChainTests.cs` — **TC-MDF-087** gate case: a configured holiday on the run day leaves exactly one ledger row (`holiday-skip`, dated, stream `eod-chain`, gate fields), zero WireMock requests, zero prices/snapshot/metrics for the day, and no alert. `SchedulerTests.cs` — **TC-MDF-087** per-job case (the intraday cadence): a holiday Monday runs the job on none of its four occurrences with exactly **one** skip row for the stream (`job:intraday-quotes`), and the next trading day runs at its own cadence; plus the amended `TC-MDF-011_Holiday_calendar_is_config`. Red→green recorded in the PR body: 7 failed / 26 passed across the four in-scope classes before the fix (the holiday-freshness `Assert.False` on `stale`, the missing `holiday-skip` rows, the ungated per-job stream, the unseeded shipped list), 33/33 after. Non-regression re-run green and unrelaxed: `MarketEndpointsTests`, `EodStaleServingTests`, `EodStallWatchdogTests`, `MorningChainTests`, `IntradayQuotesTests`, `StockOverviewAndValuationEndpointsTests`, `MarketSnapshotTests`, `LiveActivationTests`, `DataEnvelopeTests` (78/78), plus `FundSchedulingCoverageTests`, `MacroIngestionTests`, `MetricsRecomputeIdempotencyTests` (23/23) and `SchedulerTests`/`EodStallWatchdogTests` in full; `dotnet build` 0 warnings. Live criterion (orchestrator post-merge, honest, recorded verbatim on the ticket): the next BIST holiday (or a temporarily-configured past holiday date, re-verified read-only) shows the overview fresh-not-stale on the following trading morning, with the `holiday-skip` ledger row present for the gated streams. |
| v1.5.48 | 2026-10-24 | **Metric definition versioning — per-row formula provenance across formula changes** (TKT-foundation-070, §10.9.13 extended; `02` v1.4 §3.1/§8). **Recorded gap (gathered 2026-10-24):** neither `derived_metrics` (`value`, `window_years`, `is_adjusted`, `is_rested`, `regime_break`, `computed_at`) nor `sector_metric_medians` (`median_value`, `peer_count`, `excluded_count`, `is_adjusted`, `computed_at`) carried any definition/version stamp, while `CanonicalMetrics` is the single definition of every metric (BR-MDF-009) — so an edit to it silently changed what the next recompute meant (test strategy §10 already required a change record for an intentional formula change), `computed_at` moved on *write* and was therefore not a definition stamp, and the v1.5.34 input-aware recompute compared **values** only, making a formula change indistinguishable from a fact restatement: after one, no row could answer "which definition produced this value?", and historical rows were silently reinterpreted on the next recompute. **Fix (A + B1, planner-pinned after the coder's structural block):** (1) **`CanonicalMetrics.Version`** — an `int` constant in `/src/Core`, the single source of truth, bumped in the same change as any formula/input-assembly/not-meaningful-rule edit; (2) **per-row `formula_version`** (`int NULL`, CHECK `IS NULL OR >= 1`) on `derived_metrics` and `sector_metric_medians`, stamped by the **stores** (`DerivedMetricStore.UpsertAsync` / `SectorMedianStore.UpsertAsync`) on the insert path **and** the repair path — verified by grep that `new DerivedMetric` / `new SectorMetricMedian` exist nowhere else, i.e. the engines classify and never insert, so an engine-carried version would have produced a permanently-NULL column (the 058 ambiguity class this ticket exists to prevent); (3) **recompute rule** — a stored row whose stamp differs from the current version is rewritten **even when its value is identical**, and the plan reports it distinctly as `FormulaVersionRewritten` (the subset of `Rewritten` whose values were identical and only the definition moved), while same inputs **and** same version stay honestly `unchanged` (v1.5.34's idempotency preserved); (4) **honest absence** — `NULL` means "produced before versioning existed"; the migration is **additive, nullable and forward-only and writes no row** (every row present at deploy time was produced by the then-current definition — in production exactly the shipped one — but no per-row record of which definition wrote a value exists, so a back-filled stamp would be a fabricated claim); the first recompute of a day stamps it, and historical days keep `NULL` until they are actually recomputed; (5) **trigger provenance in the ledger** — `IngestResult`'s runner seam now records `stats_json.trigger`: `scheduled` for a chain run (the scheduler is the only supplier of `IngestionRunContext`, which defaults to it) and `admin` for the operator trigger (the bare runner overload), with an explicit `IngestionRequest.Trigger` override winning — additive and optional, so no job, test or 044 reason-code behaviour changes, and an operator can tell a scheduled recompute from an admin-triggered one after the fact. **Exposure recorded honestly:** the version is **DB-only** (no API/audit surface; `03` payloads unchanged). **Recorded gap (flagged, not hidden):** the distinct `FormulaVersionRewritten` count is plan-level, not a separate `stats_json` key — the two job classes were outside the ticket's audited scope; the ledger's `written` carries the total repairs as 044 established (a small follow-up for the lane that owns those jobs). **No formula change, no golden delta** (`MetricsEngine.ComputeAsync`/`SectorMedianEngine.ComputeAsync` untouched; FU §11 values byte-identical). | FR-MDF-013/014 (recompute mechanics + honest accounting), FR-MDF-015/NFR-MDF-002 (append-only, historical immutability), BR-MDF-009 (one definition per number — now recorded per row), FR-MDF-009/NFR-MDF-001 (the ledger states who asked), §10.9.13 (extended); `02` §3.1/§8 (additive nullable columns, honest empty backfill) | L2 doubles only, through the production job runner over the FU L2 universe in a migrated Testcontainers PostgreSQL; **TC-MDF-102** in `tests/Api.IntegrationTests/MetricsRecomputeIdempotencyTests.cs` (5 cases): every fresh row carries `CanonicalMetrics.Version` and a same-version re-run is `unchanged` with zero version rewrites; rows stamped `NULL` (the pre-versioning shape) are rewritten with identical values, restamped, reported distinctly (`FormulaVersionRewritten`), and the historical day keeps its `NULL` and its `computed_at` — then a settled re-run is `unchanged` again; the same rule for any non-current stamp (`Version + 1`); the median lane carries the identical discipline (15 rows); and the ledger records `trigger` = `scheduled` (scheduler context) / `admin` (bare overload) / an explicit request override, with the run's counts unaffected. Red→green: **5 failed / 4 passed** pre-implementation (the reds being `42703 column "formula_version" does not exist` and `trigger` absent from the ledger — the two absent artefacts), **9/9 green** after; `Core.UnitTests` 54/54 and the full backend integration suite re-run green (`MetricsRecomputeIdempotencyTests` 9, `SchedulerTests`, `EodChainTests`, `MorningChainTests`, `EodStallWatchdogTests`, `MarketSnapshotTests`, `FundIngestionTests`, `StatementsAndEventsIngestionTests`, `MemberSecuritiesIngestTests` et al. — 528 total, one pre-existing timing flake `TKT_foundation_026_pacing_applies_to_every_vyk_call` under full parallel load, green in isolation and untouched by this change); `dotnet build` 0 warnings. Live criterion (orchestrator post-merge, honest, recorded verbatim): on the dev stack the first recompute after this deploy stamps `formula_version` on the recomputed day's rows (previously `NULL`), a second same-day recompute is `unchanged`, and the ledger row reads `trigger: "scheduled"` or `"admin"` per how it was started — never fabricated. |
| v1.5.53 | 2026-10-25 | **The KAP public-site source skeleton: politeness as contract, the ToS/robots gate as checkable behaviour, and the lane's own evidence class** (TKT-foundation-079, new §10.9.19; user-directed lane 2026-10-25 — "i guess we need a kap scraper so lets go on with architect and testbuilder etc so it could work"). **The gate, both probe responses recorded verbatim and distinguished:** `GET https://www.kap.org.tr/robots.txt` → **HTTP 666**, 528 B, the site's generic HTML access-error page for that path (**not** a policy refusal), committed as `kap-public-robots`; `GET https://kap.org.tr/robots.txt` → **HTTP 307** → `/en/robots.txt` → **HTTP 404** (Next.js not-found). **KAP serves no `robots.txt` at all**: under RFC 9309 an absent (404) file states no rules, so **no automated-access rule is served or violated** and the lane proceeds under its politeness contract — no "Allowed" claim is made, and the absence of a policy document is not read as consent. **User decision, 2026-10-25 (ratified proceeding on the recorded probe evidence).** The posture is behaviour, not prose: `AutomatedAccess` has three values — the **code default `NotVerified`** refuses every request with `ACCESS_POSTURE_NOT_VERIFIED` (a posture nobody chose is not a permission), `Allowed` is chosen **explicitly in `appsettings.json`** (the ratified value), and `Forbidden` refuses with `AUTOMATED_ACCESS_FORBIDDEN` — all before pacing and before the flight gate, so no posture can be silently walked past. Had a prohibition been served, 079 would have **stopped and surfaced** instead of proceeding. **The OQ-1 probe, recorded verbatim:** an **empty** `mkkMemberOidList` + empty class over 2026-10-24→25 answered **HTTP 200 / 12 rows** (DKB 4, FR 3, ODA 3, DG 2; newest `1687047`; the test entity `KAP TEST A.Ş.`/`KTEST` present; rows with `stockCodes: null` present) — **084's market-wide feed is implementable**, and any exclusion of test or null-stock rows must be a **named, counted** rule. **The contract:** a separate source (`kap-public`, never `kap`) with its own base/client/pacing/options; the public site is a **website, not a contracted API** — `RequestDelayMs` **3000**, one flight gate (**never parallel**), an honest browser-shaped UA with a `DegerliBot/0.1` token, `RequestTimeoutMs` 30 s / `DetailFetchTimeoutMs` 60 s; bounded work (`MaxListPages` 40 → 2 min, `MaxDetailFetches` 200 → 10 min ≈ 12 min nominal) **plus** a wall clock (`MaxWalkDuration` **90 min** < the 120-min per-step bound) because a stalled detail could otherwise drive 3 h 20 min; a bounded pass resumes at its last completed unit. Honest statuses stay closed-set with one named reason each (`HONEST_EMPTY_WINDOW`, `UNPARSEABLE_PAYLOAD` — the export route lies `application/vnd.ms-excel` over HTML, so detection is by shape, `SOURCE_FETCH_FAILED`, `SOURCE_REQUEST_TIMEOUT`, plus `WALK_PAGE_CAP`/`WALK_DETAIL_CAP`/`WALK_DEADLINE`). **Three identifier spaces, never mixed:** public oid `4028e4a1413b7ef401413bc2251e0047`, public index `1643141` (VYK head `1231017`, VYK ASELS `866`). Five paced captures committed (four verbatim; the 1.8 MB report bounded-trimmed and **labeled as such**). The full `Ingestion:KapPublic` key set is pre-wired, the two source selectors ship inertly as `vyk`, the VYK adapter is untouched (per-step source selection = configuration, never code removal). | FR-MDF-002 (the statements source's reachability foundation), FR-MDF-008 (provenance — the `kap-public` evidence class), FR-MDF-012 (isolation — a new source beside the untouched VYK one), NFR-MDF-001 (no silent ledger hole: every status names its reason), §10.9.19 (new), §10.9.11/§10.9.15 (cited, unchanged), §10.9.17 (the 120-min step bound the walk deadline sits inside), AD-14 (config over code) | L2 doubles only, never a live site call (`tests/Api.IntegrationTests/KapPublicSourceTests.cs`): **TC-MDF-103** (the shipped config binds the full key set — constants pinned by config-equivalence, walk deadline strictly below `MaxStepDuration`, the inert `vyk` selectors; on a fake clock every consecutive request pair is separated by ≥ the configured delay and the delay is configuration; the client sends the honest UA and the fixture-faithful byCriteria POST body, details are fetched by public index, and one flight gate is never breached — the journal shows strictly sequential requests; the four honest statuses with their reason codes — 5xx → `failed` with no rows, a 200 HTML/JSON shape lie → `UNPARSEABLE_PAYLOAD` with the raw body, an empty window → `HONEST_EMPTY_WINDOW` with no cursor move; the page cap, the detail cap and a fake-clock deadline each truncate, record their reason and resume at the last completed unit); **TC-MDF-104** (the `robots.txt` capture is byte-present with its 666/no-directive reading recorded in §10.9.19; the OQ-1 market probe is committed and classifies as a well-formed 12-row list through the production reader, `KTEST` present, null-`stockCodes` rows present; a `Forbidden` posture refuses every request shape with `AUTOMATED_ACCESS_FORBIDDEN`, the code-default `NotVerified` posture refuses with `ACCESS_POSTURE_NOT_VERIFIED`, and the double/recording handler logs none; a reader-level 666 answer is a `SOURCE_FETCH_FAILED` source-down with the verbatim excerpt, and a large quarantine body is bounded to `MaxExcerptChars` with its original length in `Detail`). **No golden delta:** `VykStatementsSourceTests`, `StatementsAndEventsIngestionTests`, `VykCompanyDirectoryTests`, `CannedSourceCatalogTests` re-run green (the catalog additions are additive), `dotnet build` 0 warnings. Red→green honestly stated: the capture phase landed first, so this suite was written against a mechanism that already existed; the observed red evidence is its first run — **7 failed / 8 passed**, missing-contract and missing-mechanism assertions (never environment or syntax errors), with the architecture-reading case red until this section landed — and **15/15 green** after. Downstream re-validation: TKT-foundation-080..084 build on §10.9.19 (each extending, none replacing) and test plan §20 (v1.23, TC-MDF-103/104) is their acceptance contract; the test-planner re-derives no earlier section. |
| v1.5.54 | 2026-10-25 | **The public-site company identity map: the public oid ↔ instrument mapping is stored with provenance, and the three identifier spaces are never conflated** (TKT-foundation-080; §10.9.19 extended — the identity subsection landed here; `02` §3.1/§8 gain the table and its forward-only migration; no VYK code, cursor or table touched; no new dependency). **The problem the mapping solves:** `byCriteria` is filtered by the public site's `mkkMemberOidList` (the company **oid**), while our facts are keyed by `instruments.id` and the VYK lane speaks the numeric `companyId` — three spaces, and the investigation's OQ-4 says the mapping must be **stored, not assumed**. **The store:** `kap_public_company_map`, one row per instrument (PK `instrument_id`, FK → `instruments`, `Restrict`), with the oid and its provenance (`kap_public_oid`, `kap_public_oid_source` — the paced identity route plus its keyword, `kap_public_oid_as_of`) and, separately, the VYK numeric id with **its own** provenance (`vyk_company_id`, `vyk_company_id_source`, `vyk_company_id_as_of`, nullable, present-or-absent-with-its-evidence). **Space isolation is a store invariant, not a convention:** CHECK `^[0-9a-f]{32}$` on the oid column and `^[0-9]{1,12}$` on the VYK column mean a numeric companyId can never be written where a public oid belongs (nor an oid where the VYK id belongs) — ASELS's oid and `866` coexist as recorded facts about different spaces, and neither is derived from the other. In code they are distinct types (`PublicCompanyOid`, `VykCompanyId`) with **no** conversion operator between them and shape-checking parses only. Un-mapped is **honest absence**: no row, never a placeholder oid. **The resolution path:** `KapPublicCompanyIdentityResolver` walks the bound universe sequentially over 079's paced single-flight client, reading `POST /tr/api/search/combined {"keyword":"<ticker>"}` through `KapPublicIdentityReader` (four honest statuses, shape-based). A hit binds only when it is a company hit (`searchType` `C`) whose `cmpOrFundCode` is the bound symbol and whose `memberOrFundOid` parses as an oid; a hit for another company is not a match. Un-mapped symbol ⇒ skipped, **named** (logged), **counted** (`COMPANY_IDENTITY_UNRESOLVED` — the 044 vocabulary applied to this source) and **never fetched**; two distinct oids for one symbol ⇒ `COMPANY_IDENTITY_AMBIGUOUS` (never pick one); transport failure / timeout / non-identity body ⇒ **source-down** with its reason and **nothing stored** (the VYK directory failure posture as precedent). **Re-resolution is idempotent:** identical input leaves rows `unchanged` (no write, no timestamp move); a changed identity or new evidence date repairs the same row in place — one row per instrument, never a duplicate. Consumers read the store (`GetPublicOidAsync`/`GetPublicOidsAsync`), never re-deriving per walk. **Wiring repair recorded honestly:** 079 landed `KapPublicHttpSourceClient` over a **singleton** keyed `HttpClient` while the client's constructor re-normalizes `BaseAddress` — the second run's client therefore threw ("properties can only be modified before sending the first request"), i.e. the lane could run exactly once per process. The keyed `HttpClient` is now **scoped** (one per run, the lifetime the scoped pacer/client already implied); no 079 assertion changed. | FR-MDF-002 (the company-scoped fetch's identity input), FR-MDF-008 (provenance — per-space source and asOf), FR-MDF-012 (isolation — the 046 `COMPANY_IDENTITY_UNRESOLVED` doctrine extended to this source), NFR-MDF-001 (every skip named and counted); `02` §3.1 (the table's semantics) and §8 (forward-only migration); §10.9.19 (extended), §10.9.11 (cited, unchanged); the investigation's OQ-4 (the mapping is stored, never assumed) | `tests/Api.IntegrationTests/KapPublicIdentityTests.cs` — **TC-MDF-105** (i)/(iii) the committed `kap-public-identity-ASELS` capture resolves end-to-end and is **persisted** with provenance + asOf, the stored oid (read back through the store, not assumed) is the oid the byCriteria query is issued with and the committed ten-row FR list answers; (ii) a bound symbol whose only hit is another company is unresolved, named in the log with the 044 reason, counted, absent from the store, and **never fetched** (zero byCriteria requests), while the resolvable company still persists — and an ambiguous two-oid answer is named under its own reason with nothing stored; (iii) ASELS's oid and the VYK `866` (read out of the committed `/memberSecurities` capture, not typed in) coexist in distinct columns with distinct provenance, the types expose no conversion, and the store **refuses** a VYK id in the oid column and an oid in the VYK column (`ck_kap_public_company_map_oid_shape` / `_vyk_shape`, `23514`); (iv) an identical re-resolution is `unchanged` (no write, no timestamp move, one row) while a next-day resolution repairs that same row in place; (v) a 500 identity answer and a non-identity body are both source-down with their reasons, one request only, zero rows stored. **Doubles only, zero live requests to kap.org.tr in this ticket.** Red→green recorded: with the implementation absent the suite does not compile against the missing seam (`CS0246`), and with the seam present but the migration removed all five cases fail with `42P01: relation "kap_public_company_map" does not exist` — **5/5 green** with the migration. Non-regression: `KapPublicSourceTests`, `VykCompanyDirectoryTests`, `MemberSecuritiesIngestTests`, `MigrationsAndSeedTests`, `RealSourcePricesTests`, `CannedSourceCatalogTests` **54/54 green** (the VYK adapter and its directory are untouched). Live criterion (orchestrator, post-merge, paced): one resolution over the bound universe (XU100) with the resolved/unresolved counts recorded verbatim and unresolved symbols listed by name on the ticket. |
| v1.5.55 | 2026-10-25 | **The public-site FR report decoder: the excel-HTML tables onto the canonical statement model, the cash-flow statement included** (TKT-foundation-081, §10.9.19 extended — supersedes TKT-foundation-051, which is recorded on its own ticket; the VYK `ConceptMap` CF extension stays honestly impossible without real VYK CF shapes). **New seam:** `KapPublicReportDecoder.Decode(html, symbol)` → `KapPublicReport` (the `StatementsPayload` for the store **plus** the header/columns/per-value provenance and the counted honesty ledger); line-item logic lives in `KapPublicReportDecoder.cs`/`KapPublicReportModels.cs`, additive beside the untouched VYK/XBRL decode paths and `StatementMapper`. **Parser:** a new `Degerli.Ingestion` package, **AngleSharp 1.8.6** — the export nests each row's label panel and each value's `gwt-Label` inside nested tables, so regex row matching fails on the real capture and a DOM parser is the honest tool; the export still ships the old GWT CSS classes, so a KAP redesign of this route breaks the decoder and the 068 drift probes must cover it (flagged follow-up). **Value path de-risked:** every value cell carries the raw integer in its `title=""` attribute (`title="39468926"` beside the displayed `39.468.926`) — the attribute is the value, the displayed text is a cross-check only, a disagreement is a counted `DISPLAY_TEXT_MISMATCH` finding while the attribute wins; an unreadable attribute is a counted `VALUE_UNATTRIBUTED` drop, never `0` and never a fallback to the display text. **Column→period is read from the column headers, never from position:** value cells carry `col-order-class-N`, whose `N − 4` is the `context-header` index (verified on all four captured tables: BS 2/2, IS 4/4, CF 2/2, EQ 0 headers). The report's own sub-period columns (the IS 3-month Q2 pair, ending on the same dates as the 6-month columns) are skipped whole and counted `SUB_PERIOD_COLUMN` (6 cells) rather than overwriting the year-to-date row; the equity matrix has no `context-header` cells at all, so its 98 valued cells are honest-null drops counted `NO_COLUMN_HEADERS` and **no EQ statement row is emitted** — never a defaulted period. **Section before label:** the CF block is resolved from the capture's own aggregate rows (`CF_OPERATING`/`CF_INVESTING`/`CF_FINANCING`/`CF_SUMMARY`) **before** the label map, so the repeated labels (and the IS-shared `Dönem Karı (Zararı)`) stay attributable; unmapped labels keep the section-scoped `OTHER_{section}_{Slug}` passthrough `StatementMapper` already tolerates. **Duplicate rule = the TC-MDF-065 doctrine:** equal values collapse idempotently (one payload line, one provenance line), unequal values are a counted `DUPLICATE_LABEL_CONFLICT` per column that retains both values verbatim in the finding, keeps the first value and records `ConflictingItemCodes` so the store quarantines the row as `CONFLICTING_VALUE` — never a silent latest-wins. **Period type surfaced (user decision 2026-10-25: annual FCF + labelled interims):** `Periyot` maps only from the attested pairs `1/2/3/4 → 3 Aylık/6 Aylık/9 Aylık/Yıllık` (the committed `byCriteria` captures' own rows; the capture's non-FR sustainability filing is not a financial period and is not read as an attestation), anything else is null with `PERIOD_TYPE_UNKNOWN`, and the store's `PeriodType` fails safe (only an attested annual writes `FY`; interim **and** unknown write `Q`) so the DCF FY filter can never consume a half-year. Nothing is annualized. **Units:** `Sunum Para Birimi` `1.000 TL` → `UnitScale = thousands`, `Currency = TRY`; unrecognised → both null and counted, so the store refuses rather than scaling wrongly (02 §3.1). **CF decodes and FCF computes through the unchanged formula:** `OCF` from the statement's own operating total `İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI` (after tax paid and other operating payments — DR-2 user decision 2026-10-26), `CAPEX` from the combined tangible/intangible purchase row (its children are **not** mapped — the combined row is the definition), plus `NI`, `DEPR_AMORT`, `ΔWC`, closing `CASH`; the before-tax subtotal `Faaliyetlerden Elde Edilen Nakit Akışları` is deliberately **not** mapped onto `OCF` and stays an `OTHER_CF_OPERATING_*` companion. Captured goldens: OCF `15.206.440`/`18.013.334`, CAPEX `−33.746.150`/`−17.190.837`, and the capture's own cash ladder cross-validates the alignment (current opening cash `34.251.653` = BS prior cash; current closing `39.468.926` = BS current cash). 116 decoded lines (BS 4+4, IS 3+3, CF 51+51) across 6 statement rows, 0 display mismatches. **Recorded gaps:** `fin_line_items` has no label/table provenance column, so per-line provenance lives on `KapPublicReport.Lines` at decode time (additive schema change = its own ticket); evidenced share-count lines are never consumed here and route to TKT-foundation-065; the decoder writes no row — 082/083 own the walk and the store call. | FR-MDF-002/003 (statement ingest incl. CF), FR-MDF-008 (provenance `kap-public`), FR-MDF-012 (quarantine), FR-RES-005..018 (SNA/FCF display supply), FR-VAL-001 (DCF baseline — FCF), 02 §3.1/§6.1, `CanonicalMetrics` FCF v1.3; §10.9.19 (extended), §10.9.11/§10.9.15 (cited, unchanged) | **TC-MDF-106/107/108** (test plan §22, L2 doubles only — the committed `kap-public-report-1643141` capture is the entire input; **no test calls kap.org.tr**, and every negative case is derived by mutating that capture in memory). Implemented as `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs` (20 cases after the review rounds, tagged `TC_MDF_106`/`TC_MDF_107`/`TC_MDF_108`). Red→green recorded in the PR body: **14 failed / 0 passed** against the not-implemented decoder (all `NotImplementedException`, build clean), **14/14 green** after. Non-regression re-run green and unrelaxed: `RealSourceStatementsTests`, `StatementNormalizationTests`, `StatementVersioningTests`, `VykStatementsSourceTests` (22/22); `dotnet build` 0 warnings. Live criterion (orchestrator post-merge, gated on 082 wiring): one further real current report decoded end-to-end with OCF/CAPEX rows landed and FCF/SNA populated for that company. |
| v1.5.56 | 2026-10-26 | **The statements lane gains a public-site mode: the source is configuration, the walk is date-windowed and bounded, its cursors are its own, and the two sources coexist without a silent swallow** (TKT-foundation-082; §10.9.19 extended — the statements-lane subsection landed here; the VYK adapter, its cursor and its routes are untouched; no schema change, no new dependency). **Selection is configuration, never a rewrite:** `Ingestion:Statements:Source` selects `vyk` (shipped, and the code default) or `kap-public`; an unrecognised value fails the run **by name** (`INVALID_SOURCE_SELECTION`) with **zero** requests instead of quietly running the other source under the right-looking provenance, and switching back is a config change. **The walk is date-windowed, because the public list has no usable index paging:** a company's reports come from `POST /tr/api/disclosure/members/byCriteria` over `fromDate..toDate` — the shipped lookback `KapPublicLookbackMonths` = 24 for a company with **no** persisted cursor, `KapPublicWindowDays` = 365 per list request, windows iterated oldest→newest so the watermark advances monotonically. The public oid stored by v1.5.54's map is the only company identity the query uses — never the symbol's spelling, never the VYK `companyId`. **Selection inside the class is the site's own `disclosureCategory: FR`, not `disclosureType: FR`:** the committed ten-row capture carries nine `disclosureType: FR` rows and only three `disclosureCategory: FR` ones — exactly the three `Finansal Rapor` rows; the other six (responsibility statements, activity reports) carry no financial tables, so a class-only filter would fetch and quarantine them as unparseable. Candidates are ordered newest-first **within a window**, so a bounded pass spends its budget on what a consumer needs now; because windows iterate oldest→newest, a multi-window bootstrap bounded part-way loses the **newest windows** until a later pass (never a claim that the newer periods do not exist, and never a backwards watermark). **Boundedness is politeness, and the budget is per *pass*:** one `KapPublicWalkBudget` (page ceiling, detail ceiling, and a 90-minute deadline strictly inside the 120-minute per-step bound — all read from `Ingestion:KapPublic`) is created **once per run and shared across every company**, so the caps bound the run instead of multiplying per company; every request is admitted before it is issued, and the request path itself is v1.5.53's client (≥3 s pacing, one flight gate, honest UA, posture gate) unchanged — 082 adds no request path of its own. On truncation the walk records `truncated` with its reason (`WALK_PAGE_CAP`/`WALK_DETAIL_CAP`/`WALK_DEADLINE`), persists the last **completed** report and the next pass resumes exactly there. The universe is walked **least-recently-walked first** (by persisted watermark, never-walked ahead), so a company a spent budget never started is at the front of the next pass rather than starved behind the same head companies forever; it is named under its own `WALK_NOT_STARTED` reason, distinct from the truncation codes of a company that was walked and then bounded. A **list** source-down in a later window returns the pass's **real counts** with the named failure, so a failed company that already wrote rows makes the run `partial` — never `failed`-with-zero beside stored rows and never a falsely complete walk. **Its own cursors, in their own spaces:** `kap-public-statements-cursor` (`coverage_metadata`, instrument-scoped) holds the watermark (inclusive last covered day) plus the report indices completed since it; `kap-public-statements-depth` holds the **provenance of depth** — requestedFrom/requestedTo against achievedFrom/achievedTo, the bootstrap flag and any truncation — because the configured lookback is a *request*, and an unwalked range must never read as "there are no statements" (FR-MDF-010). Both types are outside the coverage recorder's owned set and distinct from the VYK `kap-disclosure-cursor`, so a public index (`1643141`) never enters the VYK cursor and a VYK index (`1231017`) never enters the public one; the resume rule **accumulates** the seen set across a window walked in two passes (replacing it would re-fetch what the first pass completed). **Isolation and the honest statuses (the v1.5.44/v1.5.52 doctrine applied to this source):** an unmapped company is named, counted (`COMPANY_IDENTITY_UNRESOLVED`) and never fetched; a report the source cannot deliver — or delivers in a shape that is not a report — is a named, counted quarantine carrying the source's own answer verbatim (`DISCLOSURE_DETAIL_UNFETCHABLE`, `UNPARSEABLE_PAYLOAD`) while the company's other reports are still written; an all-isolated zero-write run is `partial`, never `failed`; and a genuine list source-down is `failed` with no rows and no quarantine, because with no index there is no walk and an honest zero would be a lie. A pass truncated with zero writes reports `partial`. **Two-source coexistence — the write rule, the read preference deferred:** the natural key in `financial_statements` does not include the source and `version` is the restatement sequence, so a cross-source fact would otherwise collapse into a silent idempotent `unchanged` (or, worse, be forced into a false `restated` row). For a period the **other** source already covers, the public-site fact is therefore **not written**, the incumbent row is retained exactly as it is (not overwritten, not deleted, not relabelled, as-of queryable with its lines), and the overlap is surfaced as a **named, counted** `SOURCE_OVERLAP_RETAINED` finding. Coverage is asked on the store's **exact** key **and** on the calendar period (same period end + statement type, any period type), because VYK defaults to `FY` at 12-31 while the decoder fails safe to `Q`; that second case is a named, counted `SOURCE_PERIOD_TYPE_DISAGREEMENT` finding rather than a duplicate row for one real period. The `SOURCE_OVERLAP_RETAINED` finding's evidence names both provenances, the period and the disclosure index, and it becomes re-ingestable after the source joins the natural key only by an explicit **cursor reset** (or TKT-foundation-083's recorded re-pin), because the lane has already marked the report completed (its index is in the seen set). The rule is **per natural key, not per report**: the same report's uncovered periods are still written. The read preference for a coexisting period belongs to v1.5.57's `source_ref`-in-key migration and `StockFundamentalsEndpoints.Pick` (TC-MDF-111), not here. | FR-MDF-002 (the statements source's second, configurable body), FR-MDF-008 (provenance — `kap-public` written, `kap` untouched), FR-MDF-010 (achieved depth recorded as provenance), FR-MDF-011 (coverage honesty), FR-MDF-012 (per-company and per-report isolation), NFR-MDF-001 (every skip named and counted), §10.9.19 (extended), §10.9.11 (the VYK lane, cited and unchanged), §10.9.17 (the per-step bound the walk deadline sits inside), AD-14 (source selection is configuration) | L2 doubles only, never a live site call (`tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs`, 15 cases after review round 1): **TC-MDF-109** (i) selection by configuration — the byCriteria body carries the **stored** oid and the class filter, the export journal shows `1643141` fetched and the `disclosureCategory: ODA` row that shares the `FR` class never fetched, the facts carry `source_ref = kap-public` and **zero** VYK requests are journaled, while the `vyk` selection issues no public-site request at all and the shipped config and code default still say `vyk`; an unrecognised value fails by name with zero requests and zero rows; (ii) an unmapped company is named, counted and never fetched while the mapped one writes, one unfetchable report is quarantined with the source's verbatim body while the rest are written, an all-isolated run is `partial` with zero writes while a list 500 is `failed` with no rows and no quarantine; (iii) the detail ceiling and the page ceiling each truncate, persist the completed position and resume there (the resumed pass re-fetches nothing it completed, writes no duplicate, and a completed pass re-runs with zero report requests), and the 90-minute wall-clock bound truncates on a fake clock and is strictly below `MaxStepDuration`; (iv) the VYK cursor's `1231017` and the public cursor's `1643141` stay in their own rows and data types, none of them coverage-recorder-owned; (v) a bootstrap asks for the configured lookback (read off the wire) and records requested-vs-achieved depth with the bootstrap flag; **TC-MDF-110** the two sources coexist — a VYK FY2023 period is untouched (same provenance, version and line items) beside the public lane's periods, and the serving read path returns both — and, for a period both cover, exactly **3** `SOURCE_OVERLAP_RETAINED` findings (IS/BS/CF at 2026-06-30 `Q`, each naming both provenances and the index) with the same report's **3** uncovered comparatives still written, the incumbent rows keeping `kap` / `version 0` / no restatement date and their line items. **No golden delta:** statements/kap-public/coverage/stock-fundamentals **177/177**, `EodChainTests`/`LiveActivationTests`/`BackfillAndCoverageTests`/`DcfFinancialSectorTests` **15/15**, `dotnet build` 0 warnings. Red→green recorded: with the pre-082 job restored the suite does not compile against the missing seam (`CS0117`), with the seam present and only the public dispatch withheld **10 failed / 2 passed** on assertions, **12/12 green** with the dispatch. Doubles only — **zero live kap.org.tr requests in this ticket.** Live criterion (orchestrator, post-merge, paced): one `Source = kap-public` run over the bound universe with the written/unchanged/overlap-retained and achieved-depth counts recorded verbatim on the ticket. |
| v1.5.57 | 2026-10-26 | **The statement store's natural key gains the source, one pinned preference decides which source's fact is current on every read path, and coverage depth is reported per source** (TKT-foundation-083; §10.9.19 extended — the read-preference subsection landed here, closing the read side v1.5.56 deferred; `02` §3.1's key semantics; one forward-only migration; **no new dependency, no metric formula, no share-count consumption, no UI work**). **The key:** `financial_statements`'s unique index and `FinancialStatementStore`'s natural key become `(instrument, source_ref, period_type, period_end_date, statement_type)` + `version` (`20261026130253_StatementSourceInNaturalKey` drops and recreates the index). **No fact is rewritten:** every stored row keeps its values, `version`, `restatement_date` and `source_ref`; `version` stays the restatement sequence *within one source's own filing series*, so the public-site fact for a period the incumbent also holds is a version-0 fact, never a restatement of it. **The preference is pinned in one place** — `StatementSourcePreference` (`src/Ingestion/Statements/`, additive): rank `kap-public` < `kap` < any other recorded class (a fixture/backfill provenance **or a canonical-double body's own `sourceRef`** — the labelled classes are the evidence-class names, matched as names, never by prefix), then highest `version` within that source, then row id — a **total** order, so the answer can never depend on arrival order (never a silent latest-write, never a same-version tie-break). Rule: **public-site current wins for the periods it covers; the incumbent's rows are retained and stay as-of queryable** — selection happens *on read*, nothing is deleted or relabelled. **Whole statement, never per line:** the winner is chosen per `(period, statement type)` and all its lines come from that row, which is precisely what keeps market P/E and the per-share metrics honestly `NULL` when the current filing carries no share count instead of borrowing an older filing's count (FR-MOV-006/BR-MOV-010, the 065 Option-A posture); a balance-sheet period the current source does not cover *is* the incumbent's fact, and then the incumbent's count is legitimately that fact's. **Every consumer goes through the one rule:** `StockFundamentalsEndpoints.Pick` (served financials + balance sheet), `MetricsEngine`, `MarketSnapshotEngine` and `DcfBaselineGenerationJob` — the migration makes two same-version sources reachable for the first time, so a local `OrderByDescending(version).First()` would have resolved them by row order. **Coverage depth is per source:** `coverage_metadata` gains `statements:{sourceRef}` (`statements:kap`, `statements:kap-public`), one achieved range per (instrument, evidence class), written by `CoverageRecorder.ReconcileUniverseAsync` beside the aggregate union row, and deliberately **outside** `CoverageDataTypes.InstrumentScoped` — a missing `statements:kap-public` row means "this source wrote nothing here", never "this source found no statements" (the lane's own `kap-public-statements-depth` keeps that second answer), so honest absence is not turned into a gap claim and a 2023-only company reads as 2023-deep, never as current. **v1.5.56's write rule stands unchanged:** the lane still withholds an overlapped fact and names it `SOURCE_OVERLAP_RETAINED` (incumbent untouched, no false `restated`); with the source in the key that withheld fact *could* coexist, but re-ingesting it still needs an explicit cursor reset because the report's index is in the seen set — the re-pin is recorded on the test plan (v1.31), not implemented here. **Residue recorded honestly:** `KapPublicStatementTargets.GetForeignPeriodsAsync`'s doc comment still describes the pre-migration "at most one provenance answers per key" shape (behaviour unaffected — the overlap question is asked of *foreign* sources and the first match is named), and `GET /admin/coverage` renders only the `InstrumentScoped` set, so per-source depth is visible in `coverage_metadata` and not yet on that admin surface (an admin-API change, outside this ticket's scope). | FR-MDF-008 (provenance decides the read), FR-MDF-011 (per-source coverage honesty), FR-MDF-013/014 (recompute + honest accounting over the mixed world), FR-MDF-015/NFR-MDF-002 (append-only, historical immutability — nothing rewritten, both sources as-of queryable), FR-MOV-006/BR-MOV-010 (market P/E honest-null), FR-RES-005..018 (served financials), FR-SCR (metric supply); `02` §3.1 (the key and `version`'s meaning), §8 (forward-only migration); §10.9.19 (extended), §10.9.13 (cited, unchanged); v1.5.56 (the deferred read side, closed here) | **TC-MDF-111** (test plan §24, implemented v1.30 in `tests/Api.IntegrationTests/KapPublicRecomputeTests.cs` — 6 cases, L2 doubles through the production job runner and the in-process API host, zero live kap.org.tr requests): recompute from the current facts with every row stamped by `CanonicalMetrics.Version`; the overlap read preference (i)/(ii)/(iii) as the served payload's own numbers (ALFA REV 2,000/NI 400/FCF 200 from the public row, one period per `(periodType, periodEndDate)`, no false `restated`), the incumbent fallback (BETA, ALFA FY2023, UNSEC 2023-only) and the as-of read reaching both rows; market P/E honest-null proved as a differential over the snapshot's own exclusion count; per-source coverage depth with absence-not-zero; and the store-level coexistence (same period from two sources = two version-0 facts, restatement stays within one source, the preference serves the current fact over a newer restatement). **TC-MDF-110's two overlap-write cases re-pinned unchanged** against the migrated store (the overlap check reads `source_ref != laneSource`, not the uniqueness rule — no assertion weakened). **Red evidence recorded honestly:** the implementation environment had no Docker/Testcontainers, so the pre-migration red is the schema's own refusal — the mixed-world seed cannot be written under the source-less unique key (`23505`), and the store-level case fails as the silent `version 0` swallow (`Expected: 1, Actual: 0`) — with the CI run on the PR carrying the red→green counts. Non-regression: `KapPublicStatementsIngestTests` (incl. the re-pinned cases), `MetricsRecomputeIdempotencyTests`, `MarketSnapshotTests`, `MetricsEngineTests`, `ProfileMetricServingTests`, `StatementsAndEventsIngestionTests`, `DcfBaselineRegenerationTests`, `BackfillAndCoverageTests`, `MigrationsAndSeedTests`. Live criterion (orchestrator, post-merge, on the dev stack after a real public-site statements pass): the three derived jobs complete with current-period metrics for covered companies, every written row carrying the current `formula_version`, per-source coverage visible, market P/E honestly null, stock pages showing current-period financials — recorded verbatim on the ticket. |
| v1.5.59 | 2026-10-25 | **The watchdog's post-cancel re-check becomes a bounded settle window — "one attempt, one row" made deterministic** (TKT-foundation-085, §10.9.17 amended as a repair note; 041/059/062/064 semantics preserved; no schema/API change, no new dependency). **Recorded defect (PR #141 review round 2, escalation E3; a defect in merged 059-heritage code, not in that PR):** the post-cancel `IsCompletedSuccessfully` re-check ran **once, synchronously**, immediately after `attemptCts.Cancel()` + the awaited deadline-source cancel. That condition observes a step's terminal state only when the step's continuation resumed **inline on the cancelling thread**; when the runtime **queues** it — the normal async case under load — the re-check saw a pending task, wrote `RUN_HUNG`, and the queued continuation then wrote the step's own success row. Consequence: **two `ingest_runs` rows for one attempt, a spurious `RUN_HUNG` alert and a spurious retry** (reproduced deterministically by inserting an `await` hop into the swallowing double; it is also the CI flake class of `TC_MDF_079_DR_2` on any head). **Fix (planner-pinned shape (a), the single per-attempt claim (b) recorded as the fallback):** the re-check is an **interval** — after the cancellation the hung path races the attempt's task against a **clock-driven settle delay**, `Ingestion:Scheduler:PostCancelSettleWindow` (code default **5 s**; `<= 0` disables, the documented opt-out; a mis-typed value is the DR-1 failed-attempt path, never an escape out of the tick). A settle inside the window with no failure is the terminal truth (one row, no `RUN_HUNG`, no alert, no retry — in-flight steps that merely end on our cancellation still go hung); the window expiring is the **unchanged** hung path (064's bounds, one row + one alert naming the pre-cancel step and the bound, `DisposeWithAbandonedWork` disposal, the ladder takes over) plus the honest record of the grace: `stats_json.settleWindowSeconds` (additive, `0` when disabled), the window in the row's `error`/alert subject and body, and a `Settling …` warning when it is armed. The record's `finished_at` and the retry base stay the **attempt's own end** (the bound instant), so the grace never shifts the ladder's punctuality; the host-stop path is checked *before* the settle and is untouched (DR-5b); the delay runs on the injected `TimeProvider` (deterministic fake clock, no real `Task.Delay` bounds a test). **Residual recorded honestly:** a cancellation-swallowing step completing *after* the window still lands its own row beside `RUN_HUNG`; in production the runner writes through the attempt token, so the token-cancelled EF write throws and no second row is written, and for cancellation-swallowing steps the grace-stated `RUN_HUNG` row is the honest attempt record — shape (b) is the declared durable follow-up if it is ever seen live. | FR-MDF-009 (scheduler/retry machinery — the retry decision follows the attempt's true terminal state), FR-MDF-016 (pipeline health — the watchdog's honesty), NFR-MDF-001 (no silent ledger hole — and no *duplicate* hole); §10.9.17 (amended), §10.9.16 (cited — the morning chain the ceiling stays ahead of); 059 heritage (v1.5.37, PR #124), 064 heritage (v1.5.43, PR #133 — bounds untouched and load-bearing), 062 heritage (v1.5.41 — stream independence untouched) | **TC-MDF-113(a)** (L2, `tests/Api.IntegrationTests/SchedulerTests.cs`): the swallowing double gains the `await` hop so its continuation is **queued** — exactly two rows collected before the fix (**deterministic red: `Assert.Single` saw the step's success row + the `RUN_HUNG` row, 3/3 runs**), one `succeeded` row on attempt 1 with no `reasonCode`, no `RUN_HUNG` row, no `RUN_HUNG` alert, no retry and no exhausted alert after the fix. TC-MDF-079 DR-2 (the inline-resume half) is kept unchanged — nothing is deleted, skipped or relaxed. **TC-MDF-113(b)** (L2, `EodStallWatchdogTests.cs`): a never-completing step under a 10-min per-step bound and a configured **30 s** grace yields exactly one `RUN_HUNG` row naming the pre-cancel step with `bound: "step"`, `maxStepDurationSeconds` and `settleWindowSeconds: 30`, one alert naming the step and the grace, the `Hung run detected` line saying the same, `finished_at` = the bound instant, the ladder taking over from the attempt's end and the next attempt's healthy step `succeeded` with no second `RUN_HUNG` — 064's guarantee unweakened. **TC-MDF-113(c)** (L2): the shipped default is 5 s, it binds from `Ingestion:Scheduler:PostCancelSettleWindow`, it is two orders of magnitude below the shortest ladder rung (5 min) and both bounds, and `0` disables it — the hung verdict is then reached with **no** clock advance past a grace and the record claims none (`settleWindowSeconds: 0`, no window in the error or the alert). **Harness-only delta, premise-preserving, no assertion changed (064's TC-MDF-081(b) precedent):** every hung-path case that ends in `RUN_HUNG` now completes its tick through an `AwaitTickAsync(tick, host, window)` overload that waits for the watchdog's own `Settling …` line (the observable side of the armed grace) and then advances the fake clock by the whole window — the same premise these cases already make for the bounds; no expected value moved. Non-regression re-run green and unrelaxed: `SchedulerTests` (incl. 059's DR-1/2/3/5a/5b and TC-MDF-081), `EodStallWatchdogTests` (TC-MDF-079/098 in full) — 27/27, plus `EodChainTests`, `MorningChainTests`, `EodStaleServingTests`, `FundSchedulingCoverageTests`, `MacroIngestionTests` — 31/31; `dotnet build` 0 warnings. **Deploy sequencing (load-bearing, orchestrator):** merge freely, but **do not deploy** this change to the dev box until 064's live criterion — the **Monday 2026-10-26 05:30 UTC** chain, zero `RUN_HUNG` on shipped defaults — has been observed and recorded verbatim; deploying a watchdog behaviour change mid-observation would confound it. Live criterion: none of its own (the 059 edge-repair precedent); the standing observation is that the next genuine bound event produces exactly one row and one alert naming the step, recorded verbatim on the ticket. |
| v1.5.60 | 2026-10-26 | **The 065 contract half lands: `paidInCapital` under its own name, an honest price-derived `sharesOutstanding` beside it, and the DCF per-share divisor unblocked** (TKT-foundation-086; §10.9.11 corrected — the v1.5.36 "unit unresolved" paragraph closes to **money**, the store is renamed, the derivation lane and the precedence are recorded; `02` §3.1/§6.1 gain the renamed table plus `price_derived_share_counts`; `03` §3 gains the two-fact `shareCount` contract; no new dependency, no new source, zero additional requests). **The rename (USER DECISION D4, 2026-10-26):** `share_counts.shares_outstanding` → **`paid_in_capital`** in one forward-only migration (the 556 money rows keep their values under an honest name; entity property, store and every assertion follow; `ShareCountEntities.cs`'s "unit unresolved" paragraph closes), because 64/556 live rows carry sub-unit precision and **no** count can — `shareCount.sharesOutstanding` had been naming money since 058. **The honest count (USER DECISION D5, 2026-10-26):** the count comes from the payload the prices lane **already fetches** — İş Yatırım's `PD` (TL) beside `HGDG_KAPANIS`, `count = PD ÷ close`, both operands stored with the price-date as-of and `source_ref = isyatirim` in the additive **`price_derived_share_counts`**. Integral-or-withheld is the honesty core: `PD_MISSING`, `CLOSE_INVALID` and `NON_INTEGRAL_QUOTIENT` are three named, counted skips; a non-integral quotient is **never rounded** and no zero row is ever written (the 031/052 doctrine); the row is input-aware, monotonic (an older fact never regresses it, counted `stale`) and a withheld fact leaves a stored row alone. **The captures are the evidence and the doubles:** ASELS `1,714,560,000,000 ÷ 376 = 4,560,000,000`, THYAO `400,890,000,000 ÷ 290.5 = 1,380,000,000`, GARAN `539,700,000,000 ÷ 128.5 = 4,200,000,000`, while GARAN 06-10/07-10, FROTO and EBEBK are non-integral — both outcomes exercised without inventing a body, zero live requests. **The consumer:** `DcfBaselineGenerationJob`'s `share_count` gains the second read path with the **pinned precedence** — a statement `SHARES_DILUTED` count wins, the derived count supplies otherwise, both absent → `missingInputs` — and its ref records the source (`statement` / `price-derived`) with that source's own as-of (UXR-VAL-010); the compute path is unchanged (it already rejects `share_count ≤ 0`), so `fairValuePerShare` is served only on a real divisor (UXR-VAL-009). **The web side is the revert:** `lines.sharesOutstanding` returns to **count semantics** (a share-count label and a count unit) and stays an honest dash until a statements decoder emits `SHARES_OUT`; `lines.sharesDiluted` untouched. **Pinned non-goals:** per-share metrics and market-P/E consumption stay excluded (BR-MOV-010); no KAP-report count decode (a future lane, and it supersedes the derived count by the recorded precedence); no `fin_line_items` write from the price lane (UPSTREAM-1 stays owed). | FR-MDF-001 (the prices lane the derivation rides), FR-MDF-008 (both operands + source + as-of on every count), FR-MDF-011 (honest public surface — no money under a count name), FR-VAL-001/002 (the DCF baseline's `share_count` fact), UC-VAL-003, FR-RES-0xx (the balance-sheet supply), BR-MOV-010 (cited, unchanged); §10.9.11 (corrected), §10.9.19 (cited, unchanged); `02` §3.1/§6.1, `03` §3; test plan §26 (TC-MDF-114/115, v1.30) | L2 doubles only, never a live kap.org.tr/isYatirim request (the committed İş Yatırım price captures are the doubles; every negative case is a decoded-capture mutation). **TC-MDF-114** (`tests/Api.IntegrationTests/PriceDerivedShareCountsTests.cs`, 8 cases — the seven authored facts plus, from review round 2, the tolerance-symmetry case; the file's remaining fact is TC-MDF-115 (i)/(ii)): (i) the decoded fact carries `PD` beside the close (and the `HG_*` fallback carries none — honest absence); (ii) one row per instrument with `shares_outstanding = 4,560,000,000`, both operands, `price_date`, `source_ref`, `recorded_at`, the stored operands reproducing the stored count exactly; (iii) input-aware re-run (`unchanged`, `recorded_at` unmoved) and in-place repair on a changed market cap; (iv) an older fact never regresses the row (`stale`, row intact); (v) `PD_MISSING` / `CLOSE_INVALID` / `NON_INTEGRAL_QUOTIENT` each named and counted with no row written (GARAN 06-10/07-10, FROTO, EBEBK), the same instrument's integral 08-10 row deriving 4,200,000,000, and a withheld fact keeping the previously stored row; (vi) the production `prices` job over a single-member ASELS bound writes the identical row at one request — no new fetch. **TC-MDF-115** (`PriceDerivedShareCountsTests`, `DcfBaselineEndpointTests`, `StockPage.test.tsx`): (i) the balance-sheet block serves `paidInCapital` + `paidInCapitalAsOf` and `sharesOutstanding` + `sharesOutstandingAsOf`, the old `asOf` gone, statement lines and BVPS untouched; (ii) both null for an instrument with neither; (iii) with the statement silent the derived count supplies the divisor, the ref says `price-derived` with the row's `price_date`, the state leaves `missing_inputs`, and `POST …/dcf/compute` returns a `fairValuePerShare` that scales inversely with the divisor; (iv) a statement `SHARES_DILUTED` wins and the ref says `statement`; (v) both absent → `missingInputs` names `share_count`, `params` omits it, no fair value is served, the compute path still refuses with `share_count` named — and the SCR-005 row renders count semantics (TR/EN) and an honest dash (the round-2 web case renders the shipped null state itself: the dash, no count unit, no `0`). `MemberSecuritiesIngestTests` (rename deltas only), `StockFundamentalsEndpointsTests`, `DcfBaselineEndpointTests`, `DcfBaselineRegenerationTests`, `RealSourcePricesTests`, `EodChainTests`, `MorningChainTests` and the web `StockPage` suite re-run green. **Live criterion (orchestrator, post-merge):** one EOD prices run over the covered universe populates derived counts — ASELS serves `4,560,000,000` (the 065 evidence table's value, verbatim) — the balance-sheet payload shows `paidInCapital` (TRY) plus the derived `sharesOutstanding` with its as-of, and one `dcf-baselines` regeneration computes `fairValuePerShare` for a covered stock with the count's provenance in `params_json`. |
| v1.5.61 | 2026-10-26 | **The public-site identity map gains its production invocation surface: one registered `kap-public-identity` job, admin-triggerable and never a chain step** (TKT-foundation-087; §10.9.19 extended — the identity-population invocation subsection landed here; `03` §8's job-code catalog gains the code; no new route, no new config key, no new table, no migration, no dependency, no VYK-touching change). **The blocker closed, stated as recorded:** `KapPublicCompanyIdentityResolver.ResolveAsync` (v1.5.54) had **no production caller** — verified at `dev@22311da` and recorded on 080 and 082 — so `kap_public_company_map` never populated and a live `Source = kap-public` statements walk (v1.5.56) isolated every XU030 company as `COMPANY_IDENTITY_UNRESOLVED` and ingested nothing; the admin trigger only runs *registered* job codes, which is precisely what was missing. **The surface:** `KapPublicIdentitySyncJob` (`src/Ingestion/KapPublic/`, job code **`kap-public-identity`**), one `services.AddScoped<IIngestJob, …>()` line beside its lane peers, one catalog line — the standing per-job convention (§10.9.17), so the runner, the run ledger, the admin summary's every-registered-job list (UXR-MDF-003) and the admin trigger `POST /api/v1/admin/ingest/{job}/run` (UC-MDF-002) all discover it. **The pass:** binds `Ingestion:Statements:UniverseIndex`'s current constituents (shipped `XU030`; never a hardcoded list, never a new key), resolves them to instruments through `KapPublicStatementTargets.GetInstrumentIdsAsync`, and runs the merged resolver **unchanged** over 079's paced single-flight client — the job adds no `HttpClient`, no pacer and no parallelism, and issues one identity search per bound symbol. **Honest statuses, mapped:** `Ok` carries inserted → `written`, repaired-in-place → `rewritten`, `unchanged` → unchanged, and every unresolved symbol **named** in the per-symbol ledger stats under the 044 vocabulary (`COMPANY_IDENTITY_UNRESOLVED`/`AMBIGUOUS`) and **counted** in `skipped`, so a partly-mapped pass is `partial`, never a green run hiding a company with no identity; `SourceDown` is `failed` with the resolver's own reason and **nothing stored** (080's contract); a re-run is idempotent. **An empty bound is the honest named empty** (`EMPTY_BOUND_UNIVERSE` with the index code): zero requests, zero writes, nothing claimed resolved — not a failure (no source failed; the cold start is the EOD/morning chain's, and the statements lane names the same gap on its own walk) and never a fabricated zero. **Scheduling posture (the §10.9.11 shares-lane precedent):** admin-triggerable, **not** in `EodIngestChain.JobCodes` or `MorningIngestChain.JobCodes` — a public oid changes rarely, so a cadence is a recorded later decision, and the byte-unchanged chains are asserted by test. | FR-MDF-002 (the company-scoped fetch's identity precondition), FR-MDF-008 (the stored provenance the pass writes), FR-MDF-012 (named/counted skips, per-symbol `partial`), FR-MDF-016 (the run-ledger row), UC-MDF-002 (admin trigger), UC-MDF-004 (ops visibility via the summary); §10.9.19 (extended), §10.9.11 (the scheduling precedent, cited unchanged), `03` §8 (the catalog) | L2 doubles only, never a live kap.org.tr call (`tests/Api.IntegrationTests/KapPublicIdentityJobTests.cs`, **6 cases**, every one tagged `TC_MDF_116`; test plan §27, v1.35): (a) the job is registered exactly once in `IIngestionJobRunner.JobCodes` — the registry the admin summary enumerates and the admin trigger resolves through — and is **absent** from both shipped chains; (b) one runner pass over the bound `XU030` resolves ASELS through the committed `kap-public-identity-ASELS` capture and **stores** the mapping (oid + provenance + asOf + `recorded_at`), the run is `succeeded` with `written 1`, the ledger row carries the code, the counts and `trigger: admin`, and the pass issued exactly **one** identity request and **zero** byCriteria requests; (c) an unmappable bound symbol is named in the per-symbol stats under `COMPANY_IDENTITY_UNRESOLVED`, counted in `skipped`, logged, never stored, the pass continues and the run is `partial` (never `failed`, never a hidden skip); (d) a 500 identity answer is `failed` with `SOURCE_FETCH_FAILED` (and the symbol it stopped at) in the result and the ledger's `error`, **nothing stored**, one request only; (e) an empty configured bound is `succeeded` with `EMPTY_BOUND_UNIVERSE` naming the index, **zero** requests and zero rows; (f) a re-run is `unchanged` with `recorded_at` unmoved and one row, and flipping `Ingestion:Statements:UniverseIndex` to a wider index binds one more company with no code change. **Red recorded honestly:** this environment has **no Docker/Postgres**, so the suite cannot execute locally; the red that *was* observed is the seam's absence — the suite does not compile against the missing job (`CS0103` ×12, the deliverable being exactly that production surface) — and the red→green counts are recorded from **CI** on the ticket's PR. Non-regression: `KapPublicIdentityTests`, `KapPublicSourceTests`, `KapPublicStatementsIngestTests`, `EodChainTests`, `MorningChainTests`, `AdminEndpointsTests` (pre-listed additive deltas only — the admin summary's job list gains one row). **Live criteria (orchestrator, post-merge, never from CI):** one paced `kap-public-identity` run over the bound universe via the admin trigger on the dev stack, resolved/inserted/unresolved counts verbatim and unresolved symbols listed by name on the ticket (this also discharges 080's recorded live criterion — note honestly: 080's note says XU100 while the shipped statements universe is XU030, and a wider universe is a config value, never a code change); then 082's open criterion (the first paced `Source = kap-public` walk) sequences behind it. |
| v1.5.62 | 2026-10-27 | **The public-site decoder's IS/BS coverage: the debt/cash core and the income ladder onto canonical codes, capture-first — and the repeated-passthrough rule that stopped every live balance sheet from storing** (TKT-foundation-088; §10.9.19 extended — the IS/BS-coverage subsection landed here; `StatementMapper`, the VYK/XBRL decode paths, the equity-statement drop and the `SUB_PERIOD_COLUMN`/cumulative semantics are untouched; no new dependency, no request path, no schema change). **The gap, stated as the live run recorded it:** the first paced `kap-public` walk wrote 62 statements across 7 companies — **60 CF / 2 IS / 0 BS** — so `GET /stocks/{symbol}/dcf` for TUPRS/TAVHL answered `state: "missing_inputs"` naming `['debt','cash']`, revenue on public-site financials periods was null, and `metrics-recompute` wrote 0 rows. **The diagnosis, recorded before the fix and unchanged by it (it corrects the ticket's premise):** the captured live bodies were **not** undecodable — each decodes 146–174 balance-sheet lines and 66–94 income-statement lines — but only **2–6** mapped to a canonical code, and the statement row was then **quarantined whole** by `DUPLICATE_LABEL_CONFLICT` on `OTHER_*` passthrough codes (pre-fix conflicts per capture: TÜPRAŞ BS 5 codes × 2 columns and IS 3; MİGROS BS 11 and IS 1; ASELSAN BS 18 and IS 2). The balance sheet legitimately repeats `Ticari Alacaklar`, `Peşin Ödenmiş Giderler`, `Finansal Yatırımlar`, `Türev Araçlar` and the `ARA TOPLAM` sub-total across its current and non-current blocks; the income statement repeats `Ana Ortaklık Payları` across its profit-allocation and comprehensive-income sections. **The map extension, evidenced capture by capture (the user requirement: the DCF's `debt` and `cash` come from the KAP reports with FCF's provenance):** the income statement gains `ESAS FAALİYET KARI (ZARARI)`→`EBIT`, `Genel Yönetim Giderleri`→`SGA`, `Finansman Giderleri`→`FIN_EXP`, `SÜRDÜRÜLEN FAALİYETLER VERGİ ÖNCESİ KARI (ZARARI)`→`PRETAX_INC`, `Sürdürülen Faaliyetler Vergi (Gideri) Geliri`→`TAX_EXP`, `DÖNEM KARI (ZARARI)`→`NI` (ASELSAN 2025 Yıllık, body `1561039`); the balance sheet gains the debt/cash core `Kısa Vadeli Borçlanmalar`→`ST_DEBT`, `Uzun Vadeli Borçlanmalar`→`LT_DEBT` (financial borrowings only — never trade payables, never operating liabilities, TÜPRAŞ 2025 Yıllık body `1554106`) plus `TOPLAM ÖZKAYNAKLAR`→`TOTAL_EQUITY`, `TOPLAM DÖNEN VARLIKLAR`→`CUR_ASSETS`, `TOPLAM KISA VADELİ YÜKÜMLÜLÜKLER`→`CUR_LIAB`. Each target reuses the assignment the VYK concept map already makes for the equivalent IFRS element, so `02` §6.1 is not forked; every entry names the committed body that carries its spelling (`KapPublicLabelRule.Provenance`, the closed set `KapPublicReportDecoder.CaptureNames`), and §22's TC-MDF-107 (i) is generalized from a single-capture equality to that set (user-approved 2026-10-27 — its invariant is unchanged, nothing previously pinned is unpinned). **The duplicate rule, extended for `OTHER_*` only (user-approved 2026-10-27):** an unequal second value for a **passthrough** code within one statement row is kept beside the first under a counted, deterministic `{code}_{n}` with one `DUPLICATE_LABEL_DISAMBIGUATED` finding per kept line — both values retained, nothing overwritten, the row **not** quarantined; equal values still collapse idempotently, **canonical** codes keep the existing conflict→`CONFLICTING_VALUE` quarantine unchanged, and first-occurrence passthrough codes stay byte-identical so v1.5.55's 1643141 goldens hold unamended. Metrics ignore `OTHER_*` either way, so no canonical figure is invented. **The routed gap, never guessed:** the current portion of long-term borrowings (`Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları`, 12.863.610 thousand TL beside `Kısa Vadeli Borçlanmalar` 8.957.100 in TÜPRAŞ FY2025) is a separate presentation line and stays an `OTHER_BS_*` companion; the DCF's `debt` = `ST_DEBT` + `LT_DEBT` is the two borrowings lines only, and aggregating presentation lines into a canonical code is a modelling decision routed to the planner/user. **Captures:** three new bounded-trim doubles committed (`kap-public-report-1554106` TÜPRAŞ 2025 Yıllık, `-1561039` ASELSAN 2025 Yıllık — which also discharges 081's open annual-body criterion — and `-1648240` MİGROS 2026-06), labeled as trims with their kept/total row counts. **Recorded finding on this head, not fixed here:** TÜPRAŞ files separate income-statement and comprehensive-income tables, so its OCI rows type as `IS` and are named `NO_CANONICAL_LINES` (44) — pre-existing and unchanged. | FR-MDF-002/003 (statement ingest — the IS/BS half), FR-MDF-008 (provenance `kap-public` on the stored debt/cash facts), FR-MDF-012 (quarantine/honest gaps), FR-VAL-001 (the DCF baseline's `debt`/`cash` inputs), FR-RES-005..018 (served financials), `02` §6.1 (one definition of the chart of accounts); §10.9.19 (extended), §10.9.11/§10.9.15/§10.9.17 (cited, unchanged) | **TC-MDF-117** (test plan §28, v1.36) implemented as six cases in `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs`, every one tagged `TC_MDF_117`, L2 doubles only — the committed bodies are the entire input, **zero live kap.org.tr requests**, every negative case a mutation of a committed body: (a) each body decodes to six payload rows with no conflicting group and the store writes **all six** (the red case — the live walk's 0-BS/2-IS symptom); (b) TÜPRAŞ's FY2025 balance sheet stores `ST_DEBT`/`LT_DEBT`/`CASH` (thousands → single TL) with its canonical surface exactly the six mapped codes, trade payables passthrough, and the current-portion line never merged into `ST_DEBT`; (c) ASELSAN's FY2025 income statement stores the ladder `REV`/`COGS`/`GROSS_PROFIT`/`SGA`/`EBIT`/`FIN_EXP`/`PRETAX_INC`/`TAX_EXP`/`NI` with the filer's own sub-total companion beside it; (d) repeated passthrough labels keep both values under a counted, deterministic `{code}_n` (`ARA TOPLAM`…`_2`, `Türev Araçlar`…`_4`), 22 counted disambiguations, no conflict, no quarantine, identical on re-decode; (e) a **mapped** code's unequal duplicate still conflicts per column and quarantines that row only (`CONFLICTING_VALUE`, the other four rows land); (f) every map entry's provenance is a committed capture and its spelling is present in the body it names. **Red→green, executed (Docker/Testcontainers available in this environment):** with the passthrough branch restored to the pre-ticket conflict behaviour **5 failed / 21 passed** — (a) four of six rows carrying `ConflictingItemCodes` and `Inserted` **Expected 6, Actual 2**, (b) the TÜPRAŞ balance sheet quarantined, (c) the ASELSAN income statement quarantined, (d) conflicts on `OTHER_BS_*`, (e) 44 conflicts instead of 2; with the map extension alone reverted **4 failed / 22 passed** (`KeyNotFoundException: 'ST_DEBT'`, `'SGA'`, the provenance case, (e)), so both halves are load-bearing; **26/26 green** on the reviewed head. Non-regression **51/51 green and unrelaxed:** `KapPublicStatementsIngestTests`, `RealSourceStatementsTests`, `StatementNormalizationTests`, `StatementVersioningTests`, `RealSourcePricesTests` (TC-MDF-063's catalog `Names`/`Build` enumeration — the additions are consistent), `KapPublicSourceTests`; `dotnet build` 0 warnings. **Live criteria (orchestrator, post-merge, paced, never CI):** one `Source = kap-public` statements pass writing IS **and** BS rows with per-type counts verbatim; the ASELSAN `1561039` annual decode's own OCF/CAPEX goldens (081's open criterion); TUPRS FY2025 `fin_line_items` gaining `ST_DEBT`/`LT_DEBT`/`CASH`. |
| v1.5.63 | 2026-10-27 | **The per-share DCF consumer completes on public-site statements, and the derived lanes describe the latest stored session instead of a priceless date** (TKT-foundation-089; §10.9.19 extended — the consumer subsection landed here; §10.9.13 cited and **applied**, not amended; no DCF formula, growth, threshold or precedence change, no decoder change (088's scope), no cumulative-vs-period semantics change, no identity/UA work, no UI structural change, no per-share-metric or market-P/E share-count consumption; no new dependency, no schema change, no request path). **The verification (recorded because the ticket's premise was largely wrong):** with v1.5.62's decode landed, the declared consumer chain was already correct — one decode + store writes six `kap-public` statements, one `dcf-baselines` pass produces `missingInputs: []` with `debt` 37,385,875,000 / `cash` 107,237,211,000 / `base_fcf` 30,346,395,000 / `share_count` 2,500,000,000 and correct refs, `GET /stocks/TUPRS/dcf` answers 200 without `missing_inputs`, `POST …/dcf/compute` serves a `fairValuePerShare`, and `GET …/financials` serves revenue/NI for FY2024/FY2025 with `ebitda` honestly null (that capture's IS carries neither `EBITDA` nor `EBIT` + `DEPR_AMORT` — the existing seam's own honest-null branch, no line added). The ticket is therefore verification plus one repair, matching its own framing, and **no DCF code changed**. **The repair (§10.9.13 applied to two more lanes):** `metrics-recompute` and `medians` used `request.Date` blindly, so a request for a day whose covered universe owned no stored prices wrote nothing and reported a green `written: 0, unchanged: 0` — the 2026-10-26 live reading that kept 083's criterion open, and indistinguishable from "all current". Both lanes now resolve their day through the **one** implementation of the stored-prices-as-truth rule, `MarketSnapshotStore.ResolveLatestStoredTradingDayAsync` — exposed as a static entry beside the instance method so `MetricsEngine.ResolveComputeDayAsync` / `SectorMedianEngine.ResolveComputeDayAsync` delegate to it rather than re-deriving a day: latest trading day ≤ `request.Date` with stored prices in the covered universe, no weekday assumption, no fabricated day, and with no stored price at all the honest empty (nothing written, nothing claimed). Job constructors, DI composition and the fixture harness are unchanged, so no consumer outside the lanes moved. **Consumed as stored, never resolved here:** 088's DR-2 (the current portion of long-term borrowings stays `OTHER_BS_*`, so `debt` excludes it — a partial borrowings figure) and DR-1 (`TOTAL_EQUITY` read NCI-inclusive, unfiltered); both stay open on the planner/user's desk. | FR-VAL-001/002/003 (baseline supply + per-share value), UC-VAL-003, FR-MDF-008 (per-fact provenance: source/as-of/restated), FR-MDF-013/014 (recompute over the mixed world, the day it describes), FR-RES-005..018 (served financials for `kap-public` periods), FR-MOV-006/BR-MOV-010 (cited, unchanged); §10.9.19 (extended), §10.9.13 (cited and applied), §10.9.11 (cited, unchanged); `03` §3/§8 (cited, unchanged); test plan §29 (v1.37) | **TC-MDF-118** (test plan §29, v1.37), implemented as three cases, L2 doubles only, **zero live requests**: `tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs` — `TC_MDF_118_the_per_share_dcf_completes_on_the_decoded_public_site_statements` (the completion: `state` ≠ `missing_inputs`, empty `missingInputs`, the eight-parameter set with `debt` 37,385,875,000 / `cash` 107,237,211,000 / `share_count` 2,500,000,000, the three statement refs `source: "statement"` + `restated: false` + the as-of and the count's `price-derived` ref with its `price_date`, the served values cross-checked against the **stored** balance sheet's `ST_DEBT`+`LT_DEBT`/`CASH` and the CF statement's `OCF − |CAPEX|`, every statement `source_ref = kap-public`, no current-portion line in `debt`, and a positive `fairValuePerShare` from `POST …/dcf/compute`) and `TC_MDF_118_the_cash_flow_only_world_leaves_debt_cash_and_revenue_missing` (the recorded red as the same class's control: `state: "missing_inputs"`, exactly `['cash','debt']`, `debt`/`cash` absent from `params` with `base_fcf` present, no fair value, `400` naming `debt`, revenue/NI honestly null); `tests/Api.IntegrationTests/KapPublicRecomputeTests.cs` — `TC_MDF_118_the_derived_lanes_describe_the_latest_stored_session_not_a_priceless_date` (a Saturday request after the last stored price: **416** metric rows written for the resolved session and none for the requested date, every row stamped `CanonicalMetrics.Version`, the medians' rows on the same session). **Red→green, executed (Docker/Testcontainers available):** the day-resolution case is red on the pre-fix behaviour at **`Expected: 416, Actual: 0`** (observed by restoring `var day = request.Date;`); the CF-only control is green by construction — it pins the pre-088 shape the completion case is measured against — and is stated as such, not hidden. Green on the reviewed head: `DcfBaselineEndpointTests` 10/10 (incl. TC-VAL-005..008), `KapPublicRecomputeTests` 7/7, TC-MDF-118 filter 3/3. Non-regression **233/233 green and unrelaxed:** `DcfBaselineRegenerationTests` + `DcfFinancialSectorTests` + `StockFundamentalsEndpointsTests` 22/22, `MetricsEngineTests` + `ProfileMetricServingTests` 8/8, `MetricsRecomputeIdempotencyTests` + `RestatementMetricsTests` + `StatementVersioningTests` + `MorningChainTests` + `EodChainTests` + `RegimeBreakTests` 26/26, `Screener*` + `SavedScreenEndpointTests` + `AdminEndpointsTests` + `StockOverviewAndValuationEndpointsTests` + `Xc014PerformanceBudgetTests` + `StatementsAndEventsIngestionTests` 102/102, `Core.UnitTests` 75/75; `dotnet build` 0 warnings. **Live criteria (orchestrator, post-merge, paced, never CI):** `GET /stocks/TUPRS/dcf` (and TAVHL) ≠ `missing_inputs` with `debt`/`cash` in `params` and their refs pointing at `kap-public` statements, `fairValuePerShare` served; the financials payload's revenue/NI for `kap-public` periods; and `metrics-recompute`/`medians`/`market-snapshot` writing current-period rows stamped with the current `formula_version` — discharging 083's open criterion. |
| v1.5.64 | 2026-10-27 | **Cumulative-vs-period (YTD) column semantics verified from a real interim report, and the honest fail-safe when no column attests a period** (TKT-foundation-090; §10.9.19 extended — the period-semantics subsection landed here; the IS/BS label map, the duplicate rule, `StatementMapper`, the VYK/XBRL decode paths and the DCF consumer are untouched; no schema change, no new dependency, no request path, no new capture). **The verification (recorded because the ticket's premise is unfounded on the interim path):** the MİGROS 2026-06 body (`1648240`, `Periyot 2` → `6 Aylık`) declares its IS/CF main columns as `Cari Dönem 01.01.2026 - 30.06.2026` — six-month cumulative — and its income-statement (and OCI) tables carry the discrete quarter as a **separate** `Cari Dönem 3 Aylık 01.04.2026 - 30.06.2026` column that the decoder already skips as `SUB_PERIOD_COLUMN`; the **cash-flow table of that filing has only the two declared-range columns** (current and prior), no quarterly sibling, so the CF figure's span is attested by its own declared range. The decoded figures are the cumulative cells (`REV` 241.298.039 not 124.399.203; `NI` 1.078.256 not −644.936; `OCF` 17.837.860, `CAPEX` −6.297.863, FCF 11.539.997), so the decoder is **not** substituting a three-month cell for a cumulative one at decode time — it decodes the column whose declared duration is the report's own and stores it `Q` at the filing's own `periodEndDate` (the store carries no span, so the served label remains the `FY`/`Q` vocabulary; the span-labelled display gap is routed to 057's surface). **The annual bodies are twelve-month columns and the FY basis is unaffected:** TÜPRAŞ `1554106` (`01.01.2025 - 31.12.2025`) gives `49.464.734 − |19.118.339| = 30.346.395` thousand TL (₺30,35bn — the recorded FY2025 FCF, matching rather than contradicting the live figure), ASELSAN `1561039` gives `48.901.969 − |38.168.467| = 10.733.502`; both type `FY` because a twelve-month duration attests them, and every annual golden of v1.5.55/v1.5.62 stands verbatim. **The one real defect, found and fixed:** a report self-declaring a period while **no** column declared a readable date range had undetermined semantics, and the pre-ticket decoder typed its instant (balance-sheet) rows `FY` silently — the shape that could put a half-year `debt`/`cash` into a DCF annual basis. The decoder now raises exactly one counted **`PERIOD_SEMANTICS_UNDETERMINED`** finding per report (a new code, joined to §10.9.19's closed reason set) and types **every** statement row `Q`, in both the annual-declared and the interim-declared variant (an interim's `Q` must rest on evidence too, not just on the absence of a danger); flow tables whose columns cannot be attributed at all still invent nothing (`COLUMN_UNATTRIBUTED` drops) and the unattested balance sheet is still stored, honestly as an interim. Its **incidence is unmeasured** — no captured report lacks declared ranges, so the case is a mutation — and in such a report the 2025-12-31 annual comparative balance sheet now types `Q` and drops out of the FY-only balance-sheet read (the safe direction, named). **Span provenance added (additive, decode-result only):** `KapPublicLine.PeriodStartDate`/`DurationMonths` are populated from the resolved column, so the filing's own span is preserved **on the decode result** and available to the decode's own consumers and tests; instant (balance-sheet) lines carry `null`/`null`, and `null` is honest absence, never a default. The span is not stored or served (`Group.ToFact()` drops it, `FinancialStatement` has no field, the financials payload emits only `periodType`/`fiscalYear`/`periodEndDate`). **Repair posture (FR-MDF-015):** already-written interim rows are never rewritten in place; the route for any wrong stored interim row is an explicit cursor reset plus a re-walk for the affected companies (the v1.5.56/v1.31 condition), executed as a recorded step. **DR-1/DR-2 stay fenced and untouched** (`TOTAL_EQUITY` read NCI-inclusive as stored; `debt` = `ST_DEBT` + `LT_DEBT` narrow, no `OTHER_BS_*` aggregation). | FR-MDF-002/003 (statement ingest — the period half), FR-MDF-008 (per-fact provenance incl. the decoded span), FR-MDF-015/NFR-MDF-002 (historical immutability — no silent rewrite), FR-VAL-001 (the baselines this protects), 02 §3.1 (period semantics); §10.9.19 (extended), §10.9.11/§10.9.13 (cited, unchanged) | **TC-MDF-119** (test plan §30, v1.38) implemented as four cases in `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs`, every one tagged `TC_MDF_119`, L2 doubles only — the three bodies v1.5.62 committed are the entire input, **zero live kap.org.tr requests**, every negative case a mutation of a committed body (declared ranges stripped; `Periyot:2` → `Periyot:4`): (i) the interim column is the declared cumulative range, never a quarter — header `6 Aylık`/6 months; the CF table has exactly **two** columns (no `3 Aylık` sibling), its main column verbatim `Cari Dönem 01.01.2026 - 30.06.2026` with `PeriodStart` 2026-01-01 / `PeriodEnd` 2026-06-30 / `DurationMonths` 6 and no skip reason; **four** three-month columns all skipped `SUB_PERIOD_COLUMN` (current + comparative in both IS tables), the cumulative-vs-three-month cell arithmetic, and every decoded line carrying the six-month span; (ii) the annual columns are twelve months and their FCF is the year — both annual bodies, 12-month columns, `FY` typing, the two FCF goldens, twelve-month spans; (iii) a cumulative interim is stored `Q` and the FY filter cannot reach it — six stored rows all `Q`, the store's thousands→TL scaling, and the consumer's `PeriodType == "FY"` selection **empty**; (iv) a period no column attests is named once (`PERIOD_SEMANTICS_UNDETERMINED`, count 1) and fails safe to `Q` in both declaration variants, **no** statement typed `FY`, both balance sheets (2026-06-30 and the 2025-12-31 comparative) still stored as `Q`, no IS/CF fact invented (`COLUMN_UNATTRIBUTED` instead). **Red→green, honestly** (Docker/Testcontainers available, so the cases really executed): because the verification found the decode **correct**, (i)–(iii) are green by construction and recorded as such — **no red was manufactured**. (iv) carries the ticket's real red: with the guard forced off (the pre-ticket behaviour) it fails at `Assert.DoesNotContain() Failure: Filter matched in collection`, the collection showing `StatementFact { PeriodType = FY, PeriodEndDate = 06/30/2026 … StatementType = BS }` beside the `12/31/2025` row — the half-year balance sheet typed `FY`, exactly the danger closed; green with the guard restored. **Green:** `KapPublicReportDecoderTests` **30/30** (26 pre-existing 106/107/108/117 cases + the four new), `dotnet build` 0 warnings / 0 errors. Non-regression **88/88 green and unrelaxed:** `KapPublicStatementsIngestTests`, `StatementVersioningTests`, `DcfBaselineEndpointTests`, `KapPublicRecomputeTests`, `RealSourceStatementsTests`, `StatementNormalizationTests`, `StockFundamentalsEndpointsTests`, `DcfBaselineRegenerationTests`, `RealSourcePricesTests`, `KapPublicSourceTests`. **The four recorded suspect FCF numbers** are accounted for on the ticket, as far as the committed bodies allow: FY2025 ₺30,3bn is the body's own arithmetic (above); TUPRS Q3-2025 ₺36,9bn and Q2-2026 ₺120,5bn are **unattributed** — no TUPRS interim body is committed, so the mechanism (a declared cumulative column decoded as declared) is verified generically on MİGROS/ASELSAN interims but not on those specific filings, and their Q4-delta sketch is a plausibility note, not evidence; KCHOL FY2025 −₺204,9bn is an `FY`-typed annual row (by typing, a twelve-month column) whose cell-by-cell attribution needs a committed KCHOL body, so a mapping/label defect on that filer's line set stays a live alternative to genuine magnitude. What this ticket does establish is that no stored interim figure comes from a three-month column while claiming to be cumulative — the YTD-vs-quarter hypothesis is disposed of on the interim path, and the mapping/label hypothesis is not. Any genuinely wrong stored interim row is repaired by the recorded re-walk above. Live criteria (orchestrator, post-merge, paced): after a `kap-public` walk, no `FY`-typed half-year row in `financial_statements`, and the `/financials` period carrying `2026-06-30` reads `periodType` `Q` — the store's label vocabulary (a span-labelled display is the routed consumer ticket, not a criterion here); and — if a re-walk is decided — its before/after counts recorded verbatim on the ticket. |
| v1.5.65 | 2026-10-27 | **Comma-coded identity matching: `cmpOrFundCode` is a token list, and a shared-token ambiguity stays unresolved and named** (TKT-foundation-091; §10.9.19 extended — the comma-token subsection landed here; the resolver's classification rule changes, and nothing else does: no walk/decode change, no VYK identity change, no schedule, no quarantine change, no new route, no new config key, no new table, no migration, no dependency, and **zero live requests**). **The gap, as run 301 recorded it:** the first live `kap-public-identity` pass mapped **22 of 30** XU030 companies and isolated **8** (BIMAS, ENKAI, GARAN, ISCTR, KRDMD, SISE, VAKBN, YKBNK) as `COMPANY_IDENTITY_UNRESOLVED` — no statements, no DCF inputs, no financials for them — because the site serves those companies' codes **comma-joined** (`GARAN` → `"garan,tgb"`, several candidate rows) and the exact-equality rule could never match. **The fix — the 046 rule class applied to this source's field:** a company hit (`searchType` `C`) is a candidate when the bound symbol is one of the **comma-split tokens** of `cmpOrFundCode` (parts trimmed, `OrdinalIgnoreCase`; an empty token never matches; a code that merely contains the symbol inside a longer token is not a match), which makes the exact-equality path the **degenerate case** and leaves 080's ASELS goldens unamended. **The deliberate divergence from 046, pinned:** where **several distinct company candidates** share the token the symbol is `COMPANY_IDENTITY_AMBIGUOUS` — named, counted, **nothing stored**, the refusal naming the candidates — **never** 046's last-wins/first-wins: that list is a directory of record, this answer is a search result list, and a tie-break here would be a guess wearing a rule's clothes. No tie-break is applied (not arrival order, not title proximity, not "code starts with the symbol"); a tie-break the source itself evidences, if live captures ever show one, is **routed to the user before implementation**. **Doubles, honestly labelled:** the two comma-coded answers the rule is pinned on (`kap-public-identity-garan-comma-coded`, `-isctr-comma-coded`) are **constructed** (provenance `Constructed`, enforced by the catalog's own label rule), because this ticket issued zero live requests — each label states which facts are the run-301 observation and which are derived, and which of the eight symbols resolve vs. land ambiguous stays **unmeasured** (the live criterion, where ambiguity is a legitimate recorded outcome, not a failure to force). **Also recorded here, NOT fixed here:** the WAF finding of the 087 wave — the code-default UA's parenthetical (`…DegerliBot/0.1 (kap.org.tr public-site reader; polite, paced, single-flight)`) is **HTTP 666** on every route while the comment-free `…Chrome/124.0 Safari/537.36 Degerli/0.1` passes (probe-verified, one request at a time) — is recorded with its verified value, but the correction needs `src/Api/appsettings.json` (without it the default is inert: configuration wins) and the two `KapPublicSourceTests` assertions pinning the old token, **neither of which is in this ticket's scope**; it is routed as a follow-up with its own pin, and the politeness posture is unchanged. | FR-MDF-002 (the identity precondition of the company-scoped fetch), FR-MDF-008 (provenance of the stored mapping), FR-MDF-012 (named/counted skips — ambiguity is a distinct named outcome, never a guessed mapping), FR-MDF-016 (the ledger row the job writes); §10.9.19 (extended), §10.9.11/§10.9.15 (046's directory rule cited as the rule *class*, its last-wins posture deliberately not carried over), §10.9.17 (the job convention, cited unchanged); test plan §31 (TC-MDF-120, v1.39) | **TC-MDF-120** (test plan §31, v1.39) implemented as six cases, L2 doubles only, **zero live kap.org.tr requests**, every negative case an in-memory mutation of a committed double: `tests/Api.IntegrationTests/KapPublicIdentityTests.cs` — (a) a comma-coded single-company answer resolves through the token rule (the company's oid stored, the fund hit spelling the bare token never binds), (b) several company candidates sharing the token are `COMPANY_IDENTITY_AMBIGUOUS` — named, counted, nothing stored, both candidates named in the refusal, and a second pass does not drift into a pick, (c) the rule is token equality, not substring, with trimmed parts and case folding, and a single-token code is the degenerate exact case, (d) a mixed bound resolves what it can and continues past the ambiguous symbol (per-company isolation, the resolvable two stored, three identity requests, zero byCriteria), (e) the committed single-token ASELS capture is unchanged by the rule; `tests/Api.IntegrationTests/KapPublicIdentityJobTests.cs` — (f) through the **production job runner**, the comma-token rule is reachable in production and its two outcomes reach the ledger as two different words: the comma-coded company is `written`, the ambiguous symbol is named `COMPANY_IDENTITY_AMBIGUOUS` in the per-symbol stats and counted in `skipped`, the run is `partial`, one identity search per bound symbol. **Red→green, executed (Docker/Testcontainers available):** on the pre-ticket head **5 failed / 1 passed** — (a) and (c) at `Expected: 1, Actual: 0` on `Resolved`, (d) at `Expected: 2, Actual: 1` on `Resolved` (ASELS and ISCTR stay resolvable; the ambiguous GARAN is the missing one), (b) at `Expected: "COMPANY_IDENTITY_AMBIGUOUS", Actual: "COMPANY_IDENTITY_UNRESOLVED"` (exactly run 301's observed state), (f) at `Expected: 1, Actual: 0` on `Written` — the count assertion precedes the reason-code assertion, so the ledger mismatch is never the first failure; (e) green by construction, recorded as such rather than manufactured red — and **6/6 green** with the rule. (The (d)/(f) attribution here was wrong as first recorded and was **measured** in review round 2: the resolver file alone reverted to `origin/dev`, the `TC_MDF_120` filter re-run, then the file restored — see the ticket's evidence section for the per-case assertion and values.) Non-regression **90/90 green and unrelaxed:** `KapPublicIdentityTests` (the five 080 cases), `KapPublicIdentityJobTests` (the six 087 cases), `KapPublicSourceTests`, `KapPublicStatementsIngestTests`, `KapPublicReportDecoderTests`, `RealSourcePricesTests` (the catalog `Names`/`Build` contract, the two additions consistent); `dotnet build` 0 warnings. |
| v1.5.66 | 2026-10-27 | **The kap-public code-default User-Agent corrected to the WAF-passing honest string** (TKT-foundation-091's second half; orchestrator decision 2026-10-27, same branch/PR as v1.5.65; §10.9.19's UA bullet amended, no walk/decode/config-key/schema change, **zero live requests**). **The finding (087 live wave, probe-verified one request at a time):** the lane's shipped UA `…Chrome/124.0 Safari/537.36 DegerliBot/0.1 (kap.org.tr public-site reader; polite, paced, single-flight)` — honest, but with a parenthetical comment — is answered **HTTP 666 on every route**, while the same honest token without the comment passes (HTTP 200 on `search/combined` and `byCriteria`). The lane was therefore running on a dev-stack config override while the *shipped* config spelled the blocked string and the code default was inert behind it (configuration wins). **The correction, three parts, all landed:** (i) `KapPublicSourceOptions.DefaultUserAgent` becomes `Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 Degerli/0.1` — honest self-identification, browser-shaped, **no explanatory parenthetical comment**; (ii) the shipped `src/Api/appsettings.json` `Ingestion:KapPublic:UserAgent` is aligned to that same value, so the default is no longer shadowed by a stale override (the dev-stack override is now redundant — its removal is an orchestrator ops step, not a code change); (iii) `KapPublicSourceTests` `TC-MDF-103(a)`/`(d)` now pin the **corrected** default (still config-equivalence pins: shipped config == code default, and the on-the-wire request carries the honest token — now `Degerli/0.1`) plus the blocked-**marker** pins `DoesNotContain("DegerliBot")` + `DoesNotContain("public-site reader")`, so the blocked spelling cannot silently return (**corrected by review round 2:** this row originally said "plus `DoesNotContain("(")`", which is wrong and was never implementable — the corrected UA legitimately contains `(X11; Linux x86_64)` and `(KHTML, like Gecko)`, so a punctuation ban would fail on the correct value; the shipped assertions are `Contains("Mozilla/5.0")`, `Contains("Degerli/0.1")`, `DoesNotContain("DegerliBot")`, `DoesNotContain("public-site reader")`, and test plan v1.41 records the same correction). **Scope, honestly:** the planner's `scope:` for 091 listed neither `src/Api/appsettings.json` nor `tests/Api.IntegrationTests/KapPublicSourceTests.cs`; the ticket's `scope:` is **amended** (recorded on the ticket) because without both files the fix is inert or red — an omission in the planner's list, not a widened ticket. **The politeness contract is untouched:** ≥ 3 s start-to-start pacing, one flight gate, single-flight, no new request shape, no route, no schedule. The WAF finding is recorded as the *shape* rule it is: the honest token stays, the explanatory parenthetical goes. | FR-MDF-002 (a lane that cannot reach the site fetches nothing), NFR-MDF-001 (a blocked default is a silent ledger hole — now impossible to reintroduce), §10.9.19 (UA bullet amended), §10.9.19 v1.5.53 (the honest-UA intent, carried forward), TKT-foundation-087 (the live finding this closes) | `KapPublicSourceTests` — `TC-MDF-103(a)` (shipped config binds the corrected default; `Mozilla/5.0` and `Degerli/0.1` present, and neither blocked marker — `DegerliBot`, `public-site reader` — present) and `TC-MDF-103(d)` (the WireMock stub matches **only** on `*Degerli/0.1*`, so a request without the token 404s and the read would be a source-down — a real on-the-wire assertion — and the recorded handler shows the same token), re-run green; non-regression with the rest of the lane's suite (`KapPublicIdentityTests`, `KapPublicIdentityJobTests`, `KapPublicStatementsIngestTests`, `KapPublicReportDecoderTests`, `RealSourcePricesTests`) green; `dotnet build` 0 warnings. **No live request was made by this correction** — it lands a value 087 already probe-verified. |
| v1.5.67 | 2026-10-27 | **Quarantined-cursor recovery: a transport-failure `DISCLOSURE_DETAIL_UNFETCHABLE` detail is replayed once, bounded and persisted, so a transient outage stops bounding a company's history forever** (TKT-foundation-092, the 063 stale-cursor-reset doctrine one level down; §10.9.19 extended — the quarantine-recovery subsection landed here; no decoder change, no identity-resolver change, no new table, **no migration**, no new request path, no new config key, no new dependency, **zero live requests**). **The verified root cause this repairs (runs 302/303, recorded on 082 and on 092):** after ~30 successful exports, report details failed in **transport bursts** (~180 quarantines, `failed in transport` / `bodyReceived: false`, AKBNK 102 in pass 1; the same exports fetch fine moments later — suspected per-connection throttling or container MTU, **undetermined** and explicitly not diagnosed here). The walk marks a failed detail **completed** (`state.With(index)`), so its window closes and the watermark advances past it: under the recorded 063/046 terminal-quarantine rule those windows could **never** be re-walked, and three companies' Q2-2023/H1-2024 history was silently bounded by an outage. **The rule, in four pinned clauses.** (i) **Only a transport failure is replayable**, and the class is drawn from **what the read carried**, never from the status word: `IsSourceDown` with **no body ever received** (`RawBody` absent — the live `bodyReceived: false` signature) registers a replay; a **non-success status carrying a body** (the source answered about *this* index; excerpt retained verbatim) and a **shape verdict** (`UNPARSEABLE_PAYLOAD`) are **terminal** — replaying either would be a retry ladder against a source that already spoke, or ~900 KB spent to re-learn the same answer, and shape replay is left as its own future recorded decision. (ii) **The state is the lane's own persisted cursor, and no migration is needed:** `kap-public-statements-cursor`'s `coverage_metadata.notes` JSON gains two **additive** arrays — `quarantined` (backlog owing its one replay) and `retried` (the once-only tombstone) — written always (even empty); an **absent** array reads as an empty one, so a pre-092 row keeps its position and simply has no backlog and is never reinterpreted as "all retried"; `CloseWindow` carries both markers through every window close (a position change is never a reason to forget a debt). (iii) **It rides the ordinary pass with the pass's own rules:** the backlog is drained at the **start** of a walk, before the window loop, through 079's paced single-flight client and admitted by the same `KapPublicWalkBudget` — a replay costs a **detail fetch** like any other fetch (so `WALK_DETAIL_CAP` bounds it; a replay needs no list page, so the page ceiling is untouched); a bounded replay sets `truncated`, leaves position and backlog untouched, and the next pass resumes the backlog; the backlog is finite and strictly shrinking (every attempt spends a tombstone), so deferral is bounded by the number of indices, never unbounded. (iv) **Every attempt is named and counted (NFR-MDF-001):** success writes the facts under `kap-public` and counts **recovered**; a second failure (or a non-report body) re-quarantines **terminally** with its evidence and counts **still-quarantined**; the walk result gains `ReplaysAttempted`/`Recovered`/`StillQuarantined`, the job logs them per company (with the remaining backlog) and in the run summary (`quarantine-replays N (recovered R, still-quarantined S)`), and **no third attempt ever fires** — in this process or a later one, because the tombstone is persisted. The **explicit cursor reset stays the escape hatch** for anything beyond the one automatic replay (v1.5.56/v1.31, unchanged). **Pinned non-goals:** the quarantine evidence itself is unchanged (the verbatim bodies stand), no retry ladder/scheduler change, no VYK-lane change, and no root-causing of the transport bursts (an ops finding, routed). | FR-MDF-002 (statements ingest — the repair path), FR-MDF-010/011 (achieved-depth honesty: a recovered window updates the depth row against the same configured request), FR-MDF-012 (isolation — recovery is named and counted), FR-MDF-015/NFR-MDF-002 (historical immutability — recovery writes through the ordinary store, never by rewriting history), FR-MDF-016/NFR-MDF-001 (no silent ledger hole in either direction); TKT-foundation-063 (the stale-cursor-reset precedent this follows), TKT-foundation-082 (the live finding this closes); §10.9.19 (extended), §10.9.11/§10.9.15 (cited, unchanged), §10.9.17 (the budget/deadline bounds, cited unchanged); test plan §32 (TC-MDF-121, v1.43) | **TC-MDF-121** (test plan §32, v1.43) implemented as four cases in `tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs`, every one tagged `TC_MDF_121`, L2 doubles only: the list is the committed `kap-public-bycriteria-asels-fr` capture and the recovered body the committed `kap-public-report-1643141`; the **transport failure** is a real connection that never delivers (WireMock answers after 30 s while the lane's per-item bound is 150 ms), so **no body ever reaches the lane** — never a fabricated failure object and never a shape verdict, and **zero live kap.org.tr requests**. (i) three detail quarantines in pass 1 (all in the seen set, watermark moved), pass 2 replays each **once** — the interim recovers (its facts stored under `kap-public`, one report request in the journal), the other two re-quarantine terminally (5 unfetchable rows in total, each carrying `"bodyReceived": false`), the log names `quarantine-replays 3 (recovered 1, still-quarantined 2)`, and `Retried` holds all three; pass 3 over a **now-healthy** source issues **zero** requests for any of them (a healthy route is journaled the moment it is asked, so `0` is evidence, not blindness). (ii) a shape verdict (`UNPARSEABLE_PAYLOAD`) is terminal and never replayed — only the transport failure beside it registers, is replayed once (`quarantine-replays 1 (recovered 0, still-quarantined 1)`), and the shape index is fetched exactly once for the whole lifetime. (iii) a non-success status **carrying a body** is terminal while the transport failure beside it replays — the same quarantine word, two different classes, decided from what the read carried. (iv) the replay counts against the pass **detail** ceiling: with `MaxDetailFetches = 1` the pass spends one replay (the backlog's oldest index), reports `WALK_DETAIL_CAP`, journals **no** fetch of the recoverable interim, and leaves `Retried` 1 / pending 2; a later default-ceiling pass drains the backlog, writing the recovered report and spending the last tombstone. **Red→green, executed (Docker/Testcontainers available; the red is the live cursor-locked world):** with the replay loop disabled all four fail — (i) at `Expected: 1, Actual: 0` on the recovered report's request (the seen-set-held quarantine is never re-fetched), (ii) and (iii) on the missing `quarantine-replays …` run line, (iv) at `Expected: "WALK_DETAIL_CAP", Actual: null` (a bounded pass has nothing to replay, so nothing truncates and the backlog never drains) — and **4/4 green** with it. Non-regression **97/97 green and unrelaxed:** `KapPublicStatementsIngestTests` (20), plus `StatementsAndEventsIngestionTests`, `EodChainTests`, `MorningChainTests`, `KapPublicIdentityJobTests`, `KapDisclosure*` (77); `dotnet build` 0 warnings. **Test-harness note, recorded honestly:** an aborted response is only entered into WireMock's journal when it eventually completes, so per-index request counts are used as evidence **only** for doubles that answer at once; the still-failing replays are witnessed by the run's own ledger line and the persisted tombstones instead — and the abort delay is set far beyond any case's runtime so no stray entry can flake a later pass's journal. **Live criteria (orchestrator, post-merge, paced, never CI):** one `Source = kap-public` pass over at least AKBNK, TTKOM and TUPRS — replayed/recovered/still-quarantined counts verbatim, the recovered windows' statements visible in the store, and a second pass making **no** further replay attempts on the same indices. |

| v1.5.68 | 2026-10-28 | **Quarantine-recovery record reconciled to what the code does, and the rules a reader could only infer are written down** (TKT-foundation-092's review round 1, findings DR-1/DR-4/DR-5/DR-6/DR-7; §10.9.19's quarantine-recovery subsection amended and one log line's wording corrected, **no behavioural change** — no walk/decode change, no cursor-shape change, no migration, no new request, no predicate change, no counter semantics changed, **zero live requests**). v1.5.67 is left intact as the landed record; this row states what was under-specified in it. **(1) DR-4 — ledger honesty:** the subsection said the counts go "into the run summary", which reads as a stored stats surface. They do not: `IngestResult` (`Jobs.cs:55`) and `SymbolRunStat` (`Jobs.cs:99-105`) are untouched by this ticket, so `ReplaysAttempted`/`Recovered`/`StillQuarantined` reach **two log lines only** — the per-company cursor line and the run's completion line `quarantine-replays N (recovered R, still-quarantined S)`. The subsection now names the completion log line and says the ledger stats are unchanged. **(2) DR-5 — one quarantine row per attempt:** a replay that fails again writes a **second** `DISCLOSURE_DETAIL_UNFETCHABLE` row for the *same* index; rows are neither deduped nor superseded. This is now a recorded deliberate rule, not an inference from an expected count — `QuarantineWriter.WriteAsync` only ever adds a `QuarantinedFact` (it never updates or supersedes) and `StatementsJob.QuarantineSkipsAsync` calls it once per skip including the replay's own skip, which is what the TC-MDF-121 (i) "5 unfetchable rows for 3 indices" expectation measures. **(3) DR-6 — a recovered index leaves its original row standing:** the recovery path touches only the cursor's `quarantined`/`retried` markers and the statement store; **nothing** deletes the earlier quarantine row or annotates it (`quarantined_facts` is not written on the recovery path at all), so the evidence stands as recorded and the recovery is read from the completion log line and the stored facts. **(4) DR-1 — the zero-length-body edge is recorded, not re-opened:** `IsTransportFailure` (`IsSourceDown && string.IsNullOrEmpty(RawBody)`) also classifies a **non-success status carrying no readable body** (zero-length body, or a body read that failed with `IOException`/`HttpRequestException`) as transport, because at the read it is indistinguishable from one. That is inside the ruled class and bounded to one extra request per index (the tombstone still fires, so no ladder forms), so the subsection records the edge explicitly and states that tightening the predicate is a semantics change needing its own decision — not a defect a later reader should quietly "fix". **(5) DR-7 — the recovery log line names what the write actually did:** it printed "quarantined → written" unconditionally while the same line carried `{Inserted}/{Unchanged}/{OverlapRetained}`, so a replay whose report is entirely withheld under 083's coexistence rule (counted **recovered**, `Inserted 0`, `OverlapRetained > 0`, no depth-row hole repaired) was described as "written" beside "0 inserted". It now reads `quarantined → recovered ({Inserted} inserted, {Unchanged} unchanged, {OverlapRetained} overlap-retained)`. Wording only — `Recovered` still counts the index as having left the quarantine (it did; the source delivered a decodable report), and no counter, tombstone or store write changed. | FR-MDF-002 (the repair path, unchanged), FR-MDF-015/NFR-MDF-002 (historical immutability — evidence is never rewritten), FR-MDF-016/NFR-MDF-001 (no silent ledger hole *and* no overclaimed one: a log line is not a ledger field); §10.9.19 (amended), BR-MDF-007 (quarantine rows are never deleted; dismissal records the gap); TKT-foundation-092 | **No test changed by this row's own clauses** — it records behaviour the shipped TC-MDF-121 cases already pin: (i) the second quarantine row for a re-failed replay (5 rows for 3 indices, each `"bodyReceived": false`) and `quarantine-replays 3 (recovered 1, still-quarantined 2)`; (ii)–(iii) the terminal classes; (iv) the detail-ceiling bound. The one test **added** in this round (review DR-2, test plan v1.44) is a plain unit assertion over a literal pre-092 cursor notes document — same watermark and seen set, empty backlog/tombstone — deliberately **untagged** (`TC_MDF_121` pins replay behaviour; the plan defines no back-compatibility sub-case). Zero live requests. |
| v1.5.69 | 2026-10-28 | **The statements-view label map: the industrial IS/BS/CF row set, the additive row-scope dimension on the label-rule key, and the Konsolide/Solo filing flag** (TKT-foundation-093; §10.9.19 extended — the subsection landed here and **subsumes** the planner's dated 2026-10-28 design ruling, mechanism unchanged; `02` §6.1 carries the 42-code chart extension; test plan §33 / TC-MDF-122 its acceptance). **No DR-1/DR-2 resolution** (`ST_DEBT`/`LT_DEBT` stay exactly two lines, the current portion stays `OTHER_BS_*`, `TOTAL_EQUITY` stays NCI-inclusive as stored), **no serving/UI/metrics change** (094/095/096 own those), no share-count work, no cumulative-semantics change (090 stands), no VYK/XBRL or equity-matrix decode change, no new dependency, no schema change, **zero live requests**. **The obstacle, recorded as it was measured:** every v0 row family already decoded as a *valued* `OTHER_*` passthrough, but the map's key was `(StatementType, Label)` while the balance sheet legitimately repeats `Ticari Alacaklar`, `Ticari Borçlar`, `Kiralama İşlemlerinden Borçlar`, `Ertelenmiş Gelirler (…)` across its current/non-current blocks and the income statement repeats `Ana Ortaklık Payları` across its profit-or-loss/OCI parts — so a 1:1 map would overwrite a value or re-raise v1.5.62's `DUPLICATE_LABEL_CONFLICT` quarantine. **The mechanism (additive by construction):** a row scope resolved **before** label mapping exactly as the CF section is — BS block from the capture's `DÖNEN VARLIKLAR`/`DURAN VARLIKLAR`/`KISA VADELİ YÜKÜMLÜLÜKLER`/`UZUN VADELİ YÜKÜMLÜLÜKLER` anchors, IS part from its `Kar veya Zararda Yeniden Sınıflandırılmayacaklar` anchor, CF block as the section already carries (`KapPublicScopes` `CURRENT`/`NONCURRENT`/`PL`/`OCI`) — carried as **two projections** (`LabelMap` unqualified, still a function of `(statement, label)`; `ScopedLabelMap` one per `(statement, label, scope)`), where **an unqualified rule matches in every scope**, so every pre-093 entry and every landed golden is unchanged, and the dimension materializes in stored data as **distinct canonical codes** (serving keeps a flat code→figure map). A row whose scope is undetermined (before any anchor, or in the equity block) matches **no** qualified entry: it stays a section-scoped `OTHER_*` passthrough and is a named, counted **`BLOCK_UNDETERMINED`** finding once per row — the closed reason set grows to seventeen — never a guessed block; its incidence on captured filings is unmeasured (all five bodies resolve their blocks, zero findings), so its case is a mutation. **Evidence:** five committed bounded-trim captures carry every spelling (the four from §10.9.19 plus **THYAO `1565996` 2025 Yıllık**, committed from the already-fetched body — no new request), each entry naming its capture in `KapPublicLabelRule.Provenance` with the exact spelling in that body and every target in `StatementMapper.CanonicalCodes` (23 → **65** codes, `02` §6.1). **The flag:** `KapPublicReport.Consolidated` derives from the header field `Finansal Tablo Niteliği` (`ConsolidatedFor`: `Konsolide`→true, `Solo`→false, absent/other→null, declared string retained) — never from the `<h1>` heading, never from minority interests; persisting/serving it is 094's. **Recorded §6.1 choice:** non-current trade receivables/payables get their own codes (`LT_TRADE_RECV`/`LT_TRADE_PAY`) rather than joining `NONCUR_*_OTHER` — a caption two blocks carry is two lines of the same kind. | FR-MDF-002/003 (the row families the served financials need), FR-MDF-008 (per-entry capture provenance; `kap-public` facts), FR-MDF-012 (an unmapped family stays a labelled passthrough; an undetermined block is named and counted), FR-RES-005..018 (served financials supply), FR-VAL-001 (the DCF's `debt`/`cash` **consumed as stored, never widened**); §10.9.19 (extended), §10.9.11/§10.9.13/§10.9.15 (cited, unchanged); `02` §6.1 (amended); test plan §33 (v1.45) | **TC-MDF-122** (test plan §33, v1.45) implemented as nine cases in `tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs`, every one tagged `TC_MDF_122`, L2 doubles only — the five committed bodies are the entire input, **zero live kap.org.tr requests**, every negative case an in-memory mutation (the `DÖNEN VARLIKLAR` anchor renamed; `Konsolide`→`Solo`/`Bireysel`; the `Finansal Tablo Niteliği` field renamed): (a) both projections are evidence-bound functions (every entry's scope in the closed vocabulary, provenance in `CaptureNames`, spelling present in that body, target in the registry; each family the view needs present; the lease caption visibly split per block); (b) THYAO's FY2025 balance sheet decodes the industrial row set in millions (`ST_TRADE_RECV` 45.791, `INVENTORY` 41.472, `PPE_NET` 362.618, `ROU_ASSET` 847.758, `INTANGIBLES_NET` 8.867, `GOODWILL` 1.145, `ST_LEASE_LIAB` 86.152, `LT_LEASE_LIAB` 552.824, `ST_TRADE_PAY` 78.640, `ST_DEFERRED_REV` 151.257, `LT_DEFERRED_REV` 3.240, `RETAINED_EARNINGS` 332.426, `PAID_IN_CAPITAL` 1.380, `MINORITY_INT` 34, `TOTAL_ASSETS` 1.996.745, `TOTAL_LIAB` 1.085.489, `EQUITY_PARENT` 911.222, `CUR_ASSETS_OTHER` 12.378, `CUR_LIAB_OTHER` 4.746, `NONCUR_LIAB_OTHER` 13.528) with the v1.5.62 core byte-identical beside it and zero conflicts; (c) the block dimension is load-bearing — ASELSAN's repeated caption maps per block (`ST_TRADE_RECV` 38.859.628 / `LT_TRADE_RECV` 87.944.574, `ST_TRADE_PAY` 37.964.626 / `LT_TRADE_PAY` 24.432) with no conflict and no `OTHER_BS_Ticari_*` survivor; (d) the fail-safe — no qualified code guessed, the value stays `OTHER_BS_Ticari_Alacaklar`, `CASH` still maps, exactly one counted `BLOCK_UNDETERMINED` naming the caption; (e) the additive design's backward compatibility — ASELS 2026-06 keeps 116 lines and its 4/4, 3/3, 51/51 per-statement counts, TÜPRAŞ keeps 346 lines and every debt/cash/core golden, MİGROS 314, zero conflicts; (f) the IS part dimension — `NI` 118.117 total beside `NI_PARENT` 118.208 parent share while the OCI occurrence stays a labelled passthrough, plus the other-operating/investing/financing/monetary families and ASELSAN's `RND` −6.252.189; (g) the CF detail lines (impairments 0, provisions 7.605, asset-sale 3.301, issuance 0, buyback −273, debt issued 183.574 / repaid −147.173, dividends −8.951, interest −12.129) and TÜPRAŞ's `MONETARY_ADJ` 11.449.200; (h) the flag — `Konsolide`→true (ASELS/THYAO), `Solo`→false, `Bireysel`→null, field absent→both null; (i) the decode→store path — `StatementMapper.Map` keeps the codes verbatim and `fin_line_items` carries them under their own names in single TL (`ST_LEASE_LIAB` 86.152.000.000 … `MINORITY_INT` 34.000.000; `NI_PARENT` 118.208.000.000 beside `NI` 118.117.000.000). **Red→green, measured (Docker/Testcontainers available):** with the 41 new map entries stripped (models, scope machinery and assertions kept, so the red is the *map's* absence) the filter reads **7 failed / 2 passed** — (a) `Assert.NotEmpty() Failure: Collection was empty` on `ScopedLabelMap`, (b) `KeyNotFoundException: 'ST_TRADE_RECV'`, (c) the same, (d) `Assert.Single() Failure: The collection did not contain any matching items` for `BLOCK_UNDETERMINED` (a fail-safe needs a qualified entry to fail safe *from*), (f) `'NI_PARENT'`, (g) `'IMPAIRMENT'`, (i) `'ST_LEASE_LIAB'` — while (e)/(h) pass by construction (the additive goldens are what the unmodified map produces; the flag is model-side), stated rather than manufactured. **39/39 green** for `KapPublicReportDecoderTests`; non-regression **31/31 green and unrelaxed** (`KapPublicStatementsIngestTests | RealSourceStatementsTests | StatementNormalizationTests | StatementVersioningTests`); `dotnet build` 0 warnings. **Test re-pins (user-approved 2026-10-28, every TC id kept, nothing weakened):** TC-MDF-107 (iii)'s trade-receivable value moved to `ST_TRADE_RECV` with its passthrough proof moved to the still-unmapped related-party caption, and its financing companion moved one row over (its caption is now `DEBT_ISSUED`); TC-MDF-108 (v) mutates every `CF` entry of **both** projections instead of six hand-written labels (set-closed, stronger); TC-MDF-117 (b)'s exhaustive canonical surface grew 6 → 25 codes with `ST_DEBT`/`LT_DEBT` still the two borrowings lines and the trade-payable value now under `ST_TRADE_PAY`; TC-MDF-117 (d)/(e)'s disambiguation totals fell 22 → 18 and 42 → 34 because the repeated families are placed per block instead of disambiguated as `OTHER_*` (their conflict claim stands, 2 per column). Test plan v1.45 records the same round. Diagnostics on all five bodies post-change: 497/346/383/314/116 lines, zero conflicts and zero `BLOCK_UNDETERMINED`. **Live criteria (orchestrator, post-merge, paced, never CI):** one `Source = kap-public` statements pass writing the expanded canonical row set with per-family counts verbatim and any honest-absent family named (expected: none). |
