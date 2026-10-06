# Stock Research Specifications — SCR-002 (Stock List), SCR-005 (Stock Page)

**Domain:** stock-research · **Prepared:** 2026-10-06
**Traces:** UC-RES-001, UC-RES-002, UC-RES-003, UC-RES-005; FR-RES-001–022, 026, 027 · BR-RES-001–013 · display obligations from BR-MDF-010/011, FR-MDF-011/014/016/018/019
**Global rules applied:** UXR-G-001–029 (esp. G-006 no-data honesty, G-007/008 as-of & stale, G-009 restatement, G-010 adjusted-data, G-011 canonical consistency, G-021 section-state preservation)
**Status:** written under the standing no-stop instruction. Vs.-sector strip metric set and basis **confirmed by the builder (2026-10-06, OQ-UX-002)**.

---

# SCR-002 — Stock List (Universe Browser)

## 1. Purpose

Let any user browse, sector-filter, and search (name/code) all covered BIST 100 stocks to pick one to research — the anchor of discovery, and the landing surface for dashboard sector links and the global header search. Traces to UC-RES-001.

## 2. Entry / Exit

**Entry:** primary navigation; dashboard sector links (pre-filtered to that sector, FR-MOV-012); global header search (query applied, FR-RES-004). Publicly accessible (BR-RES-001).

**Exit:** any stock page (row activation); global navigation. The active sector filter and search term are preserved when leaving to a stock page and returning (UXR-G-021).

## 3. Information Architecture

1. **Search input** — matches stock name or code.
2. **Sector filter** — restricts the list to a sector.
3. **Stock list** — every covered stock with name, code, and sector; rows activate the stock page.
4. **Informational-only disclaimer** (UXR-G-016).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-RES-001 | The list shows every covered stock with its name, code, and sector. | FR-RES-001 | Must |
| UXR-RES-002 | The user can filter the list by sector. | FR-RES-002 | Must |
| UXR-RES-003 | The user can search the list by stock name or code; when both a search term and a sector filter are active, they apply together. | FR-RES-003 | Must |
| UXR-RES-004 | When the search and/or filter yield no stocks, an explicit no-matching-stock state is shown. | UC-RES-001a, UXR-G-005 | Must |
| UXR-RES-005 | Activating a stock in the list opens that stock's page. | UC-RES-001 step 4 | Must |
| UXR-RES-006 | Arriving from a dashboard sector link opens the list pre-filtered to that sector. | FR-MOV-012, UC-MOV-004 step 2 | Must |
| UXR-RES-007 | The global header search, available on every screen, matches stocks by name or code: selecting a match opens that stock's page directly, and viewing all matches opens this list with the query applied. | FR-RES-004, OQ-RES-001, Gate 1 §6.4 | Should |

## 5. States

- **Initial / loading:** load indication, then the full universe (UXR-G-001).
- **Filtered / searched:** reduced list reflecting the active filter and query, both shown as active.
- **Empty:** no-matching-stock state per UXR-RES-004, suggesting the user clear the search or filter.
- **Error:** plain-language error + retry (UXR-G-003).
- Stale-data marking applies to any displayed data (UXR-G-008); the list itself (names/codes/sectors) is structural.

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Universe list (name, code, sector) | Server | Current universe membership | MDF instruments/constituents (a view, not an owned entity) | Read-only; reflects the latest served universe |
| Active sector filter | UI | Session | User selection | Preserved across navigation (UXR-G-021); pre-set on arrival from dashboard sector links (UXR-RES-006) |
| Active search term | UI | Session | User input | Preserved across navigation (UXR-G-021); pre-set from header search (UXR-RES-007) |

## 7. Data-heavy surfaces

A fixed-size list (≈100 stocks): loading, filtered/searched, empty, error. No pagination or sorting is specified by the BA at this universe size; search and sector filter are the access paths (FR-RES-002/003).

## 8. Responsive behavior

Search, filter, and list remain fully operable at narrow widths (UXR-G-025); rows degrade to compact form without losing name, code, sector, or activation target.

## 9. Accessibility

Search input and sector filter are labeled; rows are activation targets with accessible names combining stock name and code; the active filter/search state is perceivable non-visually (UXR-G-028).

## 10. Critical flows

1. **Sector → stock.** Given the user activated a sector on the dashboard, when the Stock List opens, then it is pre-filtered to that sector; when the user activates a stock, then its page opens.
2. **Search misses.** Given a query matching no stock, when the user searches, then an explicit no-matching-stock state appears (with a path to clear the query).
3. **Header search → stock.** Given any screen, when the user searches via the header and selects a match, then that stock's page opens directly.

---

# SCR-005 — Stock Page (Seven Content Sections)

## 1. Purpose

The research destination for one stock: let any user understand what the company does (bilingual, builder-reviewed description) and study its valuation, financials, profitability, growth, balance sheet, and dividends — all canonical, dated, and honestly marked — then proceed to valuation. Traces to UC-RES-002, UC-RES-003, UC-RES-005. One screen with seven content sections per Gate 1 decision A.

## 2. Entry / Exit

**Entry:** Stock List; screener result rows (FR-SCR-004); dashboard gainer/loser entries (FR-MOV-011); global header search selection. Publicly accessible (BR-RES-001).

**Exit:** the DCF Calculator for this stock (UXR-RES-023); any other stock page (via header search); global navigation. The active section is preserved when navigating away and back (UXR-G-021).

## 3. Information Architecture

Seven content sections, presented as discrete navigable units with the current section indicated (the widget — tabs, accordion, scroll — is a design decision; the behavioral contract is below):

1. **Overview & Business Description** — bilingual reviewed description + last-reviewed date.
2. **Valuation** — current and historical valuation metrics + the vs.-sector strip.
3. **Financials** — revenue → EBITDA → EBIT → net income → FCF.
4. **Profitability** — ROIC, ROE, margins.
5. **Growth** — 3Y/5Y/10Y CAGRs with actual-window indication.
6. **Balance Sheet** — financial position, incl. book value and book value per share.
7. **Dividends** — dividend history.

Plus: the informational-only disclaimer (UXR-G-016) and the DCF hand-off. Every section carries its own data-as-of date (UXR-RES-021).

### 4.2 (referred to below) — Vs.-sector strip metric set — **CONFIRMED by the builder (2026-10-06, OQ-UX-002; closes the OQ-RES-002 residual)**

The strip shows the stock's **five valuation-family metrics — P/E, P/B, EV/EBITDA, EV/FCF, FCF yield — each next to the median of the same metric across covered-universe peers in the same sector, computed on the same current basis/as-of as the stock's own displayed values**. P/E sector medians exclude loss-making peers, with the exclusion disclosed on screen (consistent with BR-MOV-010). This mirrors the screener's valuation family (BR-SCR-002) and keeps the strip within the canonical-metrics discipline (BR-RES-004). Confirmed sub-decisions: (a) all five metrics; (b) sector-level peers; (c) current basis; (d) inline exclusion disclosure — plus the honesty details UXR-RES-024/025 (peer count, not-meaningful states).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-RES-008 | The page presents the seven content sections — Overview & Business Description, Valuation, Financials, Profitability, Growth, Balance Sheet, Dividends — as discrete navigable units, with the current section indicated and section navigation available at all times. | FR-RES-005–012 (structural); Gate 1 decision A | Must |
| UXR-RES-009 | The business description renders in the user's selected language, from the builder-reviewed TR and EN versions. | FR-RES-005, BR-RES-002/003, UC-RES-002 | Must |
| UXR-RES-010 | The business description displays its last-reviewed date. | FR-RES-022 | Should |
| UXR-RES-011 | Where no reviewed description is published for the stock, the Overview section shows the explicit "description in preparation" state; all numbers sections remain fully available. | FR-RES-026, OQ-RES-003, UC-RES-002a | Must |
| UXR-RES-012 | The Valuation section displays the stock's current and historical valuation metrics. | FR-RES-006 | Must |
| UXR-RES-013 | The Valuation section displays the vs.-sector strip: the stock's five valuation-family multiples (P/E, P/B, EV/EBITDA, EV/FCF, FCF yield) next to sector medians over covered-universe peers in the same sector, on the same current basis/as-of as the stock's displayed multiples, with the loss-maker exclusion disclosed. | FR-RES-007, SD-002, OQ-RES-002 residual closed (§4.2, confirmed 2026-10-06) | Should |
| UXR-RES-014 | The Financials section covers revenue → EBITDA → EBIT → net income → FCF. | FR-RES-008 | Must |
| UXR-RES-015 | The Profitability section covers ROIC, ROE, and margins. | FR-RES-009 | Must |
| UXR-RES-016 | The Growth section displays 3Y/5Y/10Y CAGRs, indicating the actual window used wherever the company's history is shorter than the target window. | FR-RES-010, BR-RES-013, FR-MDF-014 | Must |
| UXR-RES-017 | The Balance Sheet section includes book value (total equity) and book value per share. | FR-RES-027, OQ-RES-004 | Should |
| UXR-RES-018 | The Dividends section displays the stock's dividend history. | FR-RES-012 | Must |
| UXR-RES-019 | Restated figures are marked with an asterisk on the affected figures, a footnote at the bottom of the displaying surface, and a warning indicator, wherever they appear on the page. | FR-RES-014, BR-RES-006, BR-MDF-011 | Must |
| UXR-RES-020 | Any historical series or metric displayed as corporate-action-adjusted carries a visible adjusted-data disclaimer. | FR-RES-013, BR-RES-005, BR-MDF-010 | Must |
| UXR-RES-021 | Every section displays its data-as-of date, and stale data is marked as such. | FR-RES-015, BR-RES-007, FR-MDF-016 | Must |
| UXR-RES-022 | Where data is missing for a section or metric, the page shows an explicit no-data state with the coverage boundary, using the listing/IPO date as context where relevant. | FR-RES-016, BR-RES-008, FR-MDF-011 | Must |
| UXR-RES-023 | The page provides navigation to the DCF calculator for this stock. | FR-RES-018, UC-RES-005 | Must |
| UXR-RES-024 | The vs.-sector strip displays the peer count behind each sector median (e.g., "median of 23 peers"), so the user can judge the median's basis. | OQ-UX-002 confirmation (2026-10-06); honest-display principle | Should |
| UXR-RES-025 | Where the stock's own multiple is not meaningful (e.g., P/E with negative earnings), the strip shows an explicit not-meaningful state for that metric rather than a value. | OQ-UX-002 confirmation (2026-10-06); BR-RES-008 | Should |

## 5. States

- **Initial / loading:** load indication per section (UXR-G-001); sections can render independently — one slow/failing section never blocks the others.
- **Populated:** canonical figures with per-section as-of dates (UXR-RES-021).
- **Description in preparation:** per UXR-RES-011 (honest staged-publication state; numbers sections unaffected).
- **No-data (per section/metric):** explicit state with coverage boundary (UXR-RES-022) — never blanks-as-zero.
- **Shorter history:** CAGRs computed from available history with the actual window indicated (UXR-RES-016).
- **Restated:** asterisk + bottom footnote + warning on affected figures (UXR-RES-019).
- **Stale:** as-of date + stale marker (UXR-RES-021).
- **Error (per section):** plain-language error + retry for that section (UXR-G-003).
- **Mutation-pending / empty:** N/A — the Stock Page hosts no mutations (mutation matrix §2); it has no legitimate whole-page empty state (a covered stock always has a page; per-section states above handle absence).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| All financial figures and metrics (every section) | Server | EOD / per reporting period | MDF canonical facts and metrics (BR-RES-004 — the page computes nothing) | Read-only; served with as-of, staleness, restatement, and adjusted markings from MDF |
| Business description (published version) | Server | Per builder review cycle | Stock Research domain (status: published only; BR-RES-002/003) | Read-only; last-reviewed date displayed (UXR-RES-010) |
| Active section | UI | Session | User selection | Preserved across navigation away/back (UXR-G-021) |
| Language | Global | Per device / per account | UXR-G-014 | Description re-renders in the selected language (both versions exist pre-publication) |

## 7. Data-heavy surfaces

- **Financial tables/series (Financials, Profitability, Growth, Balance Sheet, Dividends):** loading, populated, no-data with coverage boundary, shorter-history indication, restatement marking, adjusted-data disclaimer on historical series, stale marking, per-section error with retry. Figures are never presented with precision or freshness beyond their source: every table binds to its as-of date/period (UXR-G-007), and adjusted series are labeled (UXR-G-010).
- **Historical valuation (Valuation section):** historical series on the adjusted basis with the adjusted-data disclaimer (BR-MDF-010), same canonical values as the screener for the current period (UXR-G-011).
- **Dividend history:** full dated history as ingested; no-data state where a stock has never paid dividends is an explicit, honest state ("no dividends paid" with coverage boundary), not an error.
- **Vs.-sector strip (Should):** stock value + sector median per metric, exclusion disclosure; degrades to a no-data state if sector medians are unavailable for a metric.

## 8. Responsive behavior

All seven sections remain navigable and readable at narrow widths (UXR-G-025); financial tables degrade in presentation without losing access to any displayed figure, marker (asterisk/adjusted/stale), or the bottom footnote; the DCF hand-off remains reachable.

## 9. Accessibility

- Tables use semantic structures with labeled rows/columns (UXR-G-028).
- Section navigation is keyboard operable; the current section is programmatically indicated (UXR-G-026/027).
- Restatement warnings and adjusted-data disclaimers are textual (not color- or icon-only); the asterisk is text and its footnote is reachable.
- Daily-change / trend direction in series is conveyed by more than color alone.

## 10. Critical flows

1. **Research hand-off (SC-002 segment).** Given the user arrived from a screener result row, when the stock page opens, then all seven sections are available; when the user moves through sections, then each shows canonical figures with as-of dates; when the user selects the DCF calculator, then it opens for this stock.
2. **Description in preparation.** Given a stock whose description is not yet published, when the user opens the page, then the Overview section shows the honest "description in preparation" state while all numbers sections remain fully populated.
3. **Restatement marking.** Given a company restated prior figures, when the user views affected sections, then those figures carry the asterisk, the bottom-of-display footnote explains the restatement, and the warning indicator is present.
4. **Short history.** Given a stock listed in 2021, when the user views the Growth section, then the 3Y/5Y CAGRs indicate the actual window used, and the 10Y CAGR shows the honest no-data/shorter-history state rather than a fabricated value.
5. **Adjusted historical data.** Given a stock with corporate actions in its history, when the user views historical valuation, then the series is corporate-action-adjusted and carries the adjusted-data disclaimer.
6. **Back-navigation preserves position.** Given the user is on the Balance Sheet section, when they navigate to another screen and return, then the Balance Sheet section is still active.
