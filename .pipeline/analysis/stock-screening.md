# Stock Screening Analysis

**Domain code:** SCR · **Slug:** `stock-screening` · **Report order:** 3 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V1 — stock screener, user accounts; §7; SC-003) · **Domain map:** `.pipeline/analysis/00-domain-map.md`

## 1. Overview

This domain is the "screen" stage of the core investing loop: a screener that lets users filter the BIST 100 universe across five metric families — Valuation (P/E, P/B, EV/EBITDA, EV/FCF, FCF yield), Quality (ROIC, ROE, gross margin, operating margin), Growth (revenue CAGR, EPS CAGR, FCF CAGR), Financial Health (net debt/EBITDA, interest coverage, current ratio), and Dividends (dividend yield, dividend CAGR, payout ratio) — 18 metrics in total. Authenticated users can save screens with their criteria, persisted server-side per account, and re-run them against the latest end-of-day data. Results link directly into stock pages, handing off to the research stage.

Business value delivered:

- The screen stage of the core loop, functional for the whole covered universe. [OBJ-002, SC-002]
- Saved screens persisted server-side per account across all five families is an explicit success criterion. [SC-003]
- For the amateur persona, screening is the alternative to tip-following: transparent criteria the user chose themselves, in plain language. [Brief §2, §4]

Screens owned by this domain: **Screener** (criteria builder + results) and **My Saved Screens** (management list).

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Anonymous retail investor | Human, unauthenticated | Build and run an ad-hoc screen; explore results (saving requires an account — BR-SCR-004) | Weekly-ish |
| Registered user | Human, authenticated | Save screens, re-run them on fresh data, manage the saved list | Weekly-ish |
| The builder | Human; first user + data steward | Use the screener in personal research sessions [OBJ-003]; verify metric coverage before launch (ASM-001, UC-SCR-004) | Weekly |
| Screener engine | System actor | Evaluate criteria against MDF canonical metrics; compute result sets on demand | On each run |
| Downstream domains | System actors | Stock Research receives click-through traffic from results | Continuous |

## 3. Business Rules

- **BR-SCR-001** — Every screener metric is the Market Data Foundation's canonical metric (BR-MDF-009); the screener defines no formulas of its own. A metric value in results must equal the same metric on that stock's page. *Source: brief §5 (same metric families span screener and stock pages); SC-002/SC-003 consistency.* *Enforced: system.*
- **BR-SCR-002** — The screenable metric set is exactly the brief's 18 metrics across the five families (listed in §1); adding metrics is a post-V1 decision. *Source: brief §5.* *Enforced: system (metric catalog).*
- **BR-SCR-003** — In V1, criteria combine with AND logic only (a stock must satisfy every selected condition); OR / boolean groups are out of scope this release. *Source: builder simplification consistent with the brief's "simple" product framing (§1).* *Enforced: system.*
- **BR-SCR-004** — Running a screen is available without an account; saving a screen requires an authenticated account, and saved screens are persisted server-side per account. *Source: brief §5 (user accounts exist for saved screens) + public research posture (§5); anonymous-run confirmed by builder at Gate 2, 2026-10-06 (OQ-SCR-001).* *Enforced: system.*
- **BR-SCR-005** — Growth criteria use the same canonical CAGR definitions and history windows as the stock pages' growth section; where a company's history is shorter than the window, the available-history rule applies and the actual window is indicated (FR-MDF-014). *Source: brief §5 (3Y/5Y/10Y CAGR on stock pages); BR-MDF-006.* *Enforced: system.*
- **BR-SCR-006** — Results are computed on the latest EOD data and displayed with the data-as-of date; stale data is marked per FR-MDF-016. *Source: brief §7 (EOD constraint).* *Enforced: system.*
- **BR-SCR-007** — All screener content is bilingual (TR default, EN toggle), with metric and criteria labels in plain language understandable to a novice-to-intermediate investor. *Source: brief §7, §4.* *Enforced: system.*
- **BR-SCR-008** — The screener is a filtering tool, not advice: no "best stocks" / ranking-scores framing, and the informational-only disclaimer is displayed. *Source: brief §6 (permanent exclusions), §7 (SPK).* *Enforced: system + content policy.*
- **BR-SCR-009** — A stock lacking data for a selected criterion is excluded from that screen's results (never counted as passing, never filled with zero); exclusions are surfaced to the user (FR-SCR-014). *Source: honest-display principle; brief §7 (accepted source limitations).* *Enforced: system.*

## 4. Use Cases

### UC-SCR-001 — Build and run an ad-hoc screen
**Primary actor:** anonymous or registered retail investor
**Preconditions:** canonical metrics computed for the universe (FR-MDF-013); daily EOD refresh done.
**Main success scenario:**
1. The user opens the Screener.
2. Criteria are organized by the five metric families; the user selects metrics and sets bounds (e.g., P/E max 15, ROE min 15%, net debt/EBITDA max 2, dividend yield min 3%).
3. The user runs the screen.
4. The system evaluates all universe stocks against the criteria (AND logic) using canonical metrics.
5. Matching stocks are listed with key values per selected criterion, the result count, and the data-as-of date.
6. The user clicks a result row → the stock page opens (research hand-off).
**Alternate / error flows:** (a) No stocks match → an honest empty state explains there are no matches and suggests relaxing criteria (FR-SCR-006). (b) Some stocks lack data for a chosen criterion → they are excluded and the exclusion count is shown (BR-SCR-009, FR-SCR-014). (c) Data is stale → as-of date + stale marker shown (BR-SCR-006).
**Postconditions:** the user has a candidate list grounded in their own transparent criteria, with entry points into stock pages.

### UC-SCR-002 — Save a screen
**Primary actor:** registered user
**Preconditions:** criteria built in the Screener; user authenticated.
**Main success scenario:**
1. With criteria active, the user chooses "Save screen" and gives it a name.
2. The system persists the criteria set server-side, associated with the account.
3. The screen appears in "My Saved Screens".
**Alternate / error flows:** (a) User is anonymous → prompted to sign in / register; in-progress criteria are preserved across the login hop (FR-SCR-012, FR-SCR-013).
**Postconditions:** the screen is retrievable from any device on next login (server-side persistence, SC-003).

### UC-SCR-003 — Re-run and manage saved screens
**Primary actor:** registered user
**Preconditions:** at least one saved screen; authenticated.
**Main success scenario:**
1. The user opens "My Saved Screens" and sees their named screens.
2. The user re-runs a screen → it executes against the latest EOD data (not a stored result set).
3. The user renames or deletes a screen.
**Alternate / error flows:** (a) A saved screen references a metric later hidden for inadequate coverage (UC-SCR-004) → the screen runs without it and clearly reports which criterion was dropped.
**Postconditions:** the user's screening workflow is repeatable day over day on fresh data.

### UC-SCR-004 — Verify metric coverage before launch
**Primary actor:** the builder (data steward)
**Preconditions:** MDF coverage report available (UC-MDF-004, FR-MDF-011).
**Main success scenario:**
1. The builder checks each of the 18 metrics against actual data coverage.
2. Metrics with adequate coverage are confirmed screenable.
3. Metrics with inadequate coverage are hidden from the screener (FR-SCR-019), and the decision is recorded.
**Alternate / error flows:** (a) Coverage improves later (better source found) → the metric is un-hidden by builder decision.
**Postconditions:** every screenable metric has verified data behind it at launch; the ASM-001 dependency for the screener is closed.

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-SCR-001 | The system shall support screening on the following 18 metrics — Valuation: P/E, P/B, EV/EBITDA, EV/FCF, FCF yield; Quality: ROIC, ROE, gross margin, operating margin; Growth: revenue CAGR, EPS CAGR, FCF CAGR; Financial Health: net debt/EBITDA, interest coverage, current ratio; Dividends: dividend yield, dividend CAGR, payout ratio. | UC-SCR-001 | Must |
| FR-SCR-002 | The system shall combine all selected criteria with AND logic. | UC-SCR-001 | Must |
| FR-SCR-003 | The system shall let the user set a minimum bound, a maximum bound, or both for each criterion. | UC-SCR-001 | Must |
| FR-SCR-004 | The system shall display matching stocks as a result list showing each stock's value for the selected criteria, with each row linking to that stock's page. | UC-SCR-001 | Must |
| FR-SCR-005 | The system shall display the results' data-as-of date and mark stale data as such. | UC-SCR-001 | Must |
| FR-SCR-006 | The system shall display an explicit empty state when no stocks match, indicating zero results. | UC-SCR-001 | Must |
| FR-SCR-007 | The system shall save the active criteria set as a named screen, persisted server-side and associated with the authenticated user's account. | UC-SCR-002 | Must |
| FR-SCR-008 | The system shall list the user's saved screens by name. | UC-SCR-003 | Must |
| FR-SCR-009 | The system shall re-run a saved screen against the latest available data on demand. | UC-SCR-003 | Must |
| FR-SCR-010 | The system shall let the user rename a saved screen. | UC-SCR-003 | Must |
| FR-SCR-011 | The system shall let the user delete a saved screen. | UC-SCR-003 | Must |
| FR-SCR-012 | The system shall prompt an anonymous user to sign in or register when they attempt to save a screen. | UC-SCR-002 | Must |
| FR-SCR-013 | The system shall preserve the anonymous user's in-progress criteria across the sign-in / registration flow. | UC-SCR-002 | Should |
| FR-SCR-014 | The system shall indicate how many stocks were excluded from results due to missing data for the selected criteria. | UC-SCR-001 | Should |
| FR-SCR-015 | The system shall let the user select the CAGR window (3Y / 5Y / 10Y) for growth criteria. | UC-SCR-001, UC-SCR-003 | Must |
| FR-SCR-016 | The system shall display the informational-only disclaimer on the Screener and My Saved Screens screens. | UC-SCR-001 | Must |
| FR-SCR-017 | The system shall let the builder hide an individual screener metric from users when its data coverage is inadequate, without code changes. | UC-SCR-004 | Should |
| FR-SCR-018 | The system shall support OR / boolean criterion groups. | — | Won't (this release) |
| FR-SCR-019 | The system shall support sharing a saved screen via a public link. | — | Won't (this release) |

## 6. Non-Functional Requirements

- **NFR-SCR-001 — Bilingual completeness:** 100% of screener content (metric names, family names, criteria controls, messages, empty states, disclaimer) exists in TR and EN, TR default [brief §7].
- **NFR-SCR-002 — Plain-language criteria:** every criterion is phrased in plain language with units (e.g., "F/K oranı en fazla 15" / "P/E ratio at most 15"), understandable without prior expertise [brief §4].
- **NFR-SCR-003 — Cross-surface consistency:** for the same stock, metric, and date, the value shown in screener results equals the value shown on the stock page (canonical metrics, BR-MDF-009).
- **NFR-SCR-004 — Freshness with honesty:** results always carry the EOD as-of date; a screen never presents data whose vintage is unclear [brief §7].
- **NFR-SCR-005 — Server-side persistence:** a user's saved screens are retrievable after device change or browser data clearing (login from any device shows the same screens) [SC-003].

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| Screenable metric catalog | the 18 metrics: family grouping, unit, plain-language label (TR/EN), screenable flag | owned here as the screener's view; metric *values* remain MDF's DerivedMetric (BR-SCR-001) |
| Criterion | metric, bound type (min/max/range), bound value(s), CAGR window (for growth metrics) | building block of a screen; not stored standalone |
| Saved screen | id, owner (user account), name, ordered criteria set, created/updated timestamps | User 1 → * SavedScreen; persisted server-side [SC-003] |

Result sets are computed on demand and are **not** persisted entities (UC-SCR-003 step 2).

## 8. Dependencies

**Upstream:**
- **Market Data Foundation** — canonical metrics for all 18 screenable metrics (FR-MDF-013), CAGRs with window indication (FR-MDF-014), coverage metadata (FR-MDF-011), staleness markers (FR-MDF-016), universe membership.
- **User Accounts** — identity for saving/loading screens; sign-in/registration flow for the save prompt (FR-SCR-012).

**Downstream:**
- **Stock Research** — result rows link to stock pages (FR-SCR-004), completing screen → research.

## 9. Open Questions & Risks

- **OQ-SCR-001 — Anonymous screen-running — RESOLVED at Gate 2 (2026-10-06).** Decision: anyone can build and run screens anonymously; only saving requires an authenticated account (BR-SCR-004). *Rationale on record: frictionless first experience for the anonymous visitors behind OBJ-005's 10-user validation.*
- **OQ-SCR-002 — Growth CAGR window — RESOLVED at Gate 2 (2026-10-06).** Decision: selectable 3Y/5Y/10Y windows for growth criteria (FR-SCR-015, promoted to Must), consistent with the stock pages' growth section; short-history handling per FR-MDF-014 with the actual window indicated.
- **OQ-SCR-003 — Metric coverage at launch.** Which of the 18 metrics have adequate data coverage is unknown until the V0 backfill and coverage report (ASM-001, UC-MDF-004). Metrics failing coverage are hidden at launch (FR-SCR-017). *Impact if ignored: screens returning wrong or empty results for broken metrics — a credibility failure.*
- **RISK-SCR-001 — Novice misinterpretation.** A user may screen on a single metric (e.g., lowest P/E) and treat results as a ranking of "best" stocks. *Mitigation: plain-language labels, no scoring/ranking framing (BR-SCR-008), disclaimer; per-metric education is explicitly out of scope (brief §6).*
- **RISK-SCR-002 — Result drift over time.** Re-running a saved screen later can yield different results (new data, restatements, universe changes) with no stored "why". *Mitigation: restatement markers (BR-MDF-011), as-of dates (BR-SCR-006); screen-diff/history explanations are out of scope this release.*
