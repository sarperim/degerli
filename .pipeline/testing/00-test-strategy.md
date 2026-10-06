# Test Strategy — Değerli (working name) BIST Value Investing Platform

**Prepared by:** test-planner · **Date:** 2026-10-06 · **Status:** GATE 1 — submitted for builder approval
**Derives from:** `00-project-brief.md` (SC-001..008) · `.pipeline/analysis/*` (7 approved domain reports, domain map, brief delta — 104 non-Won't FRs, 16 Won't FRs, 30 UCs with alternate/error flows, 36 NFRs) · `.pipeline/ux/*` (12 screens, 156 UXRs, 27-row mutation matrix, audit — all approved) · `.pipeline/architecture/` **v1.2** (components C1–C7, data model with canonical formulas §6, API design, CI design §6)
**Codebase state at authoring:** none — repository contains planning docs only. The plan precedes the code; expected values in test cases are derived by hand from the approved documents (`02-data-model.md` §6 formulas, `03-api-design.md` payloads), never from an implementation.

---

## 1. Purpose and scope of this document

This is the master test plan (IEEE 829 test plan level) for the committed milestone: **V0 data foundation + V1 product + fund data foundation**. It defines test levels, tooling, doubles policy, test data, environments, pass/fail and suspension criteria. Per-domain test case specifications follow as separate Gate 2 documents; a consolidated coverage matrix closes the package.

**The contract this package establishes:** the coder agent implements against these plans; a unit of work is done when its tests pass. Once approved, a test case may not be weakened to make an implementation pass — only the builder can change a test case (change-propagation rule, §14).

## 2. Test items (IEEE 829: test items)

The deployables and artifacts under test, per architecture `01` §2–§5:

| Item | Description | Owning arch. component |
|---|---|---|
| SPA | 11 public screens + `/admin`, TR/EN, honest-data markers, draft store | C1 |
| Public API | ~35 REST endpoints under `/api/v1` (market, stocks, screener, DCF, identity, admin), ProblemDetails error contract, honest-data envelope | C2 |
| Data platform | Ingestion adapters (KAP, İşbank, macro, TEFAS), validation + quarantine, Metrics Engine (18 metrics + CAGRs + market snapshot + sector medians + DCF baselines), scheduler + run ledger, alerting | C3a–C3d |
| Content pipeline CLI | Build-time AI drafting (evren API), baseline regeneration entrypoint | C4 |
| Database | Schema, migrations, constraints (duplicate-name, publish-gate CHECKs), views (`v_current_universe`, `v_data_freshness`), seeds | C5 |
| CI/CD | `ci.yml` (backend, web, e2e jobs), `deploy.yml` smoke-check | C6 |
| Shared quantitative core | `/src/Core` — every formula (canonical metrics, CAGR, aggregates, DCF model) | consumed by C2/C3b |

Contract sources (not test items, but the authorities tests assert against): `02-data-model.md` §6 (canonical quantitative definitions), `03-api-design.md` (payloads, error codes, caching, rate limits), `01` §8–§10 (UX constraint mechanisms, NFR numbers), `01-interaction-rules.md` (mutation matrix refresh/invalidation column).

## 3. Features to be tested (summary; details in per-domain plans)

| Domain plan file | Coverage universe |
|---|---|
| `market-data-foundation.md` | FR-MDF-001..016, 018, 019 (18); UC-MDF-001..005 incl. alternates a/b; NFR-MDF-001..006; UXR-MDF-001..028 (SCR-012 shared admin surface: run ledger, quarantine, coverage, summary, stats) |
| `market-overview.md` | FR-MOV-001..016, 020, 021 (19); UC-MOV-001..005 incl. alternates; NFR-MOV-001..006; UXR-MOV-001..012 (SCR-001) |
| `stock-screening.md` | FR-SCR-001..017 (17); UC-SCR-001..004 incl. alternates; NFR-SCR-001..005; UXR-SCR-001..019 (SCR-003/004 + admin metric-visibility) |
| `stock-research.md` | FR-RES-001..022, 026, 027 (24); UC-RES-001..005 incl. alternates; NFR-RES-001..006; UXR-RES-001..025 (SCR-002/005 + admin description review/publish) |
| `valuation-dcf.md` | FR-VAL-001..010 (10); UC-VAL-001..003 incl. alternates; NFR-VAL-001..004; UXR-VAL-001..021 (SCR-006/011 + admin baseline regeneration) |
| `user-accounts.md` | FR-ACC-001..010 (10); UC-ACC-001..005 incl. alternates; NFR-ACC-001..005; UXR-ACC-001..021 (SCR-007..010) |
| `fund-data-foundation.md` | FR-FDF-001..006 (6); UC-FDF-001..003 incl. alternates; NFR-FDF-001..004; admin fund coverage (UXR-MDF-012 fund scope) |
| `cross-cutting.md` | UXR-G-001..030 as global behaviors (incl. G-013/014 language, G-011 metric equality, G-016/017 disclaimers/advice, G-029/030); SC-002 end-to-end core loop; architecture-level performance targets (`01` §9); i18n parity; security headers; rate limits; CI reproducibility (NFR-MDF-005) |

Totals: 104 non-Won't FRs, 30 UCs (every main flow **and** every alternate/error flow), 36 NFRs, 156 UXRs, 8 success criteria. Every one lands in ≥1 test case or an explicitly recorded verification method (§9.4); orphans in either direction are defects in this plan.

**Admin surface split (decision E, §16):** SCR-012's shared infrastructure (ops summary, run ledger, quarantine, coverage, stats, role gating) is tested in the MDF plan; each product domain's plan carries its own admin-triggered operations (metric visibility → SCR plan; descriptions → RES plan; baselines → VAL plan; fund coverage → FDF plan).

## 4. Features not to be tested (IEEE 829)

1. **The 16 Won't FRs** (FR-MDF-017; FR-MOV-018/019; FR-SCR-018/019; FR-RES-023/024/025; FR-VAL-011/012/013; FR-ACC-011/012; FR-FDF-007/008/009) — out of scope by BA decision. Negative tests are written only where the architecture defines rejection behavior (e.g., unknown metric → `400 METRIC_NOT_AVAILABLE`; OR-groups are not representable in the criterion model).
2. **External sources themselves** — KAP/İşbank/TEFAS/TÜİK/ENAG/CBRT/FX-gold availability, uptime, terms, and schema drift. All external systems are doubled (§7). Source OQ validation (OQ-MDF-001/002/005, OQ-MOV-001/002, OQ-FDF-002) is a V0 builder activity against the real sources, not an automated test.
3. **Real e-mail deliverability** (Brevo, DKIM/SPF, spam scoring) — MailPit doubles SMTP everywhere in CI. Domain-dependent flows run manually on the VPS after D-04 (FLG-04).
4. **Production infrastructure** — VPS, Caddy TLS, B2 backups, UptimeRobot. Covered by the deploy smoke-check (`/health`) and the documented restore drill (manual, §9.4).
5. **SC-001 (December 2026 launch date)** — schedule, not behavior. **SC-008 (10 external users)** — market validation, measured via `GET /admin/stats` (whose function is tested) but the outcome itself is not a test.
6. **Cost NFRs (NFR-MDF-006, NFR-MOV-005, NFR-FDF-003 — $0)** — procurement policy; verified by review, not executable tests (recorded in the matrix, §9.4).
7. **Production data quality of real BIST data** — fixtures are synthetic (§8); real-data acceptance is the builder's V0 coverage review (UC-MDF-004).

## 5. Test approach — levels and what each targets

Four automated levels plus CI scripts and recorded manual/operational verification. Every test case in the Gate 2 plans names exactly one level.

### 5.1 L1 — Unit (backend)

- **Target:** `/src/Core` pure functions and in-process logic — no DB, no network, no filesystem.
- **Covers:** the 18 metric formulas and their not-meaningful rules (`02` §6.2); FCF derivation; CAGR with actual-window fallback; market P/E / div yield aggregates; sector medians with exclusions; movers ranking with eligibility threshold; the DCF model (PV, TV, equity value, fair value per share, MOS, sensitivity grid; `DCF_NOT_COMPUTABLE` constraints); adjustment-factor computation; criterion payload validation (bounds, windows, AND-model shape); macro canonical-value selection (latest `recorded_at`); error-code mapping; `t_eff` fallback (0.25).
- **Determinism:** hand-computed golden values (documented in the domain plans) from the §6 formulas; decimal precision asserted with explicit tolerances (relative 1e-9 for pure math).
- **Tooling:** xUnit (adopted from architecture, veto-able).

### 5.2 L2 — Integration (backend, real Postgres)

- **Target:** C2 endpoints + C3 workers + C5 schema/migrations against a real PostgreSQL via Testcontainers; external sources doubled at the HTTP boundary (WireMock.Net, decision point §16); SMTP doubled; time faked via `TimeProvider`.
- **Covers:** every endpoint in `03` §§3–8 — request/response shape incl. the honest-data envelope (`asOf`, `stale`, `adjusted`, `restated`, `coverage`/`state`), error codes (RFC 7807 catalog), auth/role gating, rate limits, lockout, token expiry (48h verify / 2h reset via fake clock), CSRF on unsafe methods, caching headers; EF migrations apply cleanly on a fresh container; DB constraints (duplicate-name 409, publish-gate CHECK); ingestion adapters against canned source responses — idempotent upserts, append-only enforcement, provenance refusal, quarantine on invalid facts, backfill depth recording; scheduler cron triggers, retry ladder (5/15/60 min) and run-ledger rows via fake time; Metrics Engine golden values against the seeded fixture universe (architecture CI §6.1 names this explicitly); snapshot/medians/baselines generation.
- **Isolation:** each test class gets a migrated, seeded container (or a transaction-rolled-back schema); tests are order-independent — each test creates its own users/screens/scenarios via the API or fixture builder.
- **Tooling:** xUnit + Testcontainers (adopted; the architecture explicitly rejects in-memory DB providers).

### 5.3 L3 — Component/unit (web)

- **Target:** C1 SPA modules in isolation — jsdom, API mocked.
- **Covers:** honest-data marker components (stale badge, restatement asterisk/footnote/warning, adjusted disclaimer, coverage/no-data, preparing state); the zustand draft store (criteria, DCF assumptions, filters, search, active section — preservation across navigation and the auth hop, sessionStorage persistence); form validators mirroring server contracts (password ≥10 chars, bound types, DCF parameter ranges); mutation components against the Gate 2 lifecycle (pending lock, disabled-while-invalid, input preservation on failure, inline duplicate-name error); i18n key resolution and TR-default; error-code → i18n copy mapping; query invalidation per the mutation matrix (TanStack Query test wrappers).
- **Tooling:** Vitest + Testing Library (adopted); API mocking via MSW (decision B, §16).

### 5.4 L4 — E2E / system (browser against the full compose stack)

- **Target:** the whole deployable set as users experience it — `docker compose -f compose.ci.yml` (API + Postgres + seeded fixtures + MailPit), Playwright-driven browser (Chromium).
- **Covers (minimum, per architecture CI §6.1 plus UX critical flows):** the **SC-002 core loop** anonymous — dashboard → screener run → stock page (sections) → DCF compute with own assumptions → plain-language verdict; anonymous save → register → verify (link read from MailPit) → save completes (UXR-SCR-019/UXR-G-030); duplicate-name rejection (UXR-G-029); deletion confirmations (UXR-G-022); sign-in/sign-out, language toggle preserving context; stock list filter/search; description-in-preparation page; stale/unavailable macro display; admin critical flows 1–8 (`ux/market-data-foundation.md` §10) incl. access-denied for non-builders; i18n TR-default on key screens; disclaimer presence on the 7 research surfaces; empty states.
- **Determinism:** only the seeded fixture universe is visible; MailPit API supplies tokens; no real network; assertions on i18n catalog values loaded from the repo (never hardcoded prose); total suite budget ≤ 5 min (architecture target).
- **Tooling:** Playwright (adopted).

### 5.5 CI scripts (not level-labeled; separate verdicts)

- **i18n parity script** — every key present in both `tr.json`/`en.json`, no unused keys; fails the web job (NFR-MOV-002/SCR-001/RES-001/VAL-001/ACC-003).
- **Advice-vocabulary grep** — no recommendation language in i18n catalogs (NFR-MOV-006 backstop, per architecture `01` §9).
- **OpenAPI served** — `GET /api/v1/openapi.json` returns a document (integration-level, listed here for visibility).

### 5.6 Recorded manual / operational verification (not automated — flagged, §9.4)

Where a requirement is honestly not automatable, it is recorded in the coverage matrix with a named method and owner instead of being silently dropped or vaguely tested: SC-006 full research session (builder-run, scripted checklist); UC-ACC-005 KVKK pre-launch review; NFR-VAL-002 plain-language verdict comprehension (validated inside the SC-006 session); NFR-MOV-003 99.0% availability (UptimeRobot operational measurement; tests cover the anonymous-access precondition only); NFR-MDF-005 reproducibility quickstart (compose-up smoke is automated; the ≤10-commands README claim is builder-verified); backup restore drill (RTO 4h/RPO 24h — builder-executed once pre-launch); cost NFRs (review).

## 6. Test tooling — adopted and open decisions

**Adopted from the architecture** (its §5/§6 rows prescribe these; veto here means an architecture change request, not just a test-plan change): xUnit, Testcontainers (Postgres), Vitest + Testing Library, Playwright, custom i18n-parity script, MailPit in the CI compose stack, Serilog for failure forensics.

**Open decisions presented to the builder (§16):**

| # | Decision | Options | Test-planner recommendation |
|---|---|---|---|
| A | Performance verification (targets: API p95 ≤300 ms cached / ≤800 ms screener+DCF; dashboard FCR ≤3 s on 4G; Playwright ≤5 min) | (1) In-suite budget smoke with generous tolerances (p95 ≤2× target on seeded data, warm cache, Testcontainers) + manual spot-check on the VPS; (2) NBomber load tests in CI; (3) k6 scripts | **(1)** — deterministic enough to gate CI, zero new tooling; load testing is over-engineering for a 100-stock EOD app; the VPS numbers are checked manually pre-launch |
| B | Web API mocking for L3 | (1) MSW (network-level intercept, reusable handlers mirroring `03` payloads); (2) hand-rolled fetch stubs | **(1) MSW** — intercepts what TanStack Query actually fetches; handlers double as living documentation of the API contract |
| C | Accessibility automation | (1) axe-core embedded in Playwright on key screens (SCR-001/002/003/005/006/007/010/012), zero-known-violations gate; (2) manual review only | **(1)** — free, deterministic, catches real UXR-G-026/027/028 regressions; does not replace the builder's manual pass |
| D | Security scanning | (1) Targeted automated tests only (headers, rate limits, lockout, enumeration-neutrality, cookie flags, CSP — all specified in `01` §10); (2) + OWASP ZAP baseline scan on the CI compose stack as a Could | **(1) now, (2) as an optional later ticket** — ZAP adds CI minutes and noise; targeted tests cover every specified behavior |
| E | Plan structure | (1) 7 domain plans + an 8th `cross-cutting.md` (TC-XC) for global UXR-G behaviors, SC-002 e2e, performance, i18n, security headers, rate limits; (2) fold everything into the 7 domain plans | **(1)** — global rules tested once instead of seven times; avoids orphaned G-rules |
| F | Fixture universe | (1) One synthetic, checked-in fixture universe (§8) shared by integration + e2e with now-anchored seeding; (2) per-suite ad-hoc fixtures | **(1)** — one golden dataset keeps cross-surface equality assertions (UXR-G-011) meaningful and cheap |
| G | Manual/operational records | (1) Record §5.6 items in the coverage matrix as manual checklist entries with owners; (2) omit them (matrix shows automated tests only) | **(1)** — keeps traceability honest without pretending they're automatable |

## 7. Test doubles policy

**Default rule (per mandate):** mock volatile externals; test our own logic for real. Everything the platform itself defines — API, workers, metric math, schema — runs for real at the appropriate level.

| Dependency | Double | Where real |
|---|---|---|
| KAP / İşbank / macro / TEFAS source APIs | WireMock.Net with canned responses (happy, malformed, empty, unreachable, rate-limited, revised) | never in CI; real sources touched only by the builder's V0 validation |
| evren AI API (C4) | recorded request/response fixtures at the HTTP boundary | drafting pipeline logic, status transitions, source-ref recording tested for real |
| Brevo SMTP | MailPit (L4, links read via its API); in-process fake mail dispatcher (L2) | dispatch content and trigger conditions asserted; delivery never |
| Time | `TimeProvider` fake (L1/L2): cron schedules, retry backoff, lockout windows, token expiry (48h/2h), staleness thresholds | L4 uses real clock with now-anchored seeds (§8.3) |
| Randomness | fixed seeds everywhere; Playwright runs fixed browser locale/timezone (tr-TR, Europe/Istanbul) | — |
| The database | **never doubled** — real Postgres via Testcontainers in every backend level that touches data | always |
| Identity framework | real ASP.NET Core Identity (L2+) | never mocked |
| Web API boundary (L3) | MSW handlers | real SPA code; real query/mutation logic |

## 8. Test data strategy

### 8.1 The fixture universe (decision F)

One synthetic, checked-in fixture universe — **no real BIST symbols, names, or values** — used by L2 and L4. It is engineered so every not-meaningful rule, honest-data state, and exclusion behavior in `02` §6 has a deterministic trigger. Minimum composition:

- **≥15 instruments** covering the archetypes: `STD` (full 10Y history, dividends, no actions — metric baseline); `LOSS` (NI_TTM ≤ 0 → P/E NULL, market-P/E exclusion, payout NULL); `NEGEQ` (equity ≤ 0 → P/B, ROE NULL); `NEGEBITDA` (EBITDA ≤ 0 → EV/EBITDA, net-debt/EBITDA NULL); `NEGFCF` (FCF ≤ 0 → EV/FCF, FCF yield NULL; sign-change → FCF CAGR NULL); `IPO` (listed 2024 → 3Y CAGR over ~2 FYs with `windowYears` < 3; 5Y/10Y no-data with coverage boundary); `REST` (as-reported + restated statement versions → `is_rested` flags); `ACT` (1-for-5 split + rights issue → raw vs adjusted series diverge); `NODIV` (never paid → div_yield NULL + never-paid state); `LOWVOL` (illiquid, big % move → excluded from movers by eligibility threshold); `UNSEC` (sector NULL → unclassified flag); `MOVER` (high-volume gainer/loser); `PART` (missing CF statement → DCF missing-inputs state); `HIGHPAY` (payout > 1, displayed honestly).
- **≥3 sectors with ≥3 stocks each** — one sector deliberately containing 2 loss-makers (P/E median over 3 peers, `excludedCount` 2, `peerCount` 3).
- **Macro:** all six series seeded; `TUIK_CPI` with a same-date revision (two `recorded_at` rows — canonical = latest); freshness rows making one series stale and (for L2 variants) the independent CPI absent.
- **Accounts:** builder (seeded role); verified user A; unverified user B; user C (isolation/ownership tests).
- **Funds:** 3 — full coverage (NAV+performance+holdings), NAV-only (holdings gap recorded), partial holdings incl. one symbol-matched to an MDF instrument and one unmatched `name_raw`.
- **User content:** saved screens and scenarios per the mutation-matrix flows (created by tests themselves where possible).

### 8.2 Golden values

Expected numbers for every computed-value assertion are **hand-derived from `02` §6 formulas and documented inside the test cases** (e.g., "given fixture `STD` FY2021–FY2025 revenues 100/120/144/172.8/207.36 → `rev_cagr` 20.0%, window 5"). The fixture design favors round numbers so golden values are verifiable by inspection. This is the mechanism that makes two independent implementors converge.

### 8.3 Seeding and determinism rules

- L1/L2: absolute fixture dates + fake `TimeProvider`; full determinism.
- L4: seeds are **now-anchored at container start** ("fresh" rows = seed day; "stale" rows = seed day − N). Tests assert flags, relative offsets, and payload-served values — never wall-clock dates — eliminating midnight-rollover flakes.
- Seeding is idempotent and re-runnable (mirrors the production seed discipline, `02` §8).
- Every test is order-independent and creates its own mutable state; no test reads another test's writes.
- UI copy assertions load `tr.json`/`en.json` from the repo at test time.

## 9. Environments and NFR verification

### 9.1 Environments

1. **CI (GitHub Actions, free — public repo):** backend job (Testcontainers Postgres; migrations; golden-value metric tests), web job (lint, tsc, Vitest, i18n parity, build), e2e job (compose.ci stack + MailPit + seeded fixtures; Playwright; axe). Matches architecture `01` §6.1.
2. **Local dev:** the dev compose (hot reload, MailPit) runs the same fixtures — a fresh clone bringing the stack up is itself the NFR-MDF-005 reproducibility check.
3. **Production:** never a test target; covered by the deploy smoke-check and operational monitoring only.

### 9.2 NFR verification methods (each NFR's method is finalized in its domain plan)

- **Freshness (NFR-MDF-001, MOV-001, SCR-004, RES-004):** fake-clock scheduler tests (trigger at 20:30 TRT trading days; retry ladder 5/15/60; alert on final failure; last-known-good + `stale` flag) + e2e stale-marker display.
- **Bilingual completeness (MOV-002, SCR-001, RES-001, VAL-001, ACC-003):** i18n parity script (CI-gating) + e2e TR-default assertion + language-toggle-preserves-context e2e + description publish gate.
- **Public availability / optional-account posture (MOV-003, RES-005, ACC-005):** integration — every research endpoint answers 200 anonymous; `/me` mutations demand auth. The 99.0% monthly figure is operational (UptimeRobot), recorded in the matrix as such.
- **Plain language (MOV-004, SCR-002, RES-002, VAL-002):** automatable proxies asserted (units + as-of present in labels; verdict is a single sentence with a percentage) + builder review recorded in the SC-006 session (manual).
- **Consistency (SCR-003, RES-003, VAL-003 / UXR-G-011):** cross-surface equality assertions in e2e — the same stock/metric/date value identical across screener results, stock page, and DCF price (by construction one Metrics Engine; tests prove it).
- **Credential security (ACC-002):** PBKDF2 ≥100k iterations config assertion; lockout 10/15 min; generic sign-in error; enumeration-neutral reset; cookie flags (HttpOnly/Secure/Lax).
- **Performance (architecture §9 targets):** decision A — budget smoke in-suite (p95 ≤2× target, seeded, warm) + manual VPS spot-check; Playwright ≤5 min hard gate.
- **Regulatory (MOV-006):** disclaimer presence e2e-asserted on SCR-001/002/003/004/005/006/011 + advice-vocabulary grep in CI.
- **Retention / append-only (MDF-002, FDF-002):** integration tests attempt UPDATE/DELETE-visibility checks — restated/revision rows append, as-reported survives, no TTL jobs exist.
- **Coverage transparency (MDF-003, FDF-001):** admin coverage endpoints against seeded gaps; honest no-data states on public surfaces for the same fixtures.

### 9.3 Security posture verification (targeted, decision D)

HTTPS redirect, HSTS, `nosniff`, `frame-ancestors 'none'`, CSP `default-src 'self'` (integration, header assertions); CSRF token required on unsafe methods (integration: POST without token → 400); rate limits 5/600/60 req/min with `retryAfter` (integration, test-controllable client IP); lockout; token single-use (second use → 410); role gating on `/admin` (403 for user, 401 anonymous).

### 9.4 Non-automatable items — recorded verification ledger

The coverage matrix carries a **Method** column with values: `unit | integration | component | e2e | script | manual | operational`. Manual/operational entries name the owner (builder) and the trigger (pre-launch / per-release). No requirement is left methodless.

## 10. Item pass/fail criteria (IEEE 829)

- **Per test case:** pass = every specified expected result observed, deterministically, on a clean run; fail = any expected result not observed. Flaky = fail: no retry-masks; a flaky test is a defect in the test or the code, fixed at the source.
- **Per CI run (the definition of done for merged code):** all automated Must- and Should-level test cases for the implemented scope pass; i18n parity and the advice-vocabulary grep pass; Playwright suite within its 5-minute budget.
- **MoSCoW and gating:** MoSCoW orders implementation; it does not relax tests. Once a Should feature is implemented, its tests gate CI like Must's. A Should feature may be deferred by the builder's explicit decision (recorded on the ticket); it may not be half-shipped.
- **Golden values:** an intentional formula change requires updating the affected test cases **and** recording the change per §14 (the plan is the contract; the implementation does not redefine expected results silently).
- **Manual/operational items:** pass = checklist item recorded with date and outcome by the owner.

## 11. Suspension and resumption criteria (IEEE 829)

**Suspend** the testing effort for a domain when: (1) Docker/Testcontainers is unavailable in the environment for >1 day (blocks L2/L4); (2) a blocking defect makes >50% of a plan's cases un-runnable (e.g., migrations fail → all L2+ blocked); (3) the fixture universe or golden values are invalidated by an approved requirements/architecture change and re-derivation is pending; (4) CI minutes are exhausted (public-repo free tier — monitor via usage).

**Resume** when: the blocker is fixed and a full clean run of the suspended suite has been executed and triaged; for (3), the affected plans are re-validated per §14 and the builder re-approves them.

## 12. Test deliverables

1. This strategy — `00-test-strategy.md` (Gate 1).
2. Eight per-domain test case specifications (Gate 2): §3's table; each uses the mandated IEEE 829 template with systematic case-selection justification per case (equivalence partitioning, boundary value analysis, decision tables, state transition).
3. `99-coverage-matrix.md` — every FR / UC flow / NFR / UXR / SC → test case IDs or recorded method; both directions; explicit gap flags.
4. Test code conventions (for the coder): `/tests/Core.UnitTests`, `/tests/Api.IntegrationTests`, `/tests/e2e` (Playwright), Vitest colocated in `/src/Web` or `/tests/Web` — exact placement is the coder's choice so long as the CI jobs run them; fixtures live in a checked-in `fixtures/` module used by both L2 and L4.

## 13. Responsibilities

Solo builder with AI-assisted agents. Coder implements code + tests from these plans; code-reviewer verifies conventions; the builder approves plan changes and executes the manual/operational ledger (§9.4). Test-planner (this agent) maintains these documents and the change-propagation log (§14).

## 14. Traceability, IDs, and change propagation

- **TC IDs are permanent:** `TC-<DOMAIN>-<NNN>` with domains `MDF, MOV, SCR, RES, VAL, ACC, FDF, XC`. Removed cases leave a gap (marked removed with reason); new cases take the next free number. Never renumber.
- **Change propagation:** any modification to an approved TC, or to strategy-level policy, lists the affected requirement IDs and the tickets that trace to them, and is flagged to the builder for re-validation. Upstream documents (brief, analysis, UX, architecture) are never edited by this agent; contradictions between them are raised to the builder rather than silently resolved in tests.
- **Test cases test observable behavior only** — through the API contract, the CLI entrypoints, the scheduler/run-ledger surfaces, and the browser. No test may depend on internal class shapes, private methods, or DB physical layout beyond what `02` specifies.

## 15. Risks to the test effort (and mitigations)

| Risk | Mitigation |
|---|---|
| CI timing variance makes performance smoke flaky | generous 2× tolerance; measured p95 not mean; documented as budget smoke, with the strict number verified manually on the VPS |
| Fixture/golden drift after intentional formula changes | §14 change-propagation; golden values live in the plan, not only in code |
| Playwright suite growth past the 5-minute budget | case count capped per plan; only UX-critical flows are e2e; the rest lives at L2/L3 |
| MailPit/token-link race in e2e | MailPit API polling with bounded wait (≤10 s) and explicit error on timeout — never a bare sleep |
| WireMock fixtures diverge from real source behavior | accepted by design (source OQs open, FLG-06); the builder's V0 validation against real sources is the compensating control |
| Midnight rollover in now-anchored e2e seeds | §8.3 rules (assert flags/offsets, not absolute dates) |

## 16. Gate 1 — decisions requested from the builder

Approve or adjust. Recommendations above; a bare "approve all" adopts every recommendation.

1. **Levels & adopted tooling** (§5, §6 adopted list: xUnit + Testcontainers, Vitest + Testing Library, Playwright, MailPit, i18n-parity script)?
2. **Decision A** — performance verification: in-suite budget smoke (recommended) / NBomber / k6?
3. **Decision B** — web API mocking: MSW (recommended) / fetch stubs?
4. **Decision C** — accessibility: axe-core in Playwright on key screens (recommended) / manual only?
5. **Decision D** — security: targeted tests only (recommended), ZAP baseline optional later / ZAP as a CI gate?
6. **Decision E** — 8th cross-cutting plan (TC-XC) + admin-surface split as in §3 (recommended) / fold into 7 plans?
7. **Decision F** — single shared synthetic fixture universe per §8 (recommended) / per-suite fixtures?
8. **Decision G** — manual/operational verification ledger for the non-automatable items (§5.6, §9.4) (recommended) / omit from the matrix?
9. **Sequence** — domain plans in core-loop order (MDF → MOV → SCR → RES → VAL → ACC → FDF → XC), each stopping for your approval?

---
*Change record: v1.0 2026-10-06 — initial strategy, submitted at Gate 1.*
