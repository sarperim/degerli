# Stock Screening Specifications — SCR-003 (Screener), SCR-004 (My Saved Screens)

**Domain:** stock-screening · **Prepared:** 2026-10-06
**Traces:** UC-SCR-001, UC-SCR-002, UC-SCR-003; FR-SCR-001–017 · BR-SCR-001–009
**Global rules applied:** UXR-G-001–029 (esp. G-005 empty state, G-007/008 as-of & stale, G-021 draft preservation, G-022 delete confirmation, G-023/024 login-to-save, G-029 duplicate-name rejection)
**Status:** written under the standing no-stop instruction. Save flow amended for e-mail-verification gating (2026-10-06, OQ-UX-003 → Option B).

---

# SCR-003 — Screener

## 1. Purpose

Let any user (anonymous included) find candidate stocks by building transparent, plain-language criteria across the five metric families and running them against the whole covered universe on the latest EOD data — the "screen" stage of the core loop and the platform's alternative to tip-following. Traces to UC-SCR-001, UC-SCR-002.

## 2. Entry / Exit

**Entry:** primary navigation; the dashboard's screener link (FR-MOV-013). Publicly usable without an account (BR-SCR-004).

**Exit:** any stock page (result rows); the sign-in/register prompt on save attempts (returning here with state preserved, UXR-G-024); My Saved Screens after saving; global navigation. The active criteria (and any entered save name) are preserved when leaving to a result row and returning (UXR-G-021).

## 3. Information Architecture

1. **Criteria builder** — metrics organized into the five families (Valuation, Quality, Growth, Financial Health, Dividends); each added criterion has a metric, a bound type (min / max / range), bound value(s), and — for growth metrics — a CAGR window (3Y/5Y/10Y).
2. **Run control** — executes the screen.
3. **Results** — matching stocks with each stock's value per selected criterion, the match count, the excluded-for-missing-data count, and the data-as-of date; each row links to the stock's page.
4. **Save control** — names and persists the active criteria set (signed-in; anonymous users are prompted).
5. **Informational-only disclaimer** (UXR-G-016; BR-SCR-008: no "best stocks" or ranking framing anywhere).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-SCR-001 | The criteria builder organizes the 18 screenable metrics into the five metric families and lets the user add criteria and set a minimum bound, a maximum bound, or both for each. | FR-SCR-001, FR-SCR-003, BR-SCR-002 | Must |
| UXR-SCR-002 | Each growth criterion carries a selectable CAGR window (3Y / 5Y / 10Y), which is part of the criterion. | FR-SCR-015, OQ-SCR-002, BR-SCR-005 | Must |
| UXR-SCR-003 | The user can remove any individual criterion and clear all criteria. | UX-lane baseline (criteria building, UC-SCR-001) | Must |
| UXR-SCR-004 | Running the screen evaluates all universe stocks with AND logic on canonical metrics and displays matching stocks with each stock's value per selected criterion, the match count, and the data-as-of date. | FR-SCR-002, FR-SCR-004, FR-SCR-005; BR-SCR-001, BR-SCR-003, BR-SCR-006 | Must |
| UXR-SCR-005 | Results display indicates how many stocks were excluded because they lack data for the selected criteria (excluded stocks never count as passing). | FR-SCR-014, BR-SCR-009 | Should |
| UXR-SCR-006 | When no stocks match, an explicit zero-results state is shown, stating that there are no matches and suggesting relaxing the criteria. | FR-SCR-006, UC-SCR-001a | Must |
| UXR-SCR-007 | Each result row links to that stock's page. | FR-SCR-004 | Must |
| UXR-SCR-008 | Saving persists the active criteria set (including CAGR windows) as a named screen, server-side per account; a duplicate name within the account is rejected with an inline error (UXR-G-029). | FR-SCR-007, UXR-G-029, Gate 2 decision | Must |
| UXR-SCR-009 | An anonymous save attempt presents the sign-in / register prompt. | FR-SCR-012, BR-SCR-004 | Must |
| UXR-SCR-010 | In-progress criteria and any entered screen name are preserved across the sign-in / register hop, and the user is returned to the screener with them intact. | FR-SCR-013, FR-ACC-010, UXR-G-024 | Should |
| UXR-SCR-011 | Metrics hidden for inadequate data coverage (builder decision) are not offered in the criteria builder. | FR-SCR-017, UC-SCR-004, OQ-SCR-003 | Should |
| UXR-SCR-019 | A save attempt by a signed-in but unverified account is blocked with a plain-language verify-your-e-mail message and the verification path; the criteria (and any entered name) are preserved, and the save becomes available immediately after verification. | UXR-G-030, Gate 4 decision (OQ-UX-003 → B) | Must |

## 5. States

- **Initial:** criteria builder empty (no criteria); no results yet — the screen explains that adding criteria and running produces matches.
- **Criteria built, not run:** criteria visible and editable; results area (if any prior run) remains from the last run.
- **Run pending:** visible pending indication on the run control (UXR-G-002), locked against re-invocation (UXR-G-020).
- **Results populated:** match count, per-criterion values, exclusion count (Should), as-of date; stale data marked (UXR-G-008).
- **Results empty:** zero-matches state per UXR-SCR-006 (distinct from error and from exclusion).
- **Save pending / saved:** pending lock on save control; success confirmation names the saved screen.
- **Save blocked (unverified account):** plain-language message that saving requires e-mail verification, with the verification path; criteria and name preserved (UXR-G-030).
- **Error (run or save):** plain-language error + retry; criteria (and name) fully preserved (UXR-G-003/004).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Saved screens | Server | Per account, until deleted | Stock Screening domain (SC-003) | List shown in SCR-004 reflects saves/deletes/renames without manual refresh (mutation matrix) |
| Active criteria set | UI | Session draft | User selection | Preserved across navigation and the auth hop (UXR-G-021); replaced when a saved screen is loaded or criteria cleared |
| Result set | Server-computed on demand | Ephemeral — never persisted | Canonical metrics at run time (UC-SCR-003; results are not stored entities) | Always recomputed on run/re-run; never served from a stored result set |
| CAGR window per growth criterion | Part of the criterion | Saved with the screen | — | Same canonical windows as stock pages (BR-SCR-005) |

## 7. Data-heavy surfaces

- **Results table (≤ 100 rows — the covered universe):** loading (UXR-G-001), populated, zero-matches empty state, exclusion count, error with retry, as-of date and stale marking. No pagination is required at this universe size; results are a filter of a 100-stock universe (BR-SCR-002 bounds the metric set; the universe bounds the rows). Results ordering is not specified by the BA — see audit (proposed enhancement, not spec'd).
- **Criteria builder:** the 18-metric catalog with family grouping, plain-language labels and units (NFR-SCR-002); hidden metrics excluded (UXR-SCR-011).
- **Cross-surface consistency:** a metric value in results equals the same metric on that stock's page for the same date (NFR-SCR-003, UXR-G-011).

## 8. Responsive behavior

Criteria builder and results remain fully operable at narrow widths (UXR-G-025); the results table degrades in presentation (e.g., prioritized columns) without losing access to any criterion value or row link.

## 9. Accessibility

- Criterion controls are labeled with the plain-language metric name and unit; min/max bound inputs are individually labeled.
- Results use semantic table structure with per-criterion column labels; the match count and exclusion count are text, not color-only.
- Run, save, and criteria-removal actions are keyboard operable; results arrival is perceivable (focus/announcement) for screen-reader users.

## 10. Critical flows

1. **Build & run anonymously.** Given no account and canonical metrics computed, when the user adds "P/E at most 15" and "ROE at least 15%", then runs the screen, then matching stocks are listed with each stock's P/E and ROE values, the match count, the exclusion count (Should), and the data-as-of date.
2. **Zero matches.** Given criteria that no stock satisfies, when the screen runs, then an explicit zero-results state appears suggesting relaxed criteria — not a blank list.
3. **Anonymous save → register → verify → return → save.** Given anonymous criteria and a chosen name, when the user activates save, then the sign-in/register prompt appears; when they register, then they are returned to the screener with criteria and name intact; when they attempt the save while unverified, then the verify-your-e-mail gate appears (UXR-G-030); when they verify their e-mail within the same session, then the save can be completed and the screen appears in My Saved Screens.
4. **Result → research → back.** Given results are shown, when the user opens a result row and returns, then the criteria and results are still present (UXR-G-021).
5. **Stale data.** Given a data outage at run time, when results render, then they carry the as-of date and stale marker.

---

# SCR-004 — My Saved Screens

## 1. Purpose

Let a signed-in user manage their named, server-persisted screens — re-run them on the latest data, rename, delete — making the screening workflow repeatable day over day. Traces to UC-SCR-003.

## 2. Entry / Exit

**Entry:** primary navigation (presented for signed-in users); Account Settings (FR-ACC-005); immediately after saving a screen on SCR-003.

**Exit:** the Screener (re-run presents results there; renaming may also happen inline); global navigation. Direct access while anonymous presents the sign-in path (UXR-SCR-012).

## 3. Information Architecture

1. **Saved-screens list** — the user's named screens.
2. **Per-screen actions** — re-run, rename, delete (delete behind confirmation).
3. **Informational-only disclaimer** (UXR-G-016).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-SCR-012 | My Saved Screens is presented in navigation for signed-in users; direct access while anonymous presents the sign-in path (research content never requires an account — the prompt is scoped to this feature). | BR-SCR-004, BR-ACC-003, UC-SCR-002a | Must |
| UXR-SCR-013 | The screen lists the signed-in user's saved screens by name. | FR-SCR-008 | Must |
| UXR-SCR-014 | Re-running a saved screen executes it against the latest EOD data (never a stored result set) and presents the results in the screener context. | FR-SCR-009, UC-SCR-003 | Must |
| UXR-SCR-015 | Re-running a saved screen that references a metric hidden for inadequate coverage runs the screen without that criterion and clearly reports which criterion was dropped. | UC-SCR-003a, FR-SCR-017 | Must |
| UXR-SCR-016 | Renaming a saved screen updates the name in place; a duplicate name within the account is rejected with an inline error (UXR-G-029). | FR-SCR-010, UXR-G-029 | Must |
| UXR-SCR-017 | Deleting a saved screen requires an explicit confirmation and, on confirmation, removes it from the list. | FR-SCR-011, UXR-G-022 | Must |
| UXR-SCR-018 | When the signed-in user has no saved screens, an explicit empty state explains how to create one in the Screener. | UXR-G-005, UC-SCR-003 | Must |

## 5. States

- **Loading:** list load indication (UXR-G-001).
- **Populated:** named screens with actions.
- **Empty (signed-in):** guidance state per UXR-SCR-018.
- **Anonymous:** sign-in path presented (UXR-SCR-012).
- **Mutation pending:** per-action pending indication and lock (UXR-G-002/020) for rename/delete/re-run.
- **Mutation error:** plain-language error + retry; list unchanged; any entered rename preserved (UXR-G-003/004).
- **Dropped-criterion notice:** after a re-run affected by a hidden metric, the dropped criterion is reported (UXR-SCR-015).

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Saved screens (list) | Server | Per account, until deleted | Stock Screening domain | Saves from SCR-003, renames, and deletes here reflect in the list and in SCR-003's active-screen display without manual refresh (mutation matrix) |
| Re-run results | Server-computed on demand | Ephemeral | Latest canonical metrics | Presented in the Screener context; never persisted (UC-SCR-003) |

## 7. Data-heavy surfaces

The saved-screens list is bounded by user behavior (no BA limit specified): loading, populated, empty, error with retry, per-row actions. No sorting, filtering, or pagination is specified by the BA; the list presents screens by name (FR-SCR-008).

## 8. Responsive behavior

List and actions remain fully operable at narrow widths (UXR-G-025); per-screen actions remain reachable (e.g., behind a compact control) without losing access to any action.

## 9. Accessibility

Per-row actions are keyboard operable and individually labeled; the delete confirmation is a focus-trapping dialog with clear consequence text and cancel path (UXR-G-022/027); list semantics per UXR-G-028.

## 10. Critical flows

1. **Re-run on fresh data.** Given a saved screen from a prior day, when the user re-runs it, then the screener presents results computed on the latest EOD data with the current as-of date.
2. **Dropped criterion.** Given a saved screen referencing a since-hidden metric, when the user re-runs it, then the screen runs without that criterion and clearly names the dropped criterion.
3. **Rename with collision.** Given two saved screens, when the user renames one to the other's name, then an inline error identifies the collision and the original name is retained.
4. **Delete with confirmation.** When the user deletes a saved screen, then a confirmation is required stating the screen will be removed; on confirmation the row disappears and the screen no longer appears in SCR-003/SCR-004.
