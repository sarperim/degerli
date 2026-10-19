# Fixture Universe — Shared Test Data Contract

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** v1.4 — approved (MDF Gate 2 + builder final review 2026-10-07); **v1.4 (2026-10-15)** applies the builder's post-board correctness feedback — statement unit-scale/currency declarations, `OCF` lines and the `FCF = OCF − |CAPEX|` re-baseline, EPSL action-overlapping EPS/DPS history, on-demand `BANKA`/`USDX`/second-restatement fixtures, the TMS 29 regime boundary, and the `MISSING_FX_RATE` reason. Applied by the delivery planner on the builder's explicit instruction; **test-planner confirmation pending** (this document is test-planner-owned — see the change record). **v1.5 (2026-10-17)** records the real-source catalog exception (§1 principle 5, §10b) and the ₺ volume reinterpretation (§2/§4) per architecture v1.5 §10.9 — goldens untouched; applied by the test-planner at the delivery planner's request, builder-directed.
**Purpose:** one synthetic, checked-in dataset shared by integration (L2) and e2e (L4) tests. **No real BIST symbols, names, or values.** Every golden value asserted in any domain test plan is derived here, by hand, from `02-data-model.md` §6 formulas — never from an implementation. Test code loads this dataset via the checked-in `fixtures/` module (strategy §12).

---

## 1. Principles

1. **Synthetic everywhere** — avoids source-licensing ambiguity in the public repo and makes every number intentional.
1b. **Real-source catalog exception (v1.5, architecture §10.9.7 — the recorded exception to principle 1):** a second, checked-in catalog of real-source bodies — charset-faithful raw bytes + declared charsets + honest captured-vs-constructed provenance labels — exists solely for the parsing-fidelity lane (MDF plan §8; MOV TC-MOV-028): real envelopes assert parse shape/fidelity against the **product** adapters, never derived numbers. The FU canonical envelope stays the L2 golden contract; **no golden value anywhere derives from a real capture**. Labeling rule: a hand-constructed body is never labeled "captured verbatim". Full contract in §10b.
2. **Every honest-data state has a trigger** — each not-meaningful rule, staleness state, coverage gap, and exclusion behavior in `02` §6.2 and the UX package is caused by exactly one named fixture, so failures are diagnosable at a glance.
3. **Round inputs, derived goldens** — inputs are simple round numbers; expected outputs are quoted to 6 significant digits with their derivation, so two implementors converge on the same value.
4. **Two time anchors:** L1/L2 use absolute dates with a fake `TimeProvider` (reference date **R = 2026-10-06, Tuesday**); L4 seeds are **now-anchored at container start** (`T` = seed day). Assertions use flags/offsets, never wall-clock dates.

## 2. Calendar, units, and config

- **Trading days:** Mon–Fri (fixtures carry no holidays; the production holiday calendar is config and is not exercised by automated tests — recorded interpretation).
- **Fixture trading days:** `T` (latest), `T−1`, `T−2`, … `T−9`; L2 absolute mapping with R = Tue 2026-10-06: `T` = 2026-10-06, `T−1` = 2026-10-05 (Mon), `T−2` = 2026-10-02 (Fri).
- **Money (v1.4):** the §5/§5.1/§5.2 tables are authored in **TRY millions** for readability; wire payloads declare their scale (`unitScale: "millions"`) and currency (`currency: "TRY"`), and the audience value is that **the ETL stores raw single-unit TRY** (`02` §3.1): a stored `fin_line_items.value` = tabulated figure × 10⁶, and `SHARES_DILUTED`/`SHARES_OUT` are stored as raw share counts (tabulated M × 10⁶). All §11 **ratio** goldens are scale-invariant and therefore unchanged; the tests that assert *stored absolute values* (e.g. TC-MDF-003) expect the raw value. Prices in TRY (single unit); volumes in shares. Percentages as decimals in payloads (0.05 = 5%).
- **Mover eligibility threshold (config under test):** volume ≥ **1,000,000** (BR-MOV-011; production value is set from the real volume distribution — the test value lives in test config only). **Unit (v1.5, FLG-09 — architecture §10.9.1):** volume is **TRY turnover (₺)** — the §4 values reinterpret as ₺ with unchanged numbers (**no golden re-derivation**; LAMDA at 500,000 stays excluded); the production threshold is recalibrated from the observed BIST 30 distribution during live activation (explicit TKT-foundation-018 criterion, not a TC).
- **Quarantine reason codes (defined by this plan; implementation follows):** `NEGATIVE_PRICE`, `MISSING_PROVENANCE`, `SCHEMA_MISMATCH`, `UNPARSEABLE_PAYLOAD`, `MISSING_CLASSIFICATION`, `CONFLICTING_VALUE` (a valid fact conflicting with an already-stored fact for the same key — builder decision Q1, 2026-10-06), **`MISSING_FX_RATE`** (v1.4 — a statement in a non-TRY reporting currency whose period-end FX rate is absent; the normalization precondition per `02` §3.1/§5.9).
- **Statement versioning (v1.4, `02` §3.1):** `financial_statements.version` is an integer sequence — `0` = as-reported, `1, 2, …` = restatements in publication order. Wire payloads still *declare* `"as_reported"`/`"restated"`; the store assigns the sequence (0, then 1, 2, …) per natural key. Serving = highest version.
- **TMS 29 regime boundary (v1.4):** 2023-12-31 is the inflation-accounting transition; a CAGR window with min FY `period_end_date` < 2023-12-31 ≤ max FY `period_end_date` is *spanning* → `regime_break` TRUE (TC-MDF-060).
- **Median convention:** standard statistical median; even peer count → mean of the two middle values.
- **CAGR window semantics (this plan's resolution of FR-MDF-014):** window W (3/5/10Y) = W compounding intervals between endpoint FYs; `window_years` = intervals actually used; **< 2 FYs → NULL**; sign change in the series → NULL for `fcf_cagr` only (per `02` §6.2).
- **Corporate-action adjustment factors (defined here; rights-issue formula resolved on the builder's final review 2026-10-07, Q3):** split 1-for-n → factor `1/n`; bonus issue of b new per 1 held → factor `1/(1+b)`; **rights issue of q new shares per 1 held at subscription price `P_S`, with cum price `P_C` = the last raw close on/before the action's effective date → factor `(P_C + q·P_S) / ((1+q)·P_C)`** (theoretical ex-rights price `TERP = (P_C + q·P_S)/(1+q)`; factor = `TERP/P_C`; reduces to the bonus factor `1/(1+q)` when `P_S = 0`, and to `1` when `P_S = P_C` — no adjustment); factors compose multiplicatively over time; `close_adjusted = close_raw × Π factors of all actions after the price date`.
- **TTM convention:** fixtures use FY statements as TTM; ALFA additionally carries FY2025 quarterly statements summing exactly to the FY figures, so both TTM paths (FY, ΣQ) yield identical goldens — a built-in consistency check.

## 3. Sectors and instruments (16)

| symbol | name | sector | listing_date | shares_diluted (M) | archetype |
|---|---|---|---|---|---|
| ALFA | Alfa Teknoloji A.Ş. | A — Teknoloji | 2010-01-04 | 100 | STD: full history, dividends, quarters, clean goldens |
| BETA | Beta Yazılım A.Ş. | A | 2012-05-10 | 80 | LOSS-A (NI ≤ 0) |
| GAMA | Gama Bilişim A.Ş. | A | 2015-09-01 | 60 | LOSS-B (NI ≤ 0) |
| DELTA | Delta Elektronik A.Ş. | A | 2011-03-15 | 100 | NEGEQ (equity < 0, profitable) |
| REST | Rest Telekom A.Ş. | A | 2008-11-20 | 50 | REST: FY2025 as_reported + restated versions |
| EPSL | Epsl Sanayi A.Ş. | B — Sanayi | 2009-04-01 | 400 | ACT: 1:5 split (2025-06-02) + 1:10 bonus (2026-03-02) |
| ZETA | Zeta İmalat A.Ş. | B | **2024-03-15** | 50 | IPO: exactly 2 FYs of statements |
| ETA |Eta Enerji A.Ş. | B | 2013-06-03 | 90 | MOVER-G (high-volume gainer) |
| THETA | Theta İnşaat A.Ş. | B | 2010-02-08 | 70 | MOVER-L (high-volume loser) |
| PART | Part Holding A.Ş. | B | 2005-08-15 | 120 | PART: no CF statement → FCF metrics NULL, DCF missing-inputs |
| NEWP | Yeni Gelişim A.Ş. | B | **2025-08-01** | 30 | 1 FY only → all CAGRs NULL (no-data state) |
| IOTA | Iota Gıda A.Ş. | C — Gıda | 2014-10-09 | 200 | NEGEBITDA (EBITDA ≤ 0) |
| KAPPA | Kappa İçecek A.Ş. | C | 2007-12-03 | 150 | NEGFCF (FCF < 0; FY sign change) |
| LAMDA | Lamda Tarım A.Ş. | C | 2016-04-12 | 110 | NODIV (never paid) + LOWVOL (movers-ineligible big gainer) |
| NU | Nu Perakende A.Ş. | C | 2003-01-06 | 90 | HIGHPAY (payout > 1) |
| UNSEC | Unsur Ticaret A.Ş. | **NULL** (unclassified) | 2011-07-19 | 70 | UNSEC: missing sector classification |

Sectors: `A` Teknoloji / Technology, `B` Sanayi / Industry, `C` Gıda / Food (bilingual labels per `02` §3.1).

**Index membership:** all 16 are current XU100 constituents (effective_from = listing dates, effective_to NULL). **XU30 subset:** ALFA, EPSL, KAPPA, REST, ETA.

### 3b. On-demand fixtures (v1.4 — *not* part of the seeded 16)

These exist so a new rule has a named trigger without perturbing any universe-wide golden (§11.3/§11.4). Each test **creates its own state** (§12) by ingesting the named payload; they never appear in the seeded universe or the snapshot/breadth/median aggregates.

| symbol | profile / shape | purpose |
|---|---|---|
| BANKA | `accounting_profile = bank`; bank-shaped statements (no `REV`/`COGS`/`EBIT`/`EBITDA`/`CUR_ASSETS`; carries `NI` 300, `TOTAL_EQUITY` 3000, `SHARES_DILUTED` 300, `CASH` 500, `ST_DEBT` 2000, `LT_DEBT` 1000) **plus a spurious `REV` 500 and `DEPR_AMORT` 50** to prove profile gating overrides stray lines; price 30.00 | TC-MDF-056 (ingest without quarantine), TC-MDF-057 (metric NULL matrix), TC-VAL-028 (DCF `financial_sector`) |
| SIGORTA | `accounting_profile = insurance`; same industrial-line absence; price 12.00 | TC-MDF-057, TC-VAL-028 (second financial profile) |
| USDX | `accounting_profile = industrial`, reporting currency **USD**; FY2025 lines in **USD M** (NI 10, `SHARES_DILUTED` 20, REV 100, EQUITY 60), declares `currency: "USD"`, `unitScale: "millions"`; price 420.00 TRY | TC-MDF-055 (currency normalization at period-end FX → stored NI = 10 × 10⁶ × 42.00 = 420,000,000 raw TRY) |
| REST2 | REST with a **second** restatement: v0 (NI 60), v1 (NI 75), **v2 (NI 80)** | TC-MDF-054 (highest version serves; all retained) |

BANKA/SIGORTA/USDX share counts are raw (×10⁶ at storage). BANKA-derived goldens (TC-MDF-057, raw-unit, price 30.00): `pe` = 30/(300/300) = **30.0**; `pb` = 30/(3000/300) = **3.0**; `roe` = 300/3000 = **0.10**; the ten profile-gated metrics **NULL** despite the spurious `REV`. SIGORTA-derived (price 12.00, NI 24, equity 240, shares 24): `pe` **12.0**, `pb` **1.2**, `roe` **0.10**, gated metrics NULL.

## 4. Daily prices (T−1 → T) and volumes

| symbol | close T−1 | close T | change % (4dp) | volume T (shares) | mcap T (TRY M) |
|---|---|---|---|---|---|
| ALFA | 19.80 | 20.00 | +1.0101 | 12,000,000 | 2,000 |
| BETA | 10.20 | 10.00 | −1.9608 | 5,000,000 | 800 |
| GAMA | 8.10 | 8.00 | −1.2346 | 4,000,000 | 480 |
| DELTA | 9.95 | 10.00 | +0.5025 | 6,000,000 | 1,000 |
| REST | 14.82 | 15.00 | +1.2151 | 3,000,000 | 750 |
| EPSL | 19.61 | 20.00 | +1.9888 | 8,000,000 | 8,000 |
| ZETA | 40.00 | 41.20 | +3.0000 | 7,000,000 | 2,060 |
| ETA | 20.00 | 22.00 | +10.0000 | 50,000,000 | 1,980 |
| THETA | 20.00 | 18.00 | −10.0000 | 40,000,000 | 1,260 |
| PART | 24.63 | 25.00 | +1.5022 | 5,000,000 | 3,000 |
| NEWP | 50.00 | 52.00 | +4.0000 | 9,000,000 | 1,560 |
| IOTA | 5.15 | 5.00 | −2.9126 | 10,000,000 | 1,000 |
| KAPPA | 40.00 | 41.00 | +2.5000 | 6,000,000 | 6,150 |
| LAMDA | 10.00 | 12.50 | +25.0000 | **500,000** | 1,375 |
| NU | 30.00 | 30.00 | 0.0000 | 7,000,000 | 2,700 |
| UNSEC | 9.04 | 9.00 | −0.4425 | 2,000,000 | 630 |

- All 16 have prices for T−9…T (open/high/low = close ± small deterministic offsets; close as tabulated). OHLC raw; `close_adjusted` = raw except where actions apply (EPSL).
- **EPSL adjusted series (FR-MDF-018 / BR-MDF-010):** split 1:5 effective 2025-06-02 (factor 0.2); bonus 1:10 effective 2026-03-02 (factor 10/11 = 0.909091). Absolute-date rows: 2025-05-30 raw 100.00 → adjusted 100×0.2×(10/11) = **18.1818**; 2026-02-27 raw 22.00 → adjusted 22×(10/11) = **20.0000**; 2026-06-02 raw 20.20 → adjusted **20.2000** (after both actions); T raw 20.00 → adjusted **20.0000**.
- **Index levels:** XU100 T−1 10000.00 → T **10200.00** (+2.00%); XU30 T−1 30000.00 → T **30600.00** (+2.00%).
- **Volume semantic (v1.5, FLG-09 — architecture §10.9.1):** the `volume T` column is **TRY turnover (₺)** — numeric values unchanged from v1.0..v1.4 (authored as "shares", reinterpreted, **not re-derived**); Yahoo-contingency rows carry NULL volume (different unit — deriving ₺ from shares × close rejected as computing).

## 5. Financial statements (FY2025 TTM facts, TRY M)

| symbol | REV | COGS | GP | EBIT | D&A | EBITDA | FIN_EXP | PRETAX | TAX | NI | CAPEX | ΔWC | FCF | CASH | ST_DEBT | LT_DEBT | EQUITY | CUR_ASSETS | CUR_LIAB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ALFA | 1000 | 600 | 400 | 300 | 100 | 400 | 50 | 250 | 50 | 200 | 150 | 50 | 100 | 200 | 100 | 300 | 1000 | 600 | 300 |
| BETA | 300 | 240 | 60 | −20 | 30 | 10 | 10 | −30 | 0 | **−50** | 20 | 10 | −70 | 50 | 80 | 120 | 150 | 200 | 180 |
| GAMA | 250 | 200 | 50 | −30 | 25 | −5 | 5 | −35 | 0 | **−40** | 15 | 5 | −65 | 30 | 60 | 90 | 120 | 150 | 140 |
| DELTA | 500 | 350 | 150 | 60 | 20 | 80 | 10 | 50 | 10 | 50 | 30 | 10 | 30 | 50 | 60 | 40 | **−200** | 180 | 150 |
| REST | 400 | 280 | 120 | 110 | 30 | 140 | 15 | 95 | 20 | **75*** | 40 | 5 | 60 | 80 | 50 | 70 | 500 | 250 | 160 |
| EPSL | 1200 | 800 | 400 | 360 | 90 | 450 | 60 | 300 | 60 | 300 | 180 | 40 | 170 | 300 | 200 | 500 | 1600 | 900 | 500 |
| ZETA | 100 | 70 | 30 | 25 | 8 | 33 | 4 | 21 | 5 | 60→*see §5.2* | 15 | 3 | 12 | 40 | 30 | 50 | 200 | 120 | 90 |
| ETA | 450 | 315 | 135 | 90 | 30 | 120 | 15 | 75 | 15 | 90 | 45 | 10 | 65 | 100 | 80 | 120 | 600 | 350 | 200 |
| THETA | 380 | 270 | 110 | 70 | 20 | 90 | 12 | 58 | 12 | 70 | 35 | 8 | 45 | 60 | 90 | 150 | 500 | 280 | 160 |
| PART | 600 | 420 | 180 | 90 | 30 | 120 | 20 | 70 | 15 | 120 | — | — | **NULL (no CF)** | 150 | 100 | 250 | 800 | 400 | 240 |
| NEWP | 50 | 35 | 15 | 12 | 4 | 16 | 2 | 10 | 2 | 30 | 8 | 2 | 6 | 20 | 15 | 25 | 90 | 55 | 35 |
| IOTA | 280 | 210 | 70 | −15 | 25 | **−20** | 8 | −23 | 0 | 20 | 20 | 5 | −10 | 40 | 70 | 110 | 180 | 160 | 130 |
| KAPPA | 520 | 370 | 150 | 80 | 25 | 105 | 14 | 66 | 14 | 60 | 70 | 20 | **−30** | 90 | 120 | 190 | 550 | 300 | 190 |
| LAMDA | 200 | 140 | 60 | 35 | 12 | 47 | 6 | 29 | 6 | 30 | 18 | 4 | 20 | 35 | 45 | 75 | 220 | 130 | 85 |
| NU | 320 | 230 | 90 | 45 | 15 | 60 | 9 | 36 | 7 | 40 | 20 | 5 | 27 | 45 | 60 | 95 | 260 | 150 | 100 |
| UNSEC | 260 | 185 | 75 | 40 | 12 | 52 | 7 | 33 | 7 | 45 | 22 | 5 | 28 | 40 | 55 | 90 | 210 | 120 | 80 |

*REST: FY2025 exists twice — `as_reported` (published 2026-03-10, NI 60) and `restated` (restatement_date 2026-06-20, **NI 75** — the tabulated row). Current metrics serve the highest version (v1).

**`OCF` column (v1.4 — the canonical FCF re-baseline).** The statement payloads gain an `OCF` line (net operating cash flow) and `FCF` is computed as `OCF − |CAPEX|` (`02` §6.2). To keep every existing FCF-dependent golden (§11.1 `ev_fcf`/`fcf_yield`/`fcf_cagr_*`, §11.5 `base_fcf`) **unchanged**, the fixture OCF is authored as `tabulated FCF + CAPEX`:

| symbol | ALFA | BETA | GAMA | DELTA | REST | EPSL | ZETA | ETA | THETA | PART | NEWP | IOTA | KAPPA | LAMDA | NU | UNSEC |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| OCF | 250 | −50 | −50 | 60 | 100 | 350 | 27 | 110 | 80 | — | 14 | 10 | 40 | 38 | 47 | 50 |

So, e.g., ALFA: `FCF = 250 − |150| = 100` (unchanged). PART has no CF statement → `OCF` absent → FCF metrics NULL (unchanged). Deep-history FY statements (§5.1) carry `OCF` lines likewise (`OCF = §5.1 FCF + FY CAPEX`), preserving the §5.1 FCF series.

**KAP sign convention (v1.4):** `CAPEX` is stored **as the source reports it** — KAP cash-flow statements report investing outflows as *negative*. The §5/§5.1 tables show CAPEX as positive magnitudes for readability; the canned payload `statements-kap-sign-convention.json` carries ALFA FY2025 with `CAPEX −150`, `OCF 250` (as-reported shape) and expects the same stored `FCF = 250 − |−150| = 100` (TC-MDF-059).

### 5.1 ALFA deep history (FY series for CAGRs)

| FY | 2015 | 2016 | 2017 | 2018 | 2019 | 2020 | 2021 | 2022 | 2023 | 2024 | 2025 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| REV | 200 | 220 | 250 | 270 | 290 | **400** | 430 | **640** | 700 | 850 | **1000** |
| EPS | 1.00 | 1.05 | 1.10 | 1.15 | 1.20 | **1.50** | 1.55 | **1.80** | 1.85 | 1.90 | **2.00** |
| FCF | 80 | 90 | 95 | 100 | 110 | **160** | 170 | **200** | 190 | 180 | **100** |
| DPS | 0.60 | 0.62 | 0.65 | 0.68 | 0.70 | **0.80** | 0.82 | **0.90** | 0.92 | 0.95 | **1.00** |

(Bold = 3Y/5Y/10Y endpoint years: FY2022, FY2020, FY2015.)

**ALFA FY2025 quarterly statements** (sum exactly to the FY row; exercises ΣQ TTM): REV 220/240/260/280 · COGS 130/145/155/170 · EBIT 60/70/75/95 · D&A 20/25/25/30 · FIN_EXP 10/12/13/15 · TAX 10/11/13/16 · CAPEX 30/35/40/45 · ΔWC 10/12/13/15 (Q1/Q2/Q3/Q4, period end dates 2026-03-31, 06-30, 09-30, 12-31 — *fixture FY2025 = calendar 2025; quarter-ends 2025-03-31 … 2025-12-31*).

### 5.2 Short-history series

- **ZETA** (2 FYs): FY2024 REV 80, EPS 1.00, FCF 10; FY2025 REV 100, EPS 1.20, FCF 12 (NI 60 comes from EPS×50 shares; equity 200). No dividends.
- **NEWP** (1 FY): FY2025 only → every CAGR NULL + no-data state with listing-date context (2025-08-01).
- **KAPPA FCF by FY** (sign change): 2021 +20, 2022 +25, 2023 +10, 2024 −5, 2025 −30 → `fcf_cagr` NULL; FCF_TTM −30 → `ev_fcf`, `fcf_yield` NULL.

### 5.3 EPSL deep history — corporate-action-adjusted per-share CAGRs (v1.4, TC-MDF-058)

EPSL carries the corporate actions (1:5 split effective 2025-06-02 → factor 0.2; 1:10 bonus effective 2026-03-02 → factor 10/11) mid-way through its FY history, so its per-share series must be adjusted before CAGR (`02` §6.2 — Π over actions *after* each fact's date; both actions apply to FY ≤ 2024, only the bonus to FY2025). As-reported FY values (what the payloads carry) and their adjusted equivalents:

| FY | shares (M) | NI (TRY M) | EPS as-reported | DPS as-reported (ex-date) | Π | EPS adjusted | DPS adjusted |
|---|---|---|---|---|---|---|---|
| 2022 | 80 | 250 | 3.125 | 1.25 (2022-12-31) | 2/11 | **0.568182** | **0.227273** |
| 2023 | 80 | 264 | 3.30 | 1.30 (2023-12-31) | 2/11 | **0.600000** | **0.236364** |
| 2024 | 80 | 280 | 3.50 | 1.375 (2024-12-31) | 2/11 | **0.636364** | **0.250000** |
| 2025 | 400 | 300 | 0.75 | 0.30 (2025-12-31) | 10/11 | **0.681818** | **0.272727** |

FY2025's as-reported values match §3 (shares 400 M) and §5 (NI 300); the FY2025 DPS 0.30 also reproduces §6's EPSL DIV_TTM 120 M (= 0.30 × 400 M) — no other EPSL dividend rows exist, so the TTM window catches only the 2025-12-31 ex-date. Pre-split share count = 80 M (400 ÷ 5).

Goldens: **`eps_cagr_3y` = (0.681818/0.568182)^(1/3) − 1 = 0.062659**; **`div_cagr_3y` = (0.272727/0.227273)^(1/3) − 1 = 0.062659**; `eps_cagr_5y`/`eps_cagr_10y` and `div_cagr_5y`/`div_cagr_10y` = **NULL** (4 FYs → only the 3Y window has enough intervals). *Contrast — the bug this fixes:* the unadjusted `eps_cagr_3y` would be (0.75/3.125)^(1/3) − 1 = **−0.378553** (a spurious ≈ −38 %/yr from the split alone). Both adjusted CAGR metrics carry `is_adjusted = true`.

## 6. Dividends

- **ALFA** (quarterly, TTM DPS 1.00): ex-dates 2026-01-15, 2026-04-15, 2026-07-15, 2026-10-01, each 0.25 TRY, currency TRY, pay_date ex+20d. FY DPS series per §5.1.
- DIV_TTM (TRY M): ALFA 100, DELTA 20, REST 30, EPSL 120, THETA 30, IOTA 10, KAPPA 20, NU 60; others 0. Σ = **390**.
- **LAMDA: no dividend rows at all** → `div_yield` NULL + never-paid state.

## 7. Macro series (`macro_series` / `macro_values`, absolute dates)

| code | latest value_date | value | unit | recorded_at | state |
|---|---|---|---|---|---|
| TUIK_CPI | 2026-09-01 | 45.20 | % YoY | 2026-10-03 | fresh |
| TUIK_CPI (revision case) | 2026-08-01 | **45.00** (canonical) | % YoY | 2026-09-20 | second of two rows: 44.80 @ 2026-09-05, 45.00 @ 2026-09-20 |
| INDEP_CPI | 2026-09-01 | 58.30 | % YoY | 2026-10-03 | fresh (absent in the L2 "degraded" profile) |
| CBRT_REPO | 2026-09-15 | 42.50 | % | 2026-09-15 | fresh, `per_release` cadence |
| USD_TRY | R (T) | 47.10 | TRY | T 21:30 (L2) / seed day 21:30 (L4) | fresh, daily |
| EUR_TRY | R (T) | 51.40 | TRY | T 21:30 (L2) / seed day 21:30 (L4) | fresh, daily |
| GOLD | R−10d | 5200.00 | USD/oz | R−10d | **stale** — last successful ingest 10 days before T (drives every stale-marker test) |

**Daily rows are "now" ingests (v1.6 — TKT-foundation-030):** the daily FX rows hold the *latest successful ingest*, so on the now-anchored (L4) variant their `recorded_at` sits on the **real seed day**, not on the trading-day-backed `T`. On a weekend container start `T` backs to the preceding Friday (weekday calendar, §2) while the ingest still happened at seed time; dating `recorded_at` at `T` makes Friday's ingest read as 2 days old on a Sunday, i.e. past the daily tolerable window (1 day, `02` §5.6) — the product then correctly marks a never-missed series stale and TC-MOV-024's fresh-side assertion fails (the Sunday E2E flake closed by TKT-foundation-030). The **value date/as-of stays `T`** — the Friday vintage is what the source last published — and `GOLD` (`recorded_at = T−10`) is unchanged, so the stale side of TC-MOV-024 and every stale-marker golden hold. On a trading-day start (and on L2) seed day = `T`, so those rows are byte-identical to before; `FixtureSet.MacroFreshness` derives the declared age against the same "now" (seed day at L4, `T` otherwise).

**USD_TRY historical row (v1.4 — statement FX normalization):** `USD_TRY` 2025-12-31 = **42.00** (recorded_at 2026-01-02) — the period-end rate the statement ETL uses to normalize USD reporters (§3b USDX, TC-MDF-055). The macro strip reads the *latest* row per series, so the strip goldens above are unchanged. **`INDEP_CPI` is no longer sourced from the macro adapter** (v1.4, TC-MOV-027): the independent measure is entered through the admin endpoint, so the EVDS-shaped payload covers the five official series only; `macro-indep-cpi-absent.json` still models "no manual entry yet" → the paired display degrades to official-only (UXR-MOV-008).

## 8. Funds (FDF fixtures)

| code | name | type | NAVs | performance | holdings |
|---|---|---|---|---|---|
| TEF0001 | Fon Alfa | equity | 2026-09-28…T: 10.00, 10.10, 10.05, 10.20, 10.25 | 1M 2.5%, 3M 6.0%, 1Y 18.0% (as of T) | snapshot T: ALFA w 0.05 units 250,000 · EPSL w 0.03 units 120,000 · "Yabancı Hisse X" (unmatched `name_raw`) w 0.02, instrument NULL |
| TEF0002 | Fon Beta | equity-heavy mixed | 5 rows as above | 1M 1.2%, 1Y 9.0% | **none** — coverage gap recorded (BR-FDF-004) |
| TEF0003 | Fon Gamma | equity | 5 rows as above | none | partial: 1 line "Hisse Y", weight NULL, units NULL |

## 9. Accounts (seeded)

| e-mail | password | verified | role | language_pref | pre-seeded content |
|---|---|---|---|---|---|
| builder@degerli.test | FixturePass1! | yes | **builder** | tr | — |
| user-a@degerli.test | FixturePass1! | yes | user | tr | 1 saved screen "Ekranım" — criteria `[{pe, max, 15}, {roe, min, 0.15}]`; 1 DCF scenario "Temel" (ALFA) — params = ALFA baseline except `discount_rate: 0.15` |
| user-b@degerli.test | FixturePass1! | **no** | user | tr | — |
| user-c@degerli.test | FixturePass1! | yes | user | **en** | — |

### 9b. Business descriptions (RES fixtures)

| stock | status | version | textTr | textEn | lastReviewedAt | publishedAt | source_refs_json |
|---|---|---|---|---|---|---|---|
| ALFA | **published** | 2 | "Alfa Teknoloji, haberleşme ve savunma elektronikleri…" (non-empty) | "Alfa Teknoloji is a communications and defence-electronics…" (non-empty) | 2026-09-10 | 2026-09-10 | [8841] (resolvable → kap_disclosures row: annual_report, 2026-03-14, "2025 Faaliyet Raporu", KAP URL) + [9999] (**unresolvable id → stub case**) |
| REST | draft | 1 | non-empty | non-empty | null | null | [8842] |
| ZETA | draft | 1 | non-empty | **empty** (publish-gate case) | null | null | [8843] |
| all others | — (no rows) | — | — | — | — | — | public overview → `preparing` state |

Corresponding `kap_disclosures` seed rows: ids 8841 (ALFA, annual_report, 2026-03-14), 8842 (REST), 8843 (ZETA). Id 9999 exists in no disclosure row.

## 10. WireMock canned-source catalog (named fixtures)

`prices-ok.json` (T, all 16) · `prices-ok-tminus1.json` · `prices-invalid-negative-close.json` (ALFA close −5) · `prices-missing-provenance.json` · `prices-unparseable.json` (truncated body) · `prices-conflicting-value.json` (ALFA close 21.00 for already-stored T — quarantine per Q1) · `prices-source-down` (500 forever) · `prices-source-slow.json` (5s delay — drives the in-progress run-ledger test) · `prices-variant-threshold.json` (12 eligible gainers incl. one at exactly the 1,000,000 threshold — drives the movers boundary test) · `statements-ok-alfa-fy2025.json` (incl. quarters) · `statements-restated-rest.json` · `statements-no-cf-part.json` · `dividends-ok.json` · `corporate-actions-ok.json` (EPSL split + bonus) · `disclosures-ok.json` · `universe-add-remove.json` (membership change set) · `universe-missing-sector.json` · `index-levels-ok.json` · `macro-ok.json` · `macro-indep-cpi-absent.json` (degraded profile) · `tefas-*.json` (3 funds) · `evren-draft-ok.json` (RES plan).

**v1.4 additions & reshapes:**
- **Statement payloads** carry `unitScale` + `currency` (defaults `"millions"`/`"TRY"`; USDX declares `"millions"`/`"USD"`). New: `statements-bank-banka.json` (§3b BANKA, incl. the spurious `REV`), `statements-insurance-sigorta.json` (§3b SIGORTA), `statements-usd-reporter.json` (§3b USDX), `statements-restated-rest-v2.json` (§3b REST2 — second restatement), `statements-kap-sign-convention.json` (§5 CAPEX −150). The existing statement payloads gain an `OCF` line (§5).
- **`version` in statement payloads** stays `"as_reported"`/`"restated"`; the store assigns the integer sequence (TC-MDF-054).
- **Price payloads are re-shaped to the İş Yatırım envelope** (source name `isyatirim`; per-row source recorded); asserted DB outcomes are unchanged (TC-MDF-061). A `prices-yahoo-contingency.json` models the documented fallback source so its rows record `source_ref = "yahoo"`.
- **Macro payloads are re-shaped to the EVDS envelope** (`macro-ok.json` covers the five official series; asserted DB outcomes unchanged — TC-MOV-026). INDEP_CPI arrives via the admin endpoint, not a payload.
- **Reused (no new payloads):** the constituents launch-seed/coverage-gap case (TC-MDF-062) drives the existing `universe-add-remove.json`; the holdings-coverage-gap case (TC-FDF-013) drives the existing **TEF0002** fund row (§8 — already "none; coverage gap recorded"). Both assertions are about the recorded behavior, not a new payload shape.

### 10b. Real-source catalog — the parsing-fidelity lane (v1.5, architecture §10.9.7)

A **second, checked-in catalog** sits alongside the §10 canned-source catalog: real-source bodies for the parsing-fidelity lane (MDF plan §8; MOV TC-MOV-028). It is the **recorded exception to §1 principle 1** (recorded as principle 1b).

- **Content:** İş Yatırım EOD `HGDG_*` bodies — five tickers (ASELS, THYAO, GARAN, FROTO, EBEBK), bounded 3-trading-day window (captured 2026-10-16, window 2026-10-06..2026-10-08) — **captured verbatim**; KAP XBRL fact bodies (IFRS/TR tag variance, TR-locale numbers) and a legacy windows-1254 artifact — **constructed, shape-faithful** (honestly labeled; a bounded live capture may replace them where honest — TKT-foundation-018); an EVDS capture added by TKT-foundation-019.
- **Contract:** every entry carries **raw bytes + a declared charset + an honest captured-vs-constructed provenance label** (labeling rule: a constructed body is never labeled "captured verbatim" — the DR-03 rule); entries are served through the WireMock harness at their default routes **with the correct `Content-Type; charset=` header**, and are decoded by the **production adapters in `src/Ingestion`** — the catalog ships no decoders of its own (DR-01 overturned; the decoders, the XBRL concept map and the code-pages provider registration are lifted into `src/` per AD-14). The catalog loads through the same `Names`/`Build`/`Get`/`TryGet` seam as the §10 catalog (asserted by TC-MDF-063).
- **Golden boundary:** the FU canonical envelope (§10, incl. the v1.4 "İş Yatırım envelope" — the canonical double with `isyatirim` source naming, **not** the real `HGDG_*` wire shape) stays the **L2 golden contract**; real-envelope cases assert parse shape/fidelity (capture-recorded pins), **never derived numbers** — no golden value anywhere derives from a real capture. FU volumes reinterpret as ₺ turnover with unchanged values (§2/§4 notes) — no golden re-derivation.
- **Origin & terms:** the catalog originates on branch `TKT-foundation-015` (PR #82, closed unmerged) as raw material; redistribution terms are recorded with the catalog per RISK-MDF-001 (bounded, non-commercial, parsing-fidelity only).

## 11. Derived golden values (asserted in the domain plans; derived here)

### 11.1 ALFA — all 18 canonical metrics (TC-MDF-026)

| metric | value | derivation |
|---|---|---|
| pe | **10.0** | 20 / (200/100) |
| pb | **2.0** | 20 / (1000/100) |
| ev_ebitda | **5.5** | (2000+400−200)/400 |
| ev_fcf | **22.0** | 2200/100 |
| fcf_yield | **0.05** | 100/2000 |
| roic | **0.20** | 300×(1−0.2)/1200; t_eff = 50/250 |
| roe | **0.20** | 200/1000 |
| gross_margin | **0.40** | 400/1000 |
| operating_margin | **0.30** | 300/1000 |
| net_debt_ebitda | **0.5** | 200/400 |
| interest_coverage | **6.0** | 300/50 |
| current_ratio | **2.0** | 600/300 |
| div_yield | **0.05** | 1.00/20 |
| payout_ratio | **0.50** | 100/200 |
| rev_cagr_3y | **0.160397** | (1000/640)^(1/3)−1 |
| rev_cagr_5y | **0.201124** | (1000/400)^(1/5)−1 |
| rev_cagr_10y | **0.174619** | (1000/200)^(1/10)−1 |
| eps_cagr_3y | **0.035744** | (2/1.8)^(1/3)−1 |
| eps_cagr_5y | **0.059224** | (2/1.5)^(1/5)−1 |
| eps_cagr_10y | **0.071773** | 2^(1/10)−1 |
| fcf_cagr_3y | **−0.206300** | (100/200)^(1/3)−1 |
| fcf_cagr_5y | **−0.089708** | (100/160)^(1/5)−1 |
| fcf_cagr_10y | **0.022565** | (100/80)^(1/10)−1 |
| div_cagr_3y | **0.035744** | (1/0.9)^(1/3)−1 |
| div_cagr_5y | **0.045640** | (1/0.8)^(1/5)−1 |
| div_cagr_10y | **0.052409** | (1/0.6)^(1/10)−1 |

*(18 metric concepts; the 3 growth concepts each carry 3 windows — Q5 resolved 2026-10-07: window-suffixed metric codes (e.g. `rev_cagr_3y`, matching this table), metric model stays flat; idempotency/upsert key `(instrument, metric, as_of_date)`.)*

### 11.2 Not-meaningful (NULL) matrix — decision table over the archetypes (TC-MDF-031)

| fixture | NULL metrics (rule) |
|---|---|
| BETA, GAMA | pe, payout_ratio (NI ≤ 0); market-PE exclusion counted |
| DELTA | pb, roe (equity ≤ 0); roic (IC = 100−200−50 ≤ 0) |
| IOTA | ev_ebitda, net_debt_ebitda (EBITDA ≤ 0) |
| KAPPA | ev_fcf, fcf_yield (FCF ≤ 0); fcf_cagr (sign change) |
| PART | ev_fcf, fcf_yield, fcf_cagr (FCF not computable — no CF statement) |
| LAMDA | div_yield, div_cagr (never paid) |
| NEWP | all 12 CAGR variants (< 2 FYs) |
| NU | payout_ratio = **1.5** — *not* NULL (NI > 0; may exceed 1, displayed honestly) |
| ZETA | none — CAGRs computed over 1 interval, window_years = 1 |

### 11.3 Market snapshot goldens (asserted in the MOV plan; computed from §4–§6)

- Σmcap (all) = **34,745**; Σmcap (NI > 0, excl. BETA+GAMA) = **33,465**; ΣNI (NI > 0) = **1,190**
- **market_pe = 28.1218**; **market_pe_excluded_count = 2**; **market_div_yield = 0.011225** (390/34,745)
- **Breadth:** advancing 10 (ALFA, DELTA, REST, EPSL, ZETA, ETA, PART, NEWP, KAPPA, LAMDA), declining 5 (BETA, GAMA, THETA, IOTA, UNSEC), unchanged 1 (NU)
- **Volume total = 174,500,000** shares
- **Gainers (eligible, top-10 → 9 rows):** ETA +10.0000 · NEWP +4.0000 · ZETA +3.0000 · KAPPA +2.5000 · EPSL +1.9888 · PART +1.5022 · REST +1.2151 · ALFA +1.0101 · DELTA +0.5025 — **LAMDA +25.0000 excluded** (volume 500,000 < 1,000,000)
- **Losers (5 rows):** THETA −10.0000 · IOTA −2.9126 · BETA −1.9608 · GAMA −1.2346 · UNSEC −0.4425
- **Sector daily performance** — **formula locked (builder decision Q2, 2026-10-06): cap-weighted average of members' daily % changes, Σ(mcap_i × chg_i)/Σmcap_i over sector members:** A **+0.2530%** (12.72831/5030) · B **+2.2409%** (400.36943/17860) · C **+4.1726%** (468.37379/11225). UNSEC in no sector bucket. (The 1W/1M/YTD period aggregates for FR-MOV-017 Could have **no defined formula** — blocked-on-formula, MOV plan flag.)

### 11.4 Sector P/E medians (asserted in the RES plan)

- A: values {10.0, 10.0, 20.0} (BETA/GAMA excluded) → **median 10.0**, peer_count 3, excluded_count 2
- B: {26.6667, 34.3333, 22.0, 18.0, 25.0, 52.0} → sorted 18, 22, 25, 26.6667, 34.3333, 52 → **median 25.8333**, peer_count 6, excluded_count 0
- C: {50.0, 102.5, 45.8333, 67.5} → sorted 45.8333, 50, 67.5, 102.5 → **median 58.75**, peer_count 4, excluded_count 0

### 11.5 DCF baselines (asserted in the VAL plan)

**Active baseline per stock** (every covered stock has one — BR-VAL-001; PART's returns `missing_inputs`):

| stock | params | notes |
|---|---|---|
| ALFA | `base_fcf 100, growth_rate 0.10, horizon_years 5, terminal_growth 0.03, discount_rate 0.12, debt 400 (ST+LT), cash 200, share_count 100` | canonical-fact refs → FY2025 statements (asOf R−6d, restated false) + T price 20.00 |
| REST | same shape, facts from **restated** statements | refs carry `restated: true` (UXR-VAL-010) |
| PART | row exists, statement-derived facts absent | GET → `state: "missing_inputs"` naming the CF-derived facts |
| others | valid rows from their FU §5 facts | |

**Parameter semantics — Q-VAL-1 (RESOLVED — builder ruling 2026-10-07, reading (a)):** the `02` §6.4 formula/parameter issue is closed: the 8th parameter carries **total debt (ST+LT)** and the formula stands exactly as written (`EquityValue = PV_explicit + TV/(1+r)^N + Cash − Debt`); both parameters affect the result (no-black-box, NFR-VAL-004). Independently re-verified under exact arithmetic on 2026-10-07 — goldens confirmed (FVPS 13.1969, MoS −0.5155, corner 24.0000). Upstream-doc gap flagged 2026-10-07: `02` §6.4's parameter name should be reconciled to `debt`/`total_debt` (the architect's dispatch timed out; this fixture contract is authoritative for implementation until that patch lands).

**Golden derivation (ALFA, price 20.00):**
- PV_explicit = 100·Σ(1.1/1.12)^t, t=1..5 = 98.2143 + 96.4605 + 94.7379 + 93.0462 + 91.3847 = **473.8436**
- TV = 100·1.1⁵·1.03/(0.12−0.03) = 165.88253/0.09 = **1843.1392**; TV_pv = 1843.1392/1.12⁵ = 1843.1392/1.7623417 = **1045.8467**
- EquityValue = 473.8436 + 1045.8467 + 200 − 400 = **1319.6903** → **FairValuePS = 13.1969**
- **MOS = (13.1969 − 20)/13.1969 = −0.5155** (price 51.55% above the user's fair value)
- Sensitivity grid (5×5, axes r ∈ {0.10..0.14}, g_t ∈ {0.01..0.05}, step 0.01, other params held): center **13.1969**; (r 0.10, g_t 0.05) → PV 500, TV_pv 2100.0000 → **24.0000**; (r 0.14, g_t 0.01) → PV 449.7668, TV_pv 649.8561 → **8.99623** (corrected v1.3 — hand-arithmetic slip found in the independent 2026-10-07 recomputation; was 8.9952); cells with r ≤ g_t → not computable (NULL).

### 11.6 Rights-issue adjustment (TC-MDF-053; formula FU §2, Q3 resolution 2026-10-07)

- **Worked example:** 1-for-4 rights (q = 0.25) at subscription P_S = 10.00, cum price P_C = 20.00 → TERP = (20.00 + 0.25×10.00)/1.25 = **18.00**; factor = TERP/P_C = **0.90**.
- **Composition:** rights factor 0.90 after a prior 1:5 split (0.2) → composite 0.90 × 0.2 = **0.180** (multiplicative rule unaffected, FU §2).
- **Boundaries:** P_S = P_C → factor **1** (no adjustment); P_S = 0 → reduces to the bonus factor 1/(1+q) = **0.80**. Degenerate inputs (q ≤ 0 or P_C ≤ 0) invalidate the action (rejected by validation, never a factor).

## 12. Determinism recap

Fake `TimeProvider` at L1/L2 (absolute dates above); now-anchored seeds at L4 (`T` = the latest trading day at or before the seed day; daily macro `recorded_at` = seed day, so "fresh" holds on any seed day including a weekend start; GOLD stale = T−10); assertions on flags, offsets, and payload values — never wall-clock dates; i18n assertions load `tr.json`/`en.json` from the repo; all randomness fixed; every test creates its own mutable state.

---
*Change record: v1.0 2026-10-06 — initial fixture universe, submitted with the MDF Gate 2 plan. v1.1 2026-10-06 — builder decisions applied: Q1 conflicting-price quarantine (`CONFLICTING_VALUE` + canned payload), Q2 sector-performance formula locked (cap-weighted), Q3 rights-issue adjustment deferred (no rights fixtures), Q4 compute-from-available CAGR reading confirmed, movers-boundary variant payload added. v1.2 2026-10-06 — batch-mode additions: §9b business-description fixtures (published/draft/empty-EN/unresolvable-ref cases), §9 saved-content seeds specified, §11.5 DCF baselines for all stocks with the Q-VAL-1 parameter reading and full golden derivation (FVPS 13.1969, MOS −0.5155, grid corners 24.0000 / 8.9952). v1.3 2026-10-07 — builder final-review rulings applied: Q-VAL-1/F-VAL-1 resolved (total-debt reading (a)); goldens independently re-verified under exact arithmetic — sensitivity corner (0.14, 0.01) corrected 8.9952 → **8.99623** (hand-arithmetic slip); Q3 rights-issue factor defined (FU §2, golden §11.6); Q5 windowed-CAGR storage settled (window-suffixed codes; plain (instrument, metric, as_of_date) key).* **v1.4 2026-10-15 — post-board correctness feedback (applied by the delivery planner on the builder's explicit instruction; test-planner confirmation pending):** §2 unit/currency normalization convention + `MISSING_FX_RATE` + integer statement-version semantics + TMS 29 regime boundary; §3b on-demand fixtures BANKA/SIGORTA/USDX/REST2 (outside the seeded 16 — no universe-golden cascade); §5 `OCF` column (`= FCF + CAPEX`, so all FCF goldens are unchanged) + KAP sign convention; §5.3 EPSL adjusted per-share history (eps_cagr_3y / div_cagr_3y = 0.062659; unadjusted would be −0.378553); §7 USD_TRY 2025-12-31 = 42.00 + INDEP_CPI moved to manual entry; §10 payload additions/reshapes (statements unitScale/currency + OCF, bank/insurance/USD/second-restatement/sign-convention payloads, İş Yatırım price envelope, EVDS macro envelope, universe-launch-seed, tefas-allocation-only).* **v1.5 2026-10-17 — real-source ingestion mirror (architecture v1.5 §10.9; builder-directed via the delivery planner, applied by the test-planner):** §1 principle 1b — the real-source catalog exception (charset-faithful raw bytes, honest captured-vs-constructed labels; labeling rule: never "captured verbatim" on constructed bodies — DR-03); §2 mover-threshold unit pinned to TRY turnover (₺) and §4 volume-semantic note — values unchanged, **no golden re-derivation** (FLG-09; Yahoo-contingency rows NULL volume); §10b — the real-source catalog lane (parsing-fidelity contract: raw bytes served with declared `Content-Type; charset=` headers, decoded by the production `src/Ingestion` adapters — the catalog ships no decoders, DR-01 overturned; FU canonical envelope stays the L2 golden contract; redistribution terms per RISK-MDF-001). Goldens untouched. Feeds MDF plan §8 (TC-MDF-063..076) and MOV TC-MOV-028.** **v1.6 2026-10-19 — TKT-foundation-030 (fixture-only, builder-directed):** §7 — the now-anchored daily macro rows (`USD_TRY`/`EUR_TRY`) place `recorded_at` on the real seed day (as-of/value date stays `T`, the Friday vintage) so "fresh" matches the runtime clock on a Sunday/weekend container start, closing the date-dependent TC-MOV-024 E2E flake; `GOLD` (T−10) stays stale by construction; L2 and weekday starts are byte-identical (seed day = `T`). §12 determinism recap aligned. Goldens untouched; no product/`src/` change.**
