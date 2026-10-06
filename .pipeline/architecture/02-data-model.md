# Data Model — v1.0

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** architect · **Date:** 2026-10-06 · **Status:** submitted for builder approval
**Implements:** all Data Entities sections of the 7 approved domain reports, consolidated; resolves the cross-domain conflicts listed in §5.
**Engine:** PostgreSQL 17+ · **Access:** EF Core 10 (migrations, CRUD) + raw SQL for metric computation · **Conventions:** snake_case tables/columns; all timestamps `timestamptz` (UTC); all monetary values `numeric(18,4)` with explicit `currency` where multi-currency possible (TRY default); dates `date`.

---

## 1. Modeling principles

1. **Facts are append-only and dated** (BR-MDF-004, FR-MDF-015): no UPDATE/DELETE on fact rows; corrections arrive as new dated records (statements as new versions; macro values as new `recorded_at` rows). Derived tables may be re-computed but never edit facts.
2. **Provenance is structural** (FR-MDF-008, NFR-MDF-004): every fact table carries `source_ref` (uri or run reference) and `recorded_at`, both NOT NULL.
3. **One canonical number per metric** (BR-MDF-009): metric *values* live only in `derived_metrics` / `sector_metric_medians` / `market_snapshots`, written only by the Metrics Engine (C3b). Formulas are fixed in §6.
4. **Raw and adjusted prices coexist** (BR-MDF-010): `daily_prices` stores both series; historical analysis reads adjusted, current display reads raw; the served payload states which.
5. **Restatements are versions, not overwrites** (BR-MDF-011, FR-MDF-019): `financial_statements.version ∈ {as_reported, restated}`; current display uses the latest restated version, flagged; as-reported retained.
6. **Constraints enforce invariants** the UI depends on: duplicate-name rejection (UXR-G-029) via unique indexes; publish-requires-both-languages (BR-RES-003) via CHECK.
7. **Identity uses ASP.NET Core Identity's** standard schema (prefixed `asp_net_*`), extended with custom columns — not re-invented.

## 2. Entity overview

```
 MDF (facts)                     MOV                          SCR / VAL (user data)
 ───────────────────            ──────────────────           ───────────────────────
 instruments ──┬─ sector        macro_series                 asp_net_users ──┬─ saved_screens
               ├─ daily_prices    └ macro_values              (Identity)     ├─ dcf_scenarios
               ├─ fin_statements ── fin_line_items          market_snapshots└─ consent_records
               ├─ dividends              │                  (1/trading day)
               ├─ corporate_actions      │                   RES (content)
               ├─ kap_disclosures        │                  ─────────────────
               └─ index_constituents ── indices             business_descriptions
 indices ── index_levels                            sector_metric_medians
 derived_metrics (Metrics Engine output)             VAL: dcf_baselines
 FDF: funds ── fund_navs / fund_performances / fund_holdings
 Ops: ingest_runs · metric_catalog · coverage_metadata · data_freshness(view)
```

## 3. Tables (consolidated from all domain reports)

### 3.1 Market Data Foundation

**`instruments`** — *Instrument* (MDF §7)
| column | type | notes |
|---|---|---|
| id | bigint PK | |
| symbol | text UNIQUE NOT NULL | BIST ticker, e.g. `ASELS` |
| isin | text | nullable |
| name | text NOT NULL | official name (not translated — language-neutral display) |
| sector_id | bigint FK→sectors | nullable → unclassified flag (UC-MDF-003a) |
| listing_date | date | IPO date, feeds coverage context (FR-RES-016) |
| delisting_date | date | nullable |
| status | text | active / delisted |
| source_ref, recorded_at | | provenance |

**`sectors`** — *Sector/Industry*: `id`, `code`, `name_tr`, `name_en` (bilingual labels, brief §7), `parent_sector_id` FK self (2-level sector→industry hierarchy). Cardinality: 1 sector → * instruments.

**`indices`** — *Index*: `id`, `code` (`XU100`, `XU30`), `name_tr`, `name_en`.

**`index_constituents`** — *Constituent membership* (FR-MDF-007, BR-MDF-005): `id`, `index_id` FK, `instrument_id` FK, `effective_from` date, `effective_to` date NULL, `source_ref`. Append-only — no row is ever updated; universe changes add new rows. Current universe = view `v_current_universe` (membership where `effective_to IS NULL` and index = XU100). Instrument * ↔ * Index via membership.

**`daily_prices`** — *Daily EOD price & volume* (FR-MDF-001, BR-MDF-010)
| column | type | notes |
|---|---|---|
| instrument_id | bigint FK PK-part | |
| price_date | date PK-part | one row per instrument per trading day |
| open/high/low | numeric | raw |
| close_raw | numeric NOT NULL | current-display truth |
| close_adjusted | numeric | corporate-action-adjusted — historical-analysis truth |
| volume | bigint | |
| source_ref, recorded_at | | provenance |

Also `index_levels` (`index_id`, `level_date`, `close`, `source_ref`) — same grain for indices (FR-MDF-006).

**`financial_statements`** — *Financial statement* (FR-MDF-002, BR-MDF-011/FR-MDF-019)
| column | type | notes |
|---|---|---|
| id | bigint PK | |
| instrument_id | bigint FK NOT NULL | |
| period_type | text | `Q` or `FY` |
| period_end_date | date NOT NULL | fiscal period end |
| fiscal_year | int | |
| statement_type | text | `IS` / `BS` / `CF` |
| version | text | `as_reported` or `restated` |
| restatement_date | date NULL | set when version=restated |
| published_at | date | KAP publish date |
| source_ref, recorded_at | | provenance |
| | UNIQUE | (instrument_id, period_type, period_end_date, statement_type, version) |

One instrument → * statements; each statement → * line items.

**`fin_line_items`** — statement lines mapped to the canonical chart of accounts (§6.1): `statement_id` FK, `item_code` text (canonical), `value` numeric. UNIQUE (statement_id, item_code). Canonical mapping from the source taxonomy happens in ETL (C3a) — this is where KAP/XBRL tags collapse to one internal vocabulary.

**`dividends`** — *Dividend* (FR-MDF-003): `id`, `instrument_id` FK, `ex_date`, `pay_date` NULL, `amount_per_share` numeric, `currency`, `source_ref`. Instrument 1 → * dividends.

**`corporate_actions`** — *Corporate action* (FR-MDF-004): `id`, `instrument_id` FK, `action_type` (split / rights_issue / bonus_issue / other), `action_date`, `terms_json` jsonb (ratio, terms), `source_ref`. Feeds adjustment-factor computation (C3b).

**`kap_disclosures`** — *KAP disclosure* (FR-MDF-005): `id`, `instrument_id` FK, `disclosure_type`, `publish_date`, `title`, `source_url`, `document_path` (on-disk/VPS path — documents live outside the DB to keep it lean), `source_ref`. Input to the C4 drafting pipeline (UC-RES-004).

**`derived_metrics`** — *Derived metric* (MDF §7; FR-MDF-013/014/018/019)
| column | type | notes |
|---|---|---|
| instrument_id | bigint FK PK-part | |
| metric_code | text PK-part | one of the catalog codes (§6.2) |
| as_of_date | date PK-part | computation date; current = max per (instrument, metric) |
| value | numeric | NULL = not meaningful (e.g., P/E for loss-maker) — never 0-for-missing |
| window_years | int NULL | actual window used for CAGRs (FR-MDF-014) |
| is_adjusted | bool | basis marker (FR-MDF-018) |
| is_rested | bool | computed from restated statement version (FR-MDF-019) |
| computed_at | timestamptz | |

Append-only by date; the Metrics Engine writes one row per instrument × metric per trading day for all 18 screener metrics (≈450k rows/yr — §7 volumes). **Rejected alternative:** separate current-table (upserted) + history-table — two code paths for one concept; volume does not justify it.

**`coverage_metadata`** (FR-MDF-011, FR-FDF-004): `id`, `scope` (`instrument`/`fund`/`universe`), `instrument_id`/`fund_id` NULL, `data_type`, `available_from`, `available_to`, `notes`. Written by adapters during backfill/refresh; read by admin coverage report and the honest no-data states.

**`ingest_runs`** — scheduler run ledger (UC-MDF-001 alternates; operational): `id`, `job_code`, `started_at`, `finished_at`, `status` (`succeeded/failed/partial`), `stats_json` (counts), `error` text. Also backs `data_freshness` staleness logic (FR-MDF-016) via view `v_data_freshness` (last success per job/data type).

### 3.2 Market Overview & Macro

**`macro_series`** — *Macro indicator series* (MOV §7): `code` PK (`TUIK_CPI`, `INDEP_CPI`, `CBRT_REPO`, `USD_TRY`, `EUR_TRY`, `GOLD`), `name_tr`, `name_en`, `unit`, `source_name`, `cadence` (`daily`/`monthly`/`per_release`). Six rows in V1 (BR-MOV-008: exactly one canonical value per series per date).

**`macro_values`** — *Macro indicator value* (FR-MOV-014; revisions per UC-MOV-002b no-overwrite): `series_code` FK, `value_date`, `value` numeric, `source_ref`, `recorded_at`. UNIQUE (series_code, value_date, recorded_at); canonical value = latest `recorded_at` per (series, value_date). Revisions append; nothing overwritten.

**`market_snapshots`** — *Market overview snapshot* (MOV §7; BR-MOV-009 computed from MDF facts, one per trading day)
| column | type | notes |
|---|---|---|
| snapshot_date | date PK | trading day |
| xu100_level, xu100_change_pct, xu30_level, xu30_change_pct | numeric | FR-MOV-001 |
| breadth_advancing, breadth_declining, breadth_unchanged | int | FR-MOV-003 |
| volume_total | numeric | FR-MOV-005 |
| market_pe | numeric | cap-weighted aggregate, loss-makers excluded (D-05) |
| market_pe_excluded_count | int | disclosure (BR-MOV-010) |
| market_div_yield | numeric | Σ div_TTM / Σ mcap |
| gainers_json / losers_json | jsonb | top-10 arrays: symbol, change_pct, volume (BR-MOV-011 eligibility threshold applied at computation; FR-MOV-021) |
| sector_perf_json | jsonb | sector → daily change (+ precomputed 1W/1M/YTD for FR-MOV-017 Could) |
| as_of_trading_date, computed_at | | honesty stamps |

JSON columns for the fixed-shape lists are deliberate: 1 row/day, read-only, no ad-hoc querying of mover rows — **rejected alternative:** normalized mover/sector tables (join ceremony for zero query benefit at this grain).

### 3.3 Stock Screening

**`metric_catalog`** — *Screenable metric catalog* (SCR §7; BR-SCR-002/FR-SCR-017)
| column | notes |
|---|---|
| metric_code PK | the 18 codes (§6.2) |
| family | valuation / quality / growth / financial_health / dividends |
| unit, label_tr, label_en, description_tr, description_en | plain-language labels (NFR-SCR-002) |
| is_screenable bool | builder toggle, no code change (FR-SCR-017) |
| is_growth_cagr bool | growth metrics carry a window selector (FR-SCR-015) |
| sort_order | UI ordering |

**`saved_screens`** — *Saved screen* (SCR §7; SC-003)
| column | notes |
|---|---|
| id bigint PK | |
| user_id | FK→asp_net_users, cascade delete (BR-ACC-005) |
| name text | |
| criteria_json | jsonb — ordered array of *Criterion*: `{metricCode, bound: min|max|range, minValue?, maxValue?, window?}` (SCR §7: criterion is a building block, not standalone) |
| created_at, updated_at | |
| | UNIQUE (user_id, name) → duplicate-name rejection (UXR-G-029) |

User 1 → * saved screens. Result sets are never persisted (UC-SCR-003).

### 3.4 Stock Research

**`business_descriptions`** — *Business description* (RES §7; BR-RES-002/003, FR-RES-019..022/026)
| column | notes |
|---|---|
| id bigint PK | |
| instrument_id | FK — one published version active per instrument |
| version int | draft lineage (Stock 1 → * versions) |
| status | `draft` / `reviewed` / `published` |
| text_tr, text_en | NULLable until complete |
| source_refs_json | KAP disclosure ids used |
| last_reviewed_at, published_at | FR-RES-022 |
| | CHECK: `status <> 'published' OR (text_tr IS NOT NULL AND text_en IS NOT NULL)` — BR-RES-003 publication gate, enforced in DB and API (FR-RES-020) |

Serving rule: latest `published` version per instrument; none → `preparing` state (FR-RES-026).

**`sector_metric_medians`** — *Sector metric median* (RES §7; SD-002; confirmed OQ-UX-002): `sector_id`+`metric_code`+`as_of_date` PK, `median_value`, `peer_count` (UXR-RES-024), `excluded_count` (P/E loss-maker exclusion disclosure), `is_adjusted`, `computed_at`. Five valuation-family metrics only (P/E, P/B, EV/EBITDA, EV/FCF, FCF yield), current basis.

### 3.5 Valuation & DCF

**`dcf_baselines`** — *DCF model baseline* (VAL §7; BR-VAL-008, UC-VAL-003): `id`, `instrument_id` FK, `version` int, `params_json` (the confirmed 8-parameter set), `build_date`, `is_active` bool (one active per stock). Generated by C3b from canonical facts + builder-defined default rules (growth/horizon/rates); regenerated per build cycle via admin/CLI (C4). Stock 1 → * versions.

**`dcf_scenarios`** — *DCF scenario* (VAL §7; FR-VAL-009/010, UXR-VAL-012..018): `id`, `user_id` FK (cascade delete), `instrument_id` FK, `name`, `params_json` (same 8-parameter schema), `created_at`, `updated_at`. UNIQUE (user_id, instrument_id, name) → duplicate rejection. User 1 → * scenarios, per stock.

### 3.6 User Accounts (Identity)

**`asp_net_users`** (Identity schema) extended with: `language_pref` text NOT NULL DEFAULT `'tr'` (FR-ACC-004), `is_verified` (managed by Identity's `EmailConfirmed`), standard columns (email, password hash, lockout, security stamp). Roles via standard `asp_net_roles`/`asp_net_user_roles` — `builder` role seeds the admin.

**`consent_records`** — *Consent record* (ACC §7; NFR-ACC-001/004): `id`, `user_id` bigint NULL (FK without cascade — see §5.4), `user_ref_hash` text (anonymized identifier retained after deletion), `notice_version`, `consented_at`, `action` (`register`). 1 per account per notice version.

### 3.7 Fund Data Foundation

**`funds`** — *Fund* (FDF §7; BR-FDF-006): `code` PK, `name`, `fund_type` (TEFAS classification; ingest bounded to equity + equity-heavy mixed), `status`, `source_ref`.

**`fund_navs`** — *Fund NAV*: `fund_id`+`nav_date` PK, `nav_value`, `source_ref`. One per fund per publication day.

**`fund_performances`** — *Fund performance*: `fund_id`+`period`+`as_of_date`, `return_value` (as published).

**`fund_holdings`** — *Fund holding* snapshots (FDF §7; FR-FDF-003/004): `fund_id`+`as_of_date`+`line_no` PK, `instrument_id` FK NULL (matched to MDF instrument by symbol where possible — the future look-through link, unused in V1), `name_raw`, `weight` numeric NULL, `units` numeric NULL. Partial coverage expected and recorded in `coverage_metadata` (BR-FDF-004).

## 4. Relationships & cardinality

| Relationship | Cardinality | Keys |
|---|---|---|
| Sector → Instrument | 1 → * (0..1 sector per instrument; NULL = unclassified, flagged) | instruments.sector_id |
| Instrument ↔ Index (membership) | * ↔ * over time | index_constituents (effective-dated, append-only) |
| Instrument → DailyPrice / Statement / Dividend / CorporateAction / KAPDisclosure / DerivedMetric | 1 → * | FK instrument_id |
| Statement → LineItem | 1 → * (≈40–80) | FK statement_id |
| Fund → FundNav / FundPerformance / FundHolding | 1 → * | FK fund_id |
| FundHolding → Instrument | * → 0..1 (nullable, symbol-matched) | FK instrument_id |
| User → SavedScreen / DcfScenario | 1 → * (cascade delete) | FK user_id |
| User → ConsentRecord | 1 → * (retain after deletion, §5.4) | FK user_id NULL + user_ref_hash |
| Instrument → BusinessDescription (versions) | 1 → * versions, ≤1 published | FK + status |
| Instrument → DcfBaseline (versions) | 1 → * versions, 1 active | FK + is_active |
| MacroSeries → MacroValue | 1 → * (one canonical per date) | FK series_code |
| Sector+Metric+Date → SectorMetricMedian | 1 (computed) | PK |
| TradingDate → MarketSnapshot | 1 → 1 | PK snapshot_date |

## 5. Cross-domain conflicts → resolutions (design decisions)

| # | Conflict (as recorded in reports) | Resolution |
|---|---|---|
| 5.1 | **Sector medians**: RES report owns the entity ("derived view powering the strip"), but values derive from MDF canonical facts (BR-MOV-009/BR-RES-004). | Computed by the **single Metrics Engine (C3b)** alongside all other canonical metrics — one quantitative codebase (AD-06) — stored in `sector_metric_medians`, *served* by the Research API module. RES owns the requirement; MDF owns the computation. Rationale: a second formula implementation anywhere would break UXR-G-011 consistency. |
| 5.2 | **Market P/E vs. sector medians formulas**: MOV's aggregate (formula was open, OQ-MOV-004 residual) vs. RES's confirmed sector *medians* (OQ-UX-002). | Both are canonical, with different definitions, both disclosed on screen: market P/E = **cap-weighted aggregate** Σmcap/ΣNI_TTM excluding loss-makers (builder decision D-05, 2026-10-06); sector strip = **median** over covered-universe peers (confirmed OQ-UX-002), P/E medians exclude loss-makers with counts. Rejected: median at market level (less representative of a cap-weighted market; the "market P/E" convention users meet elsewhere is aggregate). |
| 5.3 | **Screener's metric catalog vs. MDF's derived metrics**: SCR owns the 18-metric catalog; MDF owns values (BR-SCR-001). | `metric_catalog` (screener-facing: labels, flags, visibility) references `metric_code` values written only by the Metrics Engine into `derived_metrics`. The catalog never stores values; the engine never computes non-catalog codes. |
| 5.4 | **Account deletion vs. KVKK consent retention** (ACC: BR-ACC-005 remove personal data; NFR-ACC-004 consent records retained as KVKK evidence). | `DELETE /api/v1/me` hard-deletes the user row (cascade: saved_screens, dcf_scenarios) and sets `consent_records.user_id = NULL`, keeping `user_ref_hash` + notice version + date. Rationale: consent evidence must outlive the account; a salted hash identifier keeps the record useful without identifying the person. |
| 5.5 | **Business description inputs**: descriptions are RES content, drafted from KAP disclosures owned by MDF. | C4 pipeline reads MDF's `kap_disclosures`; drafts are stored in RES's `business_descriptions` with `source_refs_json` pointing back to disclosure ids. Clean one-way dependency RES→MDF, matching the domain map. |
| 5.6 | **Macro value revisions** (UC-MOV-002b: "revision stored as new dated record" vs. "one canonical value per series per date" BR-MOV-008). | `macro_values` allows multiple `recorded_at` rows per (series, date); the canonical value is the latest. Storage keeps history; serving keeps uniqueness. Same pattern as statement restatements (5.7). |
| 5.7 | **Restated statements** (BR-MDF-011: display latest restated, retain as-reported). | Both versions stored (`version` column); serving layer selects latest restated per (instrument, period, type) and flags `is_rested` through to metrics (`derived_metrics.is_rested`) and UI marks. |
| 5.8 | **DCF baselines "build-time"** (VAL: builder-defined, versioned, per build cycle) vs. canonical-fact derivation. | Baselines are *derived rows*, not hand-edited content: C3b generates them from canonical facts (FCF, net debt, cash, share count) + builder default rules for growth/horizon/rates (config). Regeneration is an admin/CLI action; every regeneration bumps `version`. Rationale: hand-maintained numbers would violate the no-black-box posture (NFR-VAL-004) and rot silently. |

## 6. Canonical quantitative definitions (owned by `/src/Core`, C3b)

### 6.1 Canonical chart of accounts (`fin_line_items.item_code`)

`REV` revenue · `COGS` cost of sales · `GROSS_PROFIT` (= REV−COGS when not reported) · `SGA` · `EBIT` operating income · `DEPR_AMORT` D&A · `EBITDA` (= EBIT + D&A when not reported) · `FIN_EXP` net interest/finance expense · `TAX_EXP` income tax expense · `PRETAX_INC` · `NI` net income · `MINORITY_INT` · `CAPEX` · `ΔWC` change in working capital (from CF statement components) · `CASH` cash & equivalents · `ST_DEBT`, `LT_DEBT` · `TOTAL_EQUITY` (attributable) · `CUR_ASSETS`, `CUR_LIAB` · `SHARES_DILUTED` · `SHARES_OUT`. `OTHER_*` codes pass through source items not yet mapped (recorded, ignored by metrics — honest gap, not silent drop).

### 6.2 The 18 screener metrics (FR-SCR-001 / FR-MDF-013)

| code | family | definition (TTM unless noted) | not-meaningful rule |
|---|---|---|---|
| `pe` | valuation | price / (NI_TTM / SHARES_DILUTED) | NI_TTM ≤ 0 → NULL |
| `pb` | valuation | price / (TOTAL_EQUITY / SHARES_DILUTED) | equity ≤ 0 → NULL |
| `ev_ebitda` | valuation | (mcap + ST+LT_DEBT − CASH) / EBITDA_TTM | EBITDA ≤ 0 → NULL |
| `ev_fcf` | valuation | EV / FCF_TTM | FCF ≤ 0 → NULL |
| `fcf_yield` | valuation | FCF_TTM / mcap | FCF ≤ 0 → NULL |
| `roic` | quality | EBIT_TTM × (1 − t_eff) / (ST+LT_DEBT + TOTAL_EQUITY − CASH); t_eff = TAX_EXP/PRETAX_INC (fallback 0.25) | IC ≤ 0 → NULL |
| `roe` | quality | NI_TTM / TOTAL_EQUITY | equity ≤ 0 → NULL |
| `gross_margin` | quality | GROSS_PROFIT / REV | |
| `operating_margin` | quality | EBIT / REV | |
| `rev_cagr` | growth | CAGR of FY REV over window (3/5/10Y), `window_years` = actual span when history is shorter (FR-MDF-014) | <2 FYs → NULL |
| `eps_cagr` | growth | CAGR of FY EPS | <2 FYs → NULL |
| `fcf_cagr` | growth | CAGR of FY FCF | <2 FYs or sign change → NULL (honest) |
| `net_debt_ebitda` | financial_health | (ST+LT_DEBT − CASH) / EBITDA_TTM | EBITDA ≤ 0 → NULL |
| `interest_coverage` | financial_health | EBIT_TTM / FIN_EXP | FIN_EXP ≤ 0 → NULL |
| `current_ratio` | financial_health | CUR_ASSETS / CUR_LIAB | |
| `div_yield` | dividends | Σ DPS_TTM / price | never paid → NULL + coverage state |
| `div_cagr` | dividends | CAGR of DPS over window | <2 payments → NULL |
| `payout_ratio` | dividends | Σ DIV_TTM / NI_TTM | NI ≤ 0 → NULL (may exceed 1 — displayed honestly) |

`FCF = NI + DEPR_AMORT − ΔWC − CAPEX` (indirect method, canonical). `mcap = close_raw × SHARES_DILUTED`. Growth criteria store the chosen window (3/5/10Y) in the criterion (FR-SCR-015).

### 6.3 Market & sector aggregates

- `market_pe = Σ mcap_i / Σ NI_TTM_i` over current-universe constituents with NI_TTM > 0 (D-05); `market_pe_excluded_count` = loss-makers excluded (BR-MOV-010 disclosure).
- `market_div_yield = Σ DIV_TTM_i / Σ mcap_i` over all constituents.
- Sector medians: median of each valuation-family metric across same-sector current-universe peers, current basis; P/E median excludes loss-makers with `excluded_count` (confirmed OQ-UX-002); `peer_count` displayed (UXR-RES-024).
- Movers: top-10 by daily % change among stocks ≥ volume-eligibility threshold (BR-MOV-011); threshold is a config value set from the observed volume distribution during V0 (recorded in `metric_catalog`-adjacent config), not hard-coded.

### 6.4 DCF model (single formula, server-side — AD-08)

```
PV_explicit = Σ_{t=1..N}  FCF₀·(1+g)^t / (1+r)^t
TV           = FCF₀·(1+g)^N·(1+g_t) / (r − g_t)          [requires r > g_t]
EquityValue  = PV_explicit + TV/(1+r)^N + Cash − NetDebt
FairValuePS  = EquityValue / SharesDiluted
MOS          = (FairValuePS − Price) / FairValuePS        (plain-language verdict, client-rendered)
```
Parameters (confirmed set, OQ-UX-001): `base_fcf, growth_rate, horizon_years (1..10), terminal_growth, discount_rate, net_debt, cash, share_count` — all user-editable (BR-VAL-002). Sensitivity grid: `discount_rate × terminal_growth` (default 5×5 centered on user values). Not computable → `DCF_NOT_COMPUTABLE` with the violated constraint named (UXR-VAL-009 analog for math, not missing data).

## 7. Volume estimates & retention

| Table | Rows (steady state, 10Y depth) | Notes |
|---|---|---|
| daily_prices | ~250k (100 × ~2,500 days) | + index_levels ~5k |
| derived_metrics | ~4.5M (100 × 18 × ~2,500 days) | largest equity table; single append table (see §3.1) |
| fin statements/line items | ~12k / ~720k | 100 × 40 periods × 3 × ~60 lines |
| kap_disclosures | 20k–100k | metadata only; documents on disk |
| fund_navs | ~2–4M | equity+equity-heavy funds (BR-FDF-006 bound keeps this finite) |
| fund_holdings | ~2–5M | periodic snapshots × ~30 lines |
| everything else | < 150k combined | macro, snapshots, medians, users, content |

Total DB estimate **2–6 GB** → fits Backblaze B2 free tier (10 GB) for the 30-day backup window (§01 §10.8). All facts retained for the life of the platform (NFR-MDF-002/NFR-FDF-002) — no TTL anywhere. Partitioning (`derived_metrics`, `fund_navs` by year) is a documented future optimization, deliberately not built now — at these sizes plain B-tree indexes on the PKs suffice; premature partitioning would complicate EF mappings for no measured benefit.

## 8. Migrations & change policy

- EF Core migrations, **forward-only** (no down-migrations in prod); applied by the deploy job (`docker compose run --rm api migrate`) before `up -d` (§01 §6.2).
- Every migration runs in CI against a real Postgres (Testcontainers) before it can merge — migration correctness is test-gated, not hope-gated.
- Fact-table schema changes are additive (new columns/tables); existing fact rows are never rewritten (§1.1). Derived tables (`derived_metrics`, snapshots, medians) may be recomputed by re-running the engine after definition changes — definitions are versioned in `/src/Core` and metric recompute is idempotent by (key, as_of_date).
- Seed data: `metric_catalog` (18 rows), `macro_series` (6 rows), `builder` role + builder account bootstrap — via a checked-in seed migration/script, exercised in CI and local compose.
