---
scope:
  - tools/source-probes/
  - tests/Core.UnitTests/SourceContractProbeTests.cs
  - tests/Core.UnitTests/Degerli.Core.UnitTests.csproj
  - docs/source-contract-probes.md
---

<!-- SCOPE AMENDMENT 2026-10-24 (orchestrator, from the 068 coder's blocker report).
     `tests/Core.UnitTests/Degerli.Core.UnitTests.csproj` was added to `scope:` because it is the
     only way a C# file inside `Core.UnitTests` can compile against a harness living under `tools/`
     — the repo has no Directory.Build.props/targets hook in the granted scope, and the established
     precedent (the shared fixture module) is a referenced project. The change is exactly one line:

       <ProjectReference Include="../../tools/source-probes/Degerli.SourceProbes/Degerli.SourceProbes.csproj" />

     No Degerli.sln edit is required (transitive project-reference restore/build verified by the
     coder). This is a mechanical build-wiring line, not new product behaviour: it does not widen
     what 068 may change, it lets the already-written in-scope test file compile. -->

# TKT-foundation-068: Source-contract probes — live shape-drift detection against the committed doubles

- Status: done — **PR #142 squash-merged 2026-10-25 (~05:00 UTC), final head `afad672`, CI green (Backend/Web/E2E), MERGEABLE/CLEAN.** Review rounds 1–2 (`reviews/TKT-foundation-068-round-{1,2}.md`; round 2 = merge basis). Round-1 blocker fixed-verified: the `evds-macro` probe was repinned from a credentialed `GET …&key={apiKey}` to the architecture-pinned **anonymous `POST /igmevdsms-dis/fe`** with the §10.9.12 body verbatim (no credential in the v3 request) + a new test pinning POST/URL/no-key/empty-secrets; the `IsEmptyCollectionShape` fabrication (non-array container reported as "answered empty") fixed with an array-kind guard + path-structured suppression (siblings survive). Final green: **SourceContractProbeTests 21/21, Core.UnitTests 75/75**, solution build 0 warnings, transitive restore with no `Degerli.sln` edit. Compile-failure red (5×CS0234 + 3×CS0246 without the reference) judged genuine-but-narrow — the correct red for the one-line build wiring. Mutation reds verified statically reproducible. No network from tests; no report written inside the repo. **Owed residue (non-blocking nits, ride-along candidates for a future commit):** DR2-1 Yahoo provenance remark cites §10.9.3 instead of §10.9.1/`03` §1.2; DR2-2 the EVDS test pins 8 of the 16 documented body fields; DR2-3 the PR-body green line said 20/20 & 74/74 (superseded numbers above are accurate). Run mode is manual CLI; a scheduled dev-box run is a recorded future decision, not this ticket.
- Size: M
- **2026-10-24 — implementation BLOCKED on scope, now unblocked.** The coder completed the whole
  harness, 8 contracts, 20 tests and the runbook on `feature/TKT-foundation-068` (commit `087c97c`,
  pushed; no PR opened on purpose) and returned a blocker: the in-scope test file cannot compile
  without the test project referencing the `tools/` harness. The scope has been amended above with
  that one line. **Re-dispatch to add the `ProjectReference`, verify `dotnet build Degerli.sln`
  clean, and open the PR** — the substance is already written and verified (20/20 green, two honest
  mutation reds). Do not redo the harness.
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 3 unparked).**
- **Evidence (gathered 2026-10-24 — the drift incidents are real, not hypothetical):**
  - **İş daily feed vs chart feed (2026-10-23, TKT-foundation-047 live criterion):** the live `HisseTekil` response for the current session returned an **empty `value` array** for all 100 symbols (run 218: every symbol honestly `skipped`), while the Yahoo chart feed carried the session. No committed double predicted that a valid-looking envelope could be empty for the current session; the source choice flipped at runtime.
  - **KAP `ER005` stale-cursor class (2026-10-23, TKT-foundation-063):** the real gateway answered the persisted-cursor indices with **HTTP 400 `{"code":"ER005","message":"Bildirim bulunamadı."}`** for 22–24 of 30 companies per run (runs 212/227/233/236/244), while CI's doubles answered the requested indices normally. The class was invisible to the test suite.
  - **KAP base-path bug (TKT-foundation-032, `v1.5.10`):** every real call 404'd because `/api/vyk` was dropped by URI resolution, while CI was green — the L2 doubles bind at the server **root**, where root-relative and base-relative resolution coincide. A root-bound double cannot catch a prefix-carrying gateway.
  - **VYK `/memberSecurities` unit question (TKT-foundation-058/065):** the served value is `currentCapital` (paid-in TL); 64 of 556 rows carry sub-unit precision. Only live probing could settle it; the committed double could not discriminate a count from money.
  - The committed doubles are deliberately **shape-faithful captured bodies** (`01` §10.9), but nothing re-checks the live shape against them after the capture date.
- **Pinned approach:**
  1. A **probe harness** (not a required CI check) with one probe per source: İş prices (`HisseTekil`), the Yahoo chart contingency, KAP `/disclosures` + `/disclosureDetail`, `/members`, `/memberSecurities`, EVDS macro, TEFAS funds. Each probe:
     - makes **one bounded, paced** real request (honour the existing pacer intervals — KAP ≥12 s, İş ≥3 s);
     - compares the **structural shape** only (required keys, types, envelope discriminators, status semantics) against the committed double's shape;
     - emits a machine-readable drift report: source, field, expected vs observed, timestamp, redacted body excerpt.
  2. **Never values, never raw bodies.** Values change daily; the probe asserts shape, not data. Secrets are redacted; no payload is retained.
  3. **Never auto-edits a double.** A double is a contract artifact; drift produces a report + a routing ticket, and a human updates the double with a fresh capture (the §10.9 capture discipline).
  4. **Run mode decision to record before implementation:** manual CLI (recommended minimum), a scheduled run on the dev box, or an admin-triggered endpoint. Whatever is chosen, it must **not** gate PRs — live sources are rate-limited and legitimately flaky.
- **Acceptance (explicit criteria):**
  - **(i)** One probe per listed source runs paced and writes a report; a deliberately drifted local fixture (a double mutated in a unit test) is flagged field-by-field, and an unchanged shape is clean.
  - **(ii)** The harness is not wired into required CI (no PR trigger); the doc states why.
  - **(iii)** Secrets are redacted and raw bodies are not retained or committed.
  - **(iv)** The runbook (`docs/source-contract-probes.md`) names the coverage set, the run modes, the report location and the drift→ticket flow.
  - **(v)** No architecture amendment expected; if `01` §10.9's posture text changes, append a §13.1 row at that time (next free above the table max).
- Traces to: FR-MDF-008 (provenance), FR-MDF-010/011 (honest achieved depth), `01` §10.9 (real-source envelope contracts), test strategy §9.4 (recorded verification).
- Architecture refs: `01-system-architecture.md` §10.9 (envelope contracts and capture discipline), §6.1 cited (why live probes are not CI checks).
- UX refs: none.
- Dependencies: none.
- Parallel group: **P-1** (scopes disjoint from 074/075/076; no shared files).

---

## Implementation record (2026-10-24) — BLOCKED on one out-of-scope line

**Delivered on branch `feature/TKT-foundation-068` (no PR opened — see the blocker).**

- `tools/source-probes/Degerli.SourceProbes/` — the probe harness (BCL-only, no new dependencies):
  flat shape extraction (`JsonShapeExtractor`), the drift comparer (`ShapeComparer`, seven finding
  kinds incl. `element-shape-missing` and `status-changed`), the three-outcome probe result
  (`ProbeOutcomeKind.Clean|Drift|Unreachable`), the report model, `Redactor`, `ProbePolicy`
  (pacing floors + chain quiet windows + explicit live opt-in), the 8-source registry, the live
  executor (one bounded, no-retry request) and the CLI (`list`/`show`/`check`/`shape`/`probe`).
- `tools/source-probes/contracts/*.json` — 8 checked-in expected-shape contracts, **derived from
  the committed doubles** via the harness's own `shape` command (provenance, catalog entry and
  `derivedAt` recorded per file; no body values are retained).
- `tests/Core.UnitTests/SourceContractProbeTests.cs` — 20 doubles-only cases mapping to the
  acceptance criteria (i) shape-equal clean / missing / extra / type / nested-array / nullability /
  empty-collection / status-400, (ii) not wired into CI, (iii) unreachable-never-clean + redaction +
  value-free report, (iv) coverage set + pacing + runbook presence.
- `docs/source-contract-probes.md` — runbook: coverage set, drift meaning per finding kind, run
  modes, politeness, report location, drift→ticket flow, the two incidents as worked examples, the
  explicit "report-only, never gating" rule.

### The blocker (scope defect)

`scope:` grants `tools/source-probes/`, the test file and the doc — **but not the test project's
own project file**. A C# file at `tests/Core.UnitTests/SourceContractProbeTests.cs` can only
compile against the harness through a project reference; no `Directory.Build.props/targets` hook
exists inside the granted scope (the repo's own precedent — the shared fixture module — is a
referenced project). Without it the test file fails to compile (CS0234 ×4+), so the branch is
deliberately not PR'd. The exact one-line widening needed:

```xml
<!-- tests/Core.UnitTests/Degerli.Core.UnitTests.csproj, after the src/Core reference -->
<ProjectReference Include="../../tools/source-probes/Degerli.SourceProbes/Degerli.SourceProbes.csproj" />
```

No `Degerli.sln` edit is required: solution restore/build traverse project references, so CI's
`dotnet restore/build Degerli.sln` picks the harness up transitively (verified locally).

### Evidence (with that one line applied locally, then reverted)

- green: `SourceContractProbeTests` 20/20 passed; full `Core.UnitTests` 74/74 passed.
- red (mutation, honest label): with the drift comparison stubbed to return no findings → **8
  failed / 12 passed**; with the unreachable mapping removed → **2 failed / 18 passed**. The tests
  bind to the behaviour, they do not pass vacuously.
- offline CLI smoke: `check --source kap-disclosures --body <double>` → clean; a mutated copy →
  `missing-key $[].companyId`, `type-changed $[].disclosureIndex`, `extra-key $[].newField`;
  the İş empty-`value` body → one `element-shape-missing $.value[]`.
- no live source was contacted; no report was written inside the repository (the tool refuses).

### Acceptance status

(i) met by the tests + CLI smoke · (ii) met (`AC68ii_ProbeHarness_IsNotWiredIntoCi` scans every
workflow/compose/CI-Dockerfile) · (iii) met (redaction + value-free report assertions) · (iv) met
by `docs/source-contract-probes.md` · (v) no architecture amendment required (§10.9 posture
unchanged; nothing appended to §13.1).

**Do not merge this branch until the ProjectReference above is granted**: `dotnet build Degerli.sln`
is red without it.

---

## Resolution record (2026-10-25) — unblocked, PR open

The scope amendment on `dev` (this file's `scope:` block) granted
`tests/Core.UnitTests/Degerli.Core.UnitTests.csproj`, so the blocker above is resolved. Executed
as granted — **one `ProjectReference` line**, no `Degerli.sln` edit (solution restore/build picks
the harness up transitively):

```xml
<ProjectReference Include="../../tools/source-probes/Degerli.SourceProbes/Degerli.SourceProbes.csproj" />
```

- Rebased onto `dev` @ `d147e9d`; applied cleanly, no conflict resolution, no file touched outside
  the ticket's scope, and the harness compiled unchanged (no namespace/API drift from 064/078/048).
- Red without the line: `CS0234` ×5 unique + `CS0246` ×3 unique in `SourceContractProbeTests.cs`
  (five distinct missing-namespace diagnostics, one per `using`, plus three missing-type
  diagnostics; MSBuild's duplicated log lines make it look like 6/3).
  Green with it: `SourceContractProbeTests` 20/20, `Core.UnitTests` 74/74, `dotnet build
  Degerli.sln` 0 warnings / 0 errors.
- PR: https://github.com/sarperim/degerli/pull/142 (base `dev`). Acceptance (i)–(v) unchanged
  from the record above. No live source was contacted; no report written inside the repository.
