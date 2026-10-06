# Market Data Foundation Analysis

**Domain code:** MDF · **Slug:** `market-data-foundation` · **Report order:** 1 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V0, §7, §8 ASM-001/ASM-003) · **Domain map:** `.pipeline/analysis/00-domain-map.md`

## 1. Overview

This domain builds and maintains the single source of truth for all BIST equity facts: the covered universe (BIST 100 constituents, BIST indices, sectors/industries), daily end-of-day prices and volume, financial statements, dividends, corporate actions, and KAP disclosures. It is the V0 data foundation — not user-facing — and every product domain (Market Overview, Stock Screening, Stock Research, Valuation & DCF) consumes its data.

Business value delivered:

- The core investing loop (discover → screen → research → value → decide) is only functional for *every* covered stock if complete, dated, trustworthy facts exist underneath it. [OBJ-002, SC-002, SC-003, SC-004]
- A daily, unattended refresh keeps the public V1 credible and current without builder babysitting. [OBJ-001, SC-005]
- The pipeline itself — sources, ETL, data model, quantitative calculations — is a deliberate portfolio artifact demonstrating data-engineering capability. [OBJ-004, SC-007]
- History stored as first-class (every fact with its date/period, never overwritten) makes future point-in-time features possible and gives the builder's own research honest historical context. [Brief §5, V0]

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Automated data pipeline | System actor | Ingest, validate, and store all data types on the daily EOD cycle without manual intervention [SC-005] | Daily (each trading day) |
| The builder (data steward) | Human, administrative | Validate source coverage and quality (ASM-001), monitor anomalies, accept/record gaps, run the historical backfill | Intensive during V0; exception-driven at steady state |
| External data sources | External systems | Provide the raw facts — KAP as primary source of record; İşbank API as candidate price source | Polled daily |
| Downstream platform domains (Market Overview, Screening, Research, DCF) | System actors | Consume facts and canonical derived metrics for their screens | Continuously (on user request / daily batch) |
| Offline AI drafting pipeline (Stock Research domain) | System actor, build-time | Consume KAP disclosures as input for bilingual business descriptions | Per build cycle |
| Portfolio audience | Human, indirect | Assess data-engineering capability via the public repo and README [OBJ-004] | Occasional |

## 3. Business Rules

- **BR-MDF-001** — KAP is the primary source of record for financial statements, dividends, corporate actions, and disclosures; supplementary free sources only fill gaps KAP does not cover (e.g., daily prices). *Source: brief §7 (Constraints — Data budget).* *Enforced: system (pipeline source routing) + policy.*
- **BR-MDF-002** — Data acquisition budget is $0: free/public sources only; paid data sources are a permanent exclusion. *Source: brief §7, §6 (permanent exclusions).* *Enforced: policy / procurement decision, never a system option.*
- **BR-MDF-003** — Data cadence is daily end-of-day only; no real-time or streaming data, ever. *Source: brief §6 (permanent exclusions), §7.* *Enforced: system.*
- **BR-MDF-004** — Every fact is stored with its date/period, and historical facts are never overwritten or discarded; corrections arrive as new dated records. *Source: brief §5 (V0 — historical data as first-class).* *Enforced: system (data model).*
- **BR-MDF-005** — The covered universe is the BIST 100 constituents (expansion is a post-V1 decision per ASM-003); constituent changes are recorded with effective dates, never mutated in place. *Source: brief §5, §8.* *Enforced: system.*
- **BR-MDF-006** — Ingest depth target is 10 years or as far back as sources allow; where a company's history is shorter (e.g., recent IPOs), metrics are computed from the available history. *Source: brief §5.* *Enforced: system + manual validation during V0.*
- **BR-MDF-007** — Coverage and quality are bounded by what free sources provide; limitations are accepted and gaps are recorded explicitly, never silently ignored. *Source: brief §7, ASM-001.* *Enforced: manual (builder-maintained coverage record).*
- **BR-MDF-008** — Source documents remain in their original Turkish; financial figures are language-neutral. The foundation performs no translation of source content. *Source: brief §7 (Language).* *Enforced: system/policy.*
- **BR-MDF-009** — Each derived metric has exactly one canonical definition, computed by this domain and consumed identically by every surface (screener, stock pages, dashboard, DCF) — a P/E shown in two places must be the same number. *Source: brief §5 (same metric families span screener and stock pages; SC-003, SC-002 consistency).* *Enforced: system.*
- **BR-MDF-010** — Historical analysis (CAGRs, historical valuation, historical charts) uses corporate-action-adjusted prices; current-day display uses raw prices; adjusted data is labeled as such on every surface that shows it. *Source: builder decision at Gate 2 review, 2026-10-06 (resolves OQ-MDF-003).* *Enforced: system.*
- **BR-MDF-011** — When a company restates previously reported figures, the latest KAP report's (restated) values are used for all current metrics and display; restated figures are visibly marked — an asterisk on the affected figures, a footnote at the bottom of the display, and a warning indicator — explaining that the company restated them; original as-reported values are retained in storage for point-in-time integrity. *Source: builder decision at Gate 2 review, 2026-10-06 (resolves OQ-MDF-004).* *Enforced: system for storage and serving of restatement markers; the visible asterisk/footnote/warning is rendered on consuming screens — FRs to be captured in the Stock Research report.*

## 4. Use Cases

### UC-MDF-001 — Refresh market data daily
**Primary actor:** automated data pipeline
**Preconditions:** sources reachable; universe membership known.
**Main success scenario:**
1. After the trading day closes, the pipeline pulls EOD prices and volume for every instrument in the covered universe.
2. It pulls newly published financial statements, dividends, corporate actions, and KAP disclosures.
3. Each fact is stamped with its date/period and source reference, validated, and stored.
4. Derived metrics (five metric families) are recomputed for the new data.
5. Downstream domains are served the refreshed data.
**Alternate / error flows:** (a) A source is unreachable → retry per policy; on failure, keep last-known-good data, mark it stale, and alert the builder (FR-MDF-016, FR-MDF-012). (b) A fact fails validation → quarantine it, alert the builder; never write invalid data.
**Postconditions:** all covered data types are current as of the last completed trading day, without manual intervention [SC-005].

### UC-MDF-002 — Backfill historical data
**Primary actor:** the builder (data steward)
**Preconditions:** sources identified per data type; backfill window agreed (10-year target).
**Main success scenario:**
1. The builder triggers historical ingest per data type (prices, statements, dividends, corporate actions, disclosures).
2. The pipeline ingests as far back as each source allows.
3. Actual achieved depth is recorded per instrument and data type.
4. A coverage report (depth + gaps per data type) is produced for ASM-001 validation.
**Alternate / error flows:** (a) A source limits history to less than 10 years → record the actual depth; metrics fall back to available history (BR-MDF-006).
**Postconditions:** history available for historical valuation, CAGRs (3/5/10Y), and point-in-time-capable storage; coverage gaps documented.

### UC-MDF-003 — Maintain universe membership and classifications
**Primary actor:** automated data pipeline (builder reviews)
**Preconditions:** index constituent data available from source.
**Main success scenario:**
1. The pipeline detects additions/removals in the BIST 100 (and BIST 30) constituents.
2. Each change is recorded with its effective date; no membership row is overwritten.
3. Sector/industry classifications are kept current per instrument.
**Alternate / error flows:** (a) Classification missing at source → record instrument as unclassified and flag it (FR-MDF-012).
**Postconditions:** the universe reflects the current BIST 100 while retaining full membership history.

### UC-MDF-004 — Validate source coverage and quality
**Primary actor:** the builder (data steward) — this is the ASM-001 validation activity
**Preconditions:** backfill (UC-MDF-002) has run; coverage report available.
**Main success scenario:**
1. The builder reviews per-data-type coverage and quality (completeness, how far back statements go, price gaps).
2. The builder records accepted limitations and gaps in the coverage record.
3. Go/no-go is confirmed for each downstream dependency (screener metrics, stock-page sections, DCF inputs).
**Alternate / error flows:** (a) A data type is inadequate at all free sources → its dependent features are flagged to be hidden or clearly caveated in V1, and the decision is recorded.
**Postconditions:** every V1 surface that consumes data has a verified data basis or a documented, accepted gap.

### UC-MDF-005 — Serve facts and canonical metrics to downstream domains
**Primary actor:** downstream platform domains (system actors)
**Preconditions:** data present for the requested instrument and period.
**Main success scenario:**
1. A downstream domain requests facts or derived metrics for an instrument (current or historical).
2. The foundation returns data computed under canonical definitions (BR-MDF-009).
3. Where history is shorter than a requested window (e.g., 10Y CAGR for a recent IPO), the available-history result is returned together with its actual window length.
**Alternate / error flows:** (a) No data for the requested period → an explicit "no data" response with the coverage boundary (FR-MDF-011), never a fabricated or silently shortened value.
**Postconditions:** every surface displays consistent, sourced numbers; the user is never shown a metric whose basis is unclear.

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-MDF-001 | The system shall ingest and store daily end-of-day prices and volume for every instrument in the covered universe. | UC-MDF-001, UC-MDF-002 | Must |
| FR-MDF-002 | The system shall ingest and store financial statements (income statement, balance sheet, cash flow statement) per instrument per reporting period. | UC-MDF-001, UC-MDF-002 | Must |
| FR-MDF-003 | The system shall ingest and store dividend records (amount per share and relevant dates) per instrument. | UC-MDF-001, UC-MDF-002 | Must |
| FR-MDF-004 | The system shall ingest and store corporate actions (type, date, and terms — e.g., splits, rights issues, bonus issues) per instrument. | UC-MDF-001, UC-MDF-002 | Must |
| FR-MDF-005 | The system shall ingest and store KAP disclosures per instrument, with disclosure type and date. | UC-MDF-001, UC-MDF-002 | Must |
| FR-MDF-006 | The system shall ingest and store BIST index levels (including BIST 100 and BIST 30) and sector/industry classifications. | UC-MDF-001, UC-MDF-003 | Must |
| FR-MDF-007 | The system shall record constituent membership changes with effective dates and never overwrite an existing membership record. | UC-MDF-003 | Must |
| FR-MDF-008 | The system shall store a complete provenance record (fact date/period plus source reference) for every ingested fact. | UC-MDF-001, UC-MDF-002, UC-MDF-005 | Must |
| FR-MDF-009 | The system shall refresh all covered data types automatically on a daily cycle without manual intervention. | UC-MDF-001 | Must |
| FR-MDF-010 | The system shall backfill historical data per data type to the 10-year target or the source's limit, whichever is reached first, and record the actual achieved depth per instrument per data type. | UC-MDF-002 | Must |
| FR-MDF-011 | The system shall expose coverage metadata per instrument (which data types exist, from what date, and the instrument's listing/IPO date) to the builder and to downstream domains. | UC-MDF-002, UC-MDF-004, UC-MDF-005 | Must |
| FR-MDF-012 | The system shall flag data-quality anomalies (missing, incomplete, late, or failed-validation data) to the builder via log alert and e-mail notification. | UC-MDF-001, UC-MDF-004 | Should |
| FR-MDF-013 | The system shall compute and expose canonical derived metrics for all five metric families (Valuation, Quality, Growth, Financial Health, Dividends) under single documented definitions per metric. | UC-MDF-005 | Must |
| FR-MDF-014 | The system shall compute growth metrics (CAGRs) from the available history whenever the full target window (3Y/5Y/10Y) is not available, and indicate the actual window used. | UC-MDF-005 | Must |
| FR-MDF-015 | The system shall preserve all historical facts without overwriting or erasure, such that as-of-date retrieval remains possible. | UC-MDF-002, UC-MDF-005 | Must |
| FR-MDF-016 | The system shall continue serving last-known-good data during a source outage and mark it as stale with its as-of date, so every consuming surface can clearly state it is showing prior-day data. | UC-MDF-001 | Should |
| FR-MDF-017 | The system shall provide historical backtesting of screens (running a screen as of a past date). | UC-MDF-005 | Won't (this release — vision item, brief §6) |
| FR-MDF-018 | The system shall indicate whether a served historical price series or derived metric is corporate-action-adjusted, so consuming surfaces can label it accordingly (BR-MDF-010). | UC-MDF-005 | Must |
| FR-MDF-019 | The system shall mark facts and derived metrics that reflect restated (as opposed to original as-reported) figures, so consuming surfaces can render the asterisk, footnote, and warning required by BR-MDF-011. | UC-MDF-005 | Must |

**Boundary notes:** Real-time/streaming data and paid data sources are permanent exclusions (BR-MDF-002, BR-MDF-003), not "Won't this release". Fund data is owned by the separate Fund Data Foundation domain. FR-MDF-015 delivers the *storage capability* for point-in-time retrieval; any point-in-time *screen UI* is part of FR-MDF-017 and is out of this release.

## 6. Non-Functional Requirements

- **NFR-MDF-001 — Freshness:** each trading day's data is ingested and available to downstream domains automatically before the next trading day begins, with no manual steps [SC-005].
- **NFR-MDF-002 — Retention:** all ingested history is retained for the life of the platform; the ingest target is 10 years per data type where sources allow [brief §5].
- **NFR-MDF-003 — Coverage transparency:** 100% of the BIST 100 universe is covered for every data type the sources provide; every gap (instrument, data type, period) is recorded in the coverage record rather than silently dropped. Coverage is measured and reported during V0 as the ASM-001 validation.
- **NFR-MDF-004 — Provenance completeness:** 100% of stored facts carry a source reference and date/period; any fact failing this cannot be published to consumers.
- **NFR-MDF-005 — Public reproducibility:** the pipeline is documented and re-runnable from the public repository, with sources, data model, and refresh cadence explained in the README to portfolio-audience standard [OBJ-004, SC-007].
- **NFR-MDF-006 — Cost:** total data acquisition cost remains $0 for the life of the milestone [brief §7].

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| Instrument (listed stock) | symbol, name, listing/IPO date, listing status, sector/industry link | ~100 active constituents + historical instruments; 1 Instrument → * prices, statements, dividends, actions, disclosures, metrics |
| Index | code (e.g., BIST 100, BIST 30), name, level series | Index 1 → * DailyPrice-like level records |
| Sector / Industry | name (TR/EN label), hierarchy | Sector 1 → * Instruments |
| Constituent membership | instrument, index, effective-from, effective-to | resolves universe changes over time; Instrument * ↔ * Index via membership |
| Daily price & volume | instrument, date, open/high/low/close, volume | one per instrument per trading day; EOD only; stored as raw and corporate-action-adjusted series (BR-MDF-010) |
| Financial statement | instrument, period (fiscal quarter/year), statement type, line items, version (as-reported / restated, with restatement date) | many per instrument; dated per BR-MDF-004; versioned per BR-MDF-011 |
| Dividend | instrument, amount per share, ex-date / pay-date, currency | many per instrument |
| Corporate action | instrument, action type, date, terms | many per instrument |
| KAP disclosure | instrument, disclosure type, publish date, source reference | many per instrument; input to the offline AI drafting pipeline (Stock Research) |
| Derived metric | instrument, metric (canonical definition), as-of date/period, value, window used | computed, not ingested; canonical per BR-MDF-009 |

## 8. Dependencies

**Upstream (external) — no internal upstream dependencies:**
- **KAP** — primary source of record: financial statements, dividends, corporate actions, disclosures [BR-MDF-001].
- **İşbank API (candidate)** — daily EOD prices and volume; final selection is open (OQ-MDF-001).
- Other free public sources may supplement per data type, subject to BR-MDF-002.

**Downstream (internal consumers of this domain):**
- Market Overview & Macro Indicators — index levels, prices, breadth aggregates, market valuation inputs.
- Stock Screening — canonical metrics for all five families [FR-MDF-013].
- Stock Research & Company Content — financial facts for all stock-page sections; KAP disclosures for the AI drafting pipeline.
- Valuation & DCF — financial statement inputs and the current daily price.
- Fund Data Foundation — none (TEFAS-sourced, independent).

**Obligations to consumers:** consistent canonical metrics (BR-MDF-009), explicit coverage boundaries (FR-MDF-011), staleness marking during outages (FR-MDF-016).

## 9. Open Questions & Risks

- **OQ-MDF-001 — Final daily-price source.** KAP does not provide daily prices; the İşbank API is a *candidate* only. *Impact if unresolved by end of V0:* no EOD prices → all valuation metrics, the DCF's current-price comparison, and dashboard index/price displays are blocked. *Owner: builder, during V0 (ASM-001 validation).*
- **OQ-MDF-002 — Financial-statement history depth.** The 10-year target for statement history on KAP is unverified. *Impact:* thinner 10Y CAGR and historical-valuation coverage for some stocks; mitigated by the available-history rule (BR-MDF-006, FR-MDF-014). *Measure during V0 backfill (UC-MDF-002).*
- **OQ-MDF-003 — Price adjustment policy for corporate actions — RESOLVED at Gate 2 (2026-10-06).** Decision: store both raw and adjusted series; adjusted is used for all historical analysis; raw for current-day display; adjusted data is labeled on every surface that shows it (BR-MDF-010, FR-MDF-018). The visible adjusted-data disclaimer on stock pages will be captured as an FR in the Stock Research report. *Impact note retained for the record: unadjusted historical metrics would have been silently incorrect (a 1-for-5 split would look like an 80% loss).*
- **OQ-MDF-004 — Restatement policy — RESOLVED at Gate 2 (2026-10-06).** Decision (option C with presentation specifics): both versions stored; the latest restated KAP values are displayed and used for current metrics; restated figures are visibly marked with an asterisk, a bottom-of-display footnote, and a warning; as-reported values are retained for point-in-time integrity (BR-MDF-011, FR-MDF-019). *Impact note retained for the record: an overwrite policy would have lost as-reported history and made saved screens silently unstable.*
- **OQ-MDF-005 — Free-source reliability.** Rate limits, downtime, and schema changes of free sources are unverified and may threaten the unattended daily refresh (SC-005). *Mitigation: FR-MDF-012, FR-MDF-016; validate during V0.*
- **RISK-MDF-001 — Public redistribution of source-derived data.** The platform publicly displays metrics derived from KAP and other public sources. Public-source data is generally redistributable in derived form, but terms should be checked per source during V0. *Impact if ignored: takedown or blocking risk for a public deployment.* Accepted-risk posture comes from brief §7 (free/public sources only).
- **RISK-MDF-002 — Solo-builder concentration.** All pipeline knowledge sits with one person (brief §4); the daily-refresh automation (FR-MDF-009) is the primary mitigation for steady-state operation.
