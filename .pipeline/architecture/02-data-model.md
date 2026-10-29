# Data Model — v1.5

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** architect · **Date:** 2026-10-06 · **Status:** approved — v1.1 added `quarantined_facts`; v1.2 (2026-10-07) applied the builder final-review mirror patches: DCF `debt`-parameter clarification (F-VAL-1), rights-issue adjustment factor (Q3), window-suffixed CAGR codes + volume correction (Q5); **v1.3 (2026-10-15) applies the builder's post-board correctness feedback** (12 verified issues across schema, formulas, sector accounting, data sources, test fidelity): statement `version` sequence, statement unit-scale/currency normalization, corporate-action-adjusted per-share CAGRs, `OCF`-based FCF, TMS 29 regime-break flag, `accounting_profile` + financial-sector metric posture, sector-aware DCF; **v1.4 (2026-10-24) adds definition-version provenance** — `formula_version` on `derived_metrics` and `sector_metric_medians` (§3.1, §8; `01` §10.9.13, TKT-foundation-070), additive and nullable, with an honest empty backfill; **v1.5 (2026-10-25) adds the public-site company identity map** — `kap_public_company_map`, one row per instrument carrying the public-site company oid and the VYK numeric `companyId` in separate, distinctly-shaped columns with per-space provenance (§3.1, §8; `01` v1.5.54 §10.9.19, TKT-foundation-080), additive and forward-only with no back-filled row. Change records in `01-system-architecture.md` §13.1.
**Implements:** all Data Entities sections of the 7 approved domain reports, consolidated; resolves the cross-domain conflicts listed in §5.
**Engine:** PostgreSQL 17+ · **Access:** EF Core 10 (migrations, CRUD) + raw SQL for metric computation · **Conventions:** snake_case tables/columns; all timestamps `timestamptz` (UTC); all monetary values `numeric(18,4)` with explicit `currency` where multi-currency possible (TRY default); **canonical monetary facts are stored in raw single-unit TRY** (1 TRY, 1 share — no thousands/millions scaling; §3.1 normalization); dates `date`.

---

## 1. Modeling principles

1. **Facts are append-only and dated** (BR-MDF-004, FR-MDF-015): no UPDATE/DELETE on fact rows; corrections arrive as new dated records (statements as new versions; macro values as new `recorded_at` rows). Derived tables may be re-computed but never edit facts.
2. **Provenance is structural** (FR-MDF-008, NFR-MDF-004): every fact table carries `source_ref` (uri or run reference) and `recorded_at`, both NOT NULL.
3. **One canonical number per metric** (BR-MDF-009): metric *values* live only in `derived_metrics` / `sector_metric_medians` / `market_snapshots`, written only by the Metrics Engine (C3b). Formulas are fixed in §6. **Derived metric rows record the definition that produced them** — `formula_version` (v1.4, §3.1; `01` §10.9.13) on `derived_metrics` and `sector_metric_medians` — so a formula change is distinguishable from a fact restatement after the fact.
4. **Raw and adjusted prices coexist** (BR-MDF-010): `daily_prices` stores both series; historical analysis reads adjusted, current display reads raw; the served payload states which.
5. **Restatements are versions, not overwrites** (BR-MDF-011, FR-MDF-019): `financial_statements.version` is an **integer sequence** — `0` = as-reported, `1, 2, …` = successive restatements in publication order (a period may be restated more than once, as on KAP under TMS 29); current display uses the highest version, flagged; every earlier version is retained.
6. **Constraints enforce invariants** the UI depends on: duplicate-name rejection (UXR-G-029) via unique indexes; publish-requires-both-languages (BR-RES-003) via CHECK.
7. **Identity uses ASP.NET Core Identity's** standard schema (prefixed `asp_net_*`), extended with custom columns — not re-invented.
8. **Scale and currency are normalized at the ETL boundary, provenance retained** (§3.1): sources report in mixed unit scales and reporting currencies; `fin_line_items` stores one canonical unit/currency, and each statement records the source's declared scale/currency for audit.

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
 Ops: ingest_runs · quarantined_facts · metric_catalog · coverage_metadata · data_freshness(view)
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
| accounting_profile | text NOT NULL DEFAULT `'industrial'` | `industrial` / `bank` / `insurance` / `reit` — drives which metrics are meaningful (§6.2) and whether the DCF applies (§6.4). Builder-curated checked-in seed for BIST 100 financial-sector members; a source sync never overwrites it (it is a classification, not a source fact). |
| listing_date | date | IPO date, feeds coverage context (FR-RES-016) |
| delisting_date | date | nullable |
| status | text | active / delisted |
| source_ref, recorded_at | | provenance |

**`sectors`** — *Sector/Industry*: `id`, `code`, `name_tr`, `name_en` (bilingual labels, brief §7), `parent_sector_id` FK self (2-level sector→industry hierarchy). Cardinality: 1 sector → * instruments.

**`indices`** — *Index*: `id`, `code` (`XU100`, `XU030`), `name_tr`, `name_en`.

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

**`share_counts`** — *Per-instrument paid-in capital* (FR-MDF-002/008; 01 §10.9.11, TKT-foundation-058 as renamed by TKT-foundation-086 — the table was never documented before, a 058 gap closed at v1.5.60). One row per instrument; the figure the VYK `/memberSecurities` security lines carry (`currentCapital`, summed across the instrument's matching tertip lines).
| column | type | notes |
|---|---|---|
| instrument_id | bigint PK, FK→instruments (`Restrict`) | one row per instrument |
| paid_in_capital | numeric(18,4) NOT NULL | **paid-in capital in TRY** (USER DECISION D4) — 64/556 live rows carry sub-unit precision, so it is money, never a share count. The column was `shares_outstanding` before v1.5.60's forward-only rename; the values are unchanged and the name now says what the value is. |
| recorded_at | timestamptz NOT NULL | when the value was last written (the served `paidInCapitalAsOf`); an unchanged re-run does not move it |
| source_ref | text NOT NULL | provenance — `kap` (the VYK gateway) |

**Honest absence:** an instrument with no security line (or only closed lines) has **no row** — never a zero (the 031/052 doctrine) — and a matched group whose tertip lines are not all readable is withheld, its stale row removed. **This table is never a source of shares outstanding** (D4 verbatim: *"no `sharesOutstanding` claim from that lane"*).

**`price_derived_share_counts`** — *Per-instrument price-derived share count* (FR-MDF-008; 01 §10.9.11, TKT-foundation-086, USER DECISION D5). One row per instrument; the honest count the prices lane derives from the payload it already fetches: `count = PD ÷ close` (İş Yatırım's market capitalisation and close, from **one** row).
| column | type | notes |
|---|---|---|
| instrument_id | bigint PK, FK→instruments (`Restrict`) | one row per instrument |
| shares_outstanding | bigint NOT NULL | the derived count — **integer-valued by construction** (a non-integral quotient is withheld, never rounded) and never zero |
| market_cap_raw | numeric(28,8) NOT NULL | the source's `PD` (TL) — the numerator, stored so the arithmetic is auditable from the row |
| close_raw | numeric(28,8) NOT NULL | the close (TL) — the denominator, stored for the same reason |
| price_date | date NOT NULL | the session date of the operand pair — the served `sharesOutstandingAsOf` |
| recorded_at | timestamptz NOT NULL | when the row was last written; an unchanged re-run does not move it |
| source_ref | text NOT NULL | provenance — `isyatirim` (the prices lane) |

**The honesty rule:** written only when `PD` is present and positive, the close is positive, and `PD ÷ close` is integral within the store's pinned tolerance; otherwise the operand pair is **withheld and counted** under `PD_MISSING` / `CLOSE_INVALID` / `NON_INTEGRAL_QUOTIENT` and **no row** is written (never rounded, never zero). The row is input-aware (identical operands → no write, no `recorded_at` movement; changed operands repaired in place), **monotonic** (an older fact never regresses it — `stale`), and a withheld fact leaves an existing row alone. **Precedence (pinned):** a statement-reported `SHARES_DILUTED` count supersedes this derived count when a statements decoder evidences one; this store is the fallback, not the authority. **The derived count never enters `fin_line_items`** (§6.1): it is not a reported statement line, it is a derived fact with its own store and provenance (UPSTREAM-1's per-line provenance stays owed, untouched by this lane).

**`financial_statements`** — *Financial statement* (FR-MDF-002, BR-MDF-011/FR-MDF-019)
| column | type | notes |
|---|---|---|
| id | bigint PK | |
| instrument_id | bigint FK NOT NULL | |
| period_type | text | `Q` or `FY` |
| period_end_date | date NOT NULL | fiscal period end |
| fiscal_year | int | |
| statement_type | text | `IS` / `BS` / `CF` |
| version | int NOT NULL | `0` = as-reported; `1, 2, …` = restatement sequence in publication order (v1.3 — a period may be restated repeatedly, e.g. successive TMS 29 re-basings) |
| restatement_date | date NULL | publication date of this restating report (NULL for version 0) |
| published_at | date | KAP publish date |
| source_unit_scale | text NOT NULL | the source's declared scale: `units` / `thousands` / `millions` — audit provenance for the §3.1 normalization (v1.3) |
| source_currency | text NOT NULL | the statement's reporting currency: `TRY` / `USD` / `EUR` (v1.3) |
| source_ref, recorded_at | | provenance |
| | UNIQUE | (instrument_id, period_type, period_end_date, statement_type, version) — serving selects max `version` per (instrument, period_type, period_end_date, statement_type) |

One instrument → * statements; each statement → * line items. **Serving rule (v1.3):** current metrics/display use `ORDER BY version DESC LIMIT 1` per natural key (highest version); `is_rested` = `version > 0`; all earlier versions retained (§1.1).

**`fin_line_items`** — statement lines mapped to the canonical chart of accounts (§6.1): `statement_id` FK, `item_code` text (canonical), `value` numeric **in raw single-unit TRY**. UNIQUE (statement_id, item_code). Canonical mapping from the source taxonomy happens in ETL (C3a) — this is where KAP/XBRL tags collapse to one internal vocabulary.

**Unit & currency normalization (v1.3; issue 2):** sources report in mixed unit scales (*nominal TRY vs. thousands vs. millions*) and reporting currencies (heavyweights such as THYAO report in USD). The C3a ETL normalizes **every** `fin_line_items.value` (including `SHARES_DILUTED`/`SHARES_OUT`, to raw share counts) to raw single-unit TRY before insert: scale is applied from the payload's declared `source_unit_scale`; a non-TRY `source_currency` is converted at the **period-end rate** (the latest `macro_values` row for `USD_TRY`/`EUR_TRY` with `value_date ≤ period_end_date`). This makes `mcap = close_raw × SHARES_DILUTED`, `NI_TTM`, and `EBITDA_TTM` dimensionally consistent, so ratios cannot be off by a factor of 1,000 or the FX rate. An undeclared/invalid scale or currency is a payload-schema failure (`SCHEMA_MISMATCH`); a currency needing conversion with no period-end rate is quarantined with reason `MISSING_FX_RATE` (catalog extended in FU §2). See §5.9 for the FX read edge.

**`dividends`** — *Dividend* (FR-MDF-003): `id`, `instrument_id` FK, `ex_date`, `pay_date` NULL, `amount_per_share` numeric, `currency`, `source_ref`. Instrument 1 → * dividends.

**`corporate_actions`** — *Corporate action* (FR-MDF-004): `id`, `instrument_id` FK, `action_type` (split / rights_issue / bonus_issue / other), `action_date`, `terms_json` jsonb (ratio, terms), `source_ref`. Feeds adjustment-factor computation (C3b).

Adjustment-factor system (BR-MDF-010; Q3 resolution 2026-10-07): split 1-for-n → `1/n`; bonus b-new-per-1 → `1/(1+b)`; rights q-new-per-1 at subscription `P_S`, cum price `P_C` (last raw close on/before `action_date`) → `(P_C + q·P_S)/((1+q)·P_C)` (theoretical ex-rights basis); factors compose multiplicatively over time; `close_adjusted = close_raw × Π factors of all actions after the price date`; degenerate rights inputs (q ≤ 0 or P_C ≤ 0) invalidate the action.

**`kap_disclosures`** — *KAP disclosure* (FR-MDF-005): `id`, `instrument_id` FK, `disclosure_type`, `publish_date`, `title`, `source_url`, `document_path` (on-disk/VPS path — documents live outside the DB to keep it lean), `source_ref`. Input to the C4 drafting pipeline (UC-RES-004).

**`kap_public_company_map`** — *The stored company identity map* (FR-MDF-002/008/012; 01 §10.9.19, TKT-foundation-080): **one row per instrument**, keyed by our `instrument_id` (PK, FK→`instruments`, `Restrict`). It records the **public site's** opaque company oid and, when a run holds that evidence, the **VYK** numeric `companyId` — **three identifier spaces in three space-labelled columns, never conflated**:

| column | type | notes |
|---|---|---|
| instrument_id | bigint PK | **our** space. An un-mapped instrument has **no row** (honest absence — never a placeholder oid) |
| kap_public_oid | text NOT NULL | the **public site's** company oid (ASELS `4028e4a1413b7ef401413bc2251e0047`); CHECK `^[0-9a-f]{32}$` |
| kap_public_oid_source | text NOT NULL | provenance of the oid — which resolution body/route (the paced `POST /tr/api/search/combined` plus its keyword) |
| kap_public_oid_as_of | date NOT NULL | the evidence date of that resolution |
| vyk_company_id | text NULL | the **VYK gateway's** numeric companyId (ASELS `866`); CHECK `^[0-9]{1,12}$` |
| vyk_company_id_source | text NULL | provenance of the VYK id — **its own**, never the oid's |
| vyk_company_id_as_of | date NULL | evidence date of the VYK id; CHECK: the id and both provenance columns are present or absent together |
| recorded_at | timestamptz | last write; an `unchanged` re-resolution does not move it |

The two CHECKs are the **isolation invariant made enforceable**: a numeric companyId cannot be written where a public oid belongs, and an oid cannot be written where the VYK id belongs — ASELS's oid and `866` coexist as recorded facts about different spaces, never as translations of each other. Re-resolution is idempotent (identical input ⇒ *unchanged*, no write; a changed identity or evidence date repairs the same row — one row per instrument, never a duplicate). The public-site lanes read their oid from here; nothing re-derives a mapping per walk, and no VYK id is ever inferred from an oid.

**`derived_metrics`** — *Derived metric* (MDF §7; FR-MDF-013/014/018/019)
| column | type | notes |
|---|---|---|
| instrument_id | bigint FK PK-part | |
| metric_code | text PK-part | one of the catalog codes (§6.2); windowed CAGRs are window-suffixed, e.g. `rev_cagr_3y` (Q5, 2026-10-07) |
| as_of_date | date PK-part | computation date; current = max per (instrument, metric) |
| value | numeric | NULL = not meaningful (e.g., P/E for loss-maker) — never 0-for-missing |
| window_years | int NULL | actual window used for CAGRs (FR-MDF-014) |
| is_adjusted | bool | basis marker (FR-MDF-018) |
| is_rested | bool | computed from `version > 0` statement rows (FR-MDF-019) |
| regime_break | bool NULL | v1.3 — CAGR metrics only: TRUE when the compounding window spans the TMS 29 transition (min FY `period_end_date` < 2023-12-31 ≤ max FY `period_end_date`); drives the regime-break warning (§6.2). |
| formula_version | int NULL | **v1.4** — the definition that produced this row (`CanonicalMetrics.Version`, `01` §10.9.13): stamped on every insert and every repair by the store, so "which formula produced this number?" is answerable after the fact and a formula change is never mistaken for a fact restatement. `NULL` = written before versioning existed (never guessed, never back-filled by a migration); a recompute of that day rewrites the row — values unchanged — and stamps the current version. CHECK `formula_version IS NULL OR formula_version >= 1`. |
| computed_at | timestamptz | |

Append-only by date; the Metrics Engine writes one row per instrument × metric per trading day for all 18 concepts / 26 window-suffixed metric codes (≈650k rows/yr — §7 volumes). **Rejected alternative:** separate current-table (upserted) + history-table — two code paths for one concept; volume does not justify it.

**`coverage_metadata`** (FR-MDF-011, FR-FDF-004): `id`, `scope` (`instrument`/`fund`/`universe`), `instrument_id`/`fund_id` NULL, `data_type`, `available_from`, `available_to`, `notes`. Written by adapters during backfill/refresh; read by admin coverage report and the honest no-data states.

**`ingest_runs`** — scheduler run ledger (UC-MDF-001 alternates; operational): `id`, `job_code`, `started_at`, `finished_at`, `status` (`succeeded/failed/partial`), `stats_json` (counts), `error` text. Also backs `data_freshness` staleness logic (FR-MDF-016) via view `v_data_freshness` (last success per job/data type).

**`quarantined_facts`** — validation-failure store (v1.1; UC-MDF-001 alternate b, FR-MDF-012, BR-MDF-007): `id`, `job_code`, `source_ref`, `payload_json` (the rejected fact, retained for inspection and re-ingestion after a fix), `reason_code` (validation-failure class), `quarantined_at`, `status` (`open`/`dismissed`), `resolution_note` text NULL. Written by C3a validation — a fact failing validation is **never** written to a fact table. Reviewed via admin endpoints (`03` §8); dismissal records the accepted gap with a note (BR-MDF-007: gaps are recorded, never silently ignored); re-ingestion is re-triggering the job after a fix. Volume is exception-driven (expected small) — sustained growth is itself an anomaly signal (C3d alert threshold).

### 3.2 Market Overview & Macro

**`macro_series`** — *Macro indicator series* (MOV §7): `code` PK (`TUIK_CPI`, `INDEP_CPI`, `CBRT_REPO`, `USD_TRY`, `EUR_TRY`, `GOLD`), `name_tr`, `name_en`, `unit`, `source_name`, `cadence` (`daily`/`monthly`/`per_release`). Six rows in V1 (BR-MOV-008: exactly one canonical value per series per date). `cadence` also selects the serving freshness tolerance — per-series failure clock + publication-aware monthly window — pinned in §5.6.

**`macro_values`** — *Macro indicator value* (FR-MOV-014; revisions per UC-MOV-002b no-overwrite): `series_code` FK, `value_date`, `value` numeric, `source_ref`, `recorded_at`. UNIQUE (series_code, value_date, recorded_at); canonical value = latest `recorded_at` per (series, value_date). Revisions append; nothing overwritten.

**`market_snapshots`** — *Market overview snapshot* (MOV §7; BR-MOV-009 computed from MDF facts, one per trading day)
| column | type | notes |
|---|---|---|
| snapshot_date | date PK | trading day |
| xu100_level, xu100_change_pct, xu30_level, xu30_change_pct | numeric (nullable) | FR-MOV-001. **Nullable by contract** (`01` §10.9.14, v1.5.18): a resolved day whose `index_levels` had not landed at compute time stores NULL — never a fabricated `0.0000` — and the overview serves `indices: null`. Existing rows keep their values; no migration is needed (the columns have always been nullable). |
| breadth_advancing, breadth_declining, breadth_unchanged | int | FR-MOV-003 |
| volume_total | numeric | FR-MOV-005 |
| market_pe | numeric | cap-weighted aggregate, loss-makers excluded (D-05) |
| market_pe_excluded_count | int | disclosure (BR-MOV-010) |
| market_div_yield | numeric | Σ div_TTM / Σ mcap |
| gainers_json / losers_json | jsonb | top-10 arrays: symbol, change_pct, volume (BR-MOV-011 eligibility threshold applied at computation; FR-MOV-021) |
| sector_perf_json | jsonb | sector → daily change (+ precomputed 1W/1M/YTD for FR-MOV-017 Could) |
| as_of_trading_date, computed_at | | honesty stamps |

One row per trading day, idempotent by `snapshot_date`. **Recompute semantics (`01` §10.9.13, v1.5.18):** the resolved day is recomputed from the current facts on every `market-snapshot` run and written input-aware — identical values leave the row untouched (`unchanged`), changed values **overwrite it in place** (counted as a repair), so a row computed before its inputs landed is repaired without SQL surgery. No other day's row is ever addressed, so historical rows stay immutable.

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

**`sector_metric_medians`** — *Sector metric median* (RES §7; SD-002; confirmed OQ-UX-002): `sector_id`+`metric_code`+`as_of_date` PK, `median_value`, `peer_count` (UXR-RES-024), `excluded_count` (P/E loss-maker exclusion disclosure), `is_adjusted`, **`formula_version` int NULL (v1.4 — the producing definition, same discipline as `derived_metrics.formula_version`)**, `computed_at`. Five valuation-family metrics only (P/E, P/B, EV/EBITDA, EV/FCF, FCF yield), current basis.

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
| 5.6 | **Macro value revisions** (UC-MOV-002b: "revision stored as new dated record" vs. "one canonical value per series per date" BR-MOV-008). | `macro_values` allows multiple `recorded_at` rows per (series, date); the canonical value is the latest. Storage keeps history; serving keeps uniqueness. Same pattern as statement restatements (5.7). **v1.5.14 amendment — freshness tolerances (`macro_series.cadence`, `01` §10.9.12 freshness note, TKT-foundation-038):** staleness is **per series**, never per job alone. (1) *Failure clock:* a series is failure-stale only when its **owning job's** latest `failed` run is newer than **that series' own** latest `recorded_at` **and** newer than the job's latest `succeeded` run — the series' stored data is the evidence its ingest happened, so an honest `partial` run that wrote the series refreshes its clock, while an outright failure that wrote nothing leaves it old and staleness stays honest. `INDEP_CPI` (admin-entered, `per_release`, no owning job) is never failure-stale. (2) *Age clock:* `daily` keeps its 1-day tolerance measured from the latest `recorded_at` (the rule that works); `monthly` is **publication-aware and measured from the published value date** (`asOf`), stale when `today > asOf + 65 days` — 65 covers TÜİK's M+1-early publication and the CBRT's ≤2-month meeting cycle, ships as the single documented constant `MacroFreshnessService.MonthlyPublicationWindowDays` (changing the window is that constant, never a semantics change), and is deliberately immune to the monthly rows being re-recorded on every run; `per_release` has no age window. (3) A series with no stored value is `unavailable`, never `stale`, and the macro strip's envelope aggregates **available** series only. |
| 5.7 | **Restated statements** (BR-MDF-011: display latest restated, retain as-reported). | All versions stored (`version` int sequence, v1.3); serving selects the **highest version** per (instrument, period, type) and flags `is_rested` (version > 0) through to metrics (`derived_metrics.is_rested`) and UI marks. A second restatement is version 2, not a constraint violation (issue 1). |
| 5.9 | **Statement currency normalization vs. MOV's ownership of FX** (v1.3): MDF statements are normalized to TRY (§3.1), but the FX series (`USD_TRY`, `EUR_TRY`) are MOV-owned macro facts. | The statement ETL reads the latest `macro_values` row at or before the period end (a read-only MDF→MOV edge, the only such edge — recorded here as a deliberate coupling). MOV ingestion for FX runs before the statement job in the daily chain; a statement whose period-end FX is unavailable is quarantined (`MISSING_FX_RATE`), never converted at a guessed rate. |
| 5.8 | **DCF baselines "build-time"** (VAL: builder-defined, versioned, per build cycle) vs. canonical-fact derivation. | Baselines are *derived rows*, not hand-edited content: C3b generates them from canonical facts (FCF, net debt, cash, share count) + builder default rules for growth/horizon/rates (config). Regeneration is an admin/CLI action; every regeneration bumps `version`. Rationale: hand-maintained numbers would violate the no-black-box posture (NFR-VAL-004) and rot silently. |

## 6. Canonical quantitative definitions (owned by `/src/Core`, C3b)

### 6.1 Canonical chart of accounts (`fin_line_items.item_code`)

`REV` revenue · `COGS` cost of sales · `GROSS_PROFIT` (= REV−COGS when not reported) · `SGA` · `EBIT` operating income · `DEPR_AMORT` D&A · `EBITDA` (= EBIT + D&A when not reported) · `FIN_EXP` net interest/finance expense · `TAX_EXP` income tax expense · `PRETAX_INC` · `NI` net income · `MINORITY_INT` · **`OCF` operating cash flow (net cash from operating activities — *İşletme Faaliyetlerinden Nakit Akışları*, reported directly on every KAP cash-flow statement; v1.3, issue 4)** · `CAPEX` (capital expenditure; stored as the source reports it — KAP reports outflows as negative) · `ΔWC` change in working capital (from CF statement components; retained for reference — no longer used in canonical FCF) · `CASH` cash & equivalents · `ST_DEBT`, `LT_DEBT` · `TOTAL_EQUITY` (attributable) · `CUR_ASSETS`, `CUR_LIAB` · `SHARES_DILUTED` · `SHARES_OUT`. `OTHER_*` codes pass through source items not yet mapped (recorded, ignored by metrics — honest gap, not silent drop). **The price-lane derived share count is not a canonical line item** (TKT-foundation-086, v1.5.60): `price_derived_share_counts` (§3.1) carries a *derived* fact with its own operands and provenance and is never written into `fin_line_items` — a derived figure in the statement's own table would blur reported and computed data, which is the distinction the whole store is built to keep. The DCF's `share_count` fact reads `SHARES_DILUTED` first and this store second (precedence: a reported count wins).

**The statements-view extension — the chart grows additively by 41 codes, and the KAP label-rule key gains a row-scope dimension (TKT-foundation-093, landed 2026-10-28; `01` v1.5.69 §10.9.19 carries the mechanism, test plan §33 / TC-MDF-122 its acceptance; this paragraph subsumes the planner's dated 2026-10-28 design note, which reserved the names — they are pinned here. Count corrected 2026-10-28, PR #155 review round 1 DR-1, user-arbitrated docs amendment: the landed map adds **42 entries targeting 41 new codes** — the 42nd wires pre-existing `MINORITY_INT` to its KAP spelling — so the registry is 23 → **64**, matching this section's own 22 + 8 + 11 bullets; the originally stated "42 additive / 65" double-counted the wiring entry.)** The statements-view v0 row set (the user's spec, recorded verbatim on TKT-foundation-095) needs the taxonomy families §6.1's original list stopped short of. Every added code is **additive — no existing code is renamed, redefined or removed** — and every one joins `StatementMapper.CanonicalCodes`, the registry `StatementMapper.Map` checks (a code missing there is rewritten to `OTHER_{code}` on write and the deliverable silently vanishes; the registry now holds **64** codes). Each code is written only where a **committed capture** evidences the source spelling, and the decoder's map entry names that capture (`KapPublicLabelRule.Provenance`).

- **Balance sheet (22).** Unqualified (one meaning in every block): `INVENTORY` (Stoklar) · `PPE_NET` (Maddi Duran Varlıklar) · `ROU_ASSET` (Kullanım Hakkı Varlıkları, TFRS 16) · `INTANGIBLES_NET` (Maddi Olmayan Duran Varlıklar) · `GOODWILL` (Şerefiye) · `PAID_IN_CAPITAL` (Ödenmiş Sermaye — the **statement line**, distinct from the `share_counts`/price-derived count, which never enters this store) · `RETAINED_EARNINGS` (Geçmiş Yıllar Karları veya Zararları) · `EQUITY_PARENT` (Ana Ortaklığa Ait Özkaynaklar) · `TOTAL_ASSETS` (TOPLAM VARLIKLAR) · `TOTAL_LIAB` (TOPLAM YÜKÜMLÜLÜKLER) · `CUR_ASSETS_OTHER`/`CUR_LIAB_OTHER`/`NONCUR_ASSETS_OTHER`/`NONCUR_LIAB_OTHER` (the `Diğer …` families) · plus `MINORITY_INT`, already in this chart, now **wired** to its KAP spelling `Kontrol Gücü Olmayan Paylar` (no new code, no redefinition). Block-qualified (a caption the sheet repeats across its current/non-current blocks, one code per block): `ST_TRADE_RECV`/`LT_TRADE_RECV` (Ticari Alacaklar) · `ST_TRADE_PAY`/`LT_TRADE_PAY` (Ticari Borçlar) · `ST_LEASE_LIAB`/`LT_LEASE_LIAB` (Kiralama İşlemlerinden Borçlar) · `ST_DEFERRED_REV`/`LT_DEFERRED_REV` (Ertelenmiş Gelirler (Müşteri Sözleşmelerinden Doğan Yükümlülüklerin Dışında Kalanlar)).
  - **The recorded choice — non-current trade receivables/payables get their own codes rather than joining the `NONCUR_*_OTHER` families.** A caption two blocks carry with different values is two lines *of the same kind*: a non-current trade receivable is a trade receivable, not an "other non-current asset", and folding it into the other-family bucket would produce a figure the statements view could not name. The same principle separates lease liabilities and deferred revenue per block. `OTHER_*` remains what it always was — the honest passthrough for a family the chart does not (yet) name, and the only destination for a row whose block cannot be determined (below).
- **Income statement (8).** `RND` (Araştırma ve Geliştirme Giderleri) · `OP_INCOME_OTHER`/`OP_EXPENSE_OTHER` (Esas Faaliyetlerden Diğer Gelirler/Giderler) · `INVEST_INCOME`/`INVEST_EXPENSE` (Yatırım Faaliyetlerinden Gelirler/Giderler) · `FIN_INCOME` (Finansman Gelirleri — the income side beside the existing `FIN_EXP`) · `NET_MONETARY_GL` (Net Parasal Pozisyon Kazançları (Kayıpları), TMS 29) · `NI_PARENT` (Ana Ortaklık Payları). **`NI` keeps its total meaning**: the parent's share is a line *beside* the total, never a replacement, and the profit-or-loss/other-comprehensive-income repeats of that caption are separated by the row-scope dimension — only the profit-or-loss occurrence is `NI_PARENT`.
- **Cash flow (11).** `IMPAIRMENT` · `PROVISIONS` · `MONETARY_ADJ` · `ASSET_SALE_PROCEEDS` · `ACQUISITION_CASH` · `EQUITY_ISSUE_CASH` · `BUYBACK` · `DIVIDENDS_PAID` · `INTEREST_PAID` · `DEBT_ISSUED` · `DEBT_REPAID`. `OCF`, `CAPEX`, `ΔWC`, `DEPR_AMORT`, `CASH` and the CF `NI` line are unchanged from the original chart.
- **The row-scope dimension (`01` v1.5.69 §10.9.19), because it is what makes the repeated captions expressible.** The balance sheet organizes itself in current/non-current blocks and repeats captions across them; a flat `(statement, label)` key would overwrite a value or raise `DUPLICATE_LABEL_CONFLICT` and quarantine the statement row. The block is therefore resolved **before** label mapping from the capture's own anchors (`DÖNEN VARLIKLAR`/`DURAN VARLIKLAR`/`KISA VADELİ YÜKÜMLÜLÜKLER`/`UZUN VADELİ YÜKÜMLÜLÜKLER`), exactly as the CF section already was — and the dimension materializes in **stored data as distinct codes**, so a consumer keeps a flat `code → figure` map with no block logic of its own. A row whose block cannot be determined (before any anchor, or in the equity block, which has no current/non-current split) matches **no** block-qualified entry: it stays a section-scoped `OTHER_*` passthrough and is a named, counted `BLOCK_UNDETERMINED` finding — never a guessed block.
- **The fail-safe hardening of the same dimension (TKT-foundation-098, `01` v1.5.74 §10.9.19; test plan §36 / TC-MDF-125 its acceptance), because it changes *which rows* reach the codes above.** The dimension's fail-safe now guards a **stale** scope as well as an absent one, and the closed reason set gains one name: **`PART_UNDETERMINED`** — the income-statement analogue of `BLOCK_UNDETERMINED` — raised when a valued row's caption can only be placed through a part-qualified entry while the row's profit-or-loss / other-comprehensive-income part is undetermined (the table's heading attests neither part, or the statement already holds that caption's part-qualified code with a different value **while a row between the two occurrences attests the other part** — the capture's own evidence that the part moved without a readable anchor). A caption repeated with an unequal value **inside its own part**, with no such attesting row between the occurrences, is **not** this case: it still `DUPLICATE_LABEL_CONFLICT`s and quarantines the statement row (TC-MDF-125 (h)). The rules pinned: **every balance-sheet total row (a label beginning `TOPLAM`) closes the block state**, so the side that follows must re-declare its block and a liabilities row can never inherit the assets block's scope (`ARA TOPLAM` is not `TOPLAM`-prefixed, so those subtotals sit inside a block and are not boundaries; the prefix test is side- and depth-blind — a future intra-block `TOPLAM …` subtotal would close the block early and demote the rest of that block's scoped captions to `OTHER_*` + `BLOCK_UNDETERMINED`, named, never a wrong code, and only block-closing totals occur on the five committed bodies); **the income statement's starting part is read from the heading's content** (naming the profit-or-loss part → `PL`, carrying only the comprehensive-income part → `OCI`, neither → undetermined), never from one exact string. In both cases the row keeps its `OTHER_*` passthrough and the miss is named and counted — never a wrong code, and never a whole-row quarantine where the scope dimension already disambiguates. **No chart code is added, renamed or redefined by this hardening** (the registry stays 64) and the DR-1/DR-2 fences are untouched.
- **Fences unchanged (DR-1/DR-2 stand).** `ST_DEBT`/`LT_DEBT` remain exactly the two borrowings lines — `ST_LEASE_LIAB`/`LT_LEASE_LIAB` are the lease family and the current portion of long-term borrowings stays `OTHER_BS_*`, so the DCF's `debt` is not widened by this extension; `TOTAL_EQUITY` remains the NCI-inclusive total as stored, with `EQUITY_PARENT` and `MINORITY_INT` decoded *beside* it, never subtracted from it. No share-count line enters this store, and no cumulative/period semantics change.
- **Evidence base.** Five committed bounded-trim captures carry every spelling in the extension: `kap-public-report-1643141` (ASELS 2026-06), `-1554106` (TÜPRAŞ 2025 Yıllık), `-1561039` (ASELSAN 2025 Yıllık), `-1648240` (MİGROS 2026-06) and, new with this extension, **`-1565996` (THYAO 2025 Yıllık — the industrial FR carrying the full IS/BS/CF taxonomy row set, `1.000.000 TL`)**, committed as a bounded trim from the already-fetched body. The corrected premise of the same round: the lease-liability spelling is `Kiralama İşlemlerinden Borçlar` (evidenced in every committed body), not `Finansal Kiralama`, and with THYAO committed **every v0 family is evidenced** (no honest-absent family remains on these bodies; the orchestrator's live criterion re-checks that on the production walk).

### 6.2 The canonical metric catalog — 18 screener concepts, 43 stored metric codes (FR-SCR-001 / FR-MDF-013)

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

**The key-value &amp; moat panel's codes (TKT-foundation-096, landed 2026-10-29; test plan §35 / TC-MDF-124 its acceptance).** The stock page's KEY VALUE METRICS &amp; MOAT INDICATORS panel and its CAPITAL &amp; DILUTION TRACKER (the user's v0 spec, recorded verbatim on TKT-foundation-095; rendering is TKT-foundation-097's) need seventeen further codes. **BR-RES-004/UXR-G-011 pin where they are computed:** the panel's canonical values must be the same numbers the screener serves, and `derived_metrics` is the formula-versioned single source (§1 principle 3) — so they go **through the Metrics Engine like the existing 26**, never a second implementation at the serving edge (AD-06, §5.1). The engine therefore writes **43** codes per instrument (25 non-growth + 18 windowed), and `CanonicalMetrics.Version` moves **1 → 2** — 070's discipline: every new and re-stamped row carries the bumped version, historical rows keep theirs until a recompute of their own day re-stamps them.

| code | family | definition (current period) | not-meaningful rule |
|---|---|---|---|
| `roa` | quality | NI / TOTAL_ASSETS | TOTAL_ASSETS ≤ 0 or absent → NULL |
| `croic` | quality | OCF / (ST+LT_DEBT + TOTAL_EQUITY − CASH) — the same invested capital as `roic` | IC ≤ 0 → NULL |
| `ev_ebit` | valuation | (mcap + ST+LT_DEBT − CASH) / EBIT | EBIT ≤ 0 → NULL |
| `p_fcf` | valuation | mcap / FCF | FCF ≤ 0 → NULL |
| `p_tbv` | valuation | mcap / (TOTAL_EQUITY − (GOODWILL + INTANGIBLES_NET)) | tangible book ≤ 0 or TOTAL_EQUITY absent → NULL; an absent goodwill/intangible component contributes 0 (KAP itemizes them only where they exist) |
| `fcf_ni` | quality | FCF / NI | NI ≤ 0 or FCF absent → NULL |
| `fcf_ni_3y` | quality | the arithmetic mean of the annual FCF/NI ratios over the **three most recent FYs** — **all-or-nothing**: any year's NI ≤ 0/absent or FCF absent → NULL (never a two-year mean wearing a three-year label); a negative annual FCF is a legitimate ratio | <3 usable FYs → NULL |
| `cfo_ebit` | quality | OCF / EBIT | EBIT ≤ 0 → NULL |
| `monetary_gain_pretax` | quality | NET_MONETARY_GL / PRETAX_INC — the TMS 29 distortion alert's input (a large share means the reported profit is largely monetary gain) | PRETAX_INC ≤ 0 → NULL |
| `cfo_interest` | financial_health | OCF / \|INTEREST_PAID\| — the CF statement's own interest line, whose KAP sign is an outflow, so the denominator is the absolute figure | INTEREST_PAID absent or 0 → NULL |
| `net_debt_fcf` | financial_health | (ST+LT_DEBT − CASH) / FCF | **FCF ≤ 0 → NULL** (the existing not-meaningful rule family) |
| `ebit_cagr_3y/5y` | growth | CAGR of FY EBIT | <2 FYs → NULL |
| `bvps_cagr_3y/5y` | growth | CAGR of FY BVPS (TOTAL_EQUITY / SHARES_DILUTED) | <2 FYs, or a share count absent for any year in the window → NULL |
| `fcf_ps_cagr_3y/5y` | growth | CAGR of FY FCF per share | <2 FYs or sign change → NULL (the `fcf_cagr` rule), or a share count absent → NULL |

**Five pinned exclusions (the user's, verbatim on TKT-foundation-095 — cited by these numbers in the code comments, so the order is the user's: #4 per-share, #5 10Y), and what each rules out:** (1) **no USD or real (CPI-deflated) basis** — every value is nominal TRY as stored; (2) **no 3M+TTM improvisation** — no code mixes a quarter with a trailing-twelve-month span; (3) **no widened gross debt** — `croic`/`net_debt_fcf`/`ev_ebit` consume `ST_DEBT`+`LT_DEBT` exactly as stored (the DR-1/DR-2 fences, §6.1), never the lease family or any re-aggregation; (4) **no per-share fabrication** — `bvps_cagr_*`/`fcf_ps_cagr_*` are gated on a stored share count per year and are honestly NULL where none exists, and the capital tracker's Y/Y share-count change is served as N/M for the same reason; (5) **no 10Y window for the panel's families** — `ebit_cagr_*`, `bvps_cagr_*` and `fcf_ps_cagr_*` exist only in the 3Y/5Y pair, because a 10Y window is impossible on the stored ~3.5-year depth and the panel asks for the pair.

**The panel's per-share growth families follow the EPS/DPS basis doctrine.** `bvps_cagr_*` and `fcf_ps_cagr_*` are built from a per-share series and are therefore multiplied by the same cumulative corporate-action factor **Π** and carry `is_adjusted = true` exactly as `eps_cagr_*`/`div_cagr_*` do, with the same window-honesty discard: an adjusted per-share series reports a window it does not fully cover as the honest no-value, never a shortened span presented as the nominal one. The basis marker is computed **per family** — an instrument whose only adjusted series belongs to one family never re-stamps the other families' rows, so the v1 EPS/DPS rows stay byte-identical.

`FCF = OCF − |CAPEX|` (v1.3, issue 4 — canonical). Operating cash flow is taken as **reported** (the KAP cash-flow statement's net operating-activities figure, which already reflects non-cash TMS 29 items such as monetary gain/loss, FX revaluations, and provisions that the old `NI + D&A − ΔWC` reconstruction missed); `|CAPEX|` guards the KAP sign convention where outflows are negative (so a reported −150 deductible is *subtracted*, not added). FCF is NULL when `OCF` or `CAPEX` is missing. *Rejected (retained for the record): the indirect `NI + DEPR_AMORT − ΔWC − CAPEX` reconstruction — sign-fragile and incomplete under Turkish IFRS/TMS 29.* `mcap = close_raw × SHARES_DILUTED` (raw single-unit TRY, §3.1). Growth criteria store the chosen window (3/5/10Y) in the criterion (FR-SCR-015). **Windowed CAGR storage (Q5 resolution, 2026-10-07): window-suffixed metric codes** — `rev_cagr_3y/5y/10y`, `eps_cagr_3y/5y/10y`, `fcf_cagr_3y/5y/10y`, `div_cagr_3y/5y/10y` — 14 non-growth + 12 windowed = 26 metric codes through v1; the panel adds 11 non-growth + 6 windowed, so the catalog is **43 metric codes** (`MetricCodes.All`) and the table's `is_screenable` flag — not the code's existence — decides what the screener offers. `derived_metrics.window_years` records the interval actually used when history is shorter than the nominal window (FR-MDF-014 honesty).

**Per-share CAGR basis — corporate-action adjustment (v1.3, issue 3):** the historical **EPS** and **DPS** series feeding `eps_cagr_*` and `div_cagr_*` are multiplied by the same cumulative corporate-action factor **Π** used for `close_adjusted` (§3.1 adjustment system), taking all splits/bonus issues/rights issues **after** the fact's basis date (a fiscal period end for EPS; the dividend's ex-date for DPS). Without this, a 400% bonus issue (*bedelsiz*) makes a historical year's EPS/DPS appear several times today's and produces a deeply negative CAGR. These growth metrics carry `is_adjusted = true` (the existing basis marker, FR-MDF-018).

**Regime-break flag — TMS 29 (v1.3, issue 5):** the TMS 29 inflation-accounting transition took effect for fiscal periods ending **2023-12-31**; nominal TRY before and inflation-adjusted TRY after are not comparable. Any CAGR whose window spans that boundary (min FY period-end < 2023-12-31 ≤ max FY period-end) is written with `derived_metrics.regime_break = TRUE` and its serving surfaces carry a regime-break warning — the value stays nominal (CPI deflation is deferred; see the optional follow-up). CAGRs entirely after the boundary are un-flagged; those entirely before are un-flagged (no TMS 29 steps inside).

**Accounting-profile gating (v1.3, issue 6 — extended by TKT-foundation-096 to twenty-six codes):** for `accounting_profile ∈ {bank, insurance}` the following metrics are **not-meaningful by profile** and evaluate to NULL *even if the underlying lines happen to exist*: `ev_ebitda`, `ev_fcf`, `fcf_yield`, `roic`, `gross_margin`, `operating_margin`, `fcf_cagr_*`, `net_debt_ebitda`, `interest_coverage`, `current_ratio` — plus the panel's industrial-shaped additions `croic`, `ev_ebit`, `p_fcf`, `p_tbv`, `fcf_ni`, `fcf_ni_3y`, `cfo_ebit`, `monetary_gain_pretax`, `cfo_interest`, `net_debt_fcf`, `ebit_cagr_3y/5y` and `fcf_ps_cagr_3y/5y`. Banks and insurers report under BDDK/insurance rules and have no `REV`/`COGS`/`EBITDA`/`FCF`/`CUR_ASSETS` semantics; a stray mapped line must never produce a plausible-looking number. `pe`, `pb`, `roe`, `roa`, `eps_cagr_*`, `bvps_cagr_*`, `div_yield`, `div_cagr_*`, `payout_ratio` compute normally (their inputs — NI, total assets, equity, shares, dividends — are meaningful for financials). `industrial` and `reit` profiles keep the natural not-meaningful rules in the tables above. Bank/insurance statements with absent industrial lines are **never quarantined** on that ground (absence is expected, not a schema failure). The gate's definition is public (`CanonicalMetrics.IsProfileGated` / `IsFinancialProfile`) so the sector-median lane excludes financial peers from a gated code on **one** definition (§6.3).

**The 5Y historical company median — a serving-side statistic over stored history (TKT-foundation-096).** The stock page shows each panel metric against the company's own history; that median is computed by the serving endpoint from the instrument's stored `derived_metrics` rows inside the 5Y window — the median of the **non-null** values, on the same `MetricMath.Median` definition every other median surface uses. It is **withheld** (`null` + `notMeaningful`) below a pinned floor of **12 non-null observations**, and the payload always discloses the nominal window (`windowYears` = 5), the observation count and the **actual depth** the stored history reaches (`depthYears`). A two-observation "median" dressed as a five-year statistic is exactly the fabrication the spec forbids: nothing is interpolated, extrapolated, or window-lengthened to look deeper. The ~3.5-year depth of the store today means the honest answer is usually the N/M — which is what the disclosure is for.

### 6.3 Market & sector aggregates

- `market_pe = Σ mcap_i / Σ NI_TTM_i` over current-universe constituents with NI_TTM > 0 (D-05); `market_pe_excluded_count` = loss-makers excluded (BR-MOV-010 disclosure).
- `market_div_yield = Σ DIV_TTM_i / Σ mcap_i` over all constituents.
- Sector medians: median of each **covered** metric across same-sector current-universe peers, current basis; P/E median excludes loss-makers with `excluded_count` (confirmed OQ-UX-002); `peer_count` displayed (UXR-RES-024). **The covered set (TKT-foundation-096, `02` §6.2 v2; test plan §35 / TC-MDF-124):** the five valuation-family codes the vs.-sector strip has always carried (`pe`, `pb`, `ev_ebitda`, `ev_fcf`, `fcf_yield`) **plus the key-value &amp; moat panel's codes** the stock page renders against its sector — `roic`, `croic`, `roe`, `roa`, `fcf_ni`, `fcf_ni_3y`, `cfo_ebit`, `monetary_gain_pretax`, `ev_ebit`, `p_fcf`, `p_tbv`, `div_yield`, `net_debt_ebitda`, `net_debt_fcf`, `interest_coverage`, `cfo_interest`, `current_ratio`, `rev_cagr_3y/5y`, `ebit_cagr_3y/5y`, `eps_cagr_3y/5y`, `fcf_ps_cagr_3y/5y`, `bvps_cagr_3y/5y` — **32 codes** in all (`SectorMedianEngine`'s covered set; a code the panel renders and this lane did not cover would be a silently missing median). `peer_count` is the count of same-day **non-null** values; `excluded_count` counts the assessed same-day rows that contributed none. **For a profile-gated code the financial-sector peers are removed from the assessed set *before* the median and counted in `excluded_count`** (§6.2's gate, one public definition): a bank's `ev_ebit` row is NULL by profile, so including it as an "assessed but not-meaningful" peer would understate nothing but disclose nothing either — excluding it and saying so is the honest form (UXR-RES-024/025).
- Movers: top-10 by daily % change among stocks ≥ volume-eligibility threshold (BR-MOV-011); threshold is a config value set from the observed volume distribution during V0 (recorded in `metric_catalog`-adjacent config), not hard-coded.

### 6.4 DCF model (single formula, server-side — AD-08)

```
PV_explicit = Σ_{t=1..N}  FCF₀·(1+g)^t / (1+r)^t
TV           = FCF₀·(1+g)^N·(1+g_t) / (r − g_t)          [requires r > g_t]
EquityValue  = PV_explicit + TV/(1+r)^N + Cash − Debt
FairValuePS  = EquityValue / SharesDiluted
MOS          = (FairValuePS − Price) / FairValuePS        (plain-language verdict, client-rendered)
```
Parameters (confirmed set, OQ-UX-001; builder clarification 2026-10-07): `base_fcf, growth_rate, horizon_years (1..10), terminal_growth, discount_rate, debt, cash, share_count` — all user-editable (BR-VAL-002); **`debt` = total debt (ST+LT)**, not net — `cash` and `debt` are distinct parameters, both affect the result (NFR-VAL-004 no-black-box; F-VAL-1 ruling). Sensitivity grid: `discount_rate × terminal_growth` (default 5×5 centered on user values). Not computable → `DCF_NOT_COMPUTABLE` with the violated constraint named (UXR-VAL-009 analog for math, not missing data). **Financial-sector exclusion (v1.3, issue 6):** for `accounting_profile ∈ {bank, insurance}` a free-cash-flow-to-firm DCF does not apply — baseline generation records the instrument with a `sector_not_applicable` state (no FCF-derived baseline), and the calculator returns `DCF_NOT_COMPUTABLE` with reason `financial_sector` and an honest sector explanation. This is a *not-applicable* verdict, distinct from the missing-inputs state.

## 7. Volume estimates & retention

| Table | Rows (steady state, 10Y depth) | Notes |
|---|---|---|
| daily_prices | ~250k (100 × ~2,500 days) | + index_levels ~5k |
| derived_metrics | ~6.5M (100 × 26 × ~2,500 days) | largest equity table; single append table (see §3.1) — 26 = 18 concepts with window-suffixed CAGR codes (Q5 correction, 2026-10-07) |
| fin statements/line items | ~12k / ~720k | 100 × 40 periods × 3 × ~60 lines |
| kap_disclosures | 20k–100k | metadata only; documents on disk |
| fund_navs | ~2–4M | equity+equity-heavy funds (BR-FDF-006 bound keeps this finite) |
| fund_holdings | ~2–5M | periodic snapshots × ~30 lines |
| everything else | < 150k combined | macro, snapshots, medians, users, content, quarantined facts (exception-driven) |

Total DB estimate **2–6 GB** → fits Backblaze B2 free tier (10 GB) for the 30-day backup window (§01 §10.8). All facts retained for the life of the platform (NFR-MDF-002/NFR-FDF-002) — no TTL anywhere. Partitioning (`derived_metrics`, `fund_navs` by year) is a documented future optimization, deliberately not built now — at these sizes plain B-tree indexes on the PKs suffice; premature partitioning would complicate EF mappings for no measured benefit.

## 8. Migrations & change policy

- EF Core migrations, **forward-only** (no down-migrations in prod); applied by the deploy job (`docker compose run --rm api migrate`) before `up -d` (§01 §6.2).
- Every migration runs in CI against a real Postgres (Testcontainers) before it can merge — migration correctness is test-gated, not hope-gated.
- Fact-table schema changes are additive (new columns/tables); existing fact rows are never rewritten (§1.1). Derived tables (`derived_metrics`, snapshots, medians) may be recomputed by re-running the engine after definition changes — definitions are versioned in `/src/Core` and metric recompute is idempotent by (key, as_of_date). **v1.3 model changes:** `financial_statements.version` type change (`text` → `int`, values migrated `as_reported`→0 / `restated`→1) plus new `source_unit_scale`/`source_currency`; `instruments.accounting_profile`; `derived_metrics.regime_break`. The `version` type change is a one-time forward migration (values remapped in the migration; no fact row invented or dropped). **v1.4 (2026-10-24) — definition-version provenance (`01` §10.9.13, TKT-foundation-070):** `derived_metrics.formula_version` and `sector_metric_medians.formula_version` (`int NULL`, CHECK `>= 1`), both **additive and nullable, with an honest empty backfill** — the migration writes no row. Every row existing at deploy time was produced by the then-current definition (in production, exactly the definition this release ships, `CanonicalMetrics.Version` = 1), but no per-row record of which definition wrote which value exists, so a back-filled stamp would be a fabricated provenance claim; `NULL` means precisely "produced before versioning existed". The gap closes from evidence: a recompute of a day rewrites those rows — values unchanged — and stamps the current version, and the recompute's own rule treats a stamp that differs from the current version as a rewrite even when the value is identical. No formula, golden or serving shape changes.
**v1.5 — the public-site company identity map (TKT-foundation-080, 01 v1.5.54 §10.9.19):** a new table `kap_public_company_map` (§3.1), **additive and forward-only** — nothing existing is altered and no row is back-filled by the migration (a mapping is only ever written from a resolved identity answer, never seeded). Its two shape CHECKs (`kap_public_oid` 32 hex, `vyk_company_id` decimal digits) plus the paired-provenance CHECK are part of the migration, so the three-space isolation is a database invariant from the first deploy. Un-mapped instruments stay **without a row** — honest absence, never a placeholder.
- Seed data: `metric_catalog` (18 rows), `macro_series` (6 rows), `builder` role + builder account bootstrap — via a checked-in seed migration/script, exercised in CI and local compose.
