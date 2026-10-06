# Stock Research & Company Content Analysis

**Domain code:** RES · **Slug:** `stock-research` · **Report order:** 4 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V1 — stock pages; §7) · **Domain map:** `.pipeline/analysis/00-domain-map.md` (incl. scope delta SD-002) · **Carries display obligations resolved at the MDF gate (BR-MDF-010, BR-MDF-011, FR-MDF-011/014/016/018/019)**

## 1. Overview

This domain is the "research" stage of the core investing loop: per-stock pages presenting, in plain bilingual language, what a company is and how its numbers look — business description (bilingual, produced by an offline build-time AI drafting pipeline from KAP filings and reviewed/corrected by the builder), valuation including historical valuation and the vs.-sector strip (scope delta SD-002), financials (revenue → EBITDA → EBIT → net income → FCF), profitability (ROIC / ROE / margins), growth (3Y/5Y/10Y CAGR from available history), balance sheet, and dividend history. It also owns the universe browser — the browsable, searchable list of covered stocks that anchors discovery.

Business value delivered:

- The research stage of the core loop, for every stock in the covered universe. [OBJ-002, SC-002]
- The surface where the builder's own research sessions happen — at least one full session using only the platform. [OBJ-003, SC-006]
- Plain-language company understanding for the amateur persona: what the business does, what its numbers mean, without tip-following. [Brief §2, §4]
- The vs.-sector strip gives multiples the context the target demographic needs, without building the full peer-comparison feature (a vision item). [SD-002, builder rationale at Gate 1]

Screens owned by this domain: **Stock List** (universe browser) and the **Stock Page** with seven sections (Overview & Business Description, Valuation, Financials, Profitability, Growth, Balance Sheet, Dividends). Section layout (tabs vs. scroll) is a design decision.

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Anonymous retail investor | Human, unauthenticated | Understand a company and its numbers; decide whether to value it | Per research session |
| Registered user | Human, authenticated | Same as anonymous (stock pages are identical — public content) | Per research session |
| The builder | Human; first user + content editor | Research stocks [OBJ-003]; review and correct AI-drafted business descriptions before publication | Continuous during content production |
| Offline AI drafting pipeline | System actor, build-time (not user-facing) | Draft bilingual business descriptions from KAP disclosures for every covered stock | Per build cycle |
| Inbound traffic sources | System actors | Stock Screening results, Market Overview gainers/loser and sector links, stock list clicks | Continuous |

## 3. Business Rules

- **BR-RES-001** — Stock pages and the stock list are publicly accessible without an account. *Source: brief §5 (public research posture).* *Enforced: system.*
- **BR-RES-002** — Business descriptions are drafted by the offline, build-time AI pipeline from KAP filings and must be reviewed and corrected by the builder before publication; no AI-generated text is ever visible to users unreviewed. *Source: brief §5 (AI drafting pipeline, reviewed and corrected by the builder).* *Enforced: manual review workflow + system publication gate.*
- **BR-RES-003** — A business description publishes only with both language versions (TR and EN) completed at build time; users are never shown machine translation at display time. *Source: brief §5 (bilingual TR + EN), §7.* *Enforced: system (publication requires both).*
- **BR-RES-004** — Every financial figure on a stock page is the Market Data Foundation's canonical fact or metric (BR-MDF-009); pages compute nothing of their own. *Source: SC-002 consistency.* *Enforced: system.*
- **BR-RES-005** — Any historical series or metric displayed as corporate-action-adjusted carries a visible adjusted-data disclaimer. *Source: builder decision at MDF Gate 2, 2026-10-06 (BR-MDF-010 display side).* *Enforced: system.*
- **BR-RES-006** — Restated figures are displayed per BR-MDF-011: an asterisk on affected figures, a footnote at the bottom of the display, and a warning indicator. *Source: builder decision at MDF Gate 2, 2026-10-06.* *Enforced: system.*
- **BR-RES-007** — Every stock page section displays its data-as-of date; stale data is clearly marked (FR-MDF-016 display side). *Source: brief §7 (EOD); builder staleness decisions.* *Enforced: system.*
- **BR-RES-008** — Missing data is displayed honestly: explicit "no data" states with the coverage boundary (e.g., "no data before 2019 — listed in 2019"), never blanks treated as zeros. *Source: FR-MDF-011/FR-MDF-014 + honest-display principle.* *Enforced: system.*
- **BR-RES-009** — All stock page content is bilingual (TR default, EN toggle), including business descriptions. *Source: brief §7.* *Enforced: system.*
- **BR-RES-010** — Content is written and presented in plain language for a novice-to-intermediate investor. *Source: brief §4.* *Enforced: content policy (review) + presentation.*
- **BR-RES-011** — Stock pages are informational only: no buy/sell language, no opinions or price targets; business descriptions are factual and descriptive, never evaluative; the disclaimer is displayed. *Source: brief §6 (permanent exclusions), §7 (SPK).* *Enforced: content policy + system (disclaimer).*
- **BR-RES-012** — User-facing AI features (KAP summaries, AI Q&A) are out of scope; the reviewed business description is the only AI-derived user-visible content. *Source: brief §5/§6 (explicit distinction between the offline drafting pipeline and user-facing AI vision items).* *Enforced: product boundary.*
- **BR-RES-013** — Growth section shows 3Y/5Y/10Y CAGRs (10Y targeted); where history is shorter, CAGRs come from available history with the actual window indicated. *Source: brief §5; FR-MDF-014.* *Enforced: system.*

## 4. Use Cases

### UC-RES-001 — Browse and find stocks (universe browser)
**Primary actor:** anonymous retail investor
**Preconditions:** universe data current.
**Main success scenario:**
1. The user opens the Stock List.
2. All covered stocks are listed with name, code, and sector.
3. The user filters by sector and/or searches by name or code.
4. The user clicks a stock → its stock page opens.
**Alternate / error flows:** (a) Search returns nothing → explicit "no matching stock" state.
**Postconditions:** the user has entered a specific stock's research page.

### UC-RES-002 — Understand what the company does
**Primary actor:** anonymous retail investor
**Preconditions:** business description published for the stock (UC-RES-004).
**Main success scenario:**
1. The user opens the stock page (Overview & Business Description section).
2. The user reads a plain-language description of the business, in their selected language (TR/EN).
3. The description carries its last-reviewed date.
**Alternate / error flows:** (a) Description not yet reviewed/published → the honest "description in preparation" state is shown (staged publication per OQ-RES-003 decision, Gate 2 2026-10-06).
**Postconditions:** the user knows what the company does and how it makes money, in plain language.

### UC-RES-003 — Study the stock's fundamentals
**Primary actor:** anonymous retail investor
**Preconditions:** canonical data present for the stock (or coverage boundaries known).
**Main success scenario:**
1. The user moves through the stock page sections: Valuation (current + historical, plus the vs.-sector strip), Financials (revenue → EBITDA → EBIT → net income → FCF), Profitability (ROIC/ROE/margins), Growth (3/5/10Y CAGRs), Balance Sheet, Dividends.
2. Every section shows canonical figures with as-of dates; historical series carry the adjusted-data disclaimer; restated figures carry asterisk/footnote/warning.
3. Where data is missing, the section shows the explicit no-data state with the coverage boundary.
4. The user forms a picture of the company's valuation, quality, growth, health, and dividends.
**Alternate / error flows:** (a) Stale data → as-of date + stale marker shown. (b) A stock's shorter history → CAGR shown with actual window indicated.
**Postconditions:** the user has a complete, honest fundamentals picture and can proceed to valuation.

### UC-RES-004 — Produce, review, and publish a business description
**Primary actor:** the builder (content editor), assisted by the offline AI drafting pipeline
**Preconditions:** KAP disclosures ingested for the stock (FR-MDF-005).
**Main success scenario:**
1. The offline pipeline drafts a business description from the stock's KAP filings, in TR and EN.
2. The builder reviews the draft for factual accuracy and plain-language quality.
3. The builder corrects both language versions.
4. The builder publishes; the description becomes visible on the stock page with its last-reviewed date.
**Alternate / error flows:** (a) The draft is inadequate → the builder re-drafts or rewrites manually. (b) Material new KAP filings arrive later → the description is re-drafted/re-reviewed on the next build cycle.
**Postconditions:** every published description is builder-reviewed, bilingual, and factually grounded in KAP filings.

### UC-RES-005 — Move from research to valuation
**Primary actor:** anonymous retail investor
**Preconditions:** user is on a stock page.
**Main success scenario:**
1. The user selects the DCF calculator from the stock page.
2. The DCF calculator opens for that stock (Valuation & DCF domain).
**Alternate / error flows:** none.
**Postconditions:** the user has moved from research to the value/decide stage — completing the SC-002 chain: dashboard → screener → stock page → DCF.

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-RES-001 | The system shall display a browsable list of all covered stocks with name, code, and sector. | UC-RES-001 | Must |
| FR-RES-002 | The system shall let the user filter the stock list by sector. | UC-RES-001 | Must |
| FR-RES-003 | The system shall let the user search the stock list by stock name or code. | UC-RES-001 | Must |
| FR-RES-004 | The system shall provide a stock search accessible from every screen (global header search). | UC-RES-001 | Should |
| FR-RES-005 | The system shall display the stock's business description in the user's selected language, from builder-reviewed TR and EN versions. | UC-RES-002 | Must |
| FR-RES-006 | The system shall display a valuation section with current and historical valuation metrics. | UC-RES-003 | Must |
| FR-RES-007 | The system shall display a vs.-sector strip showing the stock's key multiples next to sector medians, in plain language (scope delta SD-002). | UC-RES-003 | Should |
| FR-RES-008 | The system shall display a financials section covering revenue → EBITDA → EBIT → net income → FCF. | UC-RES-003 | Must |
| FR-RES-009 | The system shall display a profitability section covering ROIC, ROE, and margins. | UC-RES-003 | Must |
| FR-RES-010 | The system shall display a growth section with 3Y/5Y/10Y CAGRs, indicating the actual window used where history is shorter. | UC-RES-003 | Must |
| FR-RES-011 | The system shall display a balance sheet section. | UC-RES-003 | Must |
| FR-RES-012 | The system shall display a dividend history section. | UC-RES-003 | Must |
| FR-RES-013 | The system shall display the adjusted-data disclaimer on every historical series or metric shown as corporate-action-adjusted (BR-RES-005). | UC-RES-002, UC-RES-003 | Must |
| FR-RES-014 | The system shall mark restated figures with an asterisk, a bottom-of-display footnote, and a warning indicator (BR-RES-006). | UC-RES-002, UC-RES-003 | Must |
| FR-RES-015 | The system shall display the data-as-of date on every stock page section and mark stale data as such. | UC-RES-002, UC-RES-003 | Must |
| FR-RES-016 | The system shall display an explicit no-data state with the coverage boundary wherever data is missing, using the listing/IPO date as context where relevant (BR-RES-008). | UC-RES-003 | Must |
| FR-RES-017 | The system shall display the informational-only disclaimer on the stock list and all stock pages. | UC-RES-002, UC-RES-003 | Must |
| FR-RES-018 | The system shall provide navigation from a stock page to the DCF calculator for that stock. | UC-RES-005 | Must |
| FR-RES-019 | The offline pipeline shall produce draft business descriptions in both TR and EN from KAP filings for every covered stock. | UC-RES-004 | Must |
| FR-RES-020 | The system shall prevent publication of a business description that has not been reviewed and approved by the builder. | UC-RES-004 | Must |
| FR-RES-021 | The system shall let the builder edit and correct both language versions of a draft before publishing. | UC-RES-004 | Must |
| FR-RES-022 | The system shall display the business description's last-reviewed date. | UC-RES-002 | Should |
| FR-RES-026 | The system shall display an explicit "description in preparation" state on stock pages whose business description has not yet been published (staged publication, OQ-RES-003). | UC-RES-002 | Must |
| FR-RES-027 | The system shall display book value (total equity) and book value per share in the stock page Balance Sheet section, under the canonical-metric discipline (BR-MDF-009). | UC-RES-003 | Should |
| FR-RES-023 | The system shall provide user-facing AI summaries of KAP disclosures. | — | Won't (this release — vision item, brief §6) |
| FR-RES-024 | The system shall provide side-by-side company comparison. | — | Won't (this release — vision item, brief §6) |
| FR-RES-025 | The system shall provide per-metric education content (what it is / why it matters / good vs. bad). | — | Won't (this release — demoted vision item, brief §6) |

## 6. Non-Functional Requirements

- **NFR-RES-001 — Bilingual completeness:** 100% of stock page content (section labels, descriptions, disclaimers, no-data states, adjusted/restatement notices) exists in TR and EN, TR default [brief §7]; descriptions publish only in both languages (BR-RES-003).
- **NFR-RES-002 — Plain-language quality:** business descriptions are written for a novice-to-intermediate reader; the builder's review is the quality gate [brief §4, BR-RES-002].
- **NFR-RES-003 — Cross-surface consistency:** for the same stock, metric, and date, values on the stock page equal the screener's and dashboard's (canonical metrics, BR-MDF-009).
- **NFR-RES-004 — Freshness split:** financial figures reflect the latest EOD refresh; business descriptions reflect the latest builder-reviewed build cycle, always carrying the last-reviewed date.
- **NFR-RES-005 — Public availability:** stock list and stock pages reachable without registration [brief §5].
- **NFR-RES-006 — AI content governance:** no AI-generated text is user-visible without builder review (BR-RES-002); the description pipeline runs offline at build time, never on user request.

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| Business description | stock, TR text, EN text, status (draft / reviewed / published), source KAP references, last-reviewed date, version | Stock 1 → * versions (draft lineage); current published version served |
| Sector metric median | sector, metric, as-of date/period, median value | derived view powering the vs.-sector strip (SD-002); Sector 1 → * medians per metric per date; exclusion rules consistent with BR-MOV-010 |

The Stock List is a view over MDF's instruments (with sector and listing data), not an owned entity. All financial figures shown are views of MDF canonical facts (BR-RES-004).

## 8. Dependencies

**Upstream:**
- **Market Data Foundation** — all financial facts and canonical metrics; CAGRs with window indication (FR-MDF-014); coverage metadata incl. listing/IPO date (FR-MDF-011); adjusted/raw marking (FR-MDF-018); restatement markers (FR-MDF-019); staleness markers (FR-MDF-016); KAP disclosures as drafting input (FR-MDF-005).
- **Offline AI drafting pipeline** — consumes KAP disclosures; owned within this domain as a build-time capability.

**Downstream / adjacent:**
- **Valuation & DCF** — receives navigation from stock pages (FR-RES-018).
- **Inbound:** Stock Screening result links (FR-SCR-004); Market Overview gainer/loser and sector links (FR-MOV-011/012).
- No dependency on User Accounts (public content, BR-RES-001).

## 9. Open Questions & Risks

- **OQ-RES-001 — Global header search — RESOLVED at Gate 2 (2026-10-06).** Decision: add the global stock search, accessible from every screen (FR-RES-004, Should confirmed). *Rationale on record: fastest discover path across a 100-stock universe.*
- **OQ-RES-002 — Vs.-sector strip definition — RESOLVED at Gate 2 (2026-10-06).** Decision (principle approved): the strip shows valuation-family metrics as sector medians over covered-universe peers; loss-makers are excluded from P/E medians consistent with BR-MOV-010, with exclusions disclosed on screen; final metric set and period basis finalized with the architect during design. *Impact note retained: inconsistent strip numbers would violate the canonical-metrics discipline.*
- **OQ-RES-003 — Description coverage at launch — RESOLVED at Gate 2 (2026-10-06).** Decision (option B): staged publication — stock pages go live with the honest "description in preparation" state (FR-RES-026); reviewed descriptions publish progressively. *Rationale on record: SC-002's loop needs the numbers sections, not the prose; protects the December timeline.*
- **OQ-RES-004 — Book value metric placement — RESOLVED at Gate 2 (2026-10-06).** Decision (option a): book value (total equity) and book value per share are displayed in the stock page Balance Sheet section (FR-RES-027, Should). Option b (Valuation section) declined; screener unchanged — P/B already covers price-vs-book screening (P/B max 1 = price below book value). *Note: book value per share is derivable from canonical facts (equity, share count) — no new data ingestion; single canonical definition per BR-MDF-009.*
- **RISK-RES-001 — AI draft accuracy.** A hallucinated or outdated claim about a company is a credibility and (indirectly) regulatory-content risk. *Mitigation: KAP-grounded drafting, mandatory builder review (BR-RES-002), factual-not-evaluative content rule (BR-RES-011), last-reviewed date (FR-RES-022).*
- **RISK-RES-002 — Description staleness.** Companies change (mergers, new segments); descriptions rot between build cycles. *Mitigation: refresh trigger on material new KAP filings (UC-RES-004 alternate b), last-reviewed date always visible.*
