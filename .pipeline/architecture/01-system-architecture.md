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
