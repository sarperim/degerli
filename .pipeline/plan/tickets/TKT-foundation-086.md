---
scope:
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Persistence/Entities/ShareCountEntities.cs
  - src/Persistence/DegerliDbContext.cs
  - src/Persistence/Migrations/
  - src/Ingestion/Shares/ShareCountStore.cs
  - src/Ingestion/Prices/IsYatirimPriceDecoder.cs
  - src/Ingestion/Prices/PricesModels.cs
  - src/Ingestion/Prices/PricesSourceAdapter.cs
  - src/Ingestion/Prices/PriceDerivedShareCountStore.cs
  - src/Ingestion/Prices/PricesJob.cs
  - src/Ingestion/IngestionServiceCollectionExtensions.cs
  - src/Ingestion/Valuation/DcfBaselineGenerationJob.cs
  - tests/Api.IntegrationTests/PriceDerivedShareCountsTests.cs
  - tests/Api.IntegrationTests/MemberSecuritiesIngestTests.cs
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
  - tests/Api.IntegrationTests/DcfBaselineEndpointTests.cs
  - tests/Api.IntegrationTests/DcfBaselineRegenerationTests.cs
  - src/Web/src/screens/stock-page/StockSections.tsx
  - src/Web/src/screens/stock-page/StockPage.test.tsx
  - src/Web/src/screens/stock-page/testSupport.tsx
  - src/Web/src/i18n/catalogs/tr.json
  - src/Web/src/i18n/catalogs/en.json
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/02-data-model.md
  - .pipeline/architecture/03-api-design.md
---

# TKT-foundation-086: The 065 contract half lands — `paidInCapital` under its own name, an honest price-derived `sharesOutstanding` beside it, and the DCF per-share divisor unblocked

- Status: done — **PR #146 squash-merged 2026-10-26 (`80ac3fb` on dev), final head `00a1b21` (rebased over the 082 docs landing; the reviewed code is byte-identical to the round-3 head `45e27a3` — the rebase resolved exactly one test-plan doc conflict, append-only, both sides kept), CI run 40323609666 (3/3 green), MERGEABLE/CLEAN at merge.** Review: rounds 1–3 (`reviews/TKT-foundation-086-round-{1,2,3}.md`; round 3 = merge basis — R1-F1 and DR-1..DR-5 all fixed-verified, zero open blockers; majors DR-1 captive-dependency/DI and DR-2 web dash-rendering both closed with tests). Recorded residuals (non-blocking, round-3 artefact): unused `using Degerli.Persistence;` in `PricesJob.cs:9`; dead `PAID_IN_CAPITAL_UNIT_CODE` in `src/Web/src/lib/units.ts:51` (out of scope); `IMPLEMENTATION_STATUS.md:172,208` still claims the API serves `currentCapital` under `sharesOutstanding` — now false, status-refresh follow-up; weak substring assertion in TC-MDF-115 (v) EN case (pre-existing).
- Size: L
- **Priority: P1 — the user's this-week goal: fair value PER SHARE on the custom DCF (dashboard → stock page → KAP data → DCF).** After 082, the DCF computes company-level intrinsic value on real annual FCF; per-share stays blocked on exactly this ticket's honest divisor.
- **What this is:** the 065 implementation slice (its acceptance (ii) + the orchestrator's 2026-10-24 refinement, all three items) **plus** the honest share-count source that unblocks the DCF's `share_count` fact. One ticket because the rename is a **breaking contract change** — the API rename, the store rename, the web consumption side and the count source land together or not at all; no silent break, no lying field name left behind.

## Premise corrections (verified on `dev@1b8925a`, 2026-10-26 — recorded so the slice follows evidence, not the dispatch's assumptions)

1. **Verified true:** `StockFundamentalsEndpoints.cs:317` serves the `share_counts` store's `/memberSecurities` `currentCapital` figure (money — 64/556 rows carry sub-unit precision) under the name `shareCount.sharesOutstanding`. The DCF consumer chain is real: `DcfBaselineGenerationJob` derives `share_count` from the statement `SHARES_DILUTED` line — **0 rows anywhere** — so every baseline serves `state: "missing_inputs"` naming `share_count`, and the web calculator's `fairValuePerShare` has no honest divisor.
2. **Verified FALSE (the dispatch's data-source premise):** 081's decoder does **not** decode share-count lines — it deliberately maps none (doc-commented: "no share-count line is mapped at all"), the captured equity statement is entirely `NO_COLUMN_HEADERS`-dropped (98 cells, no EQ row emitted), and the committed fixture carries **no explicit share-count row and no count-scale value** (exhaustive search: no `Adedi`/`Sayısı`/nominal labels; only the monetary `Ödenmiş Sermaye` equity column, 4,560,000 thousand TL, itself dropped). `fin_line_items` has no per-line provenance (UPSTREAM-1, owed). **There are no decoded share counts to wire.**
3. **Verified correction (the web-consumption premise):** the web does **not** consume `shareCount.*` at all (verified by search). What the web renders is the sibling `lines.sharesOutstanding` (statement `SHARES_OUT`, an honest dash today) — which 072 labelled "Ödenmiş sermaye / Paid-in capital" as a claim-in-waiting (065's consequence 2). The breaking change's "web side" is therefore the **label/unit revert of that row**, not a `shareCount` consumer migration.

**USER DECISION D5 (2026-10-26, this slice): the honest count source is the price-lane derivation** — count = market cap ÷ close from the isYatirim daily price payload the prices lane already fetches (`PD` and `HG_KAPANIS` arrive in the same row; both are currently discarded at the decoder). Exact on the committed captures: ASELS `PD 1,714,560,000,000 ÷ 376 = 4,560,000,000`; the universe capture cross-checks A1CAP `675,000,000`, AAGYO `701,000,000`, ADEL `259,875,000`, A1YEN `600,000,000`, ACSEL `10,721,700` — every quotient integral (BIST computes `PD` = shares × close). The alternatives (KAP-report count rows — unverified to exist in any committed capture; nominal divisor — 065 Option B, no evidenced nominal) were pinned and rejected for this slice; the statement-reported count route stays open below.

## What to implement

1. **The contract rename (065 acceptance (ii), breaking, both sides in one PR):**
   - `GET /api/v1/stocks/{symbol}/balance-sheet`'s `shareCount` block: the mislabeled field becomes **`paidInCapital`** (+ `asOf`), unit **TRY**, value = the renamed store's capital figure — D4 verbatim: *"no `sharesOutstanding` claim from that lane."*
   - The block also serves the **new derived count** as **`sharesOutstanding`** (nullable, its own as-of) — the count name is reclaimed for a real, provenance-carrying count, never money again.
   - **Store rename:** `share_counts.shares_outstanding` → `paid_in_capital` (forward-only migration; the 556 money rows keep their values under an honest name — a lying column name one layer down is the same class of error D4 exists to prevent). Entity property `ShareCount.SharesOutstanding` → `PaidInCapital`; `ShareCountEntities.cs` doc comment rewritten (the "unit unresolved" paragraph closes: the value IS paid-in capital); `ShareCountStore.cs` and `MemberSecuritiesIngestTests` follow the rename (assertion deltas only, no behaviour change).
   - **The two permanent contract texts are authored, not edited:** `03` §3 never documented the `shareCount` block and `02` §3.1 never documented `share_counts` (058 doc gaps) — this ticket writes both (the block shape incl. the derived count; the table semantics + the new derived table), so the contract is answerable from the docs.
   - **`lines.sharesOutstanding` keeps count semantics (065 consequence 2):** the statement `SHARES_OUT` line's web row reverts from 072's "Ödenmiş sermaye / Paid-in capital" (TRY) claim-in-waiting to a share-count label with a count unit — and stays an **honest dash** until a statements decoder actually emits `SHARES_OUT` (none does today). The `StockSections.tsx` D4 comment is corrected (it describes the wrong field); the i18n copy is UX flow-back (the 072 precedent — see UX refs). `lines.sharesDiluted` is untouched.
2. **The honest count source — price-lane derivation (D5):**
   - `IsYatirimPriceDecoder` reads `PD` (market cap, TL) into the dated fact (additive, nullable — a row without `PD`, and the Yahoo/canned shapes, simply carries none: honest skip, counted).
   - **New additive per-instrument store** (e.g. `price_derived_share_counts`): `shares_outstanding` (integer-valued), **both operands recorded** (`market_cap_raw`, `close_raw` — the arithmetic auditable from the row), `price_date` (the as-of), `source_ref = "isyatirim"`, `recorded_at`. Input-aware (the 042/050 doctrine): unchanged → no write, no `recorded_at` movement; changed → repaired in place + counted. **Monotonic as-of:** an older fact never regresses the row.
   - **The derivation rule (the honesty core):** the count is written **only** when `PD` is present, `close > 0`, and `PD ÷ close` is integral within a pinned tolerance — a non-integral quotient means the operands disagree and the row is **withheld and counted** under its own reason code, never a rounded guess; missing `PD` or `close ≤ 0` is a named, counted skip; never zero. **No nominal, no `/memberSecurities` division** — the 065 Option-B divisor path stays forbidden.
   - Written by the prices job where the decoded rows are already held — **no new fetch, no new source, zero additional requests** (the polite-source posture is untouched).
3. **The consumer — DCF per-share, NULL-gated:**
   - `DcfBaselineGenerationJob`: the `share_count` fact gains a second read path — the statement `SHARES_DILUTED` line (existing) **or** the derived store. **Precedence pinned:** a statement-reported count (as-reported) wins; the derived count supplies otherwise; the fact's provenance refs in `params_json` (UXR-VAL-010) record which source supplied it and its as-of. Both absent → `share_count` stays in `missingInputs` (UXR-VAL-009) and **fair value per share is never computed on a guess** — the compute path is unchanged (it already rejects `share_count ≤ 0`).
   - **Web DCF calculator (SCR-006): no structural change** — the prefill and `fairValuePerShare` rendering already exist; the unblock is the baseline gaining the fact. The web diff is the SCR-005 row revert + copy + tests.

## Pinned non-goals (flow-back rule)

- **No per-share metrics / market-P/E consumption** — `MetricsEngine`/`MarketSnapshotEngine` are untouched; BR-MOV-010's exclusion doctrine stands. Those consumers get their own decision now that a count will exist.
- **No KAP-report share-count decode** — the 065/081 routing stands. When a committed capture evidences share-count rows (e.g. the annual report 1561039, 081's open live criterion), a future ticket maps them; the **statement-reported count then supersedes the derived one** (the precedence above is recorded in `01`/`02` for exactly that day).
- **No `fin_line_items` writes from the price lane; no UPSTREAM-1 per-line provenance work; the equity statement's `NO_COLUMN_HEADERS` drop is untouched.**

## Coordination — the 082 lane (read before dispatch)

**082 is LIVE in `src/Ingestion/Statements/` (StatementsJob/SourceAdapter/Models), `src/Ingestion/KapPublic/`, its test file, and holds the two permanent docs' v1.5.56/§23 reservations.** This ticket's code scope is **disjoint** from every 082 file (no `Statements/`, no `KapPublic/`, no `appsettings.json`). The shared surface is the two permanent docs (`.pipeline/testing/market-data-foundation.md`, `01-system-architecture.md`) — the standing lane doctrine applies: **second-lander rebases, keep-all-entries, no deletion or rewrite of the other ticket's rows** (§23/v1.5.56 and §26/v1.5.60 coexist). This ticket is **dispatched only after 082 merges** (queue policy), so the doc rebase is the only expected overlap; if 082's landing moves any file 086 scopes (it should not — verified disjoint), re-verify the scope list against the merged head before dispatch.

- Traces to: FR-MDF-001 (the prices lane the derivation rides), FR-MDF-008 (provenance — both operands + source + as-of on every count), FR-MDF-011 (honest public surface — no money under a count name), FR-VAL-001/002 (DCF baseline supply — the `share_count` fact), UC-VAL-003 (baseline generation), FR-RES-0xx (balance-sheet display supply — the reverted row), BR-MOV-010 (P/E exclusion — cited, stays); `01` §10.9.11 (the `shares` lane — corrected to close 065), §10.9.19 (the KAP-report count routing — cited, unchanged); `02` §3.1 + §6.1 (SHARES_OUT normalization doctrine — the derived count never enters `fin_line_items`); TKT-foundation-065 (the debt record this closes), TKT-foundation-058 (the store being renamed), TKT-foundation-072 (the label being reverted), TKT-foundation-081 (the routing record).
- Acceptance: **TC-MDF-114, TC-MDF-115** (test plan §26, v1.28) — L2 doubles only, zero live requests (the committed isYatirim price and universe captures are the doubles; every negative case derived by mutating them in memory). **Red today, honestly stated:** `share_count` is in `missingInputs` for every stock (0 `SHARES_DILUTED` rows) and `shareCount.sharesOutstanding` names money — the red is the current head's own behaviour. Non-regression: `MemberSecuritiesIngestTests` (rename assertion deltas only), `StockFundamentalsEndpointsTests`, `DcfBaselineEndpointTests` (incl. TC-VAL-008), `DcfBaselineRegenerationTests`, `RealSourcePricesTests`, `EodChainTests`, `MorningChainTests`, the web `StockPage` suite — re-run green, pre-listed additive deltas only. **Live criterion (orchestrator, post-merge):** one EOD prices run over the covered universe populates derived counts — ASELS serves `4,560,000,000` (cross-checked against 065's evidence table, recorded verbatim); the balance-sheet payload shows `paidInCapital` (TRY) + derived `sharesOutstanding` with its as-of; one `dcf-baselines` regeneration computes `fairValuePerShare` for a covered stock with the count's provenance in `params_json`. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §10.9.11 (the share-count paragraphs corrected: the store is paid-in capital; the derived-count lane + precedence recorded) + §13.1 **v1.5.60** (soft reservation, next free above the landed v1.5.55/v1.5.59 and the 082–084 reservations v1.5.56–58; re-pin at merge); `02-data-model.md` §3.1 (`share_counts` semantics authored + renamed; the `price_derived_share_counts` table + its honesty rules); `03-api-design.md` §3 (the `shareCount` block contract authored — `paidInCapital`/`sharesOutstanding`/as-ofs); test plan §26 (TC-MDF-114/115) + the v1.28 change-record entry.
- UX refs: SCR-005 (balance-sheet section — the reverted `lines.sharesOutstanding` row: count label + count unit, honest dash), SCR-006 (DCF calculator — parameter #8's baseline default gains a real value; no structural change), UXR-VAL-009 (missing-inputs explanation — `share_count` leaves the missing set only when a source exists), UXR-VAL-010 (the count's provenance/as-of carried into the parameter display), UXR-RES-0xx (balance-sheet line labels) — the exact TR/EN copy for the reverted row is **UX flow-back** (the 072 precedent: the ux-designer confirms the entries; the coder ships minimal, unopinionated copy meanwhile).
- Dependencies: none hard — 058/072/081 are merged; **dispatched behind TKT-foundation-082 by queue policy** (see coordination). 083's market-P/E honest-null posture is compatible by design (non-goal above).
- Parallel group: none (shares the two permanent docs with the lane; last-lander rebase).

### Scope amendment (orchestrator, 2026-10-26)

`src/Ingestion/IngestionServiceCollectionExtensions.cs` added to scope post-implementation: `PricesJob` (scoped, over the scoped `DegerliDbContext`) takes `PriceDerivedShareCountStore` by constructor injection — a conventional `services.AddScoped<PriceDerivedShareCountStore>()` beside the other prices stores, no lifetime change, no captive dependency, no design change. Flagged by the implementer, recorded here per the G-2 pattern (amend, never silently exceed).

## Live run — 2026-10-26/27 (orchestrator, dev stack, recorded verbatim)

- **Price-derived share counts live:** after the prices run 313 (written 100/100, today's EOD), `price_derived_share_counts` holds **20 rows, every quotient integral and matching known real counts**: ASELS 4,560,000,000; THYAO 1,380,000,000; TUPRS 1,926,795,598; BIMAS 1,200,000,000; AKBNK 5,200,000,000; MGROS 181,054,233; TAVHL 363,281,250; TTRAK 100,066,875; +12 more. The remaining ~80 are honestly withheld (non-integral quotient rule). AEFES among the withheld (non-integral) — its DCF therefore still names `share_count` as missing; TUPRS/TAVHL/MGROS resolved.
- **dcf-baselines live (runs 309/314, after the pending migrations were applied): written 630, zero failures.** AEFES baseline carries `base_fcf = 15,042,422,000` sourced from the live KAP FY2025 CF statement (canonicalFactRefs: statement, 2026-10-26). TUPRS/TAVHL now miss only `debt`/`cash` (balance-sheet lines — IS/BS decode coverage follow-up).
