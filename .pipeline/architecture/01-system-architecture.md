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
- **The lane (`src/Ingestion/Shares/`, job code `shares`).** One paced fetch per run (the 046 `/members` snapshot precedent — a whole-archive snapshot, never a per-instrument loop), decoded, then written to the new **`share_counts`** store (`02` §3.1: `instrument_id` PK, `shares_outstanding numeric(18,4)`, `recorded_at`, `source_ref` — one row per instrument, **input-aware** in place per the 042/050 doctrine: an unchanged count writes nothing and does not move `recorded_at`, a changed count is repaired in place and counted). `source_ref = kap`. Admin-triggerable; **not** in the EOD or morning chain (share capital changes rarely — scheduled cadence is a later decision, recorded not scoped).
- **Matching + aggregation (evidenced hypothesis; unit cross-validation pending).** Lines match our instruments by **`borsaKodu` primary / `isin` fallback**, keyed on at least one **trading-open** line (`borsadaIslemeAcik`) — a closed-only member seeds no count. `shares_outstanding` is the **sum of the member's `currentCapital` across all its matching security lines** (tertip aggregation A+B): the live shapes evidence it (ASELS A 2,421,818,181.816 closed + B 2,138,181,818.184 open = **4.56B**, the real count; THYAO C 0.01 + A 1,379,999,999.99 = **1.38B**; ISDMR A 20 + B 2,899,999,980 both open = **2.9B**; TUPRS = 1,926,795,598; GARAN 4.2B; AEFES 592,105,263). **The unit remains unresolved, never assumed:** every line in the committed double carries `capital == currentCapital`, so a served value matching a known count cannot by itself discriminate a share count from paid-in TL; the stored value is **treated as a count only after** the orchestrator's post-merge cross-validation table on this ticket (served vs known real count for ASELS/THYAO/TUPRS/GARAN/AEFES) passes — no fabricated division, no zeros, and a refuted unit or aggregation rule is recorded on the ticket.
- **Honest absence (031/052 doctrine).** An instrument with **no** security line (the 5 archive-absent members — GLRMK, PAHOL, TRALT, TRENJ, TRMET) or only closed lines gets **no row**, never a zero row; and a line missing `currentCapital` **on one of our matched instruments** is **skipped and counted**, with its **whole matched group withheld** (a partial tertip sum would be a fabricated count) — a re-run that withholds a group **removes** any previously stored row for it, so a stale count is never left being served; an instrument merely **absent** from a snapshot keeps its last stored row (last known wins — 042/050's "a stored row the current run no longer computes is left alone"). The skip count rides the run's `stats_json` and the run stays `succeeded` (a counted gap, not a permanently-lying freshness signal). Transport failure → `failed` run, no rows; a non-memberSecurities body → `UNPARSEABLE_PAYLOAD`.
- **Serving seam (in scope here; consumers are their own tickets).** `GET /api/v1/stocks/{symbol}/balance-sheet` gains an **additive** `shareCount: { sharesOutstanding, asOf }` block (`03` §3) — honest `null` for an instrument with no stored count (a dash downstream), never `0` — leaving the statement-reported `lines.sharesOutstanding`/`lines.sharesDiluted` and BVPS **untouched** (the market count is a distinct field, never a rewrite of the statement's own figures). Downstream consumers — per-share metrics (`MetricsEngine`), multiples/market-P/E (`MarketSnapshotEngine`, BR-MOV-010's exclusion doctrine), DCF baselines — are **pinned seams only** and stay their own tickets.
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
never clobbers them. A cursor-less company starts from `Ingestion:{Family}:EarlyDisclosureIndex`
(the first pass **is** the backfill: achieved depth is what the source returned, FR-MDF-010/011).
Two cursor-integrity rules (fix round 2): a **non-empty cursor that does not parse fails the run
loudly** (`SourceFetchException`, naming the value) instead of silently re-walking the whole
archive; and when the **first** list request of a company — the one made from a persisted cursor —
answers `400`+`ER005`, the cursor is treated as past a tail the gateway no longer serves and the
walk **resets once** to `EarlyDisclosureIndex`, so one stale row cannot poison every future run.
The reset is bounded to one attempt per company per run; since v1.5.42 it is also recorded verbatim
and its outcome persisted (the corrected cursor, or a cleared row when the re-walk is honest-empty
— see §10.9.15's statements paragraph).

**Config (`Ingestion:Dividends` / `Ingestion:CorporateActions` / `Ingestion:Disclosures`, §10.4).**
Shipped in `src/Api/appsettings.json`: each family declares `Path = /disclosures`,
`DetailPath = /disclosureDetail/{disclosureIndex}`, `EarlyDisclosureIndex = 0`,
`UniverseIndex = XU030` (its own bound key, AD-14); `Dividends`/`CorporateActions` add
`DisclosureType = CA` (`Dividends` also `DividendEventType = KP`); `Disclosures` ships
`DisclosureType = ""` (every type). The code defaults are the same verified values, so config and
code cannot drift. The pacer keeps using `Ingestion:Statements:RequestDelayMs`.

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
emitted.

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
  still runs the job (and its launch-seed path). The gate writes one `ingest_runs` row
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
(default `01:30:00`, i.e. 90 minutes ≈ 2.5× the observed full-chain duration; `<= 0` disables)
bounds **one attempt**, not one job — deliberately, so the class is closed regardless of *which*
await hangs (a source adapter, the KAP pacer's delay, a connection-pool wait, a semaphore). On
expiry the watchdog, deterministic on the injected `TimeProvider`:

1. cancels the attempt (the hung await itself is not interruptible — that is the premise; the
   abandoned task keeps its own DI scope and cancellation source, which are handed to a
   continuation that disposes them when it finally ends, and its terminal exception is observed
   so it can never surface as an unobserved-task crash),
2. writes one honest run-ledger row: `job_code` = **the step that held the run**, `status =
   failed`, `started_at` = the attempt start, `finished_at` = the expiry, `error` naming the
   bound, and `stats_json` carrying `reasonCode: "RUN_HUNG"`, `hungJob`, `step`, `stepCount`,
   `runDate`, `attempt`, `retries`, `maxAttempts`, `maxRunDurationSeconds` — so the class is
   filterable from `GET /admin/ingest-runs`,
3. raises one `IngestionAlert` with `reasonCode: "RUN_HUNG"` (the existing C3d machinery and the
   builder e-mail seam — no new channel), naming the step and the bound,
4. hands the attempt to the **unchanged retry ladder** (5/15/60, `MaxAttempts`, then
   `RETRIES_EXHAUSTED` + fail-stop), scheduled **from the instant the attempt ended** — a bound
   of 90 minutes would otherwise schedule 85 already-overdue minutes.

Two edges are decided explicitly: an attempt that **completes as the deadline fires** is a
completed attempt (the watchdog re-checks completion before declaring a hang), so a run is never
both `succeeded` and `RUN_HUNG` and never retried on that race; and a **stopping host** unwinds
without a `RUN_HUNG` row or alert (a shutdown is not a hung run).

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
