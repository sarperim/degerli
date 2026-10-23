---
scope:
  - src/Api/Stocks/StockFundamentalsEndpoints.cs
  - src/Web/src/screens/
  - src/Web/src/i18n/catalogs/
  - tests/Api.IntegrationTests/StockFundamentalsEndpointsTests.cs
---

# TKT-foundation-052: Temettü honesty wording — "Bu şirket hiç temettü ödemedi" is an unbacked claim when the dividends lane has no data

- Status: done (PR #121 — https://github.com/sarperim/degerli/pull/121)
- Size: S
- **Priority: P1 (small honesty defect) — but serialized behind TKT-foundation-049** (shared `src/Web/src/screens/` + `i18n/catalogs/` files).
- Verified evidence (user finding 2026-10-20): the stock page (SCR-005) dividends section renders **"Bu şirket hiç temettü ödemedi"** ("neverPaidBody", `src/Web/src/i18n/catalogs/tr.json`) while `dividends` had **zero rows** and the dividends job had **never completed a run** at capture time — an unbacked negative claim violating the honest-empty doctrine (the 039 lane's language: absent data ≠ verified zero).
- **Pinned fix (minimal, honest):** the dividends payload gains a server-side availability signal (follow the 037/038/039 freshness-language precedent — e.g. a `dividends: { available: bool, … }` shape pinned by the coder against the existing stock-fundamentals payload structure; the signal derives from the dividends lane's coverage/run state, not from row-count-zero alone). The section then renders three honest states: **(a) no data / not yet processed** → "veri yok / no data yet" (neutral, no claim); **(b) processed, none found** → the existing neverPaid wording (now backed); **(c) processed, rows exist** → the existing list. Post-046 the dividends job completes, so state (b) becomes genuinely reachable and backed.
- Traces to: FR-RES-0xx dividends display lane (FR-RES-005–018), FR-MDF-010/011 (honest achieved depth — availability before claims); the honest-empty doctrine (039's UX copy lane); SCR-005 dividends section.
- UX refs: **SCR-005** (Stock Page — dividends section). Copy is minimal-unopinionated per the UX standard; the "veri yok" wording should flow back to the ux-designer with 049's flow-back note (routing item, not a blocker).
- Acceptance: **explicit criteria; doubles-only.** **(i) Server signal:** `StockFundamentalsEndpointsTests` — the dividends payload distinguishes not-processed from processed-empty from populated (doubles per state). **(ii) Web states:** Vitest — the three states render their distinct honest copy (neverPaid only in the backed state); TR/EN via catalogs. **(iii) Existing stock-page tests re-run green** with pre-listed deltas. **(iv) CI green.** **Live criterion (orchestrator post-merge):** on the dev stack, a stock with no dividends coverage shows "veri yok" (not the neverPaid claim); after a completed dividends run, a genuinely-zero company shows the backed wording. Recorded verbatim.
- Architecture refs: `01-system-architecture.md` §13.1 **v1.5.32** (the 036/039 Web-lane row precedent); test-plan TC assignment follows the RES plan's next free IDs (test-planner confirmation lane).
- Dependencies: **TKT-foundation-049** (shared Web files — serialize after it). No backend dependencies (046 merged; the dividends lane completes).
- Parallel group: none

## Implementation note (in-review, PR #121)

**Signal pinned (coder, per the ticket's "pinned by the coder" latitude):** the dividends payload
carries `dividendsAvailable` on **both** branches (populated `true`; the zero-row envelope `true` when
backed / `false` when not), with `state: "no-data"` + `coverage` unchanged — additive, so the existing
`no-data` contract and every current assertion hold. Derivation is the lane's own state, never the row
count: (1) the per-instrument VYK family cursor (`coverage_metadata`, `scope=instrument`,
`data_type=kap-dividends-cursor`, persisted by the adapter only after a completed walk), else
(2) the latest terminal `dividends` run in the ledger — `succeeded` covers every resolvable
constituent; `partial` covers all but the symbols its per-symbol stats name `failed` (046's identity
isolation, never walked); unreadable stats = not backed.

**States shipped (SCR-005 §7):** (a) neutral `stockPage.dividends.unavailableTitle/unavailableBody`
("Temettü verisi yok / No dividend data") when the signal is absent or false — this is also the
**degradation** path for a pre-052 payload; (b) the existing `neverPaidTitle/neverPaidBody`, now only
over a backed signal; (c) the existing dated history + server TTM. `preparing`/`unavailable` payload
states keep their `SectionMarkers` rendering unchanged.

**Evidence:** `TC-RES-037` (API, 4 cases) red 4/4 → green 4/4, existing class 17/17 with **no
assertion edits**; `TC-RES-038` (Web, 5 cases) red 3/5 → green 5/5, `screens/stock-page` 37/37, full
Web unit suite 324/324; `check:i18n` 662 keys, `check:advice`, `tsc`, `eslint`, `dotnet build`
(0 warnings) clean. §13.1 v1.5.32 appended. Types for the additive field live in
`screens/stock-page/api.ts` — `lib/api/contracts.ts` is outside the ticket's scope.

**Flow-back / routing (not blockers):** the neutral-state copy is a ux-designer SCR-005 entry (with
049's flow-back note); TC-RES-037/038 are the RES plan's next free IDs and still need mirroring into
`.pipeline/testing/stock-research.md` (test-planner lane — outside this ticket's scope).

**CI (PR run 39796697047 pre-rebase, 39801994236 post-rebase — identical results):** Backend
**pass** (2m56s / 2m20s), Web **pass** (1m10s / 1m31s), E2E **fail** — one pre-existing dev failure,
not this branch: `tests/e2e/src/global-ux.spec.ts:97 TC-XC-004` (SCR-002 scroll preservation after a
language switch) asserts `|Δscroll| < 60` and receives **157**, identically on re-run, and identically
on `dev` at the base commit `395b81a` (run 39793314149) — every dev `ci.yml` run since 2026-10-20 has
ended `failure`. The other 75–76 e2e specs pass, all stock-page specs included; the failing spec and
SCR-002 are outside this ticket's scope → reported for the constructor/fixer lane, never patched around
here.

### Closure + live criterion — armed (2026-10-22)
- Merged as PR #121 after a clean round-1 review (dual-lane: diff + security, zero critical/high; core honesty property verified — the never-paid wording cannot render over an unbacked signal). Routing leftovers: TC-RES-037/038 test-plan mirroring (test-planner), neutral-wording polish + coverage-boundary wording clash (ux flow-back), DR-1 cursor-path API case, SEC-1 unindexed ledger query (low).
- Live criterion (post-merge, needs the merged code deployed): a no-coverage stock shows the neutral 'Temettü verisi yok' state; a zero-dividend company with a completed dividends run (run 172 walked the full universe 2026-10-22) shows the backed never-paid wording. The API force-recreate happens after tonight's EOD chain (pull-freeze until ~21:00 UTC); observation follows the redeploy.
