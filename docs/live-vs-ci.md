# Live vs CI — what each kind of proof actually establishes

**Owner:** builder (operational lane); maintained by the planner together with the test plan.
**Created:** 2026-10-24 · **Source of the worked examples:** the live ledger of the 2026-10-18 → 2026-10-24 waves (`.pipeline/plan/run-report.md`, dev stack) and the ticket files cited below.
**Related:** `.pipeline/testing/00-test-strategy.md` (§5, §9.2, §9.4, §10) · `.pipeline/testing/99-coverage-matrix.md` (Method column) · `.pipeline/architecture/01-system-architecture.md` §6.1.

---

## 1. The claim this document makes

**A green CI run is not evidence that the product works.** It is evidence that the code compiles, that the contracts hold against doubles, and that the seeded stack behaves. Nothing more. On 2026-10-23 CI was green while the scheduled EOD chain failed to publish a market — the rows were rebuilt by a manual repair, not by the schedule.

The failure mode this document exists to prevent is not ignorance of the source — it is **the wrong proof accepted as the right one**. Four of the five incidents in §5 were found by a live run and were invisible to a passing suite; the fifth (§5.5) is the inverse — a CI red that ran on every PR for days and that the process had agreed to ignore. In all five, what failed was the same thing: a green-or-waived check read as proof.

Two rules follow, and they are the whole document:

1. **Every acceptance criterion is classified at planning time as `CI`, `live`, or `manual`.** An unclassified criterion is a planning defect, not an implicit CI test.
2. **The absence of an executed live criterion is a recorded gap — never a pass.** A ticket whose live criterion was not executed is not done; it is done-for-CI with a named, dated, owned gap. A criterion that has no live dimension is not a gap: it is classified `CI` or `manual` like any other — a documentation or inspection criterion is `manual`, with a named reviewer, a date, and an outcome.

---

## 2. The boundary

| | **CI proves** | **Only a live run proves** | **Neither proves** |
|---|---|---|---|
| **What it establishes** | Compiled contracts; unit/integration behaviour on doubles; E2E on a seeded stack; migrations apply on a fresh database; lint, typecheck, i18n parity | Real source shapes and status semantics; pacing against a real gateway; end-to-end chain completion; honest states under real publication lag; actual market aggregates; per-source unit cross-validation | Operator intent; third-party access legality and availability; data correctness against an external authority; disaster recovery; the future; load beyond the tested universe |
| **Evidence type** | Deterministic, repeatable, environment-controlled | Dated, non-repeatable, environment-dependent | Attestation, contract, drill, or external assertion |
| **Responsible** | The PR's required checks + the review gates | The orchestrator's post-merge live run (or the builder on the dev/prod stack) | The builder (named human) |
| **Recorded in** | CI run URL on the PR; review artifacts `.pipeline/plan/reviews/TKT-*-round-N.md` | The ticket file's live-criterion record (verbatim ledger rows, HTTP bodies, symbol names); `.pipeline/plan/run-report.md` (the gitignored live ledger) | The coverage-matrix Method column (§9.4 ledger entry); ticket record; drill record |
| **Fails silently when…** | the doubles are more generous than the source | nobody runs it, or it "passes" on an operational workaround | it is assumed rather than asked for |

### 2.1 What CI proves

The four automated levels of the test strategy, plus the CI scripts:

- **L1** — formulas and pure logic against hand-derived golden values.
- **L2** — endpoints and workers against a real Postgres (Testcontainers) with **every external source doubled** at the HTTP boundary (WireMock.Net), SMTP via MailPit, time via `TimeProvider`.
- **L3** — SPA modules against MSW handlers that mirror the API contract.
- **L4** — the full compose stack with the **synthetic fixture universe** (§8.1 of the strategy), a real browser, MailPit for tokens.
- **Scripts** — i18n parity, advice-vocabulary grep. (*OpenAPI served* is marked integration-level in strategy §5.5 "for visibility"; no CI step calls it, so it is not credited here.)
- **Schema** — EF migrations apply cleanly on a fresh container. This is genuinely proven here and needs no live run.

The common property: **every external input is a double we wrote.** CI therefore proves our logic *given the world we assumed*. It cannot prove the world is that shape. See strategy §7 for the doubles policy and §4 item 2 for the explicit statement that external sources are not test items.

**Not run today:** CI has exactly three jobs (backend restore·build·test; web lint·typecheck·test·i18n·advice-vocabulary·build; E2E compose·health·Playwright). It runs **no secrets scan and no dependency audit** — a green run says nothing about either. Both are open items, not checks that a ticket may cite as evidence.

### 2.2 What only a live run proves

1. **Real source shapes and status semantics.** The difference between a body we modelled and a body the gateway actually returns — an empty `value` array, a `fault` envelope, a `400` with a business error code, a progressively-published day.
2. **Pacing against a real gateway.** Rate limits, penalties presented as auth errors, and per-item latency that only manifests at 100× scale. A double always answers instantly and never rate-limits.
3. **End-to-end chain completion.** That the EOD chain actually reaches `metrics-recompute` → `market-snapshot` → `medians` on a real night, with real durations, under the shipped watchdog defaults.
4. **Honest states under real publication lag.** Whether the product says `partial` (and names what is missing) or quietly publishes a sliver as the whole market.
5. **Actual market aggregates.** The numbers a user sees: breadth, volume, index levels.
6. **Per-source unit cross-validation.** That a field means what we think it means — e.g. whether `currentCapital` is a share count or paid-in capital.
7. **Operational statements that are only observable over time**: uptime (NFR-MOV-003). "Observable only over time" is a property of the evidence, not an acceptance class: human-read uptime resolves to `manual` per §4 (UptimeRobot), and only a deployed run that produces the figure resolves to `live`. (The SC-006 research session is `manual`, not live — a scripted human session with a named owner, date, and outcome (§3). RTO/RPO is a drill, §2.3 — no run establishes it.)

### 2.3 What neither proves

- **Operator intent.** A hand-triggered `metrics-recompute` → `market-snapshot` → `medians` after a failed chain produces a correct row and proves nothing about the scheduler. The row is honest data; it is not a passing schedule.
- **Third-party access legality and availability.** OQ-FDF-002 (TEFAS programmatic access) is a contractual/legal question, unanswerable by any run. A green run against an endpoint we were never granted is not a licence.
- **Data correctness against an external authority.** Our aggregates are provably *what the source served*, never provably *what the market is*. There is no independent BIST authority in the loop.
- **Disaster recovery.** RTO 4 h / RPO 24 h is a drill the builder executes once pre-launch (strategy §5.6); a passing suite is not a restore.
- **The future.** Source schemas change; a source that answers today may not tomorrow. The only compensation is the *next* live run, not a test.
- **Load beyond the tested universe.** 100 symbols, EOD cadence, one user class. Nothing in CI or in a live run says anything about 10,000 symbols.
- **Unrecorded operational drift.** An environment override that makes a broken default look healthy (`Ingestion__Scheduler__MaxRunDuration=04:00:00`, §5.2). This is the most dangerous item in the column: the system behaves and the evidence is a configuration, not a design.

---

## 3. Who is responsible, and what records it

| Verification class | Owner | Gate / step | Artifact of record |
|---|---|---|---|
| `CI` | Coder (writes it), review-lead (gates on it) | PR required checks: backend, web, E2E (lint, typecheck, i18n parity, advice-vocabulary grep, Playwright) | CI run URL in the PR body; review round artifacts under `.pipeline/plan/reviews/` |
| `live` — declared pre-merge | Ticket author (writes the pass/fail condition before the run) | **Post-merge** live run on the deployed stack; the merge itself is gated on CI only | The ticket file: run IDs, statuses, counts, gateway bodies, dates — verbatim. `.pipeline/plan/run-report.md` for the session narrative |
| `live` — discovered during a run | Orchestrator (observes and routes) | Triggered by an anomaly, not by the ticket | New ticket (e.g. 043, 044, 063, 064) or chore (e.g. #130), with the observation verbatim |
| `manual` | Builder | Pre-launch checklist, per-release review, or the SC-006 session | Coverage-matrix Method column entry (`manual`), with owner and trigger (strategy §9.4, §10) |
| `operational` — a Matrix Method sub-case, not a fourth acceptance class | Builder / operator | UptimeRobot monthly, run-ledger monitoring | Matrix entry (`operational`); the acceptance class it resolves to is `live` when a deployed run produces it, `manual` when a human observes it (§4); the live ledger for run-level facts |
| Unprovable today | Builder | Recorded as a gap, escalated: the matrix row keeps its own Method and links a dated gap note on the ticket (§9.4 has no `gap` value and none is added) | The ticket's gap note (rationale, date, owner); never a pass |

---

## 4. The classification rule (normative)

Every acceptance criterion in a ticket carries exactly one label:

- **`CI`** — an automated case at L1–L4 or a CI script can decide it, deterministically, on doubles or on the seeded stack. It must name its TC ID(s).
- **`live`** — only a real run against a real source / deployed stack can decide it. It must be written as a **pass/fail condition before the run** and recorded **verbatim after it**: run ID, status, counts, the source's own bytes where relevant. "It worked" is not a record.
- **`manual`** — a named human step with a date and an outcome, per strategy §10 ("pass = checklist item recorded with date and outcome by the owner").
- **Recorded gap** — the criterion is real but was not executed. It carries the rationale, the date, and the owner, and it is **not a pass**. The gap is a dated note on the ticket, linked from the matrix row: the row keeps its Method (there is no `gap` value in §9.4), and the note is what records the omission. A criterion that has no live dimension is not a gap — it is classified `CI` or `manual` (an inspection or documentation criterion is `manual`).

This is the same classification the coverage matrix already carries in its **Method** column (`unit | integration | component | e2e | script | manual | operational`, strategy §9.4), read from the ticket's side:

| Matrix Method | Default acceptance class |
|---|---|
| `unit`, `integration`, `component`, `e2e`, `script` | `CI` |
| `operational` | `live` when a deployed run produces it; `manual` when a human observes it (UptimeRobot, cost review) |
| `manual` | `manual` |

**No requirement is left methodless.** If a criterion does not appear in that column, the matrix is wrong and the criterion is unverified — see `.pipeline/testing/99-coverage-matrix.md` §4 (NFR coverage) and §6 (success-criteria coverage) for the rows this rule reads.

---

## 5. Worked examples — 2026-10-18 → 2026-10-24

Each example is a real incident from this week. Four of the five below were live-run finds that a passing suite could not see; §5.5 is the fifth, and it is the inverse — a CI failure that was visible on every run and ignored under a standing waiver.

### 5.1 CI was fully green while the product was broken

**The record.** On 2026-10-23 the statements lane requested KAP disclosure indices that no longer existed. The gateway answered, twice independently sampled:

```
HTTP 400
{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}
```

Runs **227 / 233 / 236 / 244** each reported `failed` on that world, and the EOD chain fail-stopped on six attempts that week before `metrics-recompute`, `market-snapshot`, and `medians`. **TKT-foundation-063** (PR #128) replaced the duplicated statements walk with the shared `VykDisclosureWalker`, reset the stale cursor once per company (persisting the outcome), isolated per disclosure detail, and made a zero-write honest-skip run end `partial` instead of `failed`. Its live criterion was then met: **run 249** (paced, 22:21 → 22:46 UTC) reported `partial` — written 0 / unchanged 60 / quarantined 25 — with **2 cursor resets**, one each for TTKOM and TOASO, and no repeated reset.

**Why CI could not see it.** The L2 doubles answer whatever index the fixture asks for; they are not a cursor that has drifted past a moving gateway tail. The gateway's `ER005` is not a transport failure — it is a *business* answer to a question we should not have asked. No unit, integration, or E2E case in the suite contained the concept "the record we persisted last week no longer exists".

**The lesson.** `requested-index-liveness` is a live-only property. A `failed` status was the honest report of a design flaw, and it took a real run to surface it.

### 5.2 The watchdog's 4 h bound was a config workaround, not proof

**The record.** `IngestionSchedulerOptions.MaxRunDuration` shipped at **90 minutes** and bounded **one whole chain attempt**. A healthy paced chain legitimately needs **100–120 minutes** (prices ~5 min @3 s, statements ~13 min @12 s, dividends ~16 min, corporate-actions **58–74 min** at ≥12 s/item, then disclosures + metrics + snapshot + medians). A healthy chain could therefore never finish.

`RUN_HUNG` rows **203** (corporate-actions, step 5 of 9, attempt 4) and **208** (disclosures, step 5) named a "hung step" that was never hung. 203's cancellation at 12:11:24 was **90 minutes after the attempt started**. 208's row carries the span **10:41:24 → 13:41:24**, but the instant that proves the bound is its cancellation at **13:41:24** — exactly 90 minutes after the *morning chain's* attempt started at **12:11:24**, not the three-hour span. The operational unblock was a configuration override, `Ingestion__Scheduler__MaxRunDuration=04:00:00` — the system then behaved, and the shipped default was still broken. **TKT-foundation-064** replaces the whole-attempt bound with a **per-step bound (120 min)** plus a **4 h chain ceiling**, and makes the ledger state which bound fired.

**Why CI could not see it.** The fake-clock scheduler tests never constructed a *slow but healthy* chain; they simulated hangs. The suite proved `RUN_HUNG` fires; it could not prove it fires for the right reason at the right scope.

**The lesson.** A green suite plus a tuned environment is the signature of a defect being managed, not fixed. The evidence that mattered was a cancellation instant in a ledger row: 13:41:24, exactly 90 minutes after the attempt start at 12:11:24 — not the three-hour span the same row also carries.

### 5.3 A thin day published as "the market"

**The record.** İş Yatırım's daily feed publishes **progressively**. On 2026-10-23 only **9 of 100** symbols had a bar at **17:36 UTC**. The `market-snapshot` job computed from whatever was present and published:

> `breadth { advancing: 0, declining: 9 }`

Real numbers, false picture: it reads as "the whole market fell". The ledger's own record of that evening is that coverage reached **64 of 100 by 20:09 UTC**, with the `prices` → `metrics-recompute` → `market-snapshot` → `medians` re-run still *planned*, not completed — so the aggregate a full-coverage re-run would have produced was never captured verbatim, and is not quoted here. What the record does carry is where the sliver came from, below.

The root cause was ours, not the source's: run **243** (`succeeded`, written 18 / unchanged 71 / quarantined 0) recorded **11 constituents as `written=0 unchanged=0 quarantined=0 failed=false`** — nothing at all. Forty minutes later, direct probes showed **9 of those 11 had a real session** at the source (ALARK 101.30, CIMSA 37.44, DOHOL 16.38, EGEEN 3895.00, GRSEL 247.00, KRDMD 38.60, LMKDC 19.50, MPARK 387.00, TKFEN 174.80); only ANSGR and BERA genuinely returned empty. The EOD step never asserted day coverage. **Chore #130** (`chore(prices): complete the day's coverage before reporting success`, PR #130, merged 2026-10-24T00:09Z) now records the honest per-symbol empty reason, re-sweeps missing symbols within a bounded budget, and reports **`partial` naming the missing symbols** instead of claiming success. **TKT-foundation-043** was closed as superseded by it (USER DECISION D8-A); its record is also where the recovery is written down: a re-run wrote exactly those 11 → **100/100**.

**Why CI could not see it.** Every fixture symbol has a bar; the fixture universe has no concept of "the source is 9% published". The suite asserted an aggregate computed from 100 of 100, always.

**The lesson.** Publication lag is a *source property*; coverage assertions are the code's duty. CI proves the code computes; only a live run proves what it published, and only against the clock.

### 5.4 A source that was never wired looked "implemented"

**The record.** Fund jobs failed instantly — **~0.4 ms**, `error: NULL`, `stats_json: {"symbols": [], ...}` — from ~2026-10-18 for days. The cause was that **`Ingestion:Funds` existed in no configuration at all** (`grep -c Funds` → 0 across `appsettings.json`, compose files, and the environment). The empty `BaseUrl` fell back to a `http://localhost` placeholder, the connect was refused immediately, and the `HttpRequestException` escaped the job's `SourcePayloadException` catch. **TKT-foundation-044** (PR #132) landed `IngestResult.FailureReason` with a vocabulary (`SOURCE_FETCH_FAILED`, `SOURCE_NOT_CONFIGURED`, `UNPARSEABLE_PAYLOAD`, `FAILED_UNSPECIFIED`), made `RecordAsync` always write `ingest_runs.error` on a failed row, and made the TEFAS adapter refuse an unconfigured source **by name before any request hits the wire**. Funds remain out of v0 scope pending OQ-FDF-002 — a scope decision, recorded as such, not a defect.

**Why CI could not see it.** There is no test for a configuration key that exists in no configuration. `symbols: []` also looked like a *data* signal; both planner hypotheses (empty universe, TEFAS down) were refuted, not merely unproven.

**The lesson.** "Implemented" is a claim about wiring, and wiring has a runtime existence that no test asserts unless the failure is observable. The fix was not the funds feature — it was making the *reason* legible, because that is what a live run needs to be diagnosable at all.

### 5.5 A test defect masqueraded as a product defect

**The record.** **TC-XC-004** (scroll preservation across a filter change) failed for days and carried a standing waiver. **TKT-foundation-060** measured before fixing: at the pinned viewport the filtered list's `maxScroll` was **157**, so `scrollTo(0, 250)` clamped to 157 — **the "mid-page" premise never existed**, and the toggle click then scrolled it into view (157 → 0). Controls confirmed it: another header button reset the scroll identically; an in-page dispatched switch preserved 157 (delta 0); a long list showed the same artefact. Determination: **(b) test defect**. The fix was test-only — viewport pinned 1280×400, an explicit `expect(scrollBefore).toBe(250)` so a clamp can never make the case vacuous, the handler dispatched directly, tolerance unchanged at 60. CI E2E came back **76/76 green** — the first PR in days without the waiver — and the review-lead **retired the TC-XC-004 standing-waived class** in `.pipeline/plan/reviews/WAIVERS.md`.

**Why CI could not see it.** It saw it every run — as a red that everyone had agreed to ignore. The proof that it was a test defect came from a *measurement*, and measurements are what an investigation produces, not what a suite produces.

**The lesson.** A waiver is a claim that a failure is not ours. It is only true when someone measured it. Until then it is an unowned red line, and the "product is broken" story is the default because it is the cheaper hypothesis.

### 5.6 Two more from the same week, in one line each

- **TKT-foundation-032 — the `/api/vyk` prefix was dropped by URI composition**, so every real-gateway request 404'd on the bare host. The L2 WireMock doubles sit at the root, so **nothing in CI could produce that 404**. Fixed in PR #104; live criterion met on the shipped config with zero 404s across a 1,501 s statements run.
- **TKT-foundation-058/065 — the share count is unproven in unit.** Run 219 wrote **556** rows, but the served field is `currentCapital`, and **64 of 556** carry sub-unit precision (LYDYE 1,831,580.69; AYCES 7,181,451.329). Equality with a share count holds only where nominal = 1 TL. CI verified the *contract*; only a live pull could show the *unit* was never established (USER DECISION D4: relabel as paid-in capital).

---

## 6. Checklist for landing a ticket

Paste evidence **here**, or the ticket is not done:

1. **Classify before implementing.** Every acceptance bullet carries `CI` / `live` / `manual` (or an explicit `gap`). No bullet is unlabelled.
2. **`CI` bullets** — named TC IDs, tests tagged with them, and the **green CI run URL** in the PR body. A red lint, typecheck, i18n-parity, or advice-vocabulary check is a red ticket, exactly like a failing test. (CI runs no secrets scan and no dependency audit today; neither may be cited as evidence.)
3. **`live` bullets** — the pass/fail condition is written **before** the run (in the ticket, at planning or PR time). After merge, paste **verbatim**: run IDs and timestamps, the resulting status, the written/unchanged/quarantined/failed counts, symbol names where they matter, and the source's own bytes for any error class. If it was not run, say so in one line with the date, the reason, and the owner — that is a **gap**, mark it, do not tick it.
4. **`manual` bullets** — the checklist item, its date, its outcome, and the owner's name.
5. **Deviations** — a design change is recorded in the architecture and test-plan documents **first**, then in the ticket. Never only in the code, never only in the ticket (strategy §14).
6. **Ticket status** — `in-review` with the PR link; the live criterion's state (`met` / `pending` / `gap`) rides the ticket until the last one closes.
7. **No number without provenance** — every figure in a ticket names the run that produced it or the probe that returned it. A number that cannot be traced is deleted, not softened.

A ticket whose live criterion is `pending` may be **merged**, because CI is the merge gate. It is **not** evidence that the feature works, and the acceptance phase must see the gap before it can claim the success criterion.

---

## 7. Why this is not pessimism about CI

CI is what makes the product maintainable: it catches regressions in the 90% of behaviour that is deterministic and ours. The incidents above are not CI failures; they are **boundary failures** — cases where a green check was read as a statement about the world, or a waived red as a statement about the product. The remedy is not more doubles. It is stating, once and in the same language every time, which claims the doubles can support.

The one thing this document forbids is the quiet substitution: presenting a `live` criterion as satisfied because CI is green, or presenting a `manual` step as done because it was described.
