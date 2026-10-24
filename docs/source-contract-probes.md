# Source-contract probes — live shape-drift detection against the committed doubles

**Status:** implemented by TKT-foundation-068 (2026-10-24) · **Owner:** builder (operator-invoked)
**Traces to:** FR-MDF-008 (provenance), FR-MDF-010/011 (honest achieved depth), `01-system-architecture.md`
§10.9 (real-source envelope contracts and capture discipline), §6.1 (why live checks are not CI
checks), test strategy §9.4 (recorded verification).

---

## 1. What this is

The product parses **real source envelopes** (`01` §10.9): İş Yatırım `HGDG_*` prices, KAP VYK
list/detail envelopes, EVDS 3.0 macro rows. The committed doubles in `fixtures/Degerli.Fixtures`
are shape-faithful captured bodies — but **nothing re-checks the live shape against them after the
capture date**. The harness under `tools/source-probes/` closes that gap:

- it makes **one bounded, paced** request per source (or reads a saved body offline);
- it compares the **structural shape** of the response — paths, kinds, nullability, array nesting,
  envelope discriminators, status — against the shape of the committed double for that source;
- it writes a **drift report** naming every drifted path, expected vs observed.

It compares **shape, never values**. It never edits a double. It is **never a required CI gate**.

## 2. Report-only, never gating (the rule)

**This harness must not be wired into CI.** Not as a step, not as a job, not as a background
sweep, not as a `needs:` dependency. Reasons, all real:

1. **Live sources are rate-limited and legitimately flaky.** A gateway that is slow at 20:00 says
   nothing about a pull request. Gating a PR on it converts a source hiccup into a red build.
2. **A drift report needs a human.** The answer to drift is: read the finding, decide whether the
   adapter or the double is wrong, re-capture with the §10.9 discipline, update the double, and
   re-derive the contract. None of that is mechanical, and none of it belongs in a PR check.
3. **`01` §6.1** pins CI as deterministic and hermetic: no live third-party dependency in the
   backend/web/e2e jobs.

Mechanically enforced, not just documented: `tests/Core.UnitTests/SourceContractProbeTests.cs`
(`AC68ii_ProbeHarness_IsNotWiredIntoCi`) fails if any workflow, compose file or CI Dockerfile
mentions the harness. The CI-runnable half of this ticket is the **doubles-only** unit suite: it
proves the comparison logic, not the live shapes.

## 3. The coverage set

One probe per source, each pinned to the committed double its expected shape was derived from
(`tools/source-probes/contracts/<key>.json`, field `double`).

| Probe key | Source | Double (shape source) | Provenance | Pacing floor |
|---|---|---|---|---|
| `isyatirim-prices` | İş Yatırım `Common/Data.aspx/HisseTekil` (EOD) | `isyatirim-prices-ASELS` (`RealSourceCatalog`) | captured 2026-10-06..08 | 3 s |
| `yahoo-chart` | Yahoo Finance `chart` (`.IS` contingency) | `prices-yahoo-contingency` (`CannedSourceCatalog`) | constructed from FU | 3 s |
| `kap-disclosures` | KAP VYK `GET /disclosures` | `kap-vyk-disclosures` (`RealSourceCatalog`) | constructed, shape live-verified 2026-10-18 | 12 s |
| `kap-disclosure-detail` | KAP VYK `GET /disclosureDetail/{index}` | `kap-vyk-disclosure-detail` (`RealSourceCatalog`) | captured 2026-10-18 | 12 s |
| `kap-members` | KAP VYK `GET /members` | `kap-vyk-members` (`RealSourceCatalog`) | constructed, shape live-verified 2026-10-18 | 12 s |
| `kap-member-securities` | KAP VYK `GET /memberSecurities` | `member-securities-ok` (`CannedSourceCatalog`) | derived from the live 2026-10-22 capture | 12 s |
| `evds-macro` | TCMB EVDS 3.0 `/igmevdsms-dis/fe` | `evds-v3-capture` (`RealSourceCatalog`) | captured 2026-10-18 | 2 s |
| `tefas-funds` | TEFAS fund feed | `tefas-tef0001` (`CannedSourceCatalog`) | constructed (FU synthetic) | 2 s |

`tefas-funds` has **no pinned endpoint** in the repository (`TefasSourceOptions.BaseUrl` ships
empty, the lane is not live). The probe therefore reports **unreachable** with that reason instead
of inventing a URL — an honest "cannot check", never a fake "clean".

### What "shape" means here

For every JSON path: the set of kinds observed (`object`, `array`, `string`, `number`, `boolean`,
`null`), whether it was ever null, and the array nesting (`$.value[].HGDG_KAPANIS`). Object
members are aggregated across array elements, so a field missing from one row but present in
another is still part of the element shape. Values never enter the comparison or the report.

| Finding | Meaning | Severity |
|---|---|---|
| `missing-key` | the double pins a path the live body no longer carries | breaking |
| `extra-key` | the live body added a path | warning |
| `type-changed` | the kind set observed is not a subset of the expected set | breaking |
| `nullability-changed` | a value can now be null where the double never was | warning |
| `element-shape-missing` | the double's array carried an element shape; the live array is empty | breaking |
| `status-changed` | the endpoint answered a status the double never serves | breaking |

Every finding is drift; the severity only ranks it. A comparison with **zero findings** is `clean`.
`unreachable` (transport failure, empty body, non-JSON body, no credential, no endpoint) is a third
outcome and is **never** reported as clean.

### The one narrow exemption

Containers whose keys are **data-derived** — EVDS series codes in `$.items[]`, the transposed date
columns and the `seriesNames` map — carry a per-source `allowExtraUnder` list in the contract, so a
new series/date key there is not reported as drift. Exemptions are per source, narrow (direct
children only) and never used to silence a difference the double actually pins.

## 4. How to run

The harness is a normal .NET project (`tools/source-probes/Degerli.SourceProbes`), run through
`dotnet run`. **The default is offline**: nothing contacts a network without `--live`.

```bash
# The coverage set, pacing floors and doubles (offline)
dotnet run --project tools/source-probes/Degerli.SourceProbes -- list

# A contract's expected shape, path by path (offline)
dotnet run --project tools/source-probes/Degerli.SourceProbes -- show --source isyatirim-prices

# Compare a saved body (a manual capture, or a deliberately mutated double) — offline
dotnet run --project tools/source-probes/Degerli.SourceProbes -- \
  check --source isyatirim-prices --body /tmp/capture.json

# Print the shape of a body, contract-formatted (offline; use it to re-derive a contract by hand)
dotnet run --project tools/source-probes/Degerli.SourceProbes -- shape --body /tmp/capture.json

# ONE paced live request for one source / for the whole set (explicit opt-in)
dotnet run --project tools/source-probes/Degerli.SourceProbes -- probe --source kap-disclosures --live
dotnet run --project tools/source-probes/Degerli.SourceProbes -- probe --all --live
```

Exit codes: `0` clean · `2` drift · `3` unreachable · `1` usage/policy refusal.

### Run modes

| Mode | What it does | Network |
|---|---|---|
| **Offline (default)** | `list`, `show`, `shape`, `check --body`. Reads contracts and a saved body only. | none |
| **Manual live CLI** | `probe … --live`: one request per source, paced, bounded (20 s timeout, no retries, no redirects). | yes, explicitly |
| Scheduled run | **not implemented by this ticket.** If it is ever added it runs on the dev box only and still must not gate PRs. | — |
| Admin endpoint | **not implemented** and not needed; a live probe is an operator act, not a product feature. | — |

### Politeness (enforced in code, `ProbePolicy`)

- **Pacing floors:** İş Yatırım ≥ 3 s, KAP VYK ≥ 12 s between calls (the posture the product's own
  ingestion already respects, `01` §10.9.2/§10.9.10). A declared interval below the floor is
  clamped **up**, so a future edit cannot quietly make the tool impolite. Requests are strictly
  sequential — never parallel.
- **No probing during a scheduled chain window:** the EOD chain (17:30 UTC) and the morning repair
  chain (05:30 UTC) own the sources. The windows `17:00–19:30 UTC` and `05:00–07:30 UTC` are
  refused unless `--force` is passed — an emergency escape, never a routine.
- Credentials come from the environment only: `Ingestion__Kap__ApiKey` / `Ingestion__Kap__ApiSecret`
  (the VYK Basic pair) and `Ingestion__Macro__EvdsApiKey` (EVDS). A missing credential is reported
  as `unreachable` — probing a gateway that requires auth without auth would only produce a
  401 shape, which teaches nothing.

### Where the report goes

Default: `<system temp>/degerli-source-probes/<yyyyMMddTHHmmssZ>-<mode>.json` (`--out` to change).
The tool **refuses** to write a report inside the repository: a report names hostnames and carries
structural excerpts, and it is an operator artifact, never a committed file.

The report contains, per source: probe key, redacted endpoint, mode, outcome, status, timestamp,
the findings (path / kind / expected / observed / severity), and a capped structural excerpt
(paths and kinds only). **No body values, no raw bodies, no credentials** — query parameters named
in the probe's `secretParameters` and URL userinfo are masked, and secret values are scrubbed from
transport-error text before it is recorded.

## 5. Drift → ticket flow

Drift is a **finding**, not a failure, and it never edits anything by itself:

1. **Run** the probe (manual live mode, outside the chain windows).
2. **Read** the report. `clean` needs no action. `unreachable` is not evidence about the shape —
   re-run later; if it persists, that is a source-availability finding of its own.
3. **Triage** each drifted path: did the source change, or is the double (and therefore the
   contract) stale? The two produce different fixes and only a human can tell them apart.
4. **Route**: a real source change → a ticket that updates the adapter *and* re-captures the double
   under the `01` §10.9.7 capture discipline (bounded, charset-faithful, provenance-labeled), then
   re-derives the contract file with `shape --body`. A double that no longer matches reality is a
   contract defect, not something a probe tool should paper over.
5. **Never** hand-edit a contract file to make a live probe pass. The tool has no write mode for
   contracts on purpose; `shape` prints, the human copies.

## 6. Two worked examples (the incidents that produced this ticket)

### 6.1 İş Yatırım daily feed — the empty `value` (2026-10-23)

`Common/Data.aspx/HisseTekil` returned `{"ok":true,…,"value":[]}` for the current session while the
chart feed carried the session, so run 218 skipped every symbol and a 9/100-day snapshot was
published as "the market". No committed double modelled an empty-but-valid `value` array, so the
test suite was green.

What the probe says about that body (reproduced by
`AC68i_EmptyCollection_IsNotClean_ItNamesTheUnverifiableShape`):

```
[drift] isyatirim-prices  200  compared against isyatirim-prices-ASELS
  - $.value[]  element-shape-missing  expected object · observed absent (the collection answered empty)  [breaking]
```

One finding, naming exactly the collection that made the element shape unverifiable. It is *not*
clean: "we could not see rows" and "the rows matched" are different answers.

### 6.2 KAP VYK `ER005` — the silent semantic drift (2026-10-23)

`GET /api/vyk/disclosures?disclosureIndex=…` answered `400 {"code":"ER005","message":"Bildirim
bulunamadı."}` for indices that no longer exist, while the CI doubles answered the requested
indices normally. The class fail-stopped the EOD chain six times until TKT-foundation-063 landed.

What the probe says about that answer (excerpt; reproduced by
`AC68i_StatusChanged_NamesTheErrorEnvelope`):

```
[drift] kap-disclosures  400  HTTP 400 — the double serves 200; observed envelope shape only
  - $            type-changed    expected array · observed object  [breaking]
  - http.status  status-changed  expected 200 · observed 400       [breaking]
  - $.code       extra-key       expected absent · observed string [warning]
  - $.message    extra-key       expected absent · observed string [warning]
```

The point is not that the probe would have fixed the bug: it is that the **shape** of the failure
(a 400 with an error envelope the doubles never serve) is visible in one report instead of in six
nightly chains.

## 7. When a double is re-captured

1. Capture the new bounded body (§10.9.7 discipline: bounded, charset-faithful, provenance stated).
2. Save it, run `shape --body <file>`.
3. Update `tools/source-probes/contracts/<key>.json` by hand: the `paths` list, `double.note`, and
   `derivedAt`. Keep `double.catalogEntry` pointing at the double the shape came from.
4. Re-run `check --source <key> --body <the new capture>` → expected `clean`.
5. Note the change in the PR that updates the double. A contract change without a double change is
   a review smell; a double change without a contract change leaves the probe blind.
