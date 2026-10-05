# Project Brief: Değerli (working name) — BIST Value Investing Platform

> Working name "Değerli" — Turkish for "valuable". The domain `degerli.tech` is under consideration; the name is subject to change.

## 1. Elevator Pitch

Değerli (working name; Turkish for "valuable") is a bilingual (Turkish-default, English toggle) value-investing research platform for Borsa İstanbul (BIST), built for the amateur-but-willing-to-learn Turkish retail investor. The first milestone delivers the complete core investing loop — discover, screen, research, value, decide — for BIST 100 stocks with a simple, fully user-adjustable DCF fair-value view, on top of a data foundation that stores history as first-class data and also ingests fund data. It is built to commercial product standards, with the builder's learning and portfolio value as guaranteed by-products and revenue as an explicitly uncommitted stretch goal.

## 2. Problem Statement

Turkish retail investors who want to practice value investing have no accessible home for it: international tools (Finviz, GuruFocus) barely cover BIST; professional terminals are expensive; raw public sources (KAP, TEFAS) require expertise to turn into decisions. As a result, amateur investors either skip fundamental analysis entirely or follow tips. The builder experiences this problem personally as a learner.

## 3. Business Objectives

- OBJ-001: Launch a publicly accessible, bilingual (Turkish default) V1 web app covering the BIST 100 universe by end of December 2026.
- OBJ-002: Make the core investing loop (discover → screen → research → value → decide) functional end-to-end within V1 for every stock in the covered universe.
- OBJ-003: Deepen the builder's own value-investing knowledge through building and using the platform, demonstrated by completing at least one full research session using only the platform.
- OBJ-004: Produce a public repository + README that demonstrates data engineering capability (data pipelines / ETL, data modeling, quantitative calculations, deployment).
- OBJ-005: Validate real-world usefulness with at least 10 external users in the first month after launch (commercial stretch). Revenue is not committed for V1.

## 4. Stakeholders & Personas

- **The builder (solo)** — also the first user. An amateur value-investing learner ("not a pro — trying to internalize this knowledge"). Success for them: learn value investing deeply by building and using the platform; gain a portfolio-grade public artifact; keep the door open to a commercial product. Works intensively (full-time-ish), with the bulk of work targeted for October 2026, using an AI-assisted workflow.
- **Primary persona: the amateur-but-willing-to-learn Turkish retail investor** — between novice and intermediate; does not want to just follow tips; needs metrics presented in plain language; Turkish-speaking (English toggle available). Success for them: independently discover, screen, research, and value BIST stocks.
- **Future external users** — the commercial-stretch audience (at least 10 users in the first month after launch).
- **Portfolio audience (potential employers)** — readers of the public repo. Success for them: see demonstrated data-engineering and quantitative capability.

## 5. Scope — In

Product identity note: this is a research tool with plain-language presentation, not a teaching platform (education content is a low-priority vision item — see Scope — Out).

Committed first milestone = **V0 (data foundation) + V1 (first product) + fund data foundation.**

**V0 — data foundation (not user-facing):**
- Universe: BIST 100 constituents (expandable later); BIST indices; BIST sectors / industries
- Daily (end-of-day) prices and volume
- Financial statements
- Dividends
- Corporate actions
- KAP disclosures
- Fund data foundation: NAV history, performance, holdings where publicly available — data only, no fund UI in V1
- Historical data as first-class: every fact stored with its date/period, so point-in-time screens ("what passed this screen in 2021?") are possible later

**V1 — product (bilingual: Turkish default, English toggle):**
- **BIST dashboard**: BIST 100 and BIST 30 levels, sector performance, market breadth, biggest gainers/losers, volume, market-wide valuation overview (market P/E, dividend yield), basic macro indicators
- **Stock screener** with user-saved screens, across five metric families:
  - Valuation: P/E, P/B, EV/EBITDA, EV/FCF, FCF yield
  - Quality: ROIC, ROE, gross margin, operating margin
  - Growth: revenue CAGR, EPS CAGR, FCF CAGR
  - Financial Health: net debt/EBITDA, interest coverage, current ratio
  - Dividends: dividend yield, dividend CAGR, payout ratio
- **Stock pages** with sections: business description; valuation (incl. historical valuation); financials (revenue → EBITDA → EBIT → net income → FCF); profitability (ROIC / ROE / margins); growth (3Y / 5Y / 10Y CAGR); balance sheet; dividend history
- **Simple, fully user-adjustable DCF**: a per-stock DCF calculator where the user can adjust every model assumption (discount rate and the rest); based on the user's own inputs, it shows how cheap the stock's current (daily) price is relative to the computed fair value — presented in plain language an ordinary investor can understand
- Publicly deployed web app (free/cheap hosting) with a public repo + README

## 6. Scope — Out

**Not in the committed milestone — long-term vision items** (follow the V0–V6 roadmap generally; it is modifiable and these are not commitments):
- Education content system (per-metric explanations: what it is / why it matters / good vs bad / how to interpret) — demoted by the builder to nice-to-have, low priority
- Fund screener and fund pages UI (the fund *data foundation* is in V0 scope; the fund *product* is not)
- Portfolio tracking and portfolio analytics
- Look-through exposure analysis
- Investment ideas / opportunity engine
- BIST Value Score (proprietary 0–100 scoring)
- Historical backtesting of screens
- Advanced charts, side-by-side company comparison, alerts
- KAP intelligence / AI summaries
- Investment thesis builder

**Permanent exclusions (never):**
- Personalized investment advice or buy/sell recommendations — informational purposes only
- Trade execution / order placement
- Real-time or streaming market data — daily/EOD only (streaming data is paid)
- Crypto, FX, bonds, foreign markets
- Native mobile apps
- Paid data sources (revisit only if commercialization justifies the cost)

## 7. Constraints

- **Data budget: $0** — free/public sources only. The **primary data source is KAP reports** (financial statements, dividends, corporate actions, disclosures); candidate supplementary sources are free public APIs such as the İşbank (İş Bankası) API — e.g., for daily prices. TEFAS is public for fund data. Coverage and quality are bounded by what these sources provide; the limitations are accepted.
- **Data freshness: daily (EOD) refresh** — no real-time or streaming data.
- **Timeline** — intensive build (full-time-ish). Target: publicly accessible V1 by end of December 2026. The bulk of the work is targeted for October 2026 while the builder has high daily AI-assist capacity (evren API: 10M daily token limit; actual usage around 100M tokens/day with cache). Slippage into later months is acceptable.
- **Team: solo builder.**
- **Regulatory: informational only** — no investment recommendations, no personalized advice, disclaimers in place. (Personalized investment advice is a licensed activity in Turkey — SPK.)
- **Language: bilingual** — Turkish default, English toggle.
- **Deployment: publicly accessible web app on free/cheap hosting.**
- **Development approach: AI-assisted development is central** to the plan and to the timeline assumptions.

## 8. Assumptions

- ASM-001: The chosen sources — KAP reports as the primary source, plus candidate free public APIs such as the İşbank API — can adequately cover BIST 100 daily prices, financial statements, dividends, corporate actions, and index data for V1. *Validation: verify per-data-type coverage and quality during the V0 build, before the screener depends on it.*
- ASM-002: Public sources (e.g., TEFAS) provide sufficient fund NAV, performance, and holdings data for future fund features; holdings may be available only for some funds. *Validation: measure coverage during the fund data foundation work and record gaps.*
- ASM-003: The BIST 100 universe is sufficient for the first milestone; expansion to all BIST-listed equities is a post-V1 decision. *Validation: builder decision after V1.*
- ASM-004: An informational-only framing (no recommendations, no personalized advice, disclaimers) keeps the platform outside SPK's licensed-advice territory. *Validation: re-verify before any monetization or commercial launch step.*
- ASM-005: The V0–V6 roadmap remains a generally valid guide for post-V1 phases. *Validation: re-plan each phase before committing to it; the roadmap is explicitly modifiable.*
- ASM-006: The builder's current intensive availability and AI-assist capacity hold long enough to reach V1. *Validation: progress review at the end of the October 2026 sprint.*
- ASM-007: "Adjustable in every way" for the DCF means a finite, definable set of user-editable model parameters; the discount rate is confirmed, and the full parameter list is a design decision. *Validation: finalize the parameter list during V1 DCF design.*

## 9. Success Criteria

- SC-001: A publicly accessible bilingual V1 is live by end of December 2026. [OBJ-001]
- SC-002: The core loop works end-to-end on the covered universe: dashboard → screener → stock page → DCF. [OBJ-002]
- SC-003: The screener supports saved screens across all five metric families (valuation, quality, growth, financial health, dividends). [OBJ-002]
- SC-004: A fully user-adjustable DCF (discount rate and other model assumptions) is available for every covered stock and shows how the current daily price compares to the user's computed fair value. [OBJ-002]
- SC-005: The data pipeline refreshes daily without manual intervention. [OBJ-001, OBJ-002]
- SC-006: The builder completes at least one full personal research session using only the platform. [OBJ-003]
- SC-007: A public repo + README demonstrates the data engineering (ETL, data modeling, quantitative calculations). [OBJ-004]
- SC-008: At least 10 external users use the platform within the first month after launch. [OBJ-005]

## 10. Glossary

| Term | Definition (as used in this brief) |
|---|---|
| Değerli | Working name of the platform; Turkish for "valuable" |
| BIST | Borsa İstanbul, the Turkish stock exchange |
| BIST 100 | Index of BIST's 100 constituent stocks; the committed first-milestone universe |
| KAP | Turkey's public disclosure platform for company announcements — the platform's primary data source |
| İşbank API | The free public API of İş Bankası (Turkey); a candidate source for market data such as daily prices |
| TEFAS | Turkey's public platform for investment-fund data (NAV, performance, holdings) |
| SPK | Capital Markets Board of Turkey; personalized investment advice is a licensed activity |
| DCF | Discounted cash flow — the builder's personal first-look valuation method; estimates what future cash flows are worth today |
| Fair value / margin of safety (MOS) | DCF-estimated intrinsic value; margin of safety = the discount of current price to fair value |
| Core investing loop | discover → screen → research → value → decide |
| Metric families | The five screener groups: Valuation, Quality, Growth, Financial Health, Dividends |
| P/E, P/B, EV/EBITDA, EV/FCF, FCF yield | Valuation metrics used in the screener |
| ROIC / ROE | Return on invested capital / return on equity — quality metrics |
| FCF | Free cash flow |
| CAGR | Compound annual growth rate |
| Net debt/EBITDA, interest coverage, current ratio | Financial-health metrics |
| Payout ratio | Share of earnings paid out as dividends |
| NAV | Net asset value of an investment fund |
| EOD / daily refresh | End-of-day data cadence; no real-time or streaming data |
| Market breadth | Share of advancing vs declining stocks in the market |
| Look-through exposure | Aggregating what funds actually own across a portfolio (vision feature, not committed) |
| Value score | Proprietary 0–100 scoring framework (vision feature, not committed) |
| V0 / V1 / V1.5–V6 | Roadmap phase labels: data foundation / first product / later vision phases |
