---
scope:
  - .github/workflows/ci.yml
  - .github/workflows/security.yml
  - .github/dependabot.yml
  - .gitleaks.toml
  - .trivyignore.yaml
  - deploy/README.md
  - deploy/caddy/Dockerfile
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-066: CI hardening — secret scanning, dependency audit and container scanning as required checks

- Status: in-review — PR https://github.com/sarperim/degerli/pull/165
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 1 unparked).**
- **Evidence (gathered 2026-10-24, current repo state):**
  - `.github/workflows/ci.yml` has exactly three jobs — `backend` (`:44`), `web` (`:81`), `e2e` (`:128`) — and `permissions: contents: read` (`:25-26`). There is no security job, no `.github/dependabot.yml`, and no scanner config anywhere under `.github/` (`ls -a .github/` → `workflows` only).
  - The repo is **public** and the production images are **public on GHCR** (`deploy/README.md` §Topology), so a committed secret or a vulnerable dependency/image is immediately public.
  - Real credentials have been used **operationally during the 2026-10-23 wave**: KAP Basic API key/secret (`Ingestion__Kap__ApiKey`/`ApiSecret`), the Evren/EVDS key, PostgreSQL password, Brevo SMTP credentials, and the SSH deploy key + pinned host key (`deploy/README.md` §Secrets, `deploy.yml:17-20`). The 2026-10-23 deploy was performed **by hand on the dev stack** (`run-report.md:177`), outside the workflow's secret plumbing.
  - The gap was already recorded by prior review: `TKT-foundation-020` review — "CI is test-suite-only (no lint/secrets-scan/dependency-audit)"; `TKT-int-006` routing item **SEC-7** — "no secret-scan/dep-audit".
- **Pinned approach (one small decision recorded, not invented):**
  1. **Secret scanning** — gitleaks over full history on push and over the PR diff on pull requests; a reviewed `.gitleaks.toml` allowlist only (no blanket excludes, no `continue-on-error`), redacted output.
  2. **Dependency audit** — `dotnet list Degerli.sln package --vulnerable --include-transitive`; `npm audit --audit-level=high` in `src/Web` and `tests/e2e`. Fail on high/critical. Add `.github/dependabot.yml` for the two npm ecosystems + NuGet so the audit does not only fail at PR time.
  3. **Container scanning** — build `src/Api/Dockerfile` and `deploy/caddy/Dockerfile` in CI and scan with Trivy (`--severity HIGH,CRITICAL --exit-code 1`). **Decision to record on the ticket before implementation:** image scan (preferred — scans what actually ships) vs `trivy fs` (interim if the image-build job is too slow). Whichever is chosen, the check is required.
  4. **Required, not advisory** — the three check names are recorded in `deploy/README.md` (and in the ticket) for branch protection; `continue-on-error` is forbidden. A baseline allowlist for pre-existing findings must be dated, owned and per-finding.
- **Acceptance (explicit criteria — foundation convention, no TC lane):**
  - **(i)** Three new required checks run on every PR/push to `dev`/`main`; each is **red on a seeded violation** (a throwaway secret, a known-vulnerable package version, a deliberately vulnerable image layer) and **green on the current tree**.
  - **(ii)** No check is advisory: `continue-on-error` absent; the exact required check names are listed in `deploy/README.md`; the allowlist (if any) is per-finding, dated and owned.
  - **(iii)** The scanners never print secret values (gitleaks redaction) and upload no raw secret material as an artifact.
  - **(iv)** The existing `backend`/`web`/`e2e` jobs are unchanged in behaviour; total CI wall-clock stays within the free-tier budget (record the measured added minutes).
  - **(v)** `01` §6.1/§6.3 and `deploy/README.md` record the checks, the required names and the allowlist policy; §13.1 row appended (**soft reservation v1.5.45**, above landed max v1.5.42 and the 064/065 reservations v1.5.43/44; re-pin at merge).
- Traces to: NFR-MOV-003/NFR-MDF-005 operational posture (public repo + public images), `01` §6.1/§6.3; prior SEC-7 routing item (`TKT-int-006`).
- Architecture refs: `01-system-architecture.md` §6.1 (CI jobs), §6.3 (secrets policy), §13.1 (change record).
- UX refs: none.
- Dependencies: none.
- Parallel group: none (touches `01`, which the in-flight 044/045/048/064 also touch; doc edits serialize by last-lander rebase).

## Notes / deliberately not invented

- The **branch-protection setting itself** is builder-owned (repo settings, not a file). This ticket delivers the checks and records their names; marking them required is a builder action to perform alongside the merge.
- The scanner vendors and thresholds are the pinned minimum. If the builder prefers a different vendor, the acceptance (red on a seeded violation, green on tree, required) is vendor-neutral.

## Implementation decisions (recorded 2026-10-29/30, before/while implementing)

1. **Image scan, not `trivy fs`** — the ticket's preferred option, taken because it was feasible: both production images build in ~2–5 min on this machine and the job's `timeout-minutes` is 30. An image scan is the only one that can see the base OS layers, and most of the actual findings below were exactly there.
2. **Separate workflow `security.yml`, not new jobs inside `ci.yml`** — the three checks are independent of the build/test graph (no `needs:`), so a security failure reports on its own check name instead of masking or being masked by the E2E job. `ci.yml`'s three jobs are byte-unchanged apart from a header comment (acceptance iv).
3. **Full history on push, `base..head` on PR** — a secret stays compromised after deletion, so push scans everything; a PR scans only its own commits so a long-lived branch does not pay for the history every time. An unresolvable base commit **fails** the check rather than quietly scanning nothing.
4. **`dotnet list package --vulnerable` exits 0 even when it finds a CVE** (verified: seeded `Newtonsoft.Json 12.0.3` → prints `High`/GHSA-5crp-9r3c-p9vr, exit **0**). The job therefore parses the JSON and fails itself; an advisory with no named severity is treated as blocking too.
   - **Found while seeding (fixed):** `Directory.Build.props` sets `TreatWarningsAsErrors`, so a vulnerable package makes **restore** fail first with `NU1903` and an opaque `"Restore failed. Run dotnet restore for more details"` — the CVE hidden behind a build error. The restore step therefore runs with `-p:NuGetAudit=false` and the explicit audit step (with `--no-restore`) is the authoritative gate: it names the package, version, framework, project and advisory URL. The parser also treats a `problems` entry as blocking, so an audit that *could not run* is never mistaken for a clean one. Both paths measured (below).
5. **Tools are pinned binaries with a verified SHA-256**, installed from their GitHub releases (gitleaks 8.30.1, Trivy 0.76.1) — no third-party action decides what runs, and a re-tagged release cannot change it silently.

## Evidence — red on a seeded violation, green on this tree (measured, not assumed)

All three gates were executed locally on 2026-10-29/30 with the pinned versions (gitleaks 8.30.1, Trivy 0.76.1, .NET SDK 10.0.402, npm 22.x).

**(i) `Secrets (gitleaks)`**

| Run | Result |
|---|---|
| Full history of this tree, 754 commits / 19.86 MB, **before** `.gitleaks.toml` | exit 1, **2** findings (both false positives, below) |
| Same, **with** the reviewed per-finding allowlist | `no leaks found`, exit **0** |
| Tree **+ a seeded throwaway `aws_access_key_id`/`secret_access_key` pair** in a tracked file | exit **1**, 2 findings (`aws-access-token`, `generic-api-key`), values printed as `REDACTED` |
| Same tree after the seeded file is removed | exit **0** |
| The **verbatim workflow scan command** with `--log-opts base..head` (PR mode) over this branch | exit **0**, and the range resolves (`git cat-file` guard passes) |

The two real-tree findings, both verified false positives and both allowlisted per-finding with an exact path **and** an exact literal: the empty `Ingestion__Kap__ApiKey=`/`ApiSecret=` template lines in `.env.example` (the generic-api-key match spans the newline and names the *next* line's key as the secret; both values are empty by design), and the deterministic L2 fixture password constant `Degerli-Test-2026` in `tests/Api.IntegrationTests/Harness/FixtureBuilder.cs`. Redaction is confirmed by the seeded run above: the redacted report shows `REDACTED` for both the AWS pair and the generic match.

**(ii) `Dependencies (NuGet · npm audit)`**

| Run | Result |
|---|---|
| `dotnet list Degerli.sln package --vulnerable --include-transitive` (all 9 projects) | clean, 0 findings → parser exit **0** |
| Same, against a seeded `Newtonsoft.Json 12.0.3` project | prints `high … GHSA-5crp-9r3c-p9vr` → parser exit **1** (while `dotnet list` itself exits **0** — the reason for parsing) |
| The **verbatim workflow step**, seeded solution | `high Newtonsoft.Json 12.0.3 (net10.0) in …/Degerli.Seed.csproj — GHSA-5crp-9r3c-p9vr` + `::error::`, exit **1** |
| The **verbatim workflow step**, this tree | `NuGet audit clean (0 low/moderate finding(s), none blocking).`, exit **0** |
| Parser against a report whose restore failed (`problems`) | `::error::NuGet audit could not run: Restore failed.`, exit **1** — an audit that could not run is never "clean" |
| `npm audit --audit-level=high` in `src/Web` | exit **0** — one **moderate** Vite advisory (GHSA-8g5g-mxqh-grgj), below the gate; recorded, not suppressed |
| `npm audit --audit-level=high` in `tests/e2e` | exit **0**, 0 vulnerabilities |
| Same, against a seeded `lodash 4.17.15` | exit **1** (1 high) |

**(iii) `Containers (Trivy image scan)`**

The scan **found real problems on the current tree, and they were fixed rather than muted**:

| Image | Before | After |
|---|---|---|
| `degerli-api:scan` (`src/Api/Dockerfile`) | 0 findings | exit **0** |
| `degerli-web:scan` (`deploy/caddy/Dockerfile`) | **10**: CRITICAL `brotli` CVE-2026-111497, HIGH `curl`/`libcurl` CVE-2026-92392, plus 7 HIGH inside the prebuilt Caddy binary | exit **0** |

The three **fixable Alpine** findings were cleared in `deploy/caddy/Dockerfile` with `RUN apk upgrade --no-cache` (the `caddy:2` base layer lags the Alpine security repo between upstream rebuilds; `docker pull caddy:2` shows it is already the newest upstream release, v2.11.7, so waiting was not an option). The remaining **seven HIGH are inside the Caddy Go binary** (Go stdlib 1.26.8 `crypto/x509`/`net/http`/`crypto/tls`, `golang.org/x/net`, `go-jose` v3+v4) and need an upstream Caddy rebuild on Go 1.26.10+/1.27.3+ — not fixable from this repository, so they are the **only** entries in the new `.trivyignore.yaml` (5 CVE ids × exact target path `usr/bin/caddy`, owner, reason, `expired_at: 2026-12-31`).

Red-on-seed, measured for this job too:

| Run | Result |
|---|---|
| both images, real Dockerfiles, real baseline | exit **0** |
| web image, **no** `--ignorefile` | exit **1**, `Total: 7 (HIGH: 7)` |
| web image, baseline with the `expired_at` dates moved into the past | exit **1**, `Total: 7 (HIGH: 7)` — the expiry is really enforced, not decorative |
| seeded deliberately-vulnerable image (`alpine:3.10`) **with** the real baseline applied | exit **1** (CRITICAL apk-tools CVE-2021-36159) — a Caddy-scoped entry cannot absorb an unrelated image CVE |

**Green evidence for the workflow wiring itself** is the `Security` workflow run on this branch's PR (CI is the runner for workflow changes; the local runs above prove the scanners and the gate logic, not GitHub's scheduling of them).

## Scope amendment (recorded, not hidden)

The frontmatter `scope:` gains two files: **`.trivyignore.yaml`** and **`deploy/caddy/Dockerfile`**. Acceptance (i) requires each check to be *green on the current tree*, and the honest route there is to fix the fixable findings and baseline only the unfixable ones. Baselining a CRITICAL that one `apk upgrade` line clears would be exactly the performative gate this repo forbids. The omission is a planner scope gap routed to the orchestrator (the 091/094/101 precedent). No product code, test file, schema or test-plan section is touched.

## Builder action outstanding

Mark the six checks required for `dev`/`main` in *Settings → Branches* (names listed verbatim in `deploy/README.md` §Security checks). Branch protection is a repository setting, not a file — the ticket's own Notes section says so.
