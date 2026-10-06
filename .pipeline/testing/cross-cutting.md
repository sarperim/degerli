# Cross-Cutting Test Plan (TC-XC)

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** approved (builder final review 2026-10-07 — §6 interpretations upheld; v1.0 unchanged)
**Derives from:** `01-interaction-rules.md` (UXR-G-001..030 as global behaviors; mutation lifecycle) · `00-project-brief.md` (SC-001..008) · `architecture/` v1.2 (`01` §6 CI, §9 performance targets, §10 cross-cutting; `03` §2/§7/§12/§13) · strategy (approved; decisions A–G) · fixture contract FU.
**TC ID convention:** `TC-XC-NNN`, permanent. Levels: script (CI), L2, L3, L4.

## 1. Scope

**Tested here:** the behaviors that span domains — the SC-002 core loop as one journey; the global UXR-G rules in their cross-screen form (G-011 equality, G-013/014 language, G-016/017 content posture, G-012 EOD framing); i18n parity; security headers, CSRF, rate limits; caching policy; performance budgets; accessibility automation; reproducibility; OpenAPI visibility; responsive/keyboard spot checks.

**Not tested here (owned by domain plans, cross-referenced in §5):** per-screen loading/error/empty/no-data states, per-form validation, draft preservation specifics, confirmations, per-endpoint behaviors. **Not automated anywhere (manual/operational ledger):** SC-001 launch date, SC-006 research session, SC-008 user count (measured via `GET /admin/stats` — MDF TC-044), 99.0% availability, $0-cost NFRs, restore drill, README quality.

## 2. Test Case Specification

**TC-XC-001 — SC-002 core loop, anonymous, end-to-end**
- Traces: SC-002, OBJ-002, `01` §6.1 e2e job · Level: L4 · Design: EP — the canonical journey
- Expected: one anonymous browser session: dashboard renders (blocks + macro) → open Screener → add "P/E at most 15" + "ROE at least 15%" → run → 3 results (ALFA/REST/UNSEC) with values and as-of → open ALFA's stock page → move through sections (canonical figures, honest marks) → open the DCF calculator → baseline result visible → change the discount rate → recompute → read the plain-language verdict — the complete discover → screen → research → value → decide chain without an account. Runtime contributes to the ≤ 5-minute e2e budget.
- Dependencies: —

**TC-XC-002 — i18n parity (CI script)**
- Traces: NFR-MOV-002/SCR-001/RES-001/VAL-001/ACC-003, `01` §6.1 web job · Level: script · Design: structural completeness (both catalogs, no unused keys)
- Expected: the parity script fails the build on any key present in one catalog but not the other, or unused in both; TR catalog is the reference set.
- Dependencies: —

**TC-XC-003 — Turkish default on first visit**
- Traces: UXR-G-013 (TR default), brief §7 · Level: L4 · Design: EP — pristine-device class
- Expected: a fresh browser context (no storage) renders SCR-001/002/003/005/006/007 in Turkish; language resolves account-pref > localStorage > `tr` (the last two legs here).
- Dependencies: —

**TC-XC-004 — Language switch preserves context (cross-screen)**
- Traces: UXR-G-013 · Level: L4 · Design: EP — toggle with active state on data-bearing screens
- Expected: with a sector filter + search term active on SCR-002, criteria built on SCR-003, and DCF assumptions modified on SCR-006, toggling TR↔EN re-renders in the other language with the same filters, inputs, values, and scroll position — data caches untouched.
- Dependencies: —

**TC-XC-005 — Language persistence (device and account)**
- Traces: UXR-G-014, BR-ACC-004 · Level: L4 · Design: state transition — anonymous choice persists; signed-in syncs
- Expected: anonymous EN choice survives a page reload and a new visit on the same device (localStorage); signed-in toggle issues the PATCH (optimistic; on failure reverts with a notice — MSW-driven failure variant at L3); at next sign-in on another device the account preference applies.
- Dependencies: —

**TC-XC-006 — Disclaimer sweep across the seven research surfaces**
- Traces: UXR-G-016, FR-MOV-020/SCR-016/RES-017/VAL-008 · Level: L4 · Design: EP — surface × presence
- Expected: the informational-only disclaimer component is rendered on SCR-001, SCR-002, SCR-003, SCR-004, SCR-005, SCR-006, and SCR-011 — asserted by presence per surface in one sweep.
- Dependencies: —

**TC-XC-007 — No advice vocabulary (CI grep)**
- Traces: UXR-G-017, NFR-MOV-006, `01` §9 · Level: script · Design: negative probe — banned-term classes
- Expected: the grep fails the build if any i18n catalog value contains recommendation vocabulary (buy/sell/öner/al recommendation framing — the maintained term list lives with the script); e2e additionally asserts the DCF verdict renders "based on your assumptions" framing (VAL TC-019).
- Dependencies: —

**TC-XC-008 — Cross-surface metric equality**
- Traces: UXR-G-011, BR-MDF-009, NFR-SCR-003/RES-003/VAL-003 · Level: L4 · Design: EP — same stock/metric/date across surfaces
- Expected: ALFA's P/E 10.0 is byte-identical in screener results (TC-SCR-002 run), the stock page valuation section (TC-RES-007), and — for price — the DCF calculator (20.00, TC-VAL-012); the dashboard's market P/E (28.1218) is the same aggregate the snapshot stores. One number, every surface.
- Dependencies: —

**TC-XC-009 — Security headers**
- Traces: `01` §10.5 · Level: L2 · Design: EP — header classes
- Expected: on representative responses: `X-Content-Type-Options: nosniff`; `frame-ancestors 'none'` (CSP frame-ancestors); `Content-Security-Policy: default-src 'self'`; HSTS asserted over TLS (prod posture — checked manually pre-launch; CI compose runs plain HTTP, recorded). No third-party script origins in CSP.
- Dependencies: —

**TC-XC-010 — CSRF on unsafe methods**
- Traces: `03` §2 · Level: L2 · Design: decision table — method × token present/absent
- Expected: POST `/screener/run`, POST `/me/screens`, PATCH/DELETE `/me/screens/{id}`, POST `/auth/login` etc. without `X-CSRF-Token` → 400; with the token from `GET /api/v1/auth/csrf-token` → proceeds; GETs unaffected.
- Dependencies: —

**TC-XC-011 — Rate limits and credential-hardening config**
- Traces: `03` §12, `01` §10.1, RISK-ACC-001, NFR-ACC-002 · Level: L2 · Design: BVA — limit edges (5th/6th auth request per IP per minute; global 600; admin 60)
- Preconditions: the rate limiter keys on a test-controllable client IP (documented testability requirement — `X-Forwarded-For` honored in the test configuration).
- Expected: `/auth/*` 6th request within the window → 429 `RATE_LIMITED` with `params.retryAfter`; window reset releases (fake/controllable clock); global and admin limits enforced at their edges; password hashing configuration asserts PBKDF2 with ≥ 100,000 iterations (config assertion).
- Dependencies: —

**TC-XC-012 — Loading and pending feedback (spot)**
- Traces: UXR-G-001/002, `01` M-5 · Level: L4 · Design: EP — slow-load and mutation-pending classes (MSW/throttled handlers)
- Expected: screen loads show visible loading indication until content/empty/error renders; a mutation's trigger shows pending state and is locked against re-invocation (UXR-G-020) — asserted on one read surface (SCR-002) and one mutation (save screen).
- Dependencies: —

**TC-XC-013 — Plain-language errors; no technical leakage**
- Traces: UXR-G-003, `03` §7 INTERNAL · Level: L3 · Design: EP — 5xx class (MSW returns 500)
- Expected: a forced 500 renders the generic plain-language error with retry in the active language; no stack trace, raw exception, or internal identifier beyond the support `traceId` is displayed; input preserved (UXR-G-004).
- Dependencies: —

**TC-XC-014 — Performance budget smoke**
- Traces: `01` §9 targets, strategy decision A · Level: L2 (+ CI policy) · Design: BVA — budget edges with tolerance ×2
- Expected (seeded data, warm cache, single user, Testcontainers): cached public GETs p95 ≤ **600 ms** (target 300 × 2); `POST /screener/run` and `POST …/dcf/compute` p95 ≤ **1600 ms** (target 800 × 2); measured over ≥ 30 calls each. The Playwright suite completes within **5 minutes** (CI job duration gate). Documented tolerance rationale: CI hardware variance; strict numbers verified manually on the VPS pre-launch (manual ledger).
- Dependencies: —

**TC-XC-015 — Caching policy per surface**
- Traces: `03` §13 · Level: L2 · Design: decision table — surface class × header
- Expected: `GET /market/*`, `/stocks*`, `/screener/metrics`, `/stocks/{s}/dcf` → `Cache-Control: public, max-age=300` + strong ETag (revalidation returns 304 on unchanged); `POST /screener/run`, `…/dcf/compute` → `no-store`; `/me/*`, `/auth/*`, `/admin` → `no-store`.
- Dependencies: —

**TC-XC-016 — Reproducibility: fresh stack from the repo**
- Traces: NFR-MDF-005, NFR-FDF-004, SC-007, `01` §6 · Level: L4 (the CI e2e bootstrap is the executable form) · Design: EP — cold-start class
- Expected: the CI e2e job's `docker compose -f compose.ci.yml up` from a clean checkout brings up API + Postgres + MailPit with seeded fixtures, and `GET /health` returns 200 (liveness + DB check) before tests run — a fresh clone runs the full stack; the README quickstart's ≤ 10 commands is builder-verified (manual ledger).
- Dependencies: —

**TC-XC-017 — Accessibility automation (axe)**
- Traces: UXR-G-026/027/028, strategy decision C · Level: L4 · Design: EP — key-surface scan
- Expected: axe-core reports **zero known violations** on SCR-001, SCR-002, SCR-003, SCR-005, SCR-006, SCR-007, SCR-010, SCR-012 (populated states); findings are triaged as defects, not suppressed.
- Dependencies: —

**TC-XC-018 — OpenAPI document served**
- Traces: `03` header (OpenAPI anonymous), contract visibility · Level: L2 · Design: EP — anonymous fetch
- Expected: `GET /api/v1/openapi.json` → 200 anonymous, valid document covering the `/api/v1` routes; contains no fund-facing public routes (cross-ref TC-FDF-010).
- Dependencies: —

**TC-XC-019 — Responsive operability at narrow width**
- Traces: UXR-G-025 · Level: L4 · Design: EP — 375px viewport class
- Expected: at 375px width, SCR-002/003/005/006/012 remain fully operable — navigation, reading, and every mutation reachable; data tables degrade in presentation without losing any row, value, marker, or action (the inflation pair and honest-data marks survive).
- Dependencies: —

**TC-XC-020 — Keyboard operability and focus management**
- Traces: UXR-G-026/027/028 · Level: L4 · Design: EP — keyboard-only journey
- Expected: a keyboard-only user can traverse SCR-003 (build criteria → run → open a result) and SCR-006 (adjust a parameter → recompute → save prompt) with visible focus throughout; focus order follows reading order; dialogs trap focus and restore it on close; interactive elements carry programmatically determinable labels in the active language.
- Dependencies: —

**TC-XC-021 — No real-time framing on market-data surfaces**
- Traces: UXR-G-012 (with the recorded SCR-012 exception) · Level: L4 · Design: negative probe — polling absence
- Expected: on SCR-003 (after a run), SCR-005, and SCR-006, a 3-second quiet window shows no periodic `/api` requests and no countdown/live indicators — data is EOD-framed with as-of dates. (SCR-001 asserted in TC-MOV-022; the admin run ledger's in-progress auto-refresh is the recorded exception — TC-MDF-048.)
- Dependencies: —

## 3. Test Design Specification — Systematic Case Selection

- **Equivalence partitioning:** surfaces × disclaimer/language/a11y presence (TC-003/006/017/019); header classes (TC-009/015); method × token (TC-010); error classes (TC-013).
- **Boundary value analysis:** rate-limit edges 5/6, 600, 60 (TC-011); performance budgets with ×2 tolerance (TC-014); pristine vs. stored device state (TC-003/005).
- **Decision tables:** caching surface × policy (TC-015); CSRF method × token (TC-010).
- **State transition testing:** anonymous language choice → persisted → account-synced at sign-in (TC-005); cold stack → healthy (TC-016).
- **Negative probes (absence assertions):** advice vocabulary (TC-007), polling/live framing (TC-021), fund public routes (with FDF), technical leakage (TC-013).

## 4. Item Pass/Fail Criteria and Suspension Criteria

**Pass:** the core loop completes anonymous in one session; scripts and budget gates green; equality/parity/presence sweeps complete; absence probes observe nothing. **Fail:** any loop break, any parity/vocabulary violation, any budget breach beyond tolerance, any a11y known-violation. Flaky = fail (network-quiet windows are bounded and deterministic).

**Suspension:** CI infrastructure unavailable >1 day; a global mechanism change (i18n, auth, caching, rate limiting) invalidating a group; performance-budget environment deemed non-representative (re-baseline tolerance with builder approval). **Resumption:** clean triaged run of the affected group.

## 5. Coverage Matrix — global rules and success criteria (domain-owned G-rule instances cross-referenced)

| Item | Test Cases (owner) | Status |
|---|---|---|
| SC-002 core loop | TC-XC-001 (+ domain legs SCR-026, RES-028, VAL-023) | planned |
| SC-005 daily refresh unattended | TC-MDF-010..014, TC-FDF-006 | planned |
| SC-007 repo/README artifact | TC-XC-016 (executable form); README quality → manual ledger | planned |
| SC-001 / SC-006 / SC-008 | process/manual/operational ledger (SC-008 measurement via TC-MDF-044) | manual |
| UXR-G-001/002 (loading/pending) | TC-XC-012 + per-screen states (all domain plans) | planned |
| UXR-G-003/004 (errors/input preserved) | TC-XC-013 + per-failure-path cases (SCR-024, RES-019, VAL-021, ACC-022/023) | planned |
| UXR-G-005/006 (empty/no-data) | per-domain empty/no-data cases (SCR-025/027, RES-006/011/013, MOV-018, VAL-022, MDF-050) | planned |
| UXR-G-007/008 (as-of/stale) | TC-MOV-012/013/024, TC-RES-015/024, TC-MDF-013 | planned |
| UXR-G-009/010 (restated/adjusted) | TC-RES-009/024/030/032, TC-MDF-030/032 | planned |
| UXR-G-011 (cross-surface equality) | TC-XC-008 | planned |
| UXR-G-012 (no real-time) | TC-XC-021, TC-MOV-022 (+ recorded admin exception TC-MDF-048) | planned |
| UXR-G-013 (instant switch, context) | TC-XC-003/004 + per-screen (MOV-023, RES-026) | planned |
| UXR-G-014 (language persistence) | TC-XC-005, TC-ACC-019/029 | planned |
| UXR-G-015 (plain language + units) | units/as-of presence (MOV-016, SCR-022, VAL-019); review ledger | planned |
| UXR-G-016 (disclaimers) | TC-XC-006 | planned |
| UXR-G-017 (no advice language) | TC-XC-007 | planned |
| UXR-G-018/019/020 (form safety) | TC-ACC-022, TC-SCR-022/024, TC-VAL-018/021, TC-MDF-047 (spot XC-012) | planned |
| UXR-G-021 (state preserved across navigation) | TC-SCR-023/029, TC-RES-027/033, TC-VAL-021, TC-MDF-051 | planned |
| UXR-G-022 (destructive confirmations) | TC-SCR-030, TC-VAL-022/027, TC-ACC-026/032 (+ admin confirmations TC-MDF-049, RES-034, VAL-016, SCR-021) | planned |
| UXR-G-023/024 (login-to-save pattern) | TC-SCR-028, TC-VAL-024, TC-ACC-028 | planned |
| UXR-G-025 (responsive) | TC-XC-019 | planned |
| UXR-G-026/027/028 (keyboard/focus/labels) | TC-XC-017/020 | planned |
| UXR-G-029 (duplicate names) | TC-SCR-013/015, TC-VAL-013 | planned |
| UXR-G-030 (unverified-save gate) | TC-SCR-014/024/028/032, TC-VAL-013/021/024, TC-ACC-011/027 | planned |
| Architecture perf targets (`01` §9) | TC-XC-014 (+ manual VPS spot-check ledger) | planned |
| Caching (`03` §13) | TC-XC-015 | planned |
| Security headers/CSRF/rate limits (`01` §10) | TC-XC-009/010/011 | planned |
| Reproducibility (NFR-MDF-005/FDF-004) | TC-XC-016 | planned |

## 6. Recorded interpretations

- **I-XC-1:** HSTS is asserted only in the TLS (production) posture — the CI compose stack runs plain HTTP; pre-launch manual checklist item.
- **I-XC-2:** the rate-limit test requires a test-controllable client-IP key (`X-Forwarded-For` honored in test config) — a documented testability requirement for the coder, not a production behavior change.
- **I-XC-3:** the banned-vocabulary term list for TC-XC-007 lives beside the script and is maintained by review (TR + EN lists); adding terms is a plan-change (recorded per §14 of the strategy).

---
*Change record: v1.0 2026-10-06 — initial cross-cutting test plan, batch mode. Approved at the builder's final review 2026-10-07 — §6 interpretations I-XC-1..3 upheld.*
