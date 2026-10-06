# Fund Data Foundation Analysis

**Domain code:** FDF · **Slug:** `fund-data-foundation` · **Report order:** 7 of 7 · **Status:** APPROVED at Gate 2 (2026-10-06, amendments incorporated)
**Source:** `.pipeline/00-project-brief.md` (§5 V0 — fund data foundation; §6 Scope — Out; §7; §8 ASM-002) · **Domain map:** `.pipeline/analysis/00-domain-map.md`

## 1. Overview

This domain ingests and stores publicly available fund data from TEFAS — NAV history, performance, and holdings where published. It is a **data-only** capability in this release: no fund UI exists in V1 (the fund screener and fund pages are explicitly out of scope; the fund *product* is not committed). Its purpose is to de-risk future fund features (fund screener, look-through exposure — vision items) and to record coverage gaps per ASM-002, while adding a second data pipeline to the platform's data-engineering demonstration.

Business value delivered:

- A verified, dated fund-data store ready for future phases, with measured coverage (ASM-002 validation: "measure coverage during the fund data foundation work and record gaps"). [Brief §8 ASM-002; §6 vision items]
- A second, source-independent pipeline strengthening the public data-engineering artifact. [OBJ-004, SC-007]
- No V1 user-facing dependency: nothing in the V1 product consumes this data, so its risk to the December launch is contained. [OBJ-001, OBJ-002]

Screens owned: none (headless; fund UI is Won't-this-release).

## 2. Actors

| Actor | Role | Goal | Frequency |
|---|---|---|---|
| Automated fund data pipeline | System actor | Ingest and refresh NAV, performance, and holdings from TEFAS | Daily (TEFAS publishes NAVs on business days) |
| The builder | Human; data steward | Define the fund universe (BR-FDF-006); run the initial backfill; measure and record coverage gaps (ASM-002 validation) | Intensive during V0; exception-driven after |
| TEFAS | External source | Public fund data — NAV, performance, holdings | Polled daily |
| Future fund features (post-V1 phases) | Eventual consumers | Fund screener, fund pages, look-through exposure — none in V1 | — |

## 3. Business Rules

- **BR-FDF-001** — Fund data is data-only in this release: no fund screener, no fund pages, no fund UI of any kind in V1. *Source: brief §5 (fund data foundation — "data only, no fund UI in V1"), §6 (fund product out of scope).* *Enforced: product boundary.*
- **BR-FDF-002** — TEFAS is the source for fund data; data acquisition remains free/public ($0 budget). *Source: brief §7 (Constraints).* *Enforced: policy.*
- **BR-FDF-003** — Fund facts follow the same history-first-class principles as the equity foundation: every fact stored with its date, historical facts never overwritten or discarded. *Source: brief §5 (V0 — historical data as first-class, applies to the data foundation as a whole); BR-MDF-004 consistency.* *Enforced: system.*
- **BR-FDF-004** — Holdings are ingested where publicly available; partial holdings coverage is expected and recorded per fund, not treated as failure. *Source: brief §8 ASM-002 ("holdings may be available only for some funds").* *Enforced: system + builder-maintained coverage record.*
- **BR-FDF-005** — Fund data follows the platform's daily refresh discipline (NAV on TEFAS publication cadence; holdings at their publication cadence), without manual intervention. *Source: brief §7 (daily EOD refresh), SC-005.* *Enforced: system.*
- **BR-FDF-006** — The fund data foundation covers equity funds and equity-heavy mixed funds first; expansion to other fund types (bond, money market, etc.) is a post-V1 decision. *Source: builder decision at Gate 2 review, 2026-10-06 (resolves OQ-FDF-001).* *Enforced: system (ingest scope).*

## 4. Use Cases

### UC-FDF-001 — Backfill fund data history
**Primary actor:** the builder (data steward)
**Preconditions:** fund universe defined (BR-FDF-006); TEFAS access method verified (OQ-FDF-002).
**Main success scenario:**
1. The builder triggers the initial historical ingest for the defined fund universe.
2. The pipeline ingests NAV history, performance data, and holdings (where published) from TEFAS.
3. Every fact is stored with its date and source reference.
4. Achieved depth per data type is recorded.
**Alternate / error flows:** (a) TEFAS limits bulk access → backfill proceeds incrementally; the limitation is recorded.
**Postconditions:** fund history available in storage for future phases; no V1 surface depends on it.

### UC-FDF-002 — Refresh fund data
**Primary actor:** automated fund data pipeline
**Preconditions:** backfill done; TEFAS reachable.
**Main success scenario:**
1. On the daily cycle, the pipeline pulls new NAV values and newly published performance and holdings data.
2. Facts are validated, dated, and stored.
**Alternate / error flows:** (a) Source unavailable → last-known-good retained, staleness recorded, builder alerted (FR-FDF-005) — mirroring the MDF pattern (FR-MDF-012/016).
**Postconditions:** fund store current per TEFAS publication cadence, unattended. [SC-005]

### UC-FDF-003 — Measure and record fund data coverage
**Primary actor:** the builder (data steward) — the ASM-002 validation activity
**Preconditions:** backfill has run.
**Main success scenario:**
1. The builder measures coverage per data type: share of funds with NAV history, performance data, and holdings; depth achieved.
2. Gaps are recorded in the coverage record (which funds lack holdings, from when data exists).
3. The coverage record is stored for future fund-feature planning.
**Alternate / error flows:** (a) Holdings coverage is very low → recorded; future features must plan around it (accepted per ASM-002).
**Postconditions:** ASM-002 validation closed: the future fund features know exactly what data they can build on.

## 5. Functional Requirements

| ID | Statement | Traces to | MoSCoW |
|---|---|---|---|
| FR-FDF-001 | The system shall ingest and store fund NAV history per fund, dated. | UC-FDF-001, UC-FDF-002 | Must |
| FR-FDF-002 | The system shall ingest and store fund performance data, dated. | UC-FDF-001, UC-FDF-002 | Must |
| FR-FDF-003 | The system shall ingest and store fund holdings where publicly available, with their as-of dates. | UC-FDF-001, UC-FDF-002 | Must |
| FR-FDF-004 | The system shall record per-fund coverage metadata (which data types exist and from what date, including holdings availability). | UC-FDF-003 | Should |
| FR-FDF-005 | The system shall flag fund-data anomalies (failed, missing, or late ingest) to the builder via log alert and e-mail. | UC-FDF-002 | Should |
| FR-FDF-006 | The system shall refresh fund data on the daily cycle without manual intervention. | UC-FDF-002 | Must |
| FR-FDF-007 | The system shall provide a fund screener UI. | — | Won't (this release — brief §6 explicit) |
| FR-FDF-008 | The system shall provide fund pages UI. | — | Won't (this release — brief §6 explicit) |
| FR-FDF-009 | The system shall provide look-through exposure analysis. | — | Won't (this release — vision item, brief §6) |

## 6. Non-Functional Requirements

- **NFR-FDF-001 — Coverage transparency:** NAV, performance, and holdings coverage is measured and recorded during V0 (ASM-002 validation); gaps are explicit per fund, never silently dropped.
- **NFR-FDF-002 — Retention:** fund history retained for the life of the platform, stored point-in-time-consistent (BR-FDF-003).
- **NFR-FDF-003 — Cost:** fund data acquisition cost remains $0 (TEFAS public source).
- **NFR-FDF-004 — Public reproducibility:** the fund pipeline is documented and re-runnable from the public repository alongside the equity pipeline [OBJ-004, SC-007].

## 7. Data Entities

| Entity | Key attributes (conceptual) | Notes / cardinality |
|---|---|---|
| Fund | code, name, type (e.g., equity, bond, money market — TEFAS classification), status | Fund 1 → * NAV records, * performance records, * holdings snapshots |
| Fund NAV | fund, date, NAV value | one per fund per publication day |
| Fund performance | fund, period, return metric as published | per fund per period |
| Fund holding | fund, as-of date, holding lines (instrument, weight/units where published) | snapshot per publication; each line may reference an MDF instrument (BIST stock) — the future look-through link, unused in V1 |

## 8. Dependencies

**Upstream:** external only — **TEFAS** (public source). No internal upstream dependencies; this pipeline is independent of the equity pipeline (separate source, same engineering patterns).

**Downstream:** none in V1 — by design (BR-FDF-001). Future phases (fund screener/pages, look-through exposure) are the eventual consumers; fund holding lines will reference MDF instruments for look-through.

## 9. Open Questions & Risks

- **OQ-FDF-001 — Fund universe scope — RESOLVED at Gate 2 (2026-10-06).** Decision: start with equity funds and equity-heavy mixed funds — the funds the future look-through and fund-screener features would actually use (BR-FDF-006); expansion to other fund types is a post-V1 decision. *Impact note retained for the record: an unbounded universe would have meant backfill scope creep for a solo builder.*
- **OQ-FDF-002 — TEFAS access method.** Whether TEFAS offers a practical programmatic interface (vs. page scraping) is unverified; terms and stability affect both backfill and daily refresh. *Owner: builder, during V0 (ASM-002 validation). Impact if hostile: the fund foundation slips or degrades — contained, since no V1 surface depends on it.*
- **RISK-FDF-001 — Partial holdings coverage.** Holdings may be available only for some funds (ASM-002, accepted). *Mitigation: per-fund coverage record (FR-FDF-004); future features must degrade gracefully.*
- **RISK-FDF-002 — Fund-UI scope creep.** The vision features (fund screener, look-through) are attractive and data will be sitting there. *Mitigation: BR-FDF-001 boundary; fund UI remains a separate, explicitly scoped future phase (brief §6).*
