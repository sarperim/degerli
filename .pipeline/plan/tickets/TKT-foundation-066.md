---
scope:
  - .github/workflows/ci.yml
  - .github/workflows/security.yml
  - .github/dependabot.yml
  - .gitleaks.toml
  - deploy/README.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-066: CI hardening — secret scanning, dependency audit and container scanning as required checks

- Status: todo
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
