# Domain Map

**Project:** Değerli (working name) — BIST Value Investing Platform
**Source of truth:** `.pipeline/00-project-brief.md` (approved)
**Date:** 2026-10-05

**How to read this map.** Domains are business capabilities, not technical layers. All seven domains are **Must** at domain level because the whole milestone (V0 data foundation + V1 product + fund data foundation) is committed in the brief; FR-level MoSCoW inside each domain report provides the Must/Should/Could nuance. The **Key Screens** column and the Screen Inventory below capture the UX surface of V1 per domain — what screens exist and which stage of the core loop (discover → screen → research → value → decide) they serve. Screen *layout* (tabs vs. scroll, component placement) is a design decision for the architect/planner, not part of this analysis.

## Domains

| Domain | Description (2-3 sentences) | Owned Data Entities | Dependencies | Key Screens | MoSCoW |
|--------|-----------------------------|---------------------|--------------|-------------|--------|
| **Market Data Foundation** (slug: `market-data-foundation`) | Builds and maintains the single source of truth for all BIST equity facts: the BIST 100 universe (constituents, indices, sectors/industries), daily EOD prices and volume, financial statements, dividends, corporate actions, and KAP disclosures. Every fact is stored with its date/period so history is first-class and point-in-time queries are possible later; ingest depth target is 10 years or as far back as sources allow. Not user-facing (V0); every product domain consumes its data. | Instrument (listed stock); Index; Sector/Industry; Constituent membership; Daily EOD price & volume; Financial statement (by period); Dividend; Corporate action; KAP disclosure | External sources only — KAP (primary), İşbank API (candidate, e.g. daily prices). No internal dependencies. | None (headless; feeds all screens) | Must |
| **Market Overview & Macro Indicators** (slug: `market-overview`) | The daily "discover" entry point of the core loop: a BIST dashboard with BIST 100/30 index levels, sector performance, market breadth, biggest gainers/losers, volume, and a market-wide valuation overview (market P/E, dividend yield). Also owns the macro series it displays: TÜİK CPI plus an independent inflation measure (e.g., ENAG), CBRT 1-week repo rate, USD/TRY, EUR/TRY, and gold price. All presented in plain language for the amateur-but-willing-to-learn investor. | Macro indicator series & values; Market overview snapshot (derived daily aggregates: breadth, sector performance, market-wide valuation) | Market Data Foundation (prices, index levels, constituents, dividends); external macro sources (TÜİK, ENAG, CBRT, FX/gold — ASM-009) | Market Overview (home page) | Must |
| **Stock Screening** (slug: `stock-screening`) | Lets users find candidate stocks across the BIST 100 universe by filtering on five metric families — Valuation, Quality, Growth, Financial Health, Dividends — with metrics presented in plain language. Authenticated users can save screens and re-run them; saved screens are persisted server-side per user account. Results link directly into stock pages, connecting screen → research. | Saved screen (user-owned name + criteria set) | Market Data Foundation (all metric inputs); User Accounts (identity for saved screens) | Screener (criteria builder + results); My Saved Screens | Must |
| **Stock Research & Company Content** (slug: `stock-research`) | The "research" stage: per-stock pages presenting the bilingual business description (produced by an offline, build-time AI drafting pipeline from KAP filings and reviewed/corrected by the builder), valuation incl. historical valuation and a vs.-sector strip comparing the stock's key multiples to sector medians (scope delta SD-002), financials (revenue → EBITDA → EBIT → net income → FCF), profitability (ROIC / ROE / margins), growth (3Y/5Y/10Y CAGR, computed from available history where it is shorter), balance sheet, and dividend history. Also owns the universe browser — the browsable/searchable list of covered stocks that anchors discovery. | Business description (TR + EN, with draft/review status from the offline AI pipeline); Sector metric medians (derived view powering the vs.-sector strip, SD-002) | Market Data Foundation (all displayed financial facts are views of its data); no dependency on User Accounts (stock pages are public) | Stock List (universe browser); Stock Page with sections: Overview & Business Description, Valuation, Financials, Profitability, Growth, Balance Sheet, Dividends | Must |
| **Valuation & DCF** (slug: `valuation-dcf`) | The "value & decide" stage: a per-stock DCF calculator where the user can adjust every model assumption (discount rate and the rest — full parameter list per ASM-007). It computes fair value from the user's own inputs and shows, in plain language an ordinary investor can understand, how cheap or expensive the current daily price is relative to that fair value (margin of safety), plus a sensitivity table showing how fair value moves across a grid of key assumptions such as discount rate × growth (scope delta SD-001). | DCF model baseline per stock (build-time defaults); user DCF scenario (persistence is an open question — see domain report) | Market Data Foundation (financial statement inputs, current daily price); Stock Research (navigation context); User Accounts (only if user scenarios are persisted) | DCF Calculator (per stock) | Must |
| **User Accounts & Personalization** (slug: `user-accounts`) | Registration and login giving users a server-side identity, required for persisting saved screens per account. Owns the user's language preference (Turkish default, English toggle) and registration data, collected minimally to keep KVKK exposure low. Research content stays publicly accessible without an account. | User account (credentials, language preference, minimal registration data) | No internal dependencies; external obligation: KVKK compliance | Register; Sign In; Account Settings | Must |
| **Fund Data Foundation** (slug: `fund-data-foundation`) | Ingests and stores publicly available fund data from TEFAS — NAV history, performance, and holdings where published. A data-only capability in this release (no fund UI — explicitly out of V1 scope); it de-risks future fund features and records coverage gaps per ASM-002. | Fund; Fund NAV history; Fund performance; Fund holdings | External source only (TEFAS); no internal consumers in V1 | None (headless; fund UI is Won't-this-release) | Must (data only) |

## Scope deltas (builder decisions, post-brief)

Both additions below were decided by the builder during the Gate 1 review on 2026-10-05. They extend — do not replace — the approved brief. Analysis documents never modify the brief; the consolidated paste-ready delta text for all post-brief scope decisions (Gate 1 and Gate 2) lives in `.pipeline/analysis/brief-delta.md`. Until it is pasted into the brief, the analysis package is the authoritative record of these decisions.

| ID | Addition | Where it lands | Proposed FR-level MoSCoW (confirm at report gate) |
|---|---|---|---|
| SD-001 | DCF sensitivity table: fair value displayed across a grid of key assumptions (e.g., discount rate × growth), so the user sees how sensitive the result is | Valuation & DCF domain — DCF Calculator screen | Should |
| SD-002 | Vs.-sector relative-valuation strip: the stock's key multiples shown next to sector medians, in plain language for the amateur persona | Stock Research domain — Stock Page, Valuation section | Should |

Rationale recorded from the builder: SD-001 — easy to implement, natural extension of the adjustable DCF; SD-002 — good for the target demographic (gives multiples context without a full peer-comparison feature, which remains a vision item per brief §6).

Proposed brief delta text (for the builder to paste into §5, V1):
- Stock pages → valuation: "valuation (incl. historical valuation and a vs.-sector comparison of the stock's key multiples against sector medians)"
- Simple, fully user-adjustable DCF → append: "plus a sensitivity table showing fair value across a grid of key assumptions (e.g., discount rate × growth)"

## Cross-cutting concerns (apply to every domain; enforced via each domain's FRs/NFRs — deliberately not domains)

1. **Bilingual content** — Turkish default, English toggle. Everything displayed (UI text, dashboard, stock page content, business descriptions, DCF explanations) exists in both TR and EN; KAP source documents remain in original Turkish; financial figures are language-neutral. (Brief §7, Constraints — Language.)
2. **Plain-language presentation** — the primary persona is between novice and intermediate and does not want to follow tips; metrics and DCF output must be understandable without prior expertise. (Brief §4.)
3. **Informational-only compliance (SPK)** — no buy/sell recommendations, no personalized advice; disclaimers on advice-adjacent surfaces (dashboard, stock pages, DCF). (Brief §6 permanent exclusions, §7 regulatory.)
4. **Point-in-time historical correctness** — every fact stored with its date/period; owned by Market Data Foundation, consumed by all. (Brief §5, V0.)
5. **Daily (EOD) data cadence** — no real-time or streaming data anywhere. (Brief §7.)

## V1 Screen Inventory (UX plan)

| # | Screen | Domain | Purpose (user goal) | Core-loop stage |
|---|--------|--------|---------------------|-----------------|
| 1 | Market Overview (home) | market-overview | See market state (indices, sectors, breadth, gainers/losers, valuation overview) + macro indicators; find entry points into stocks | Discover |
| 2 | Stock List (universe browser) | stock-research | Browse/search all covered BIST 100 stocks (by sector, by name) to pick what to research | Discover |
| 3 | Screener | stock-screening | Build and run a screen across the five metric families; see matching stocks | Screen |
| 4 | My Saved Screens | stock-screening | Re-run, rename, delete persisted screens (per account) | Screen |
| 5 | Stock Page — Overview & Business Description | stock-research | Understand what the company does, in plain TR/EN | Research |
| 6 | Stock Page — Valuation | stock-research | See current + historical valuation (P/E, etc.) alongside sector medians (vs.-sector strip, SD-002) | Research → Value |
| 7 | Stock Page — Financials | stock-research | Revenue → EBITDA → EBIT → net income → FCF | Research |
| 8 | Stock Page — Profitability | stock-research | ROIC, ROE, margins | Research |
| 9 | Stock Page — Growth | stock-research | 3Y/5Y/10Y CAGRs | Research |
| 10 | Stock Page — Balance Sheet | stock-research | Financial position | Research |
| 11 | Stock Page — Dividends | stock-research | Dividend history | Research |
| 12 | DCF Calculator (per stock) | valuation-dcf | Adjust every DCF assumption; see fair value vs. current price in plain language; margin of safety; sensitivity grid of fair value across key assumptions (SD-001) | Value → Decide |
| 13 | Register | user-accounts | Create an account (minimal data, KVKK-aware) | — |
| 14 | Sign In | user-accounts | Authenticate to access saved screens | — |
| 15 | Account Settings | user-accounts | Language preference, entry to saved screens, account deletion | — |

**Notes on the inventory:**
- All 15 screens are required for the core loop to work end-to-end (SC-002: dashboard → screener → stock page → DCF); screens 13–15 are required for saved screens (SC-003).
- Every screen carries the global language toggle and informational-only disclaimer (cross-cutting concerns above).
- The Stock Page "sections" (5–11) are content requirements; whether they render as tabs, an accordion, or one scrolling page is a design decision outside this analysis.
- A global stock search (search box in the header, reachable from any screen) is a candidate UX addition not explicitly in the brief — it will be proposed as a Should-level FR in the Stock Research report and decided at that report's gate.

## Domain report order

Reports will be produced and approved in this order (all Must; order follows the core loop, data foundation first):

1. `market-data-foundation.md` — everything depends on it
2. `market-overview.md` — discover
3. `stock-screening.md` — screen
4. `stock-research.md` — research
5. `valuation-dcf.md` — value / decide
6. `user-accounts.md` — identity for persistence
7. `fund-data-foundation.md` — data-only, no V1 consumers
