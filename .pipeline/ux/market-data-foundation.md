# Admin Dashboard Specification — SCR-012

**Domain:** market-data-foundation · **Screen:** SCR-012 · **Prepared:** 2026-10-06
**Traces:** UC-MDF-001 (alternate b + ops side), UC-MDF-002, UC-MDF-004, UC-MOV-005, UC-SCR-004, UC-RES-004, UC-VAL-003, UC-FDF-002 (ops side), UC-FDF-003; FR-MDF-010/011/012/016, FR-FDF-004, FR-SCR-017, FR-RES-020/021 · BR-MDF-007, BR-RES-002/003, BR-VAL-008, BR-ACC-002/008 (posture) · SC-008/OBJ-005 (stats panel — recorded exception, mirrors architecture `03` §10) · architecture AD-13 (v1.1)
**Global rules applied:** UXR-G-001–030 (esp. G-001/002 pending feedback, G-003/004 errors & input preservation, G-005/006 empty & no-data, G-007/008 as-of & stale, G-013/014 language, G-018–020 form safety, G-021 in-session input preservation, G-025–028 responsive/a11y)
**Status:** post-Gate-4 amendment (2026-10-06) — added by builder instruction via architecture AD-13; change records in `00-screen-inventory.md` §6.5 and `99-ux-audit.md` §8. Flag rulings applied 2026-10-06: all five confirmations kept; stats panel kept (Should); UXR-MDF-020 kept and **confirmed via architecture v1.2**; UXR-MDF-015 amended to in-progress run-ledger auto-refresh and UXR-MDF-028 (manual refresh) added.

## 1. Purpose

The builder's single operational surface for running the platform: see pipeline health at a glance (last runs, per-data-type freshness, description coverage, open quarantine count), review and dismiss quarantined facts with a recorded note, inspect coverage, monitor and trigger ingest jobs (including historical backfill), review/edit/publish business descriptions, hide screener metrics with inadequate coverage, regenerate DCF baselines, and read aggregate-only usage counts. Before this amendment these were headless or CLI operations; UC-MDF-002, UC-MDF-004, UC-MOV-005, UC-SCR-004, UC-RES-004, UC-VAL-003, and UC-FDF-003 now have their operational surface here, while their end-user-visible effects remain specified on the research screens (inventory §5).

## 2. Entry / Exit

**Entry:** an admin entry point in the global navigation, presented only when the signed-in account holds the builder role (UXR-MDF-001); direct URL navigation (non-builders receive the access-denied state, UXR-MDF-002).

**Exit:** any public screen via global navigation; from a summary line into its detail (open-quarantine count → quarantine queue, UXR-MDF-006); from a quarantine item into the run ledger filtered to that job (UXR-MDF-010). Queue filters, active section, and any unsaved description-editor text are preserved across in-session navigation (UXR-G-021).

## 3. Information Architecture

The admin area is **one screen with eight content sections** (structure only; whether they render as tabs, an accordion, or one scrolling page is a design decision — same posture as Stock Page §6.1):

1. **Ops summary (one-glance)** — per-job last run, per-data-type freshness/staleness, description coverage counts, open-quarantine count.
2. **Quarantine review queue** — open and dismissed validation-failure items; dismiss with required note.
3. **Coverage report** — per-instrument and per-fund coverage metadata (data types present, from-when, listing date, gaps).
4. **Ingest run ledger + job triggers** — run history filtered by job/status; trigger a job run; backfill mode with a start date.
5. **Description review & publication** — queue by status (draft / reviewed / published); bilingual editor; publication gate.
6. **Screener metric visibility** — the 18 screenable metrics with their visibility state and a hide/unhide toggle.
7. **DCF baseline regeneration** — trigger rebuild from canonical facts (version bump).
8. **Aggregate stats** — registered accounts, verified accounts, saved screens, DCF scenarios (aggregate-only).

## 4. Interactions

| ID | Requirement | Traces to | MoSCoW |
|----|-------------|-----------|--------|
| UXR-MDF-001 | The admin area's entry point is presented in the global navigation only when the signed-in account holds the builder role. | Architecture AD-13 + `01` §Identity (builder role); BA silent — flagged | Must |
| UXR-MDF-002 | Direct navigation to the admin route by anyone not holding the builder role (anonymous or regular account) presents a plain-language access-denied state with a path back to platform content — never a blank page or a raw technical error. | AD-13; NFR-ACC-002 (spirit) | Must |
| UXR-MDF-003 | The ops summary displays, per ingest job, the status and completion time of that job's most recent run. | UC-MDF-001 (ops), UC-MOV-005; AD-13 | Should |
| UXR-MDF-004 | The ops summary displays, per data type, the date of its last successful ingest, with a visible stale indication when that data type is currently served as last-known-good. | FR-MDF-016, UC-MOV-005, UC-MDF-004; AD-13 | Should |
| UXR-MDF-005 | The ops summary displays description coverage as counts: published, draft, and universe total. | UC-MDF-004; AD-13 | Should |
| UXR-MDF-006 | The ops summary displays the count of open quarantined facts, and the count links into the quarantine review queue. | FR-MDF-012, AD-13 | Should |
| UXR-MDF-007 | The quarantine queue lists open quarantined facts, each showing its job, source reference, reason code with a plain-language explanation, and quarantine date, with the rejected payload inspectable. | UC-MDF-001 alternate b, FR-MDF-012; AD-13 | Should |
| UXR-MDF-008 | Dismissing a quarantined fact requires a non-empty resolution note (inline validation per UXR-G-018/019) and moves the item to the dismissed state; a dismissal records the accepted gap — never a silent drop. | BR-MDF-007, UC-MDF-001 alternate b; AD-13 | Should |
| UXR-MDF-009 | Dismissed quarantined facts remain reviewable, each with its resolution note. | BR-MDF-007, UC-MDF-001 alternate b; AD-13 | Should |
| UXR-MDF-010 | Each quarantine item offers navigation to the run-ledger view filtered to that item's job, from which the job can be re-triggered after a fix (the re-ingestion path). | UC-MDF-001 alternate b; architecture `03` §8 (re-ingestion note) | Should |
| UXR-MDF-011 | The coverage report displays, per instrument, which data types exist and from what date, together with the instrument's listing/IPO date; coverage gaps are shown explicitly, never omitted. | FR-MDF-011, BR-MDF-007, NFR-MDF-003, UC-MDF-004 | Must |
| UXR-MDF-012 | The coverage report displays, per fund, which data types exist and from what date, including holdings availability. | FR-FDF-004, UC-FDF-003, NFR-FDF-001 | Should |
| UXR-MDF-013 | The coverage report lets the builder switch between the instrument scope and the fund scope. | UC-MDF-004, UC-FDF-003; architecture `03` §8 (`scope` parameter) | Should |
| UXR-MDF-014 | The run ledger lists ingest runs with job, status, and timing, filterable by job and by status. | UC-MDF-001 (ops), UC-MOV-005, UC-FDF-002 (ops); architecture `03` §8 | Should |
| UXR-MDF-015 | While any ingest run is queued or running, the run ledger refreshes its data periodically without user action, and the view carries a visible indication that it updates periodically. | Builder decision 2026-10-06 (flag ruling 4 — auto-refresh); recorded scoped exception to UXR-G-012; polling interval is an implementation concern bounded by the `/admin` rate limit (architecture `03` §12) | Should |
| UXR-MDF-016 | The builder can trigger any ingest job run from the admin area; the action requires an explicit confirmation that names the job before the run starts. | UC-MDF-002, FR-MDF-010; confirmation UX-lane — flagged | Must |
| UXR-MDF-017 | A job trigger can specify backfill mode with a start date, and the confirmation states the job and the backfill start date. | UC-MDF-002, FR-MDF-010 | Must |
| UXR-MDF-018 | The description review queue lists business descriptions filterable by status (draft / reviewed / published), each showing its stock and status. | UC-RES-004; architecture `03` §8 | Must |
| UXR-MDF-019 | The description editor lets the builder edit and save both language versions (TR and EN) of a description; on save failure both texts are preserved exactly as entered (UXR-G-004). | FR-RES-021, UC-RES-004, BR-RES-002 | Must |
| UXR-MDF-020 | The description editor presents each description's KAP source references alongside the editable texts. | UC-RES-004 step 2; Business description entity (`source KAP references`) — builder-approved 2026-10-06; **satisfied via architecture v1.2** (`GET /admin/descriptions` response sketch serves `sourceRefs`, `03` §8) | Should |
| UXR-MDF-021 | The publish control for a description is available only when both language versions are non-empty, and its disabled state explains that both languages are required. | FR-RES-020, BR-RES-003, UXR-G-019 | Must |
| UXR-MDF-022 | Publishing a description requires an explicit confirmation stating that the description will become publicly visible on that stock's page. | UC-RES-004 step 4, FR-RES-020; confirmation UX-lane — flagged | Must |
| UXR-MDF-023 | The metric visibility panel lists the 18 screenable metrics with their current visibility state (screenable / hidden) and lets the builder toggle each metric's visibility. | FR-SCR-017, UC-SCR-004 | Should |
| UXR-MDF-024 | Toggling a metric's visibility requires a confirmation that states the user-visible consequence: hidden metrics disappear from the screener's criteria builder, and saved screens referencing them report the dropped criterion on re-run. | FR-SCR-017, UC-SCR-004, UC-SCR-003a; confirmation UX-lane — flagged | Should |
| UXR-MDF-025 | The builder can trigger DCF baseline regeneration; the action requires a confirmation stating that baselines are rebuilt from canonical facts and versioned. | UC-VAL-003, BR-VAL-008; architecture `03` §8 | Should |
| UXR-MDF-026 | The stats panel displays aggregate counts only — registered accounts, verified accounts, saved screens, and DCF scenarios — and no per-user or per-account data of any kind (no lists, no per-user breakdowns). | SC-008, OBJ-005, BR-ACC-002, BR-ACC-008; AD-13 — no UC/FR trace (recorded exception, mirrors architecture `03` §10) | Should |
| UXR-MDF-027 | Every admin list surface (quarantine queue, coverage report, run ledger, description queue) shows an explicit empty state naming the condition when it legitimately has no items. | UXR-G-005 (scoped restatement) | Must |
| UXR-MDF-028 | The ops summary and the run ledger each offer a manual refresh action that re-fetches their data at any time. | UX-lane baseline; architecture `03` §13 (`/admin` is `no-store`) | Should |

**Form-safety notes.** All mutations follow the Gate 2 lifecycle and their matrix rows (`01-interaction-rules.md` §2): pending indication and trigger lock (UXR-G-002/020), plain-language errors with retry (UXR-G-003), input preservation on failure (UXR-G-004). The dismiss-note field and the backfill start date validate inline (UXR-G-018). Confirmations are focus-trapping dialogs with a cancel path (UXR-G-022 pattern, UXR-G-027).

## 5. States

- **Initial / loading:** visible loading indication per section (UXR-G-001); sections render and fail independently — one section's error never blanks the screen (SCR-001 pattern).
- **Access denied:** non-builder navigation presents the plain-language denied state with a path back to content (UXR-MDF-002).
- **Populated:** per section — summary values with their dates; queues and ledgers with rows; editor with both language texts.
- **In-progress monitoring:** while any ingest run is queued or running, the run ledger updates periodically with a visible periodic-update indication (UXR-MDF-015).
- **Empty:** per UXR-MDF-027 — e.g., no open quarantined facts (the healthy steady state), no runs for the selected filter, no descriptions in the selected status.
- **Error:** per-section plain-language error with retry (UXR-G-003); other sections remain usable.
- **Mutation pending:** per-action pending indication and lock (UXR-G-002/020) — job trigger, dismiss, description save, publish, metric toggle, baseline regeneration.
- **Mutation error:** plain-language error + retry; affected input preserved — dismiss note, backfill date, both description texts (UXR-G-004).
- **Stale-data:** the summary's per-data-type staleness display (UXR-MDF-004) is itself the honest-data surface for pipeline outages; admin views are always fetched fresh (architecture `no-store`) so no admin view serves cached stale content.

## 6. Server vs UI state

| State | Owner | Lifetime | Source of truth | Sync / invalidation |
|-------|-------|----------|-----------------|--------------------|
| Ingest run ledger | Server | Append-only, for the life of the platform | `ingest_runs` (architecture) | Triggered runs appear in the open ledger view without manual refresh (matrix) and update periodically while non-terminal (UXR-MDF-015) |
| Quarantined facts (open/dismissed + notes) | Server | Exception-driven, retained | `quarantined_facts` (architecture) | Dismissals move items and update the summary's open count without manual refresh (matrix) |
| Coverage metadata (instrument + fund) | Server | Written by adapters during backfill/refresh | `coverage_metadata` (FR-MDF-011, FR-FDF-004) | Read-only here; reflects the latest runs when loaded |
| Business descriptions (texts, status) | Server | Versioned; published current version served | `business_descriptions` (UC-RES-004) | Edits save in place; publication changes status and is served on SCR-005 on next load (matrix) |
| Screener metric visibility flags | Server | Until toggled | metric catalog `is_screenable` (FR-SCR-017) | Toggles reflect in SCR-003's criteria builder on next load (matrix) |
| DCF baselines | Server | Versioned; one active per stock | `dcf_baselines` (UC-VAL-003, BR-VAL-008) | Regeneration bumps version; SCR-006 serves the new active baseline on next load (matrix) |
| Aggregate stats | Server | Computed aggregate | counts over accounts/screens/scenarios (AD-13) | Read-only; no per-user data exists to display (UXR-MDF-026) |
| Queue filters (quarantine status, run job/status, description status, coverage scope), active section | UI | Session | User selection | Preserved across in-session navigation (UXR-G-021) |
| Unsaved description-editor text | UI | Session draft | User input | Preserved on failure (UXR-G-004) and across in-session navigation (UXR-G-021) |
| Language | Global | Per device / per account | UXR-G-014 | — |

## 7. Data-heavy surfaces

- **Run ledger:** unbounded over time — filterable by job and status (UXR-MDF-014); loading, error, empty-for-filter states; each run shows job, status, timing. Pagination is not specified by the BA or architecture — recorded as a proposed enhancement in the audit, not spec'd.
- **Coverage report:** bounded by the universes (~100 instruments; fund universe per BR-FDF-006); explicit gap display (UXR-MDF-011/012); no BA-specified sorting/search — symbol search recorded as a proposed enhancement, not spec'd.
- **Quarantine queue:** expected small and exception-driven (architecture note — sustained growth is itself an anomaly signal); open/dismissed views (UXR-MDF-007/009).
- **Description queue:** bounded by the covered universe; status filter (UXR-MDF-018).
- **Freshness framing:** every displayed date is explicit (UXR-G-007). The run ledger auto-refreshes while any run is in progress, with a visible periodic-update indication (UXR-MDF-015 — recorded scoped exception to the UXR-G-012 posture, builder decision 2026-10-06); the ops summary does not auto-refresh — it reflects completed runs when next loaded or manually refreshed (UXR-MDF-028).

## 8. Responsive behavior

All sections remain present and fully operable at narrow viewport widths (UXR-G-025); the ledger, coverage, and queue tables degrade in presentation without losing access to any row, date, marker, or action; confirmations remain reachable and focus-trapped.

## 9. Accessibility

- All actions (trigger, dismiss, save, publish, toggle, regenerate, refresh) are keyboard operable with visible focus (UXR-G-026/027); confirmations are focus-trapping dialogs with clear consequence text and a cancel path.
- Run status, staleness, and visibility state are conveyed by text (not color alone); semantic table structures with labeled columns (UXR-G-028).
- Reason codes render with a plain-language explanation (UXR-MDF-007); dates and counts are text.
- The run ledger's periodic-update indication is textual; auto-refresh never moves keyboard focus and does not announce every poll to assistive technology (updated content remains discoverable on demand).

## 10. Critical flows

1. **Backfill a data type.** Given the builder needs statement history re-run, when they trigger the statements job in backfill mode from a start date and confirm (job and date stated), then the run appears in the ledger without a manual refresh, the ledger continues to update periodically while the run is in progress (UXR-MDF-015), and the completion is reflected in the summary's last-run and freshness for statements when the summary is next loaded.
2. **Review and dismiss a quarantined fact.** Given an open quarantined fact, when the builder inspects its payload and dismisses it with a note, then the item leaves the open queue, the summary's open-quarantine count updates without manual refresh, and the item remains viewable as dismissed with its note.
3. **Quarantine → fix → re-ingest.** Given a quarantined fact caused by a source issue, when the builder navigates from the item to its job's run-ledger view and re-triggers the job after the fix, then a new run appears in the ledger and its outcome is visible there.
4. **Publish a description.** Given a draft with both language versions filled, when the builder publishes it (confirmation states public visibility), then the description's status becomes published, the summary's coverage counts update without manual refresh, and the stock's page serves the description with its last-reviewed date on next visit — its "description in preparation" state is gone.
5. **Publication gate holds.** Given a draft with an empty EN text, when the builder views the publish control, then it is unavailable with an explanation that both languages are required — and a server-side rejection, should one occur, is presented in plain language with both texts preserved.
6. **Hide a metric for inadequate coverage.** Given ROE coverage is inadequate, when the builder hides ROE (confirmation states the consequences), then the screener's criteria builder no longer offers ROE when next loaded, and a saved screen referencing ROE reports the dropped criterion on its next re-run.
7. **Detect staleness at a glance.** Given a macro source has failed, when the builder opens the admin summary, then that data type shows its last-success date with a stale indication, and the builder can proceed into the run ledger for that job.
8. **Non-builder access denied.** Given a signed-in account without the builder role, when the user navigates to the admin URL, then a plain-language access-denied state appears with a path back to platform content.
