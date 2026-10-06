# Market Overview & Macro Indicators Analysis

**Domain code:** MOV · **Slug:** `market-overview` · **Report order:** 2 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V1 — BIST dashboard, §7, §8 ASM-009) · **Domain map:** `.pipeline/analysis/00-domain-map.md`

## 1. Overview

This domain is the daily "discover" entry point of the core investing loop: a public BIST dashboard showing BIST 100 and BIST 30 index levels, sector performance, market breadth, biggest gainers/losers, market volume, and a market-wide valuation overview (market P/E, dividend yield), alongside five macro indicators — official TÜİK CPI *plus* an independent inflation measure (e.g., ENAG, because the builder considers the official figures understated), CBRT 1-week repo rate, USD/TRY, EUR/TRY, and gold price. The domain also owns ingestion and storage of the macro series, since the dashboard is their only V1 consumer.

Business value delivered:

- The first screen of the core loop: the user starts the day here and picks where to go next (a stock, a sector, the screener). [OBJ-002, SC-002]
- A publicly accessible, bilingual, plain-language market home makes the platform immediately useful to anonymous visitors — the audience for the 10-user validation stretch. [OBJ-001, OBJ-005]
- The dual-inflation display is a deliberate trust position: presenting the official and independent measures side by side, sourced and dated, serves investors who share the builder's skepticism of official figures. [Brief §5, ASM-009]

Screen owned by this domain: **Market Overview (home page)** — see UC-MOV-001 for its content contract.

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Anonymous retail investor (primary persona) | Human, unauthenticated | Grasp today's market state in plain language; find a stock or sector to dig into | Daily or near-daily |
| Registered user | Human, authenticated | Same as above; then jump into saved screens | Daily |
| The builder | Human; first user + data steward | Use the dashboard as the start of a research session [OBJ-003]; monitor macro source quality (ASM-009) | Daily |
| Automated macro ingestion | System actor | Pull the six macro series from their sources on each series' cadence | Daily (FX/gold/rate) / monthly (CPI) / per release |
| External macro sources | External systems | TÜİK (CPI), ENAG or equivalent (independent CPI), CBRT (1-week repo), free FX/gold sources | Per cadence |
| Downstream domains | System actors | Receive navigation traffic (stock pages, stock list, screener) and macro facts if needed later | Continuous |

## 3. Business Rules

- **BR-MOV-001** — All dashboard data is end-of-day and carries a visible data-as-of date; stale data (source outage) is explicitly marked as such rather than silently shown as current. *Source: brief §7 (EOD constraint); FR-MDF-016.* *Enforced: system.*
- **BR-MOV-002** — Inflation is always displayed as a pair: the official TÜİK CPI and an independent measure (e.g., ENAG), side by side, each labeled with its source and as-of date. Neither is hidden or merged. *Source: brief §5 (the builder considers official figures understated).* *Enforced: system.*
- **BR-MOV-003** — Every macro value is shown with source attribution and its release/as-of date. *Source: brief §7 (free/public sources), trust position.* *Enforced: system.*
- **BR-MOV-004** — The 10Y government bond yield is not displayed; it was considered and rejected. *Source: brief §5 (explicit).* *Enforced: product decision — do not reintroduce.*
- **BR-MOV-005** — The dashboard is descriptive and informational only: no buy/sell language, no "top picks" framing; the informational-only disclaimer is displayed. *Source: brief §6 (permanent exclusions), §7 (SPK).* *Enforced: system (disclaimer) + content policy.*
- **BR-MOV-006** — All dashboard content is bilingual: Turkish default, English toggle. *Source: brief §7 (Language).* *Enforced: system.*
- **BR-MOV-007** — The dashboard is publicly accessible without an account. *Source: brief §5 (research tool, public deployment; accounts exist for saved screens).* *Enforced: system.*
- **BR-MOV-008** — Each macro series has one canonical stored value per date, owned by this domain (mirroring the canonical-facts principle of BR-MDF-009). *Source: domain map (owned entities).* *Enforced: system.*
- **BR-MOV-009** — All market aggregates (breadth, sector performance, gainers/losers, market valuation, volume) are computed from the Market Data Foundation's canonical facts — no dashboard-side re-derivation. *Source: BR-MDF-009 consistency; SC-002.* *Enforced: system.*
- **BR-MOV-010** — The market P/E aggregate excludes loss-making companies (negative earnings render P/E meaningless), and the exclusion is disclosed on the dashboard; the aggregation formula (e.g., cap-weighted vs. median) is a design decision made with the architect. *Source: builder decision at Gate 2 review, 2026-10-06 (resolves OQ-MOV-004).* *Enforced: system (computation + on-screen disclosure).*
- **BR-MOV-011** — The biggest gainers/losers lists rank the top 10 by daily % price change, but only among stocks meeting a minimum traded-volume eligibility threshold; the threshold value is set during design once the real volume distribution is known. Each listed entry also displays its traded volume. *Source: builder decision at Gate 2 review, 2026-10-06 (resolves OQ-MOV-003).* *Enforced: system.*

## 4. Use Cases

### UC-MOV-001 — Check today's market state
**Primary actor:** anonymous retail investor
**Preconditions:** daily EOD refresh has run (UC-MDF-001); macro series ingested (UC-MOV-002).
**Main success scenario:**
1. The user opens the platform home page (no account needed).
2. The dashboard shows: BIST 100 and BIST 30 levels with daily change; sector performance; market breadth; biggest gainers and losers; market volume; market-wide P/E and dividend yield; the macro strip (TÜİK CPI, independent inflation, CBRT 1-week repo, USD/TRY, EUR/TRY, gold).
3. Every block carries its data-as-of date; the language toggle (TR/EN) is available.
4. The user grasps the market picture and chooses a next step.
**Alternate / error flows:** (a) A macro source is stale/unavailable → that indicator shows the last-known value with its as-of date and a stale marker (FR-MOV-016). (b) Equity data is stale → the dashboard shows the as-of date prominently (BR-MOV-001).
**Postconditions:** the user has a plain-language market overview and one or more entry points into stocks or the screener.

### UC-MOV-002 — Ingest macro indicators
**Primary actor:** automated macro ingestion (owned by this domain)
**Preconditions:** sources reachable per series; series cadence known (CPI monthly, policy rate per CBRT decision, FX/gold daily).
**Main success scenario:**
1. The ingestion pulls each series on its cadence from its source.
2. Values are stored with date, source reference, and unit (BR-MOV-008).
3. The dashboard serves the latest value per series.
**Alternate / error flows:** (a) The independent inflation source (no official API — ASM-009) is unreachable → keep last-known value, mark stale, alert the builder (FR-MOV-015, FR-MOV-016). (b) A series revises a past value → the revision is stored as a new dated record, consistent with BR-MDF-004's no-overwrite principle.
**Postconditions:** macro series current as of each source's latest release; gaps visible.

### UC-MOV-003 — Compare official vs. independent inflation
**Primary actor:** anonymous retail investor
**Preconditions:** both inflation series ingested.
**Main success scenario:**
1. The user views the macro strip.
2. Official TÜİK CPI and the independent measure are displayed side by side, each with source label and as-of date.
3. The user sees both figures and their divergence, in plain language, without editorial commentary from the platform.
**Alternate / error flows:** (a) The independent measure is unavailable → the official figure shows alone with a note that the independent measure is unavailable (not silently dropped).
**Postconditions:** the user understands inflation is measured differently by different sources and sees both numbers.

### UC-MOV-004 — Navigate from the dashboard into research or screening
**Primary actor:** anonymous retail investor or registered user
**Preconditions:** dashboard rendered.
**Main success scenario:**
1. The user clicks a gainer/loser entry → the stock page opens.
2. The user clicks a sector → the stock list opens, filtered to that sector.
3. The user selects the screener from navigation → the screener opens.
**Alternate / error flows:** (a) A linked stock has no stock page (should not happen for covered universe) → stock list fallback.
**Postconditions:** the user has left the dashboard toward a specific stock, a sector's stocks, or the screener — the discover stage hands off to screen/research. [SC-002]

### UC-MOV-005 — Detect macro source failure or staleness
**Primary actor:** the builder (data steward)
**Preconditions:** macro ingestion runs on schedule.
**Main success scenario:**
1. A source fails, returns invalid data, or is late.
2. The system logs the failure and notifies the builder (log alert + e-mail).
3. The dashboard continues showing the last-known value with its as-of date and stale marker.
4. The builder investigates and, if needed, selects a replacement source.
**Alternate / error flows:** (a) A source is permanently dead (e.g., the independent measure disappears) → the indicator is retired or replaced by builder decision; the change is recorded.
**Postconditions:** macro data issues are surfaced to the operator, never silently hidden from users.

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-MOV-001 | The system shall display BIST 100 and BIST 30 index levels with daily change. | UC-MOV-001 | Must |
| FR-MOV-002 | The system shall display daily sector performance for the covered universe. | UC-MOV-001 | Must |
| FR-MOV-003 | The system shall display market breadth (share of advancing vs. declining stocks in the covered universe). | UC-MOV-001 | Must |
| FR-MOV-004 | The system shall display the biggest daily gainers and losers (top 10 by daily price change, per the eligibility rule BR-MOV-011). | UC-MOV-001 | Must |
| FR-MOV-005 | The system shall display total market volume for the covered universe. | UC-MOV-001 | Must |
| FR-MOV-006 | The system shall display a market-wide valuation overview: market P/E (computed per BR-MOV-010) and market dividend yield for the covered universe. | UC-MOV-001 | Must |
| FR-MOV-007 | The system shall display the macro indicators: official TÜİK CPI, an independent inflation measure (e.g., ENAG), CBRT 1-week repo rate, USD/TRY, EUR/TRY, and gold price. | UC-MOV-001, UC-MOV-003 | Must |
| FR-MOV-008 | The system shall display the official and independent inflation measures side by side, each with its source label. | UC-MOV-003 | Must |
| FR-MOV-009 | The system shall display every macro value with source attribution and its release/as-of date. | UC-MOV-001, UC-MOV-003 | Must |
| FR-MOV-010 | The system shall display the data-as-of date on the dashboard, and clearly mark any stale (last-known-good) data as such. | UC-MOV-001 | Must |
| FR-MOV-011 | The system shall make each gainer/loser entry clickable, linking to that stock's page. | UC-MOV-004 | Must |
| FR-MOV-012 | The system shall make each sector performance entry clickable, linking to the stock list filtered to that sector. | UC-MOV-004 | Must |
| FR-MOV-013 | The system shall provide navigation from the dashboard to the screener and to the stock list. | UC-MOV-004 | Must |
| FR-MOV-014 | The system shall ingest and store each macro series on its cadence, with date, unit, and source reference per value. | UC-MOV-002 | Must |
| FR-MOV-015 | The system shall notify the builder via log alert and e-mail when a macro source fails, returns invalid data, or is late. | UC-MOV-005 | Should |
| FR-MOV-016 | The system shall continue displaying the last-known value of a macro series (with as-of date and stale marker) when its source is unavailable. | UC-MOV-002, UC-MOV-005 | Should |
| FR-MOV-017 | The system shall offer a selectable time period for sector performance (e.g., 1W / 1M / YTD). | UC-MOV-001 | Could |
| FR-MOV-018 | The system shall display historical time-series charts for macro indicators. | UC-MOV-001, UC-MOV-003 | Won't (this release — not in brief scope) |
| FR-MOV-019 | The system shall display the 10Y government bond yield as a macro indicator. | — | Won't (rejected in brief §5 — permanent) |
| FR-MOV-020 | The system shall display the informational-only disclaimer on the dashboard. | UC-MOV-001 | Must |
| FR-MOV-021 | The system shall display the daily traded volume alongside each gainer/loser entry in the top movers lists (data already ingested per FR-MDF-001). | UC-MOV-001 | Should |

## 6. Non-Functional Requirements

- **NFR-MOV-001 — Freshness:** the dashboard reflects the latest completed trading day's EOD equity data; macro values are as fresh as each source's cadence allows (FX/gold daily, CPI monthly, policy rate per CBRT decision), always with an explicit as-of date.
- **NFR-MOV-002 — Bilingual completeness:** 100% of dashboard content (labels, indicator names, tooltips, disclaimer, stale-data notices) exists in Turkish and English, Turkish default [brief §7].
- **NFR-MOV-003 — Public availability:** the dashboard is reachable without registration or login, at all times the platform is up [brief §5].
- **NFR-MOV-004 — Plain-language presentation:** every indicator is labeled in plain language with its unit and as-of date, understandable to a novice-to-intermediate investor; per-metric teaching content (what it is / why it matters) is out of scope — education content is a demoted vision item [brief §4, §5, §6].
- **NFR-MOV-005 — Cost:** macro data acquisition cost remains $0 (free/public sources only, including the independent inflation measure) [brief §7, ASM-009].
- **NFR-MOV-006 — Regulatory posture:** no element of the dashboard constitutes investment advice or a recommendation; the informational-only disclaimer is always visible [brief §6, §7].

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| Macro indicator series | code, name (TR/EN), unit, source, cadence, description label | six V1 series: TÜİK CPI, independent CPI, CBRT 1-week repo, USD/TRY, EUR/TRY, gold |
| Macro indicator value | series, date, value, source reference | many per series; one canonical value per series per date (BR-MOV-008) |
| Market overview snapshot | date, index levels + daily change, breadth counts, sector performance set, top gainers/losers set, market volume, market P/E, market dividend yield | one per trading day; derived from MDF canonical facts (BR-MOV-009), not independently owned truth |

## 8. Dependencies

**Upstream:**
- **Market Data Foundation** — index levels, EOD prices and volume, constituent membership, sector classifications, dividends, canonical metrics. All market aggregates on the dashboard derive from MDF facts (BR-MOV-009).
- **External macro sources** — TÜİK, ENAG (or equivalent independent measure — no official API, ASM-009), CBRT, free FX/gold sources. Selection and terms verification happens during V0.

**Downstream:**
- **Stock Research** — receives click-through traffic via gainer/loser and sector links (FR-MOV-011, FR-MOV-012).
- **Stock Screening** — receives navigation traffic (FR-MOV-013).
- No dependency on User Accounts (dashboard is public, BR-MOV-007).

## 9. Open Questions & Risks

- **OQ-MOV-001 — Independent inflation source.** ENAG (the candidate) has no official API and its redistribution terms are unverified. *Impact if unresolved by end of V0:* the dual-inflation display (a deliberate trust position of the product) degrades to official CPI only. *Owner: builder, during V0 (ASM-009 validation).*
- **OQ-MOV-002 — FX and gold daily source.** A free, reliable daily source for USD/TRY, EUR/TRY, and gold must be selected. *Impact if unresolved:* the macro strip shows three "unavailable" indicators. *Owner: builder, during V0.*
- **OQ-MOV-003 — Gainers/losers definition — RESOLVED at Gate 2 (2026-10-06).** Decision (option A): top 10 by daily % change with minimum traded-volume eligibility; threshold value set during design from the real volume distribution; each entry displays its traded volume (BR-MOV-011, FR-MOV-021). *Impact note retained for the record: a pure % list would have been dominated by illiquid stocks moving on negligible volume.*
- **OQ-MOV-004 — Market P/E definition — RESOLVED at Gate 2 (2026-10-06).** Decision: loss-making companies are excluded from the market P/E aggregate and the exclusion is disclosed on screen (BR-MOV-010); the aggregation formula (cap-weighted vs. median) is deferred to design with the architect. *Impact note retained for the record: counting loss-makers as zero or negative would have produced a mathematically broken market P/E.*
- **RISK-MOV-001 — ENAG redistribution terms.** Republishing an independent group's figures on a public site may carry terms risk beyond technical availability. *Mitigation: verify terms during V0 together with OQ-MOV-001; fall back to another independent measure or official-only with a note.*
- **RISK-MOV-002 — Mixed macro cadences.** Monthly CPI next to daily FX can be misread as equally current. *Mitigation: FR-MOV-009 / FR-MOV-010 as-of labeling on every value; plain-language labels (NFR-MOV-004).*
