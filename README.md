# Değerli (working name)

**A bilingual (Turkish-default, English toggle) value-investing research platform for Borsa İstanbul (BIST).**

> *Değerli* is Turkish for *valuable*. The name is a working name and may change.

**Status: planning complete — implementation starting.** This repository currently contains the complete,
approved planning package (requirements, UX specifications, architecture, test plans, and the implementation
ticket queue). Product code lands through that queue; every unit of work is accepted by pre-approved test
cases, written before the implementation that satisfies them (TDD).

---

## Why this exists

Turkish retail investors who want to practice value investing have no accessible home for it:
international tools barely cover BIST, professional terminals are expensive, and raw public sources
(KAP, TEFAS) require expertise to turn into decisions. The result: amateur investors skip fundamental
analysis entirely, or follow tips.

Değerli covers the complete core investing loop for the BIST 100 universe — **discover → screen →
research → value → decide** — in plain language, for the amateur-but-willing-to-learn investor:

- **Market overview** — BIST 100/30 levels, sector performance, market breadth, biggest movers, volume,
  market-wide valuation, and six macro indicators (official + independent inflation, CBRT policy rate,
  USD/TRY, EUR/TRY, gold)
- **Stock screener** — 18 metrics across five families (valuation, quality, growth, financial health,
  dividends) with user-saved screens persisted server-side
- **Stock pages** — bilingual business descriptions (AI-drafted offline, human-reviewed), valuation with
  sector-median comparison, financials, profitability, growth, balance sheet, dividend history
- **DCF calculator** — every model assumption user-editable; fair value vs. current price in plain
  language; sensitivity grid across discount rate x terminal growth; named scenarios per account
- **User accounts** — minimal-data (KVKK-aware) registration with saved screens and DCF scenarios

## Data foundation (the engineering core)

- **Sources ($0 budget):** KAP as the primary source (financial statements, dividends, corporate actions,
  disclosures); a free public API candidate for daily prices; TEFAS for fund data (equity funds only —
  data foundation, no fund UI in V1)
- **History as first-class data:** every fact is stored with its date/period; facts are append-only —
  restatements arrive as new versions, never overwrites; prices stored both raw and
  corporate-action-adjusted (with TERP-based rights-issue factors); point-in-time queries are possible
  by construction
- **Daily EOD cadence:** scheduled batch jobs with a run ledger, retry ladder, quarantine of invalid
  facts, and honest staleness marking — never real-time, never pretending to be
- **One canonical number per metric:** a single Metrics Engine computes every value any surface
  displays; the same P/E for the same stock on the same date is byte-identical on the dashboard, in
  screener results, on the stock page, and in the DCF calculator

## Architecture at a glance

One VPS, one database, one backend process, one SPA:

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core (.NET 10 LTS) Minimal APIs, feature-module organized |
| Database | PostgreSQL 17+ (EF Core 10 migrations; raw SQL for window-function metric math) |
| Data platform | In-process ingestion workers + cron scheduler + run ledger; source adapters (swappable per source) |
| Quantitative core | Shared C# library — every formula (18 metrics, CAGRs, market aggregates, DCF model) defined exactly once |
| Frontend | React 19 + Vite + TypeScript SPA; TanStack Query; zustand draft store; react-i18next (TR/EN) |
| UI | Tailwind CSS v4 + shadcn/ui (Radix a11y primitives) |
| Content pipeline | Offline CLI: bilingual business descriptions drafted from KAP filings via an AI API, published only after human review |
| Auth | ASP.NET Core Identity, cookie sessions, e-mail verification gating persistence |
| Infra | Docker Compose, Caddy (auto-TLS), GitHub Actions CI/CD, nightly encrypted backups |
| Testing | xUnit + Testcontainers (real Postgres), Vitest + Testing Library + MSW, Playwright e2e, axe-core a11y gate |

Planned layout:

```
/src/Api              public REST API (feature modules) + hosted workers
/src/Core             shared quantitative core (single definition per number)
/src/Ingestion        source adapters, validation/quarantine, metrics engine, scheduler
/src/ContentPipeline  offline AI drafting CLI (build-time, builder-triggered)
/src/Web              React SPA (12 screens: 11 public + builder-only admin)
/tests                backend unit/integration, e2e
fixtures/             synthetic fixture universe shared by integration + e2e tests
.pipeline/            the planning package (see below)
```

## Planning-first: the docs are the contract

This repository is built documentation-first. The `.pipeline/` directory contains the full, approved
planning package, and code is derived from it — never the other way around:

| Document | What it pins |
|---|---|
| [`00-project-brief.md`](.pipeline/00-project-brief.md) | objectives, scope, constraints, success criteria |
| [`analysis/`](.pipeline/analysis) | domain map + 7 domain reports: 104 committed functional requirements, 30 use cases with alternate/error flows |
| [`ux/`](.pipeline/ux) | 12-screen inventory, 156 interaction requirements, a 27-row mutation/state-synchronization matrix |
| [`architecture/`](.pipeline/architecture) | system architecture (v1.3), data model with canonical quantitative definitions, API design (~35 endpoints) |
| [`testing/`](.pipeline/testing) | test strategy, a synthetic fixture universe with hand-derived golden values, 8 domain test plans — **238 test cases** — and a both-directions coverage matrix |
| [`plan/tickets/`](.pipeline/plan/tickets) | 74 implementation tickets tracing to the requirements above |

Traceability is mechanical: every FR maps to test cases, every test case gates exactly one ticket, and
every ticket points into the permanent documents rather than paraphrasing them. Expected values in tests
are hand-derived from the documented formulas (never from an implementation), which is what makes two
independent implementations converge on the same numbers.

**Test data is synthetic.** No real BIST symbols, names, or values appear in this repository — the
fixture universe is engineered so that every "not meaningful" rule, restatement, corporate-action
adjustment, staleness state, and coverage gap has a deterministic trigger.

## Testing

- Unit (pure math: metrics, CAGR windows, adjustment factors, DCF model + sensitivity grid)
- Integration (real PostgreSQL via Testcontainers; every endpoint contract incl. the honest-data
  envelope — as-of dates, stale/restated/adjusted flags; ingestion idempotency and quarantine)
- Component (SPA modules against MSW-mocked APIs)
- E2E (Playwright against a full Compose stack with MailPit; the anonymous core loop
  dashboard → screener → stock page → DCF is one test)
- CI gates: i18n TR/EN key parity, advice-vocabulary grep (no recommendation language anywhere),
  axe-core zero-known-violations, performance budget smoke

## Disclaimers

This platform is **informational only**. It is not investment advice, contains no buy/sell
recommendations or price targets, and personalized investment advice is a licensed activity in
Turkey (SPK). All DCF output reflects the user's own assumptions, framed as such. KAP source
documents remain in their original Turkish.

## About

Solo-built with an AI-assisted workflow, planned for public launch by end of 2026. The planning
package, the data pipeline, and the quantitative rigor are the portfolio artifacts as much as the
product is.

## License

TBD — to be decided before launch.
