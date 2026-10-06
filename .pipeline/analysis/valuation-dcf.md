# Valuation & DCF Analysis

**Domain code:** VAL · **Slug:** `valuation-dcf` · **Report order:** 5 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V1 — simple, fully user-adjustable DCF; §7; §8 ASM-007; SC-004) · **Domain map:** `.pipeline/analysis/00-domain-map.md` (incl. scope delta SD-001) · **Glossary:** fair value, margin of safety (§10)

## 1. Overview

This domain is the "value & decide" stage of the core investing loop: a per-stock DCF calculator where the user can adjust **every** model assumption — the discount rate is confirmed, and the full parameter list is finalized during V1 design (ASM-007). Based on the user's own inputs, it computes a fair value and shows, in plain language an ordinary investor can understand, how cheap or expensive the stock's current daily price is relative to that fair value (margin of safety). Per the builder's Gate 1 decision, it also shows a sensitivity table — fair value across a grid of key assumptions such as discount rate × growth (scope delta SD-001).

Business value delivered:

- The value/decide stage of the core loop, available for **every** stock in the covered universe — an explicit success criterion. [OBJ-002, SC-004]
- The builder's personal first-look valuation method, internalized by building and using it — central to the learning objective. [OBJ-003, brief §10 glossary]
- For the amateur persona: an honest "your own numbers" valuation instead of tip-following — the tool never claims to know the true value. [Brief §2, §4, §5]

Screen owned by this domain: **DCF Calculator** (per stock, reached from the stock page).

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Anonymous retail investor | Human, unauthenticated | Run a DCF with their own assumptions; see how price compares to their fair value (saving scenarios requires an account) | Per research session |
| Registered user | Human, authenticated | Same, plus saving/reloading DCF scenarios | Per research session |
| The builder | Human; first user + model definer | Use the DCF in personal research sessions [OBJ-003, SC-006]; define per-stock baseline default assumptions | Continuous |
| Market Data Foundation | System actor, upstream | Supplies the current daily price, financial statement inputs, as-of dates, restatement/adjusted markings |
| Stock Research | System actor, upstream navigation | Stock page hands off to the DCF calculator (FR-RES-018) |

## 3. Business Rules

- **BR-VAL-001** — The DCF calculator is available for every stock in the covered universe. *Source: SC-004.* *Enforced: system.*
- **BR-VAL-002** — Every DCF model assumption is visible to and editable by the user; there are no hidden, locked, or platform-secret assumptions. Defaults are a starting point, not a mandate. *Source: brief §5 ("fully user-adjustable... adjust every model assumption"), ASM-007.* *Enforced: system.*
- **BR-VAL-003** — The price comparison uses the stock's current daily (EOD) price from canonical MDF data, displayed with its as-of date. *Source: brief §5 ("current (daily) price").* *Enforced: system.*
- **BR-VAL-004** — The result is presented in plain language an ordinary investor can understand — a clear statement of how the current price compares to the user's computed fair value (the margin of safety), not just a raw number. *Source: brief §5 (explicit "presented in plain language").* *Enforced: system + content review.*
- **BR-VAL-005** — The DCF is the *user's own* model: outputs are always framed as "based on your assumptions," never as the platform's estimate of true value, never as a buy/sell signal; the informational-only disclaimer is displayed. *Source: brief §5 ("based on the user's own inputs"), §6 (permanent exclusions), §7 (SPK).* *Enforced: system + content policy.*
- **BR-VAL-006** — All DCF content is bilingual (TR default, EN toggle), including the plain-language verdict. *Source: brief §7.* *Enforced: system.*
- **BR-VAL-007** — The DCF parameter set is finite and fully defined; the discount rate is confirmed as a user-adjustable parameter; the complete list is finalized during V1 DCF design and recorded (ASM-007). *Source: brief §8 ASM-007.* *Enforced: design gate before implementation.*
- **BR-VAL-008** — Per-stock baseline default assumptions exist so the calculator shows a result immediately on open; the user adjusts from there. *Source: approved domain map ("DCF model baseline per stock, build-time defaults"); brief §5 "simple".* *Enforced: system.*
- **BR-VAL-009** — The sensitivity table (SD-001) shows fair value across a grid of key assumptions (e.g., discount rate × growth) alongside the single-point result, so the user sees how sensitive the verdict is. *Source: builder decision at Gate 1, 2026-10-05 (SD-001).* *Enforced: system.*
- **BR-VAL-010** — The DCF calculator is publicly usable without an account; saving scenarios requires an account. *Source: public research posture (§5), mirroring BR-SCR-004; confirmed by builder at Gate 2, 2026-10-06 (OQ-VAL-003).* *Enforced: system.*

## 4. Use Cases

### UC-VAL-001 — Value a stock with your own assumptions
**Primary actor:** anonymous or registered retail investor
**Preconditions:** on a stock page; canonical EOD price and statement data present; baseline defaults defined for the stock.
**Main success scenario:**
1. The user opens the DCF calculator from the stock page.
2. The calculator opens with per-stock baseline defaults and immediately shows a baseline fair value, the current price (with as-of date), and the plain-language comparison.
3. The user adjusts any assumption (discount rate and all others per the finalized parameter list).
4. Fair value and the comparison recompute.
5. The user reads the plain-language verdict: how far the current price sits above/below their fair value (margin of safety).
6. The user views the sensitivity table and sees how the verdict moves across the assumption grid.
**Alternate / error flows:** (a) Statement inputs missing for a stock (should not happen for covered universe; coverage per FR-MDF-011) → the calculator explains what is missing rather than computing on partial data. (b) Inputs reflect restated figures → restatement markings carry into the calculator's input display (BR-MDF-011). (c) Price data stale → as-of date + stale marker shown.
**Postconditions:** the user knows, at their own assumptions, whether the current price is cheap or expensive relative to their fair value — and how robust that conclusion is. [SC-004]

### UC-VAL-002 — Save and reload a DCF scenario
**Primary actor:** registered user
**Preconditions:** scenario persistence approved at gate; user authenticated; a DCF with assumptions in progress.
**Main success scenario:**
1. The user saves the current assumptions as a named scenario for that stock, server-side per account.
2. Later, the user reopens the calculator and loads the scenario; assumptions restore exactly.
**Alternate / error flows:** (a) Anonymous user attempts to save → sign-in prompt (mirroring FR-SCR-012).
**Postconditions:** the user's valuation work is durable across sessions and devices.

### UC-VAL-003 — Define per-stock baseline defaults
**Primary actor:** the builder
**Preconditions:** parameter list finalized (ASM-007, BR-VAL-007).
**Main success scenario:**
1. The builder defines baseline default assumptions per stock at build time (from the stock's canonical financial data).
2. Baselines are versioned; updates ship in build cycles.
3. Users' calculators open pre-loaded with the current baseline.
**Alternate / error flows:** (a) New stock enters the universe → a baseline is produced for it before its DCF goes live.
**Postconditions:** every covered stock has an instantly usable DCF (BR-VAL-001, BR-VAL-008).

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-VAL-001 | The system shall provide a DCF calculator for every stock in the covered universe, reachable from that stock's page. | UC-VAL-001 | Must |
| FR-VAL-002 | The system shall display the stock's current daily (EOD) price, with its as-of date, alongside the computed fair value. | UC-VAL-001 | Must |
| FR-VAL-003 | The system shall open the calculator with per-stock baseline default assumptions and an immediately visible baseline result. | UC-VAL-001, UC-VAL-003 | Must |
| FR-VAL-004 | The system shall let the user adjust every DCF model assumption (the full parameter list per BR-VAL-007, discount rate confirmed). | UC-VAL-001 | Must |
| FR-VAL-005 | The system shall recompute the fair value and price comparison whenever any assumption changes. | UC-VAL-001 | Must |
| FR-VAL-006 | The system shall display the verdict in plain language: how the current price compares to the user's computed fair value (margin of safety, as a percentage), framed as "based on your assumptions". | UC-VAL-001 | Must |
| FR-VAL-007 | The system shall display a sensitivity table showing fair value across a grid of key assumptions (e.g., discount rate × growth), per SD-001; grid axes finalized in design. | UC-VAL-001 | Should |
| FR-VAL-008 | The system shall display the informational-only disclaimer and the "based on your assumptions" framing on the DCF calculator. | UC-VAL-001 | Must |
| FR-VAL-009 | The system shall save the current DCF assumptions as a named scenario, persisted server-side per user account and per stock. | UC-VAL-002 | Should |
| FR-VAL-010 | The system shall reload a saved scenario's assumptions into the calculator exactly as saved. | UC-VAL-002 | Should |
| FR-VAL-011 | The system shall provide a reverse DCF (solving for the growth implied by the current price). | — | Won't (this release) |
| FR-VAL-012 | The system shall provide additional valuation models (e.g., DDM, Graham-style net-net). | — | Won't (this release) |
| FR-VAL-013 | The system shall support sharing a DCF scenario via a public link. | — | Won't (this release) |

## 6. Non-Functional Requirements

- **NFR-VAL-001 — Bilingual completeness:** 100% of DCF content (labels, inputs, verdict text, sensitivity table headers, disclaimer) exists in TR and EN, TR default [brief §7].
- **NFR-VAL-002 — Plain-language usability:** an ordinary investor without financial training can understand the verdict ("at your assumptions, the price is X% below your fair value") — validated by the builder during a full research session [brief §5, OBJ-003, SC-006].
- **NFR-VAL-003 — Price consistency:** the price used in the DCF comparison is the same canonical EOD price shown on the stock page and elsewhere (BR-MDF-009 discipline).
- **NFR-VAL-004 — No black box:** every number feeding the model is either user-entered or a labeled canonical fact; nothing affects the result invisibly (BR-VAL-002).

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| DCF model baseline | stock, parameter values (per finalized list), version, build date | Stock 1 → * versions; build-time, builder-defined (UC-VAL-003) |
| DCF scenario | owner (user), stock, name, parameter values, created/updated timestamps | User 1 → * scenarios; per stock; server-side persistence mirroring saved screens |

## 8. Dependencies

**Upstream:**
- **Market Data Foundation** — current daily EOD price; financial statement inputs for baseline defaults (FCF, debt, cash, shares — per the finalized parameter list); as-of dates and staleness markers; restatement and adjusted-data markings carried into input displays (BR-MDF-010/011).
- **Stock Research** — navigation hand-off from the stock page (FR-RES-018); shares the honest-display obligations.

**Adjacent:**
- **User Accounts** — identity for saving/loading DCF scenarios and the sign-in prompt on save (scenario persistence confirmed at Gate 2, 2026-10-06).

**Downstream:** none — this is the terminal stage of the V1 core loop (decide).

## 9. Open Questions & Risks

- **OQ-VAL-001 — DCF parameter list (ASM-007).** The full user-editable parameter set (discount rate confirmed; candidates include growth assumptions, terminal value inputs, projection horizon, capital structure items) must be finalized during V1 design under the BR-VAL-002 constraint that *all* of them are visible and editable. *Impact if late: DCF implementation blocked; SC-004 at risk.* *Owner: builder, during V1 design (brief ASM-007 validation).*
- **OQ-VAL-002 — Scenario persistence — RESOLVED at Gate 2 (2026-10-06).** Decision: yes — named DCF scenarios are saved/reloaded per account (FR-VAL-009/010, Should confirmed; UC-VAL-002 in scope). *Rationale on record: mirrors saved screens, cheap once accounts exist, and iterating on assumptions is exactly the behavior the platform wants.*
- **OQ-VAL-003 — Anonymous calculator access — RESOLVED at Gate 2 (2026-10-06).** Decision: the DCF calculator is publicly usable without an account; login is required only for saving scenarios (BR-VAL-010), consistent with the screener decision (BR-SCR-004).
- **RISK-VAL-001 — Garbage in, garbage out.** A user can enter absurd assumptions and get an absurd verdict. *Mitigation: sensible per-stock defaults (BR-VAL-008), "based on your assumptions" framing (BR-VAL-005), sensitivity table (BR-VAL-009), disclaimer; the platform does not validate assumptions against "correct" values because there are none — it is the user's model.*
- **RISK-VAL-002 — Simplicity vs. trust.** A deliberately simple DCF can diverge from professional-grade models. *Mitigation: honest positioning as the user's first-look tool (brief §10 glossary: "the builder's personal first-look valuation method"), plain-language framing, no claims of accuracy.*
