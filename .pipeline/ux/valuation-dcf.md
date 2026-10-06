# Valuation & DCF Specifications — SCR-006 (DCF Calculator), SCR-011 (My DCF Scenarios)

**Domain:** valuation-dcf · **Prepared:** 2026-10-06
**Traces:** UC-VAL-001, UC-VAL-002; FR-VAL-001–010 · BR-VAL-001–010 · SD-001 · ASM-007
**Global rules applied:** UXR-G-001–029 (esp. G-003/004 error & input preservation, G-018/019/020 form safety, G-021 draft preservation, G-023/024 login-to-save, G-029 duplicate-name rejection)
**Status:** written under the standing no-stop instruction. DCF parameter set (§4.1) and sensitivity axes **confirmed by the builder (2026-10-06, OQ-UX-001)**. Scenario-save flow amended for e-mail-verification gating (2026-10-06, OQ-UX-003 → Option B).

---

# SCR-006 — DCF Calculator (Per Stock)

## 1. Purpose

The "value & decide" stage of the core loop: let any user (anonymous included) value a stock with **their own** assumptions — every model parameter visible and editable — and see, in plain language, how the current daily price compares to the resulting fair value (margin of safety), plus how sensitive that verdict is across an assumption grid. Traces to UC-VAL-001, UC-VAL-002; SC-004.

## 2. Entry / Exit

**Entry:** the Stock Page for that stock (FR-RES-018); later, direct re-entry (e.g., via My DCF Scenarios, which loads a scenario). Publicly usable without an account; saving requires one (BR-VAL-010).

**Exit:** back to the Stock Page; the sign-in/register prompt on save attempts (returning here with state preserved); My DCF Scenarios; global navigation. In-progress assumptions (and any entered scenario name) are preserved when navigating away and back within the session (UXR-G-021).

## 3. Information Architecture

1. **Stock context** — which stock is being valued; its current daily (EOD) price with as-of date (and stale marker if applicable).
2. **Parameter panel** — every model assumption, each with a visible, editable value; canonical-fact defaults are labeled as such (source/as-of; restatement markings carried in, UXR-VAL-010).
3. **Results** — computed fair value, the price comparison, and the plain-language verdict ("based on your assumptions", margin of safety as a percentage).
4. **Sensitivity table** — fair value across the assumption grid (Should).
5. **Scenario actions** — save (named), load (picker of this stock's scenarios).
6. **Informational-only disclaimer + "based on your assumptions" framing** (UXR-VAL-007).

### 4.1 — DCF parameter set — **CONFIRMED by the builder (2026-10-06, OQ-UX-001; closes ASM-007 / OQ-VAL-001)**

The complete user-editable parameter list for V1 (all visible, all editable per BR-VAL-002; canonical-fact defaults per BR-VAL-008):

| # | Parameter | Default source |
|---|-----------|----------------|
| 1 | Base free cash flow (starting FCF) | Canonical fact, editable |
| 2 | Growth rate during the explicit projection period | Baseline default, editable |
| 3 | Projection horizon (years) | Baseline default, editable |
| 4 | Terminal growth rate (after the explicit period) | Baseline default, editable |
| 5 | Discount rate (user's required return) | Baseline default, editable |
| 6 | Net debt | Canonical fact, editable |
| 7 | Cash & equivalents | Canonical fact, editable |
| 8 | Share count (diluted) | Canonical fact, editable |

Sensitivity grid axes (SD-001): **discount rate × terminal growth rate — confirmed (OQ-UX-001)**. This 8-parameter list is the finalized list recorded per BR-VAL-007. Interaction behavior below is parameter-list-agnostic: every entry in the finalized list is visible and editable (UXR-VAL-003).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-VAL-001 | A DCF calculator is reachable from every stock page in the covered universe. | FR-VAL-001, BR-VAL-001 | Must |
| UXR-VAL-002 | On open, the calculator loads the per-stock baseline defaults and immediately shows the baseline fair value, the current EOD price with its as-of date, and the plain-language comparison — before the user changes anything. | FR-VAL-002, FR-VAL-003; BR-VAL-003, BR-VAL-008 | Must |
| UXR-VAL-003 | Every model parameter in the confirmed list (§4.1) is visible and editable; no hidden, locked, or platform-secret assumptions exist. | BR-VAL-002, FR-VAL-004, ASM-007 (closed via OQ-UX-001) | Must |
| UXR-VAL-004 | Changing any parameter recomputes the fair value, the price comparison, the verdict, and the sensitivity table without a page reload, always reflecting the currently displayed inputs. | FR-VAL-005, mutation matrix | Must |
| UXR-VAL-005 | The verdict states, in plain language and as a percentage, how far the current price sits above or below the user's computed fair value (margin of safety), framed as "based on your assumptions" — never as the platform's estimate of true value. | FR-VAL-006, BR-VAL-004, BR-VAL-005, NFR-VAL-002 | Must |
| UXR-VAL-006 | The sensitivity table displays fair value across the assumption grid — axes: discount rate × terminal growth rate (confirmed, OQ-UX-001) — alongside the single-point result. | FR-VAL-007, BR-VAL-009, SD-001 | Should |
| UXR-VAL-007 | The informational-only disclaimer and the "based on your assumptions" framing are displayed on the calculator. | FR-VAL-008, BR-VAL-005 | Must |
| UXR-VAL-008 | Invalid parameter input (e.g., non-numeric or out-of-range) produces inline validation; outputs are not recomputed from invalid input, and the last valid result remains visible until the input is valid. | UXR-G-018/019, mutation matrix | Must |
| UXR-VAL-009 | Where the stock's statement inputs are missing, the calculator explains what is missing and does not compute on partial data. | UC-VAL-001a, BR-VAL-001 | Must |
| UXR-VAL-010 | Parameters whose defaults reflect restated figures carry the restatement marking (asterisk + footnote + warning) into the parameter display. | UC-VAL-001b, BR-MDF-011, FR-MDF-019 | Must |
| UXR-VAL-011 | A stale price is shown with its as-of date and a stale marker. | UC-VAL-001c, FR-MDF-016 | Must |
| UXR-VAL-012 | Saving persists the current parameter values as a named scenario, server-side per account and per stock; a duplicate name for that stock is rejected with an inline error (UXR-G-029). | FR-VAL-009, UXR-G-029, Gate 2 decision | Should |
| UXR-VAL-013 | An anonymous save attempt presents the sign-in / register prompt, and the user is returned to the calculator with assumptions and entered name preserved. | BR-VAL-010, FR-ACC-010, UXR-G-023/024 | Should |
| UXR-VAL-021 | A save attempt by a signed-in but unverified account is blocked with a plain-language verify-your-e-mail message and the verification path; the assumptions (and entered name) are preserved, and the save becomes available immediately after verification. | UXR-G-030, Gate 4 decision (OQ-UX-003 → B) | Should |
| UXR-VAL-014 | Loading a scenario restores its parameter values exactly as saved and recomputes all outputs from them; loading over unsaved changes requires an explicit confirmation that the draft will be discarded. | FR-VAL-010, UC-VAL-002, mutation matrix, UXR-G-022 pattern | Should |
| UXR-VAL-015 | The calculator provides a picker listing this stock's saved scenarios for loading. | FR-VAL-010, UC-VAL-002 | Should |

## 5. States

- **Baseline-loaded (initial):** per-stock defaults + immediate baseline result (UXR-VAL-002); no unsaved-changes indication.
- **Modified:** any parameter changed → outputs recompute live; the draft is indicated as modified (unsaved) relative to the baseline/loaded scenario.
- **Scenario-loaded:** assumptions exactly as saved; modification indication resets.
- **Validation-error (per parameter):** inline message; recompute suppressed; last valid result stays (UXR-VAL-008).
- **Missing-inputs:** explanation of what is missing; no computation (UXR-VAL-009).
- **Save/load pending:** pending indication and lock on the trigger (UXR-G-002/020).
- **Saved:** confirmation; scenario appears in picker and SCR-011 (mutation matrix).
- **Error (save/load):** plain-language error + retry; all inputs and the entered name preserved (UXR-G-003/004).
- **Save blocked (unverified account):** plain-language message that saving requires e-mail verification, with the verification path; assumptions and name preserved (UXR-G-030).
- **Stale / restated:** price as-of + stale marker (UXR-VAL-011); restatement markings on affected parameter defaults (UXR-VAL-010).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| DCF model baseline (per stock) | Server | Build-time, versioned | Valuation & DCF domain (builder-defined, UC-VAL-003) | Read-only to users; updates ship in build cycles; a loaded calculator always states which baseline it opened with |
| Saved scenarios | Server | Per account, per stock, until deleted | Valuation & DCF domain (FR-VAL-009) | Saves/renames/deletes reflect in the picker and SCR-011 without manual refresh (mutation matrix) |
| Active parameter values (draft) | UI | Session draft | User edits | Preserved across navigation and the auth hop (UXR-G-021); replaced only by loading a scenario or explicit reset to baseline |
| Current EOD price | Server | Daily | MDF canonical price (BR-VAL-003, NFR-VAL-003) | Displayed with as-of date; same value as the stock page |
| Computed outputs (fair value, verdict, sensitivity) | Derived | Ephemeral | Pure function of displayed inputs + canonical price | Recomputed on every parameter change (UXR-VAL-004); never persisted except as part of a saved scenario's inputs |

## 7. Data-heavy surfaces

- **Parameter panel:** 8 (proposed) labeled inputs with units; canonical defaults labeled with source/as-of; restatement markings where applicable; inline validation.
- **Sensitivity table (Should):** grid of fair values across the two axes; recomputes with every parameter change; current single-point result identifiable within the grid; degrades to a no-data state if not computable.
- **No pagination/sorting** — fixed structures. Freshness: the price is EOD with as-of date; outputs never imply precision beyond the user's own inputs and the canonical facts (NFR-VAL-004 — no black box).

## 8. Responsive behavior

Parameter panel, results, and sensitivity table remain fully operable at narrow widths (UXR-G-025); the sensitivity table degrades in presentation (e.g., scrollable/compact) without losing access to any cell; save/load actions remain reachable.

## 9. Accessibility

- Every parameter input is labeled with its name and unit (UXR-G-028); validation errors are textual and announced.
- The verdict is text, not color-only (e.g., "cheap/expensive" is never conveyed by color alone).
- Sensitivity table has row/column headers; the current-result cell is programmatically indicated.
- Keyboard operability and visible focus throughout (UXR-G-026/027).

## 10. Critical flows

1. **Value with own assumptions (UC-VAL-001, SC-004).** Given the user is on a stock page, when they open the DCF calculator, then it opens with baseline defaults and an immediately visible baseline result; when they change the discount rate, then fair value, comparison, verdict, and sensitivity recompute without a page reload; then the verdict states in plain language how far the current price sits from their fair value, framed as "based on your assumptions".
2. **Anonymous save → register → verify → return → save (UC-VAL-002, amended for verification gating).** Given anonymous assumptions and a chosen name, when the user activates save, then the sign-in/register prompt appears; when they register, then they return to the calculator with assumptions and name intact; when they attempt the save while unverified, then the verify-your-e-mail gate appears (UXR-G-030); when they verify their e-mail within the same session, then the save becomes available and can be completed.
3. **Load scenario exactly (FR-VAL-010).** Given a saved scenario and an unmodified calculator, when the user loads it, then every parameter restores exactly as saved and outputs recompute from those values.
4. **Unsaved-changes guard.** Given unsaved modifications, when the user loads a scenario, then a confirmation is required before the draft is discarded.
5. **Missing inputs (UC-VAL-001a).** Given a stock with missing statement inputs, when the user opens the calculator, then it explains what is missing instead of computing on partial data.
6. **Full core loop (SC-002).** Given an anonymous user on the dashboard, when they run a screener, open a result's stock page, study sections, and open the DCF calculator with their own assumptions, then they end with a plain-language verdict of price vs. their fair value — the complete discover → screen → research → value → decide chain without ever needing an account.

---

# SCR-011 — My DCF Scenarios

## 1. Purpose

Let a signed-in user find and manage their saved DCF scenarios across stocks — grouped by stock, each opening the calculator loaded with that scenario — making valuation work durable across sessions and devices. Traces to UC-VAL-002; FR-ACC-005's entry point; Gate 1 decision C.

## 2. Entry / Exit

**Entry:** Account Settings ("my DCF scenarios" entry point, FR-ACC-005); the DCF Calculator's scenario picker (cross-link). Signed-in only — anonymous direct access presents the sign-in path (UXR-VAL-019).

**Exit:** any stock's DCF Calculator (loaded with the selected scenario); Account Settings; global navigation.

## 3. Information Architecture

1. **Scenario list** — the user's saved scenarios grouped by stock, each with its name.
2. **Per-scenario actions** — open (loads the calculator), rename, delete (delete behind confirmation).
3. **Informational-only disclaimer** (UXR-G-016, SCR-011 included per Gate 1 decision C).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-VAL-016 | My DCF Scenarios lists the signed-in user's saved scenarios grouped by stock; activating a scenario opens that stock's DCF calculator loaded with it. | FR-VAL-009/010, FR-ACC-005, Gate 1 decision C | Should |
| UXR-VAL-017 | A scenario can be renamed in place; a duplicate name within the same stock is rejected with an inline error (UXR-G-029). | Gate 1 decision C, UXR-G-029 | Should |
| UXR-VAL-018 | A scenario can be deleted; deletion requires an explicit confirmation and removes it from the list and from the calculator's picker. | Gate 1 decision C, UXR-G-022, mutation matrix | Should |
| UXR-VAL-019 | Direct access while anonymous presents the sign-in path. | BR-VAL-010, BR-ACC-003 | Should |
| UXR-VAL-020 | When the signed-in user has no saved scenarios, an explicit empty state explains how to create one in the DCF calculator. | UXR-G-005 | Should |

## 5. States

- **Loading:** list load indication (UXR-G-001).
- **Populated:** scenarios grouped by stock with actions.
- **Empty:** guidance state per UXR-VAL-020.
- **Anonymous:** sign-in path (UXR-VAL-019).
- **Mutation pending / error:** per-action pending lock; plain-language error + retry; list unchanged; entered rename preserved (UXR-G-002/003/004/020).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Saved scenarios | Server | Per account, per stock, until deleted (or account deletion, BR-ACC-005) | Valuation & DCF domain | Saves from SCR-006, renames, and deletes here reflect in this list and the calculator picker without manual refresh (mutation matrix) |

## 7. Data-heavy surfaces

The scenario list is bounded by user behavior (no BA limit specified): loading, populated, empty, error with retry, per-row actions. Grouping by stock is the access path; no sorting/filtering/pagination is specified.

## 8. Responsive behavior

Grouped list and actions remain fully operable at narrow widths (UXR-G-025); per-scenario actions remain reachable without losing access to any action.

## 9. Accessibility

Per-row actions keyboard operable and individually labeled; delete confirmation is a focus-trapping dialog with consequence text and cancel path (UXR-G-022/027); group (stock) semantics labeled per UXR-G-028.

## 10. Critical flows

1. **Resume valuation work (UC-VAL-002).** Given a saved scenario, when the user opens it from My DCF Scenarios, then that stock's calculator opens with the scenario's assumptions restored exactly and outputs recomputed.
2. **Rename with collision.** Given two scenarios for the same stock, when the user renames one to the other's name, then an inline error identifies the collision and the original name is retained.
3. **Delete with confirmation.** When the user deletes a scenario, then a confirmation is required; on confirmation the scenario disappears from this list and from the calculator's picker.
