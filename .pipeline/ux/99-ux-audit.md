# UX Quality Audit — Gate 4

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** ux-designer · **Date:** 2026-10-06
**Scope audited:** `.pipeline/ux/` in full — 00-screen-inventory.md (12 screens after the 2026-10-06 SCR-012 amendment), 01-interaction-rules.md (30 global rules + 27-row mutation matrix after the amendment), market-overview.md, stock-screening.md, stock-research.md, valuation-dcf.md, user-accounts.md, market-data-foundation.md (156 numbered UXRs total: 128 at Gate 4 + 28 added with SCR-012 after the flag rulings).
**Status:** ✅ all three flagged decisions resolved by the builder (2026-10-06) — see §2. Specs amended in place; change-propagation recorded in §6. **Post-Gate-4 amendment (2026-10-06):** SCR-012 Admin Dashboard added per builder instruction via architecture AD-13 — audited in §8; §1–§7 below remain the Gate 4 record for the original 11 screens.

---

## 1. Failure-catalog audit — all screens

Each row: failure type → finding → resolution. "Fixed in spec" = the spec contains testable requirements that prevent the failure.

| Failure type | Finding | Resolution |
|---|---|---|
| **Static-page failure** (disconnected pages) | The core loop is wired: dashboard → sector/stock/screener (UXR-MOV-002/004/011); screener results → stock pages (UXR-SCR-007); stock page → DCF (UXR-RES-023); header search everywhere (UXR-RES-007); auth hops return to work (UXR-G-024). Every screen defines entry/exit and context preservation. | **Fixed in spec** |
| **Stale-state failure** (mutation leaves UI stale) | All 20 mutations carry explicit refresh/invalidation contracts (matrix §2 of 01-interaction-rules.md): saved screens/scenarios appear in lists and pickers without manual refresh; renames update in place; header auth/language state updates immediately; account deletion ends the session and removes items. | **Fixed in spec** |
| **Missing-feedback failure** | UXR-G-001/002 (loading + pending indication on every load and async action) plus per-screen pending states; mutation triggers lock while pending (UXR-G-020). | **Fixed in spec** |
| **Error-state failure** | UXR-G-003/004 (plain-language errors, retry, input preservation) plus: dashboard blocks fail independently (one block's error never blanks the screen); stock-page sections render/fail independently; DCF explains missing inputs instead of computing (UXR-VAL-009); auth errors are generic where required (UXR-ACC-008) and enumeration-neutral (UXR-ACC-012). | **Fixed in spec** |
| **Empty-state failure** | Explicit empty states everywhere data can legitimately be absent: screener zero-matches with relax suggestion (UXR-SCR-006); stock list no-match (UXR-RES-004); saved screens/scenarios empty + anonymous states (UXR-SCR-012/018, UXR-VAL-019/020); description-in-preparation (UXR-RES-011); no-data sections with coverage boundary (UXR-RES-022); macro unavailable (UXR-MOV-009); inflation-pair degradation note (UXR-MOV-008). | **Fixed in spec** |
| **Navigation failure** (lost context) | UXR-G-021 preserves criteria, assumptions, filters, search terms, and active section across in-session navigation; auth hops preserve and return work (UXR-G-024, UXR-SCR-010, UXR-ACC-005/009); sector links pre-filter the stock list (UXR-RES-006). | **Fixed in spec** |
| **Form-state failure** (lost input) | Input preservation on failure (UXR-G-004); inline validation with visible rules (UXR-G-018); disabled/locked submits (UXR-G-019/020); duplicate-name rejection without data loss (UXR-G-029); DCF unsaved-changes guard with confirm-before-discard (UXR-VAL-014). | **Fixed in spec** |
| **Responsive failure** | UXR-G-025 plus per-screen responsive sections; tables degrade without losing data, markers, or actions; the vs.-inflation pair and honest-data markings survive narrow widths. | **Fixed in spec** |
| **Accessibility failure** | UXR-G-026/027/028 plus per-screen a11y: keyboard operability, visible/managed focus, textual (non-color) markers for change direction, restatement warnings, and the DCF verdict; focus-trapping confirmations. | **Fixed in spec** |
| **Consistency failure** | Save-screen and save-scenario flows are parallel end-to-end (prompt → preserve → return → save → manage → delete-with-confirmation → duplicate-name rejection); canonical metric equality across surfaces (UXR-G-011); honest-data rules are global, not per-screen. Two **accepted asymmetries**, mirroring BA MoSCoW, documented not defects: saved-screen management is Must while scenario management is Should; My Saved Screens is primary navigation while My DCF Scenarios is reached via Account Settings (FR-ACC-005's entry points). | **Fixed in spec** (asymmetries documented) |

**Verdict: no unresolved failure-catalog defects.** The spec cannot be satisfied by an implementation of disconnected static pages with stale state.

## 2. Flagged decisions — all RESOLVED by the builder (2026-10-06)

| # | Item | Decision | Spec effect |
|---|------|----------|-------------|
| OQ-UX-001 | **DCF parameter set** (ASM-007, BR-VAL-007, OQ-VAL-001) | **Confirmed as proposed:** 8 parameters — base FCF, explicit-period growth, projection horizon, terminal growth, discount rate, net debt, cash, diluted share count; sensitivity axes **discount rate × terminal growth**. | valuation-dcf.md §4.1 marked CONFIRMED; UXR-VAL-003/006 updated. Closes ASM-007's design gate — the finalized list is recorded. |
| OQ-UX-002 | **Vs.-sector strip metric set & basis** (OQ-RES-002 residual) | **Confirmed as proposed:** all five valuation-family metrics; sector-level peers; current basis; inline loss-maker-exclusion disclosure; plus the honesty details — peer count displayed (UXR-RES-024) and not-meaningful states for the stock's own values (UXR-RES-025). | stock-research.md §4.2 marked CONFIRMED; UXR-RES-013 amended; UXR-RES-024/025 added (Should). |
| OQ-UX-003 | **What e-mail verification gates** (BA silent) | **Option B — gate persistence:** saving screens and DCF scenarios requires a verified e-mail; all non-persistence capabilities (browsing, running screens, DCF calculator, signing in) remain available to unverified accounts. | UXR-G-030 added (global gate rule, Must); UXR-ACC-005/006/007/016 amended; UXR-ACC-021 (resend verification, Should) added; UXR-SCR-019 (Must) and UXR-VAL-021 (Should) added; the register-from-save-prompt critical flows in stock-screening.md, valuation-dcf.md, and user-accounts.md rewritten to include the verify step; mutation matrix updated (verify row amended, resend row added). ⚑ **Deviates from the BA package's implied flow** (UC-ACC-001 main flow + FR-ACC-010 return-to-save) — brief-delta line recommended; the resend control is a necessary consequence (a lost first e-mail would otherwise permanently block saving). |

## 3. UX-lane additions beyond the BA package (all user-approved or flagged baselines)

1. **Scenario rename/delete** (UXR-VAL-017/018) — user-approved at Gate 1; **needs a brief-delta line** so architecture/test plans trace it.
2. **E-mail-verification gating of persistence** (UXR-G-030, UXR-ACC-007 as amended) **+ resend-verification control** (UXR-ACC-021) — user-approved at Gate 4 (OQ-UX-003 → B); deviates from the approved BA flow — **needs a brief-delta line**.
3. **Duplicate-name rejection** (UXR-G-029) — user-approved at Gate 2; brief-delta candidate.
4. **Deletion confirmation for saved screens/scenarios** (UXR-G-022 extension) and **form-safety, responsive, and accessibility baselines** (UXR-G-001/002/019/020/025–028, UXR-SCR-003) — UX-lane baselines where the BA is silent; each is individually testable and flagged in its spec; veto still possible at this gate.

## 4. Proposed enhancements — out of scope, NOT specified (per do-not-overdesign)

- **Sortable screener results** — BA specifies no result ordering; at ≤100 rows the screen works without it. If wanted, it is a small post-V1 addition.
- **Resend-verification-e-mail option** — the BA only sends on registration (FR-ACC-008); a resend control would be new scope (interacts with OQ-UX-003).
- **Screener result-diff / "why did results change" explanations** — BA explicitly out of scope this release (RISK-SCR-002 mitigation).
- **Password strength meter** — plain-language rules (UXR-ACC-003) already satisfy the BA; a meter is polish.

## 5. Traceability verification (both directions)

- **UC ↔ screen:** every user-facing UC maps to ≥1 screen; every screen traces to ≥1 UC — verified in 00-screen-inventory.md §4. ✓
- **UXR → UC/FR:** all 128 UXRs carry traces; UX-lane baselines are explicitly flagged as such. ✓
- **FR → UXR (user-facing FRs only):** spot-verified complete — MOV-001..013/016/017/020/021, SCR-001..017, RES-001..018/022/026/027, VAL-001..010, ACC-001..010, and MDF's display-side FRs (011/014/016/018/019) all land on ≥1 UXR. Non-user-facing FRs (ingestion, builder tooling, Won't items) correctly land nowhere. ✓
- **MoSCoW inheritance:** screen MoSCoW = highest hosted FR (inventory §2); per-UXR MoSCoW matches its FR trace, with user-approved additions marked. ✓

## 6. Change-propagation notice

The 2026-10-06 decision amendments (§2) were applied in place with IDs fixed: UXR-ACC-005/006/007/016 amended; UXR-G-030, UXR-ACC-021, UXR-SCR-019, UXR-VAL-021, UXR-RES-024/025 added; UXR-RES-013 amended; and the register-from-save-prompt critical flows in stock-screening.md, valuation-dcf.md, and user-accounts.md rewritten to include the verification step. Architecture and test-plan derivations from these UXRs must use the amended text. Any future change follows the same rule: IDs stay fixed, amendments are recorded, and downstream artifacts are re-validated — route changes through the builder, never edit silently.

## 7. Hand-off readiness

The UX package is complete and internally consistent: 11 screens, 128 testable requirements, a 21-row mutation contract the architecture must satisfy, and all flagged decisions resolved and recorded. **Next step: run the architect agent** — it consumes `.pipeline/ux/` as constraints (esp. the matrix's refresh/invalidation column, the confirmed DCF parameter set, the verification-gating rule UXR-G-030, and the canonical-metrics obligations).

*(Post-amendment note, 2026-10-06: the architect has since run and produced v1.1, whose builder-directed AD-13 admin-dashboard extension is the origin of SCR-012 — the UX package now matches the architecture on this point. The next step from here is the test-planner, which derives from the amended 12-screen package; see §8.)*

## 8. Post-Gate-4 amendment — SCR-012 Admin Dashboard (2026-10-06)

**Provenance.** During the architecture phase the builder directed an admin-dashboard extension (architecture AD-13, v1.1 — no new FRs; every piece traces to approved items). The builder then instructed the UX package to add the screen. SCR-012 is specified in `market-data-foundation.md` (one screen, eight sections — same posture as Stock Page §6.1 Option A); the inventory (§2/§3/§4/§5/§6.5), the mutation matrix (+6 rows), and the flagged-items list (§3 items 7–9 of `01-interaction-rules.md`) were amended in place with IDs fixed. SCR-001–011 and all existing UXR/G IDs are untouched.

**Failure-catalog audit — SCR-012:**

| Failure type | Finding | Resolution |
|---|---|---|
| Static-page failure | SCR-012 is wired into the app: navigation entry (builder-gated, UXR-MDF-001), summary lines linking into their queues (UXR-MDF-006/010), quarantine → run-ledger → re-trigger path (critical flow 3), exits to all public screens. | **Fixed in spec** |
| Stale-state failure | All six new matrix rows carry explicit refresh/invalidation contracts, including the public-surface effects: published descriptions served on SCR-005, hidden metrics reflected in SCR-003's criteria builder + dropped-criterion reporting on re-run, new baselines served on SCR-006 — all "on next load, no cache clearing, no manual refresh" of the admin view itself. | **Fixed in spec** |
| Missing-feedback failure | Global UXR-G-001/002 + per-mutation pending states and trigger locks (§5 of the spec); run-ledger auto-refresh while runs are in progress with a visible periodic-update indication, manual refresh elsewhere (UXR-MDF-015/028, builder decision 2026-10-06). | **Fixed in spec** |
| Error-state failure | Per-section independent errors (one section's failure never blanks the screen); access-denied state for non-builders (UXR-MDF-002); publication-gate rejection handled both client-side (disabled control) and server-side (plain-language error, input preserved). | **Fixed in spec** |
| Empty-state failure | Consolidated empty-state rule for all admin lists (UXR-MDF-027), including the healthy no-open-quarantine condition. | **Fixed in spec** |
| Navigation failure | Queue filters, active section, and unsaved editor text preserved across in-session navigation (UXR-G-021, §2 of the spec). | **Fixed in spec** |
| Form-state failure | Dismiss note + backfill date inline validation (UXR-G-018); both description texts preserved on any failure (UXR-MDF-019, matrix row); publish disabled while a language is empty (UXR-MDF-021). | **Fixed in spec** |
| Responsive failure | UXR-G-025 + §8 of the spec — ledger/coverage/queue tables degrade without losing rows, dates, markers, or actions. | **Fixed in spec** |
| Accessibility failure | Keyboard operability, focus-trapping confirmations, text-based status/staleness indicators, reason codes with plain-language explanations (§9 of the spec). | **Fixed in spec** |
| Consistency failure | Admin mutations reuse the global lifecycle and the established confirmation pattern (UXR-G-022 pattern); dismissal records a note rather than deleting (BR-MDF-007), matching the platform's recorded-gap posture; stats mirror the architecture's aggregate-only/KVKK-minimal constraint. | **Fixed in spec** |

**Traceability after the amendment.** UC ↔ screen: the builder-operational UCs (UC-MDF-002/004, UC-MOV-005, UC-SCR-004, UC-RES-004, UC-VAL-003, UC-FDF-003, ops sides of UC-MDF-001/UC-FDF-002) now map to SCR-012; pure pipeline UCs remain headless by design (inventory §4). UXR → UC/FR: 22 of the 28 new UXRs trace directly to UCs/FRs/BRs; UXR-MDF-027 restates global rule UXR-G-005; UXR-MDF-001/002/015/028 rest on builder decisions / UX-lane baselines rather than direct BA traces (builder-role gating, access-denied state, run-ledger auto-refresh, manual refresh); and the one recorded exception is UXR-MDF-026 (stats), tracing to SC-008/OBJ-005 — mirroring the architecture's own recorded exception for `GET /admin/stats`. FR → UXR: the builder-facing FRs that previously had no UI surface (FR-RES-020/021, FR-MDF-011, FR-MDF-010, FR-MDF-012, FR-SCR-017, FR-FDF-004) now land on SCR-012 UXRs. ✓

**Flag rulings — all resolved by the builder (2026-10-06):**

1. **Confirmations on consequential admin actions — KEPT** (all five UXRs: job trigger UXR-MDF-016, backfill UXR-MDF-017, publish UXR-MDF-022, metric toggle UXR-MDF-024, baseline regeneration UXR-MDF-025). UX-lane baselines; the BA is silent.
2. **UXR-MDF-026 stats panel — KEPT as Should**, with the SC-008/OBJ-005 trace exception (no UC/FR; mirrors the architecture's `GET /admin/stats` exception).
3. **UXR-MDF-020 (KAP source references in the description editor, Should) — KEPT; RESOLVED via architecture v1.2 (2026-10-06).** The architect added an additive response sketch for `GET /admin/descriptions` (`03` §8): each entry serves `sourceRefs` — `business_descriptions.source_refs_json` resolved against `kap_disclosures` (id, type, publish date, title, KAP URL); unresolvable ids serve a stub, never silently dropped; `PATCH` remains text-only so provenance is never hand-edited. No new endpoints, no schema change (`02` untouched); change record in `01` §13.1.
4. **UXR-MDF-015 — AMENDED to auto-refresh:** while any ingest run is queued or running, the run ledger refreshes periodically with a visible periodic-update indication — a recorded scoped exception to UXR-G-012 (annotated in place; the rule's force for market-data surfaces is unchanged). Manual refresh retained at all times via the new UXR-MDF-028. The ops summary deliberately does not auto-refresh (it reflects completed runs on next load or manual refresh) — consistent with the approved option's scope; extend on request.

**Proposed enhancements (out of scope, NOT specified):** run-ledger pagination; symbol search/filtering in the coverage report; a "reviewed" status-transition control for descriptions (no endpoint exists in the architecture — only edit and publish).

**Change propagation.** Architecture already records AD-13 (v1.1) — the UX amendment adds no architectural obligation beyond what v1.1 carries; the matrix's six new rows state the invalidation contracts the implemented architecture must already satisfy given `no-store` admin caching. **The one architect-routed open item (flag ruling 3, UXR-MDF-020) is CLOSED:** the architect's v1.2 amendment (2026-10-06) documents `GET /admin/descriptions` serving `sourceRefs` (`03` §8; change record `01` §13.1) — additive response documentation only. The UXR-G-012 annotation and UXR-MDF-015 amendment (auto-refresh) were verified client-side only — no architectural change (the `/admin` 60 req/min limit comfortably accommodates polling; conclusion recorded in the v1.2 change record). The architecture's three accuracy touches from v1.2 (inputs count 12 screens/156 UXRs; C1 "public screens" + SCR-012 tag; §8.3 SCR-012 note) are consistent with this package. Test-planner/planner have not yet run, so no downstream artifact requires re-validation — they derive from the amended UX package and **architecture v1.2**. A brief-delta line for the admin surface (BA lane) is still recommended, as it is a post-brief scope addition even though every function traces to approved items.
