# UX Quality Audit — Gate 4

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** ux-designer · **Date:** 2026-10-06
**Scope audited:** `.pipeline/ux/` in full — 00-screen-inventory.md (11 screens), 01-interaction-rules.md (30 global rules + 21-row mutation matrix), market-overview.md, stock-screening.md, stock-research.md, valuation-dcf.md, user-accounts.md (128 numbered UXRs total after the 2026-10-06 decision amendments).
**Status:** ✅ all three flagged decisions resolved by the builder (2026-10-06) — see §2. Specs amended in place; change-propagation recorded in §6.

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
