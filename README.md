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
