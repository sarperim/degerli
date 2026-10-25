---
scope:
  - IMPLEMENTATION_STATUS.md
  - README.md
  - scripts/implementation-status.sh
---

# TKT-foundation-074: IMPLEMENTATION_STATUS.md — a standing implementation-status surface

- Status: done — **PR #136 squash-merged 2026-10-25 02:11 UTC (`1a9ebe0` on dev), head `9621cbb`, CI run 40152779807 (Backend/Web/E2E green).** Review history: rounds 1–3 with 2 fix rounds; **round 3 escalated on DR3-1** (the 2026-10-23 prices 100/100 was falsely attributed to chore #130's re-sweep, which merged 2026-10-24T00:09Z — after the observation; the 100/100 was an operator re-run per ticket 043). **USER AUTHORIZED one more fix round (verbatim: "fix it then", 2026-10-25 ~01:50 UTC); round 4 closed DR3-1 and all riding minors (DR3-2..DR3-11).** Round artefacts: `.pipeline/plan/reviews/TKT-foundation-074-round-{1,2,3,4}.md` (round 4 = the merge basis). Non-blocking residue DR4-1/DR4-2 (ticket-file hygiene nits) recorded in the round-4 artefact.
- PR: https://github.com/sarperim/degerli/pull/136
- Size: S
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 11 unparked).**
- **Evidence (gathered 2026-10-24):**
  - No implementation-status file exists at the repo root (`ls` → `README.md`, `deploy/`, `.pipeline/`, source trees). The board is **gitignored/regenerable** by design (`.gitignore`), and ticket files are the source of record — there are 140+ of them.
  - Answering "what is actually implemented vs pending vs blocked, and which live criteria are still open?" requires reading individual tickets: e.g. 063's live run 249, 064's 4h workaround, 065's 64/556 sub-unit finding, 044/045/048 still `todo`.
  - `run-report.md` is a running transcript, not a standing status. The acceptance agent writes a report at the end, not a maintained surface.
  - The risk this ticket must avoid is the one the board already has: a status artifact that silently goes stale.
- **Pinned approach (decision recorded):**
  - Create a committed `IMPLEMENTATION_STATUS.md` with: per-domain implemented / pending / blocked, the **live-criteria ledger** (ticket, criterion, status, evidence link), known gaps and parked items, and an explicit **update rule**.
  - **Options recorded:** (a) hand-maintained with a "last verified" date and a rule that every merged ticket updates it (simple, drifts); (b) **generated from ticket frontmatter/status by a script, committed, with the script failing when stale** (repeatable, more work — recommended if a small parser is feasible; wiring it into required CI is a follow-up, since `ci.yml` belongs to TKT-foundation-066 this wave); (c) generated on demand, never committed (no standing surface).
  - Never invent status: every line cites a ticket or a doc; unknown is written as "not recorded".
  - The board remains a view; this file is a deliberate standing artifact.
- **Acceptance (explicit criteria):**
  - **(i)** The file exists and lists every domain plus every non-done ticket's status and blocker, each with a source citation.
  - **(ii)** The update rule is documented and mechanically checkable (script or CI check), or — if option (a) is chosen — the doc says honestly that it is manual and carries the last-verified date.
  - **(iii)** If generated/checked, a deliberately stale entry is caught by the script; the check is deterministic and does not depend on GitHub state. Wiring it into required CI is a follow-up (TKT-foundation-066 owns `ci.yml` in this wave).
  - **(iv)** `README.md` links to it; no status is asserted without a citation.
  - **(v)** No source change; no architecture change (this is a delivery artifact, not a contract).
- Traces to: none directly (delivery/traceability artifact; supports the acceptance lane and SC-007's honesty posture).
- Architecture refs: none.
- UX refs: none.
- Dependencies: none.
- Parallel group: **P-1** (scopes disjoint from 068/075/076).

---
## Implementation record (coder, 2026-10-24)

### What landed

- **`IMPLEMENTATION_STATUS.md`** (repo root): 19 domain rows, each with one fixed-vocabulary
  status (`live`, `live-degraded`, `ci-only`, `wired-not-configured`, `scaffolded`,
  `blocked-upstream`), a
  one-line cited evidence pointer and a `Checks` cell naming the code/config the claim stands on;
  a **live-criteria ledger** (LC-01..LC-19: claim / what CI proves / what only a live run proves /
  last live observation, dated or explicitly `not yet observed`); the open-ticket ledger for all
  26 non-terminal tickets; known gaps and parked items; and the **update rule**.
- **`scripts/implementation-status.sh`**: read-only, no network, no DB, deterministic (clock
  pinnable with `--today`). It fails when a documented claim no longer holds: a `live`/
  `live-degraded` domain whose `dir:`/`file:` is gone, a claimed `config:` key missing from
  `src/Api/appsettings.json`, a `config-empty:` claim that is no longer empty (EVDS key), a
  `noconfig:` claim that is now configured (Funds), a status word outside the fixed vocabulary, a
  missing required section, a ledger row without a dated/`not yet observed` last observation, a
  missing README pointer, an open ticket missing from the ledger (the set of
  `todo`/`in-review`/`in-progress`/`blocked`/`deferred` tickets must match exactly), a ledger row
  whose class disagrees with its ticket, and a `Last verified` date older than 60 days.
- **`--self-test`** (the suite has since grown to 18 cases, see Fix round 1): builds a valid
  fixture in `mktemp -d`, then breaks it seventeen ways (missing implementation path, absent config
  key, empty-vs-absent config, section claimed absent but present, undeclared status word, missing
  section, undated ledger row, missing open-ticket row, multi-word status counted as open, stale
  date, ticket/doc class mismatch, README without the pointer, empty Evidence cell, `live` row with
  no LC citation, citation of a nonexistent LC, `live` row whose only LC is undated, and an
  impossible `--today` usage error) and requires a non-zero exit for each, with the valid fixture
  passing — **18/18 cases pass**. No fixture files are committed (they are generated in `/tmp`), so
  the ticket's three-file scope is exactly what changed.
- **`README.md`**: one pointer paragraph in "Project Status".

### Cross-checks performed (bash accuracy over optimism)

Read against the tree at `origin/dev@8f4b5e5` plus the appsettings/ticket text, not guessed:

| Row | What was checked | Result |
|---|---|---|
| Funds | `src/Api/appsettings.json` has **no** `Funds` section (`python3 json` key walk: `Ingestion` keys = Scheduler, TradingCalendar, Prices, Intraday, Universe, Kap, Statements, Dividends, CorporateActions, Disclosures, Macro, MarketSnapshot) and `TefasSourceOptions.SectionName = "Ingestion:Funds"` exists in code with a `SOURCE_NOT_CONFIGURED` guard | `wired-not-configured` (OQ-FDF-002), encoded as `noconfig:Ingestion:Funds:BaseUrl` — the checker fails the day someone wires it until the row is updated |
| Statements | `Ingestion:Statements:Path` present; TKT-foundation-063 live run 249 `partial` (0 written / 60 unchanged / 25 quarantined) | `live-degraded`, not "done" |
| Prices | `Ingestion:Prices:UniverseIndex = XU100`; the 2026-10-23 9/100 thin day (9/100 at 17:36 UTC → 64/100 at 20:09 UTC, run 243's eleven all-zero constituents) repaired to 100/100 only by an operator re-run (undated in ticket 043); chore #130's in-run gate merged 2026-10-24 and has not run on a trading day; 043 superseded, not done | `live-degraded` |
| Intraday | `Ingestion:Intraday:BaseUrl` present; appsettings `Path` is now the `ChartData/IntradayDelay` chart endpoint (chore #129) while the only live sweep (run 218) predates the repoint | `ci-only`, ledger LC-06 `not yet observed` |
| Shares | Shares lane reads the **Kap** section (no own config section) and is **not** in `EodIngestChain.JobCodes` (universe-sync, prices, statements, dividends, corporate-actions, disclosures, metrics-recompute, market-snapshot, medians) — so run 219 was operator-triggered | `live-degraded`, unit claim unresolved (065 blocked) |
| Pipeline | `IngestionSchedulerOptions` ships the 120-min per-step / 4-h chain defaults since 064 merged (PR #133); the old 90-min whole-attempt bound and the 4 h env override are both gone (fix round 1) | `live-degraded`; LC-05 records that 064's live criterion is **unrun**, so the watchdog is not called solved |
| Macro | `Ingestion:Macro:EvdsApiKey` exists but is empty; TKT-foundation-019 recorded "constructed, capture pending" | `live-degraded` with `config-empty:` — the manual-entry path, not an EVDS pull, is what was observed 2026-10-20 |

### Honesty notes (deliberately not flattered)

- The snapshot row is `live-degraded` and LC-07 says the coverage guard has **no ticket yet**
  (user decision D7), so the nine-symbol aggregate is recorded as a live counter-example rather
  than a fixed defect.
- TKT-foundation-065 is listed by its **ticket** status (`partially resolved`); the user's D4-A
  decision is recorded verbatim on the ticket, and the display half (PR #137) is separated from
  the still-owed contract half rather than being counted as done.
- TKT-foundation-061/044 rows stay `in-review` (PRs #125/#132 merged, ticket flips pending) — the
  checker's one documented tolerance, stated in the open-ticket section and the update rule.
- CI does **not** run the checker: `ci.yml` belongs to TKT-foundation-066. The gap is stated in
  the file's "Known gaps" rather than implied to be wired.

### Evidence

- *Superseded by the fix-round-1 evidence below* — design-time counts only: `--self-test`
  **11/11 cases**, checker **131 checks, 0 failures**. The authoritative pair is 18/18 cases and
  157 checks.

---
## Fix round 1 (fixer, 2026-10-24) — review `.pipeline/plan/reviews/TKT-foundation-074-round-1.md`

The branch was **merged forward onto `origin/dev@aa2d0b8`** (048 and 078 merged, 064/065/072/075
flipped), then the document and the checker were corrected. All five blockers and all five majors
are addressed; no finding was disputed and none required an escalation.

- **DR-2 (staleness):** the merge is part of the fix. `bash scripts/implementation-status.sh` on
  the merged tree exits **0** — 157 checks, 0 failures. The open-ticket ledger now carries 078 and
  drops the three tickets that left the open set (048/072/075); 045/064 flip to `in-review`, 065 to
  `partially`.
- **DR-1 (KAP currency):** a callout above the stamp and the five KAP-sourced rows now state that
  `Ingestion:Kap:BaseUrl` is KAP's **DEV** gateway, head `1231017` = "Test Bildirimi" dated
  31.12.2023, `financial_statements` 2022-12-31 → 2023-09-30, production hosts not resolving,
  **OQ-KAP-PROD** outstanding. New ledger row **LC-15** records the probe with its date, and it is
  cited by statements, disclosures, dividends, corporate actions, fundamentals serving and share
  counts.
- **DR-3 (064):** the 90-minute/4 h-override claim is gone. The pipeline row and LC-05 state the
  shipped 120-min per-step / 4-h chain defaults (`DefaultMaxStepDuration`/`DefaultMaxRunDuration`,
  PR #133) and that **the live criterion is unrun**, with the calendar facts as of the 2026-10-24
  verification (a Saturday: `TradingDaysOnly` morning chain, no admin *chain* trigger) and the
  earliest honest slot **Monday 2026-10-26**. Worked example 3 is now explicitly historical.
- **DR-4 (disclosures/LC-11):** run 252 is recorded verbatim (partial, 50 written, 50/21/50/970,
  truncated false, cursor to head, 864 s, TRALT/TRMET `COMPANY_IDENTITY_UNRESOLVED`) on the
  disclosures row, LC-11 and the new **LC-16**, together with the still-unrun second-run
  incremental half and LC-15's currency truth.
- **DR-5 (checker false negative):** terminal detection inverted to an explicit allowlist
  (`done`/`superseded`/`cancelled`); every other status word — including `partially resolved` — is
  open and must have a row. Self-test case added for a multi-word/unknown status.
- **DR-6..DR-10:** `docs/live-vs-ci.md` marked committed/`done`; 065/072 attribution corrected
  (D4-A recorded on the ticket, display half in #137); 048 recorded as `done` with its live
  criterion unrun and **market overview only** (+ new LC-19, stock-page follow-up named); `live`
  rows now rest on dated LC observations (new LC-17 for admin; accounts demoted to the new
  `ci-only` term after no dated observation could be found — LC-18 says `not yet observed`); the
  Evidence cell is now parsed (non-empty, every cited LC must exist, `live` needs a dated one) and
  the update-rule line names the steps the script actually enforces.
- **DR-11..DR-15:** six runs described as four named ER005 runs plus two earlier zero-write
  attempts; statements coverage corrected to **25** instruments; the 075 citation dropped from the
  pipeline row; self-test header/count/message assertions and the missing-ticket case fixed;
  `config:` distinguishes present-but-empty from absent; `--today` validates as a real calendar
  date (usage error, exit 2).

### Evidence
- `scripts/implementation-status.sh --self-test` → **18/18 cases behaved as required** (exit 0).
- `scripts/implementation-status.sh` on the merged tree → **157 checks, 0 failures** (exit 0).

---
## Fix round 2 (fixer, 2026-10-24) — review `.pipeline/plan/reviews/TKT-foundation-074-round-2.md`

Six residual accuracy items; all fixed in place, none disputed, none escalated. No test, rule or
claim was weakened — every change makes the prose say what the checker actually does, or narrows
a claim to what the run-report supports.

- **DR2-1 (Evidence rule):** the prose said *every* domain row must cite an `LC-nn`; the checker
  enforces it only for `live`/`live-degraded`. The domain-section note and update rule 6 now say
  exactly that (`blocked-upstream` rows may cite none), and the script's own comment/message were
  aligned.
- **DR2-2 (ledger forms):** the rule now states both accepted forms,
  `not yet observed` / `never observed` (which is what LC-08 uses and the checker accepts).
- **DR2-3 (accounts):** the claim is narrowed to registration, reset *delivery* and settings
  changes; LC-18 records that the 2026-10-23 dev session came from a DB-level reset and that its
  sign-in only reached the admin trigger (run-report:159, :388) — not an acceptance observation.
  `ci-only` is kept.
- **DR2-4 (061):** run 252 is attributed to 078; 061's own live criteria (a Friday EOD **chain**
  with an honest CA `partial`; the 2026-10-22 admin-triggered **chain** recovery) are stated as
  still unrun.
- **DR2-5 (this ticket):** the design-time 11/11 / 131-check pair is marked superseded and the
  authoritative pair is the 18/18 / 157 record above.
- **DR2-6 (update rule):** "steps 2–6 are enforced by the script" is replaced with what is
  mechanical (stale date, open-ticket row/class, evidence & `Checks` resolution, observation-cell
  parse, `live` + dated `LC-nn`) versus review discipline (same-commit, right-row reflection,
  verbatim observations, steps 7–8).

### Evidence
- `scripts/implementation-status.sh --self-test` → **18/18 cases behaved as required** (exit 0).
- `scripts/implementation-status.sh` → **157 checks passed**, exit 0 (last verified 2026-10-24).
