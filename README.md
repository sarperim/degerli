# Değerli

> **A value-investing research platform for Borsa İstanbul (BIST).**

**Değerli** *(working name; Turkish for “valuable”)* is a bilingual, research-first platform for Turkish retail investors who want to learn and practice fundamental value investing without relying on tips.

It brings together **market discovery, stock screening, fundamental research, and user-driven valuation** in one place, backed by a historical-first data platform designed around reproducibility and quantitative consistency.

> **Status:** Implementation in progress · Target public launch: December 2026

---

## Why Değerli?

Fundamental research on Turkish equities is fragmented.

International platforms provide limited BIST coverage. Professional terminals are expensive. Public sources such as KAP and TEFAS contain the underlying information, but turning that information into a coherent research workflow requires significant financial and technical knowledge.

Değerli is intended to bridge that gap.

The core workflow is:

**Discover → Screen → Research → Value → Decide**

The goal is not to tell users what to buy.

The goal is to give them the data and tools to **make their own investment decisions**.

---

## What It Does

### Market Overview

A daily snapshot of the BIST market:

* BIST 100 and BIST 30 levels
* Sector performance
* Market breadth
* Biggest gainers and losers
* Trading volume
* Market-wide valuation metrics
* Macro indicators:

  * CPI / inflation
  * CBRT policy rate
  * USD/TRY
  * EUR/TRY
  * Gold

Data is explicitly presented with its **as-of date and freshness state** rather than implying real-time coverage.

### Stock Screener

Screen the BIST 100 universe using fundamental metrics across five categories:

| Category             | Metrics                                           |
| -------------------- | ------------------------------------------------- |
| **Valuation**        | P/E, P/B, EV/EBITDA, EV/FCF, FCF Yield            |
| **Quality**          | ROIC, ROE, Gross Margin, Operating Margin         |
| **Growth**           | Revenue CAGR, EPS CAGR, FCF CAGR                  |
| **Financial Health** | Net Debt/EBITDA, Interest Coverage, Current Ratio |
| **Dividends**        | Dividend Yield, Dividend CAGR, Payout Ratio       |

Screens can be saved to user accounts and reused later.

### Stock Research

Each covered company has a dedicated research page containing:

* Business description
* Valuation
* Historical valuation
* Financial statements
* Profitability
* Growth
* Balance sheet
* Dividend history

Financial history is treated as a first-class dataset rather than simply storing the latest reported value.

### DCF Valuation

A simple, transparent DCF calculator lets users define the assumptions themselves.

The platform does **not** provide a proprietary target price or investment recommendation.

Instead, it answers a narrower question:

> **Given these assumptions, what does this model imply about the company's value relative to today's price?**

Users can adjust the model parameters, create named scenarios, and inspect sensitivity across discount rate and terminal growth assumptions.

---

# Engineering Philosophy

The product is deliberately built around a few principles.

### 1. History is data

Financial facts are not simply overwritten when new information arrives.

Facts carry their relevant dates and periods, allowing the system to preserve historical state and support point-in-time analysis later.

Restatements are represented as new versions rather than silently replacing historical information.

### 2. One definition per number

A metric should not be independently calculated by the dashboard, screener, stock page, and valuation engine.

Değerli therefore has a shared quantitative core and Metrics Engine responsible for canonical calculations.

For example:

> **The P/E displayed for a company on a given date should be the same number everywhere in the application.**

This is a data-integrity requirement, not merely a code-reuse preference.

### 3. Honest data

Değerli is an **EOD platform**, not a trading terminal.

Every relevant response can expose information such as:

* as-of date
* stale state
* restatement state
* corporate-action adjustment state
* data coverage limitations

When the underlying data is incomplete or stale, the product should say so rather than manufacture false precision.

### 4. Reproducible calculations

Quantitative outputs are derived from documented formulas and deterministic test fixtures.

Expected values in the test suite are independently derived from the specifications rather than copied from the implementation.

This allows the implementation and the expected result to be treated as two separate sources of truth.

### 5. Documentation is the contract

Development starts with requirements, UX specifications, architecture, and test cases.

The implementation is derived from those documents rather than becoming the source of truth itself.

---

# Data Foundation

The first milestone includes a data platform designed to support the product rather than treating data ingestion as an afterthought.

### Primary data sources

* **KAP** — financial statements, disclosures, dividends, and corporate actions
* **Public market-data sources** — daily prices and volume
* **TEFAS** — investment-fund data

The system is designed around **source adapters**, allowing individual data sources to be replaced without rewriting the rest of the platform.

### Daily ingestion

Data is refreshed through scheduled EOD jobs with:

* run ledger
* retry handling
* validation
* invalid-fact quarantine
* idempotent ingestion
* staleness detection

The objective is not to hide data problems.

It is to make them observable.

### Corporate actions

Price history is stored in both raw and corporate-action-adjusted forms where applicable.

Adjustment logic is explicitly modeled and tested, including rights-issue adjustments.

### Fund data

Fund data is being established as part of the underlying data foundation even though fund-facing UI is outside the first product release.

The initial target includes:

* NAV history
* performance
* publicly available holdings

This provides a foundation for future fund research and portfolio-analysis features without expanding the first release's product scope.

---

# Architecture

The first release intentionally favors a **small, deployable architecture** over premature distribution.

```text
                         ┌─────────────────────┐
                         │      React SPA       │
                         │ React + TypeScript   │
                         └──────────┬──────────┘
                                    │
                              REST / HTTP
                                    │
                         ┌──────────▼──────────┐
                         │    ASP.NET Core      │
                         │   Feature Modules    │
                         └──────┬─────┬────────┘
                                │     │
                 ┌──────────────┘     └──────────────┐
                 │                                   │
        ┌────────▼────────┐                 ┌────────▼────────┐
        │ Quantitative    │                 │   PostgreSQL     │
        │ Core / Metrics  │                 │ Historical Data  │
        └─────────────────┘                 └────────▲────────┘
                                                     │
                                            ┌────────┴────────┐
                                            │    Ingestion     │
                                            │ Workers / Jobs   │
                                            └────────┬────────┘
                                                     │
                                      ┌───────────────┼───────────────┐
                                      │               │               │
                                     KAP          Market Data       TEFAS
```

### Backend

* ASP.NET Core
* .NET 10
* Minimal APIs
* Feature-module organization
* ASP.NET Core Identity
* EF Core
* Raw SQL where appropriate for quantitative/window-function workloads

### Data

* PostgreSQL
* Historical, versioned financial facts
* Source-specific ingestion adapters
* Shared quantitative model

### Frontend

* React
* TypeScript
* Vite
* TanStack Query
* Zustand
* react-i18next
* Tailwind CSS
* shadcn/ui

### Infrastructure

* Docker Compose
* Caddy
* GitHub Actions
* Automated TLS
* Nightly encrypted backups

The initial deployment is intentionally designed around:

> **One VPS · one PostgreSQL database · one backend process · one SPA**

The architecture can evolve if real requirements justify additional infrastructure.

---

# AI-Assisted Development

AI is part of the development workflow, but not the source of truth.

The repository uses a documentation-first, specification-driven workflow in which AI agents can assist with:

1. Requirements analysis
2. UX / interaction specification
3. Architecture
4. Test planning
5. Implementation
6. Review

The important constraint is that generated implementation must satisfy **pre-existing specifications and tests**.

The project therefore treats AI as an engineering tool rather than replacing engineering judgment.

### Offline content pipeline

Business descriptions are generated through a separate build-time content pipeline:

```text
KAP filings
     │
     ▼
AI-assisted draft
     │
     ▼
Human review / correction
     │
     ▼
Published bilingual content
```

AI-generated content is not presented directly to users without review.

---

# Testing

Testing is designed around the data and quantitative risks of the product rather than only HTTP coverage.

### Backend

* **xUnit** — unit tests
* **Testcontainers** — integration tests against real PostgreSQL
* Metric and formula verification
* Ingestion idempotency
* Validation and quarantine
* API contract tests

### Frontend

* **Vitest**
* **Testing Library**
* **MSW** for API mocking

### End-to-end

* **Playwright**
* Full Docker Compose environment
* MailPit for authentication email flows

The primary end-to-end path is:

**Dashboard → Screener → Stock → DCF**

### Quality gates

CI includes checks for:

* Backend tests
* Frontend tests
* E2E tests
* i18n Turkish/English key parity
* Accessibility via axe-core
* Performance smoke budgets
* Prohibited investment-advice vocabulary

---

# Planning & Traceability

The repository is intentionally **planning-first**.

The `.pipeline/` directory contains the detailed product and engineering contract from which implementation is derived.

```text
.pipeline/
├── 00-project-brief.md
├── analysis/
│   ├── domain map
│   └── domain reports
├── ux/
│   ├── screen inventory
│   └── interaction specifications
├── architecture/
│   ├── system architecture
│   ├── data model
│   └── API design
├── testing/
│   ├── test strategy
│   ├── fixture universe
│   ├── domain test plans
│   └── coverage matrix
└── plan/
    └── tickets/
```

The current planning package contains:

* **104** committed functional requirements
* **30** use cases
* **156** interaction requirements
* **27** mutation/state-synchronization requirements
* ~**35** API endpoints
* **238** test cases
* **74** implementation tickets

The objective is mechanical traceability:

```text
Requirement
    ↓
Test Case
    ↓
Implementation Ticket
    ↓
Implementation
```

Every ticket points back to the permanent specification instead of creating another copy of the requirement.

---

# Project Structure

```text
/
├── src/
│   ├── Api/              # Public API + hosted workers
│   ├── Core/             # Shared quantitative core
│   ├── Ingestion/        # Source adapters + data pipeline
│   ├── ContentPipeline/  # Offline AI-assisted content generation
│   └── Web/              # React SPA
│
├── tests/
│   ├── Backend/
│   └── E2E/
│
├── fixtures/             # Synthetic test universe
│
└── .pipeline/            # Product & engineering specifications
```

---

# Local Development

The backend runs entirely with Docker Compose — PostgreSQL 17, MailPit and the API
(with hot reload). A fresh clone needs no `.env` file: every value has a safe local
default.

### Prerequisites

* Docker + Docker Compose
* Node.js 22 (to run the SPA dev server)
* .NET 10 SDK (only if you build or run the API without Docker)

### Quickstart

The backend stack — PostgreSQL 17, MailPit and the API — runs in Docker; the React SPA
runs on Vite and proxies `/api` to it. Six commands from a fresh clone:

```bash
git clone https://github.com/sarperim/degerli.git
cd degerli
docker compose -f compose.dev.yml up -d            # Postgres + MailPit + API
docker compose -f compose.dev.yml run --rm migrate # apply EF migrations + seed
cd src/Web && npm ci                              # install SPA dependencies (locked)
npm run dev                                        # SPA at http://localhost:5173
```

Then open <http://localhost:5173>. The supporting services are:

* API liveness — <http://localhost:8080/health> → `200 Healthy`
* MailPit inbox — <http://localhost:8025>
* PostgreSQL — `localhost:5432` (user / password / database: `degerli`)

No `.env` file is required: every value has a safe local default. Sources under `src/`
are bind-mounted, so editing a `.cs` file rebuilds and reloads the running API.

### Dev-stack data: real-only vs. FU fixture universe

The dev stack starts with **no market data** — `migrate` applies the schema and only the
*application* seed (the metric catalog, the macro series and the `builder` account), never
instruments. From there you choose which data world the dev database holds. **Real-only is the
posture**; the synthetic FU fixture universe is an **opt-in** world for FU-based testing and
demos.

> **The two worlds do not mix cleanly.** The FU fixture universe (`fixtures/`, the `degerli-ci`
> L4 stack) is a small set of synthetic instruments the L2/L4 test suites and CI depend on; the
> real İş Yatırım chain produces the full real instrument set and the BIST 100 membership.
> Seeding the fixtures *into* a database that also holds real rows yields a mixed stock list —
> the `116 = 100 real + 16 fixture` condition this posture fixes. `FixtureSeedEntry` prints an
> explicit warning every time it seeds, for exactly this reason.

#### Real-only dev stack (the default path)

A fresh database built by the real chain alone:

```bash
# 1. Drop the dev volume (removes any fixture rows a previous bootstrap left) and start fresh.
docker compose -f compose.dev.yml down -v
docker compose -f compose.dev.yml up -d
docker compose -f compose.dev.yml run --rm migrate   # schema + application seed only

# 2. Trigger the real universe chain as the seeded builder account.
#    (dev/CI only — the checked-in default password is refused outside Development/Test.)
BASE=http://localhost:8080
TOKEN=$(curl -sS -c cookies.txt "$BASE/api/v1/auth/csrf-token" | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')
curl -sS -b cookies.txt -c cookies.txt -H "X-CSRF-Token: $TOKEN" -H "Content-Type: application/json" \
  -d '{"email":"builder@degerli.local","password":"Degerli-Builder-2026"}' "$BASE/api/v1/auth/login"
TOKEN=$(curl -sS -b cookies.txt -c cookies.txt "$BASE/api/v1/auth/csrf-token" | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# universe-sync: real instruments + the XU100 launch-seed (the covered universe)
curl -sS -b cookies.txt -c cookies.txt -H "X-CSRF-Token: $TOKEN" -H "Content-Type: application/json" \
  -d '{}' "$BASE/api/v1/admin/ingest/universe-sync/run"

# prices backfill: per-ticker over the configured index (XU030 at launch)
curl -sS -b cookies.txt -c cookies.txt -H "X-CSRF-Token: $TOKEN" -H "Content-Type: application/json" \
  -d '{"backfillFrom":"2024-01-01"}' "$BASE/api/v1/admin/ingest/prices/run"
```

After step 2 the browsing surface is real-only: `GET /api/v1/stocks` serves exactly the XU100
membership (the real covered universe), and `GET /api/v1/stocks/search?q=ASELS` finds `ASELS`.
Price and statement coverage on detail pages is bounded by what has landed: the prices backfill
covers the configured index (XU030), while KAP statement ingestion and XU100-wide price/overview
coverage arrive in their own tickets — until then, real stocks outside the backfilled index show
the honest no-data state by design.

#### FU-seeded dev/demo stack (opt-in)

To browse the synthetic FU universe instead — for FU-based frontend work or a demo — bring up
the CI/demo stack, which migrates, seeds the now-anchored fixture universe and runs the
derived-data jobs before the API starts:

```bash
docker compose -f compose.ci.yml up --build --wait
```

This is the L4 world the Playwright suite runs against; it is **never** the production stack
(`compose.prod.yml` does not run the fixtures entrypoint). To seed the FU universe into *any*
other database (for example to reproduce a demo), run the same fixtures entrypoint against it —
the `fixtures` service in `compose.ci.yml` is one-off and idempotent (exit 0; a re-run adds no
rows). Treat that as an explicit, opt-in action: **never point the fixtures entrypoint at a
database you intend to browse real data on.**

### Configuration

Copy `.env.example` to `.env` only when you need to override a default:

```bash
cp .env.example .env
```

`.env` is git-ignored and never committed. The architecture's secrets are
`ConnectionStrings__Default`, `Smtp__*`, `Evren__ApiKey`, `APP_URL` and `ALERT_EMAIL`.

### Building and running without Docker

```bash
dotnet build Degerli.sln
dotnet run --project src/Api
```

### Content pipeline (offline)

The AI-assisted content pipeline is an alternate entrypoint of the same image and is
run on demand:

```bash
docker compose -f compose.dev.yml run --rm contentpipeline
```

---

# Production & Operations

The public deployment is a single VPS running [`compose.prod.yml`](compose.prod.yml):
Caddy (automatic Let's Encrypt TLS, SPA statics, `/api` reverse proxy) in front of the
ASP.NET Core API and PostgreSQL. Images are built and pushed to public GHCR by GitHub
Actions and deployed over SSH after CI passes on `main`.

* **CI** — [`ci.yml`](.github/workflows/ci.yml): backend · web · e2e on every PR.
* **CD** — [`deploy.yml`](.github/workflows/deploy.yml): build + push
  `ghcr.io/sarperim/degerli/{api,web}`, apply migrations, `compose up -d`, then a
  `GET /health` smoke-check (non-200 fails the job). Rollback is a re-deploy of the
  previous image tag.
* **Backups** — nightly encrypted `pg_dump` → Backblaze B2 (30-day retention, rclone),
  with a documented restore drill (RTO 4 h / RPO 24 h).
* **Uptime** — UptimeRobot 5-minute checks against `/health`.

The full runbook (first-time setup, deploy, rollback, backups, monitoring) lives in
[`deploy/README.md`](deploy/README.md). No secrets are committed — `.env.example` is the
template and the images carry none.

---

# Scope

### V1

The first public release focuses on:

* BIST 100
* Market dashboard
* Fundamental stock screener
* Saved screens
* Stock research pages
* Historical financial data
* DCF valuation
* User accounts
* Bilingual UI
* Daily automated data ingestion

### Deliberately out of scope

The following are future possibilities rather than V1 commitments:

* Fund screener and fund research UI
* Portfolio tracking
* Portfolio analytics
* Look-through fund exposure
* Backtesting
* Advanced stock comparison
* Alerts
* Investment ideas / opportunity engine
* Proprietary value score
* KAP intelligence
* User-facing AI summaries
* Investment thesis builder
* Educational content system

Some of these may become future releases; none are required for V1.

---

# What Değerli Will Never Be

Değerli is intentionally **not**:

* A trading terminal
* A brokerage
* A trade-execution platform
* A stock-tip service
* A personalized investment-advice service
* A real-time market-data terminal
* A crypto or FX platform

There will be **no buy/sell recommendations or personalized investment advice**.

DCF results represent calculations based on the **user's own assumptions**, not a platform-issued price target.

The platform is intended for informational and research purposes.

---

# Data & Regulatory Disclaimer

Değerli is an informational research tool and does not provide investment advice.

Financial calculations and DCF outputs are provided for informational purposes only and depend on the underlying data and assumptions selected by the user.

KAP documents remain in their original Turkish form.

Data coverage, historical depth, source availability, and freshness may vary by company and metric. The application is designed to expose those limitations rather than conceal them.

Before any commercial launch or monetization, the project's regulatory and data-licensing assumptions will be reviewed again.

---

# Roadmap

The first milestone is deliberately narrow:

```text
V0 — Data Foundation
        │
        ▼
V1 — First Product
        │
        ├── BIST research workflow
        ├── Screener
        ├── Stock pages
        └── DCF
        │
        ▼
Future phases
        │
        ├── Fund research
        ├── Portfolio analytics
        ├── Backtesting
        ├── Comparison & alerts
        └── Other research tools
```

The post-V1 roadmap is intentionally flexible and will be revisited based on actual user needs and data quality.

---

# Project Status

**Current phase: implementation**

Planning is complete. The repository currently contains the approved requirements, UX specifications, architecture, test strategy, fixtures, and implementation queue.

Implementation is being developed against that package, with tests written before the implementation they are intended to validate.

**What is actually implemented right now — and what only looks implemented — lives in
[`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md)**: per-domain status (`live` /
`live-degraded` / `ci-only` / `wired-not-configured` / `scaffolded` / `blocked-upstream`), a ledger
of what CI proves versus what only a live run proves (with the date of the last live observation),
and the checkable rule for keeping it honest (`scripts/implementation-status.sh`).

The target is a **publicly accessible V1 by December 2026**.

---

# Why Build This?

Değerli is both a product experiment and an engineering project.

The product provides a practical environment for working with:

* Financial data ingestion
* Historical data modeling
* Data quality and lineage
* Quantitative calculations
* PostgreSQL
* REST API design
* React state management
* Automated testing
* CI/CD
* Containerized deployment
* AI-assisted software engineering

The goal is not to demonstrate how many technologies can be put into one repository.

The goal is to build a system where **the data, calculations, product behavior, and engineering decisions have to be correct together**.

---

## License

TBD — to be decided before public launch.
