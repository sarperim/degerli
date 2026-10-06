# UX Screen Inventory — Gate 1

**Project:** Değerli (working name) — BIST Value Investing Platform
**Prepared by:** ux-designer · **Date:** 2026-10-06
**Source inputs:** `.pipeline/00-project-brief.md` (approved) · `.pipeline/analysis/00-domain-map.md` + 7 domain reports (all APPROVED at BA Gate 2, 2026-10-06) · `.pipeline/analysis/brief-delta.md`
**Status:** ✅ GATE 1 APPROVED (2026-10-06). Decisions: §6.1 → Option A (one Stock Page screen, seven sections); §6.2 → kept as proposed; §6.3 → keep SCR-011 **and** add scenario rename/delete as Should-level requirements. §7 deferral of the two design decisions to their Gate 3 batches confirmed. **Amended 2026-10-06 (post-Gate-4):** SCR-012 Admin Dashboard added per builder instruction via architecture AD-13 — see §6.5.

**ID convention:** SCR-xxx IDs are permanent. Removed screens leave a gap; new screens take the next free number. Screen MoSCoW = the highest MoSCoW among the FRs the screen hosts (UCs carry no MoSCoW of their own; FRs do).

---

## 1. Method

Every user-facing use case across the seven domain reports was collected and grouped by the surface on which a user performs it. Non-user-facing UCs (system pipelines, builder-only operational UCs) have no screen; their **user-visible side effects** (as-of dates, stale markers, no-data states, hidden metrics, staged content) are specified as states on the screens that display them (§5).

The inventory below is the authoritative UX surface list. The domain map's "V1 Screen Inventory (UX plan)" was a pre-UX sketch; deviations from it are listed with rationale in §6 and require user approval at this gate.

## 2. Screens — 12 total (11 approved at Gate 1 + SCR-012 added by the 2026-10-06 amendment, §6.5)

| ID | Screen | Domain (spec file) | Purpose | Traces to | Entry points | MoSCoW |
|----|--------|--------------------|---------|-----------|--------------|--------|
| SCR-001 | Market Overview (home) | market-overview | The daily "discover" entry point: grasp today's BIST state (BIST 100/30, sectors, breadth, movers, volume, market valuation) and the six macro indicators in plain language, then jump into a stock, a sector, or the screener. | UC-MOV-001, UC-MOV-003, UC-MOV-004; FR-MOV-001–013, 020, 021 (FR-MOV-017 Could) | Platform root URL; primary navigation from every screen | Must |
| SCR-002 | Stock List (universe browser) | stock-research | Browse, sector-filter, and search (name/code) all covered BIST 100 stocks to pick one to research; also the landing surface for dashboard sector links and the global header search. | UC-RES-001; FR-RES-001–004, 017 | Primary navigation; dashboard sector links (FR-MOV-012); global header search results (FR-RES-004) | Must |
| SCR-003 | Screener | stock-screening | Build and run a screen across the five metric families with AND-combined min/max bounds, see matching stocks with their criterion values and the data-as-of date, and save the criteria as a named screen (saving requires an account). | UC-SCR-001, UC-SCR-002; FR-SCR-001–007, 012–016 (FR-SCR-013 Should) | Primary navigation; dashboard link (FR-MOV-013) | Must |
| SCR-004 | My Saved Screens | stock-screening | Manage the signed-in user's named screens: re-run against the latest EOD data, rename, delete. | UC-SCR-003; FR-SCR-008–011, 016 | Primary navigation (signed-in); Account Settings (FR-ACC-005); immediately after saving a screen on SCR-003 | Must |
| SCR-005 | Stock Page (per stock; 7 content sections) | stock-research | The research destination for one stock: understand the business (bilingual, builder-reviewed description) and study valuation (incl. historical + vs.-sector strip), financials, profitability, growth, balance sheet, and dividends — canonical, dated, honestly marked — then proceed to the DCF. | UC-RES-002, UC-RES-003, UC-RES-005; FR-RES-005–018, 026, 027 (FR-RES-007/022/027 Should) | Stock List; screener result rows (FR-SCR-004); dashboard gainers/losers (FR-MOV-011); global header search selection | Must |
| SCR-006 | DCF Calculator (per stock) | valuation-dcf | Value a stock with the user's own assumptions: adjust every model parameter from per-stock baseline defaults, see fair value vs. current daily price in plain language (margin of safety, "based on your assumptions"), explore the sensitivity grid, and save/reload named scenarios (saving requires an account). | UC-VAL-001, UC-VAL-002; FR-VAL-001–010 (FR-VAL-007/009/010 Should) | Stock Page (FR-RES-018) | Must |
| SCR-007 | Register | user-accounts | Create an account with minimal data (e-mail + password + bilingual privacy-notice consent) and be returned to in-progress work; hosts registration-completion and e-mail-verification outcome states (FR-ACC-008 link landing). | UC-ACC-001; FR-ACC-001, 008, 010 (FR-ACC-010 Should) | Header (anonymous); save prompt from SCR-003 / SCR-006 | Must |
| SCR-008 | Sign In | user-accounts | Authenticate with e-mail + password; the account's stored language preference is applied and the user is returned to their in-progress work; sign out ends the session. | UC-ACC-002; FR-ACC-002–004, 010 | Header; save prompt from SCR-003 / SCR-006; "already registered" path from SCR-007 | Must |
| SCR-009 | Password Reset | user-accounts | Regain access: request a reset link from the sign-in screen, then set a new password via the e-mailed time-limited link. | UC-ACC-004; FR-ACC-009 | Sign In ("forgot password"); e-mailed reset link (external entry point) | Must |
| SCR-010 | Account Settings | user-accounts | Manage the signed-in account: language preference, password change, entry points to saved screens and DCF scenarios, and account deletion (destructive, confirmed). | UC-ACC-003; FR-ACC-004–007 (FR-ACC-007 Should) | Header (signed-in) | Must |
| SCR-011 | My DCF Scenarios | valuation-dcf | The signed-in user's saved DCF scenarios grouped by stock, each opening the DCF Calculator loaded with that scenario — the destination of Account Settings' scenario entry point; rename/delete per Gate 1 decision. | UC-VAL-002; FR-VAL-009/010 (Should); FR-ACC-005 (entry point, Must) + Gate 1 decision C | Account Settings (FR-ACC-005); DCF Calculator scenario picker | Should |
| SCR-012 | Admin Dashboard (builder-only) | market-data-foundation | The builder's single operational surface: one-glance pipeline health (last runs, per-data-type freshness, description coverage, open-quarantine count), quarantine review with recorded dismissals, coverage report (instruments + funds), ingest run ledger with job/backfill triggers, description review/edit/publish, screener-metric visibility, DCF baseline regeneration, and aggregate-only usage stats. | UC-MDF-002, UC-MDF-004, UC-MOV-005, UC-SCR-004, UC-RES-004, UC-VAL-003, UC-FDF-003 (+ ops side of UC-MDF-001/UC-FDF-002); FR-MDF-010–012, 016; FR-FDF-004; FR-SCR-017; FR-RES-020/021; SC-008/OBJ-005 (stats panel, AD-13 exception) | Global navigation (builder role only, UXR-MDF-001); direct URL | Must |

SCR-011's scope note (scenario rename/delete as a UX-lane addition) is recorded in §6.3.

## 3. Global, non-screen surfaces (specified in `01-interaction-rules.md`, not as screens)

- **Language toggle (TR default / EN toggle)** — present on every screen; per-device persistence for anonymous users, account-stored preference applied on sign-in (BR-ACC-004). Traces: BR-MOV-006, BR-SCR-007, BR-RES-009, BR-VAL-006, BR-ACC-004/007.
- **Global stock search (header, every screen)** — FR-RES-004 (Should); results land on SCR-002 with the query applied, or directly on SCR-005 when a stock is selected. No separate search-results screen.
- **Authentication state display + sign in / sign out controls** — FR-ACC-002/003; visible on every screen.
- **Admin entry point — SCR-012 (added 2026-10-06, §6.5)** — presented in the global navigation only when the signed-in account holds the builder role (UXR-MDF-001; architecture AD-13); non-builder direct navigation receives an access-denied state (UXR-MDF-002).
- **Informational-only disclaimer** — research surfaces: SCR-001 (FR-MOV-020), SCR-003/004 (FR-SCR-016), SCR-002/005 (FR-RES-017), SCR-006 (FR-VAL-008); SCR-011 proposed for consistency (§6.3).
- **Honest-data display obligations** (as-of dates, stale markers, restatement asterisk/footnote/warning, adjusted-data disclaimers, no-data states with coverage boundaries) — display side of MDF/RES rules; realized per consuming screen in Gate 3 specs.

## 4. UC ↔ screen traceability (both directions)

| Use case | Screen(s) | | Use case | Screen(s) |
|---|---|---|---|---|
| UC-MOV-001 Check today's market state | SCR-001 | | UC-RES-001 Browse/find stocks | SCR-002 |
| UC-MOV-003 Compare official vs. independent inflation | SCR-001 | | UC-RES-002 Understand the company | SCR-005 |
| UC-MOV-004 Navigate from dashboard | SCR-001 (→ SCR-002/003/005) | | UC-RES-003 Study fundamentals | SCR-005 |
| UC-SCR-001 Build and run ad-hoc screen | SCR-003 | | UC-RES-005 Move to valuation | SCR-005 (→ SCR-006) |
| UC-SCR-002 Save a screen | SCR-003 (+ hop via SCR-007/008) | | UC-VAL-001 Value a stock | SCR-006 |
| UC-SCR-003 Re-run and manage saved screens | SCR-004 (+ results on SCR-003) | | UC-VAL-002 Save/reload a scenario | SCR-006 (+ SCR-011) |
| UC-ACC-001 Register | SCR-007 | | UC-ACC-002 Sign in / sign out | SCR-008 |
| UC-ACC-003 Manage account settings | SCR-010 | | UC-ACC-004 Recover access | SCR-009 |

**Builder-operational UCs (added with SCR-012, 2026-10-06 — §6.5):**

| Use case | Screen(s) | | Use case | Screen(s) |
|---|---|---|---|---|
| UC-MDF-001 alternate b — quarantine review (ops side: run-ledger monitoring) | SCR-012 | | UC-MDF-002 Backfill historical data | SCR-012 |
| UC-MDF-004 Validate source coverage & quality | SCR-012 | | UC-MOV-005 Detect macro failure/staleness | SCR-012 (+ stale display on SCR-001) |
| UC-SCR-004 Verify metric coverage | SCR-012 (+ hidden-metric effects on SCR-003/004) | | UC-RES-004 Produce/review/publish description | SCR-012 (+ published content on SCR-005) |
| UC-VAL-003 Define per-stock baselines | SCR-012 (+ defaults on SCR-006) | | UC-FDF-002 (ops side) / UC-FDF-003 fund refresh monitoring / coverage | SCR-012 |

Every screen traces to at least one UC; every user-facing UC maps to at least one screen; every builder-operational UC with a UI surface maps to SCR-012 (UC-ACC-005 KVKK review and the pure pipeline UCs — UC-MDF-003/005, UC-MOV-002, UC-FDF-001 — remain headless/CLI by design, architecture `03` §9–10). ✓ No orphan in either direction.

## 5. Builder/system UCs and where their effects land

**Amended 2026-10-06 (SCR-012):** these UCs are builder- or system-operated. Their **builder-operational side** now has a surface — SCR-012 — where one exists; their **end-user-visible side effects** remain specified as states on the research screens that display them and are unchanged by the amendment.

| Use case | Ops surface (SCR-012, where one exists) | User-visible effect | Landing screen(s) |
|---|---|---|---|
| UC-MDF-001/002 Daily refresh / backfill | Run ledger, job/backfill triggers, quarantine review | Data-as-of dates; stale markers on outage (FR-MDF-016) | All data-bearing screens |
| UC-MDF-004 + UC-SCR-004 Coverage validation | Coverage report, metric visibility panel | Metrics hidden when coverage inadequate (FR-SCR-017); no-data states with coverage boundaries (FR-RES-016); dropped-criterion reporting on re-run (UC-SCR-003a) | SCR-002/003/004/005 |
| UC-MOV-002/005 Macro ingest / failure detection | Ops summary (staleness), run ledger; macro jobs admin-triggerable | Last-known value with as-of date + stale marker; "independent measure unavailable" note (UC-MOV-003a) | SCR-001 |
| UC-RES-004 Produce/review/publish description | Description review queue, editor, publication gate | "Description in preparation" state (FR-RES-026); last-reviewed date (FR-RES-022); published description (FR-RES-020) | SCR-005 |
| UC-VAL-003 Define per-stock baselines | Baseline regeneration trigger | Calculator opens pre-loaded with baseline + immediate result (FR-VAL-003) | SCR-006 |
| UC-ACC-005 Pre-launch KVKK review | None (content gate on the privacy notice; runs before launch) | None | — |
| UC-FDF-001/002/003 Fund backfill/refresh/coverage | Fund job triggers, fund coverage report | None in V1 — headless by design (BR-FDF-001) | — |

## 6. Deviations from the domain-map sketch & flagged gaps

**6.1 Stock Page = one screen (SCR-005), not seven. — RESOLVED at Gate 1 (2026-10-06): Option A approved.** The domain map's sketch listed the seven stock-page sections as rows 5–11, while itself noting they are "content requirements" and that section layout (tabs vs. scroll) is a design decision. This inventory treats the Stock Page as **one screen hosting seven content sections**, because the user experiences it as one destination and cross-section coherence (as-of dating, restatement footnote at the bottom of the display, adjusted-data disclaimers, single disclaimer, DCF hand-off) is a core UX requirement. The Gate 3 spec will define each section's content, interactions, and states within SCR-005.

**6.2 SCR-009 Password Reset added. — RESOLVED at Gate 1 (2026-10-06): kept as proposed.** Not in the domain-map sketch (written before BA Gate 2), but UC-ACC-004 / FR-ACC-009 (password reset via e-mailed link) were approved as **Must** — the flow needs its own surface with an external entry point (the e-mailed link). E-mail verification (FR-ACC-008) is handled as a state within SCR-007 (link landing shows verification outcome); no separate screen proposed.

**6.3 SCR-011 My DCF Scenarios — RESOLVED at Gate 1 (2026-10-06): kept, and scenario rename/delete ADDED as Should.** FR-ACC-005 (Must) requires Account Settings to contain "entry points to saved screens **and DCF scenarios**"; FR-VAL-009/010 (Should) require saving/reloading named scenarios per account. The user approved keeping SCR-011 **and** adding scenario rename and delete as Should-level UX requirements for parity with saved screens (FR-SCR-010/011). ⚑ **Scope note:** rename/delete for scenarios has no FR in the approved BA package — it is a UX-lane addition approved by the builder at this gate. It should be recorded in the brief delta (builder's lane) so architecture and test plans pick it up; the UX spec carries it as UXR-VAL requirements tracing to UC-VAL-002 + this decision.

**6.4 No separate search-results screen.** Global header search lands on SCR-002 (query applied) / SCR-005 (direct selection). Avoids an invented page.

**6.5 SCR-012 Admin Dashboard added — post-Gate-4 amendment (2026-10-06).** At Gate 1 every builder-operational UC was deliberately headless (§1 Method: "non-user-facing UCs … have no screen"); their user-visible effects were specified as states on research screens (§5). During the architecture phase the builder directed an **admin dashboard extension** (architecture `01-system-architecture.md` AD-13, v1.1): a builder-only `/admin` area giving those operations a UI — ops one-glance summary, quarantine review with recorded dismissals, coverage report, run ledger with job/backfill triggers, description review/edit/publish, metric visibility, DCF baseline regeneration, and aggregate-only stats (SC-008/OBJ-005 measurement). AD-13 records that **no new FRs** were introduced — every piece traces to approved items (UC-MDF-001b, UC-MDF-004, UC-MOV-005, FR-MDF-012, BR-MDF-007, SC-008/OBJ-005). The builder has now instructed the UX package to match: SCR-012 is added as **one screen with eight sections** (same posture as §6.1 Option A), specified in `market-data-foundation.md` (primary domain; the screen hosts UCs from MDF, MOV, SCR, RES, VAL, and FDF), with six new mutation-matrix rows. ⚑ The stats panel is the one piece tracing to success criteria rather than a UC/FR — the same recorded exception the architecture carries (`03` §10). ⚑ A brief-delta line is recommended so the BA package records the admin surface as a post-brief scope addition. Change propagation: architecture already records AD-13 (v1.1); test-planner/planner have not yet run, so no downstream artifact requires re-validation — they derive from the amended package.

## 7. Open decisions carried into Gate 3 (listed now per workflow; not silently resolved)

- **OQ-VAL-001 — DCF parameter list (ASM-007 / BR-VAL-007).** The full user-editable parameter set for SCR-006 is assigned by the BA to "V1 design" — i.e., this spec plus the architect. A complete proposed parameter list (all visible and editable per BR-VAL-002) will be presented for user decision at the **valuation-dcf Gate 3 batch**. The interaction behavior (baseline defaults, recompute on every change, plain-language verdict, sensitivity grid) is fully specifiable regardless.
- **OQ-RES-002 (residual) — vs.-sector strip metric set & period basis.** Principle approved (valuation-family metrics vs. sector medians, loss-makers excluded from P/E medians with disclosure); the final metric list and period basis will be proposed for user decision at the **stock-research Gate 3 batch**.
- **FR-MOV-017 (Could) — sector-performance period selector (1W/1M/YTD)** will be specified as Could on SCR-001.
- **Unresolved data-source OQs (OQ-MDF-001/002/005, OQ-MOV-001/002, OQ-SCR-003)** do not change UI behavior: the honest-display rules (as-of dates, stale markers, unavailable/no-data states, hidden metrics, dropped-criterion reporting) already specify the UI for both the resolved and unresolved outcomes. No user decision needed for UX purposes.

---

## Gate 1 approval record (2026-10-06)

1. **Inventory of 11 screens approved as listed** (no add/remove/merge/split/rename).
2. **§6.1:** Option A — Stock Page is one screen (SCR-005) with seven content sections.
3. **§6.2:** kept as proposed — SCR-009 Password Reset as a screen; e-mail verification as states within SCR-007.
4. **§6.3:** SCR-011 kept; scenario **rename and delete added as Should** (UX-lane scope addition, to be recorded in the brief delta).
5. **§7:** DCF parameter list (OQ-VAL-001) and vs.-sector strip metric set (OQ-RES-002 residual) to be decided at their Gate 3 batches with concrete proposals.

## Amendment record (2026-10-06, post-Gate-4)

6. **SCR-012 Admin Dashboard added** per builder instruction via architecture AD-13 (§6.5): one screen, eight sections, specified in `market-data-foundation.md`; mutation matrix extended by six rows (SCR-003/004/005/006/011 specs unchanged — their requirements already cover the receiving end of every admin-driven change: hidden metrics, dropped criteria, published descriptions, baselines). SCR-001–011 IDs and requirements are untouched.
