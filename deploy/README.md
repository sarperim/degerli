# Production & Operations

Operational runbook for the Değerli VPS deployment (architecture
`01-system-architecture.md` §6.2/§6.3/§10.8; ticket **TKT-int-006**). It documents the
deploy path, rollback, backups + restore drill, and uptime monitoring. The repo README
carries the product story; this file carries the ops story.

---

## Topology

```text
Internet ──▶ Caddy (ghcr.io/sarperim/degerli/web)  :443  Let's Encrypt auto-TLS
              │  /api/*, /health → reverse_proxy
              │  everything else → SPA statics (/srv)
              ▼
            api (ghcr.io/sarperim/degerli/api)      :8080  ASP.NET Core + in-process workers
              ▼
            postgres (postgres:17)                  internal only (no published port)
```

Everything runs from [`compose.prod.yml`](../compose.prod.yml) on one Hetzner VPS:

| Service | Image | Notes |
|---|---|---|
| `caddy` | `ghcr.io/sarperim/degerli/web` | public entrypoint; auto-TLS, HTTPS redirect, SPA + `/api` proxy |
| `api` | `ghcr.io/sarperim/degerli/api` | API + hosted ingestion workers (AD-04); also the `migrate` entrypoint |
| `postgres` | `postgres:17` | private; backed up nightly |

**Images are public on GHCR** (portfolio artifact, architecture §5). The VPS pulls
without registry credentials. Build/push is owned by [`deploy.yml`](../.github/workflows/deploy.yml).

---

## First-time VPS setup

1. **DNS** — an `A` record for your domain pointing at the VPS IP. Caddy needs it to
   issue the certificate.
2. **Docker** — install Docker Engine + the Compose v2 plugin.
3. **Checkout** the repo where the deploy job expects it (repo variable `DEPLOY_PATH`,
   default suggestion `/opt/degerli`):
   ```bash
   sudo mkdir -p /opt/degerli && sudo chown "$USER" /opt/degerli
   git clone https://github.com/sarperim/degerli.git /opt/degerli
   ```
4. **Create the production `.env`** (git-ignored, never committed):
   ```bash
   cd /opt/degerli
   cp .env.example .env
   # edit .env: POSTGRES_PASSWORD, ConnectionStrings__Default, Smtp__*, Evren__ApiKey,
   #            APP_URL, ALERT_EMAIL, DOMAIN
   ```
5. **Make the GHCR packages public** — once, in the GitHub UI: *Packages → degerli/api*
   and *degerli/web* → *Package settings* → *Change visibility → Public*. The deploy
   host has no registry login, so it relies on public images.
6. **First deploy** — either push to `main` (CI green → CD runs) or run the `Deploy`
   workflow manually from the Actions tab.

---

## Deploying

### Automatic

`.github/workflows/deploy.yml` listens for the **CI** workflow completing on `main`
(`workflow_run`). On a green run it:

1. builds + pushes `ghcr.io/sarperim/degerli/api` and `.../web`, tagged `:<short-sha>`
   and `:latest`;
2. SSHes to the VPS and runs, in the deploy directory:
   ```bash
   docker compose -f compose.prod.yml pull
   docker compose -f compose.prod.yml run --rm api migrate   # forward-only EF migrations
   docker compose -f compose.prod.yml up -d
   curl -fsS "$PUBLIC_URL/health"                            # must be 200
   ```
3. fails the job if `/health` never returns `200` within ~150 s. There is no
   in-workflow alert step; runtime alerting is handled by the UptimeRobot monitor
   (see [Monitoring](#monitoring--uptimerobot)).

> `workflow_run` requires `deploy.yml` to be on the **default branch** (`main`), so CD
> is inert until this workflow is merged to `main`.

### Manual (same thing, on demand)

Actions → **Deploy** → *Run workflow*. This is also the rollback path.

---

## Rollback

Images are **tag-pinned** by SHA, so rollback is a re-deploy of the previous tag —
no rebuild:

1. Find the last good short SHA (Actions run history, or `gh api .../packages`).
2. Actions → **Deploy** → *Run workflow* → set **image_tag** to that SHA.
3. The job runs `compose pull` (fetches that tag) → `migrate` → `up -d` → smoke-check.

From the box, equivalently:

```bash
cd /opt/degerli
IMAGE_TAG=<previous-sha> docker compose -f compose.prod.yml pull
IMAGE_TAG=<previous-sha> docker compose -f compose.prod.yml up -d
curl -fsS "$PUBLIC_URL/health"
```

Migrations are **forward-only** (`02-data-model.md` §8): a rollback that needs a schema
change must ship a new forward migration, not a down-migration.

---

## Backups & restore drill

Nightly `pg_dump -Fc` → **AES-256 encrypted** → Backblaze B2 (rclone), 30-day
retention. Script: [`deploy/backup/pg-backup.sh`](backup/pg-backup.sh).
(RTO ≤ 4 h, RPO ≤ 24 h — architecture §10.8.)

Config: `deploy/backup/backup.env.example` (kept in a root-only file,
**not** the compose `.env` — see that file's header for why).

### Schedule it

```bash
# one-time: rclone remote for B2, then the env file
rclone config           # create remote 'b2' → bucket 'degerli-backups'
sudo install -m 600 /dev/null /etc/degerli/backup.env
sudo editor /etc/degerli/backup.env      # from backup.env.example

# crontab -e  (03:00 TRT daily)
0 3 * * * set -a; . /etc/degerli/backup.env; set +a; /opt/degerli/deploy/backup/pg-backup.sh >> /var/log/degerli-backup.log 2>&1
```

### Restore drill (run once before launch — manual ledger §5.6)

[`deploy/backup/restore.sh`](backup/restore.sh) restores a chosen archive into a
**scratch** database, so it is safe on the live box:

```bash
set -a; . /etc/degerli/backup.env; set +a
/opt/degerli/deploy/backup/restore.sh /opt/degerli/backups/degerli-<timestamp>.dump.gpg
```

Then confirm row counts / spot rows in `degerli_restore`, record the elapsed time, and
drop the scratch DB. **Execution before launch is a builder-executed manual-ledger
item** (test strategy §5.6) — it cannot be automated from CI.

---

## Monitoring — UptimeRobot

Free tier, 5-minute checks, alert e-mail (architecture §5, NFR-MOV-003 operational).

1. Create an UptimeRobot account; add a **HTTP(s)** monitor.
2. **URL:** `https://<your-domain>/health` (the API liveness + DB check, §10.3).
3. **Interval:** 5 minutes. **Monitor type:** HTTP(s) keyword — expect the response to
   contain a healthy status (or "Status 200").
4. **Alert contacts:** the builder e-mail (`ALERT_EMAIL`). Optionally a second monitor
   for `https://<your-domain>/` returning 200 (SPA reachable).
5. Confirm the monitor turns green after the first deploy; the 99.0% monthly figure is
   read from UptimeRobot's report (recorded as `operational` in the coverage matrix).

Note: UptimeRobot also acts as the **outside-in** check that the tunnel/cert/TLS chain
is healthy, which the on-box smoke-check cannot prove.

---

## Security checks (required CI gates)

Three checks guard this **public** repo and its **public** GHCR images
(architecture §6.1 job 4 / §6.3; ticket **TKT-foundation-066**). They live in
[`.github/workflows/security.yml`](../.github/workflows/security.yml) and run on every
push to and every PR targeting `dev` and `main`.

| Required check name | Job | What it does | Fails on |
|---|---|---|---|
| **`Secrets (gitleaks)`** | `secrets` | gitleaks over the **full commit history** on push, over the **PR's own commits** (`base..head`) on `pull_request`; `--redact` always | any unreviewed finding |
| **`Dependencies (NuGet · npm audit)`** | `dependencies` | `dotnet list Degerli.sln package --vulnerable --include-transitive` (JSON parsed) + `npm audit --audit-level=high` in `src/Web` and `tests/e2e` | any high/critical (or unrated) advisory |
| **`Containers (Trivy image scan)`** | `containers` | builds `src/Api/Dockerfile` and `deploy/caddy/Dockerfile` — the exact images `deploy.yml` ships — and scans each with Trivy `--severity HIGH,CRITICAL --exit-code 1` | any high/critical OS/library CVE in an image (minus a live `.trivyignore.yaml` entry) |

### They are required, not advisory

- **No `continue-on-error`** anywhere in `security.yml`. A scanner that cannot run
  (network, download, config error) is a **red check**, never a skipped one.
- **Scan the shipped artifact.** Container scanning is an *image* scan, not
  `trivy fs`: the base OS layers only exist in the built image, and those are most of
  what a CVE lands in.
- **Pinned, checksum-verified tools.** gitleaks and Trivy are installed from their
  GitHub releases and their tarball's SHA-256 is verified before execution, so a
  re-tagged release cannot silently change what runs. Action pins are full commit SHAs.
- **No secret material leaves the job.** gitleaks runs `--redact`; the JSON report is
  printed on failure only, from `$RUNNER_TEMP`. No artifact upload exists in this
  workflow at all.

### Marking them required (builder action, repo settings)

Branch protection is a repository setting, not a file — so it is a builder step to
perform alongside the merge of this ticket. On GitHub: *Settings → Branches → dev /
main → Require status checks*, and add these six names, copied verbatim from the job
names above and in `ci.yml`:

```
Backend (restore · build · test)
Web (lint · typecheck · test · i18n · build)
E2E (compose · Playwright · MailPit)
Secrets (gitleaks)
Dependencies (NuGet · npm audit)
Containers (Trivy image scan)
```

The E2E job depends on the other two CI jobs, so a full PR run keeps its existing
ordering; the security jobs are independent and run in parallel.

### Allowlist policy (`.gitleaks.toml`)

The scan uses gitleaks' built-in rule pack unchanged (`[extend] useDefault = true`).
The config file exists **only** to record reviewed false positives:

- one entry per finding — an exact file path **and** an exact literal, joined by
  `condition = "AND"`, so loosening either half is a visible diff;
- **no** `[allowlist].commits`, no bare `paths` entry, no rule-wide exclusion, no
  blanket "generic-api-key is noisy" switch;
- every entry carries a date, an owner and the reason it is not a secret;
- a **real** credential is never allowlisted — it is rotated and removed from history,
  and the allowlist is not the place to paper over it.

Baseline at the config's creation (2026-10-29, gitleaks 8.30.1, full history of 754
commits): **two findings, both false positives** — the empty
`Ingestion__Kap__ApiKey=`/`ApiSecret=` template lines in `.env.example`, and the
deterministic L2 fixture password constant in
`tests/Api.IntegrationTests/Harness/FixtureBuilder.cs`. Both are recorded in the file
with their pinning regexes. Nothing else is allowlisted.

**Dependency-audit baselines** need no allowlist today: `dotnet list … --vulnerable`
and both `npm audit` runs are clean on the current tree (one *moderate* Vite advisory
in `src/Web` is below the `high` gate). If a future advisory has to be accepted, it is
recorded here dated and owned, with the advisory URL and the reason — never by
lowering `--audit-level`.

### Container baseline (`.trivyignore.yaml`)

The image scan's allowlist is separate from the secret one, and `.trivyignore.yaml` is
**empty by default** — the point of a scan is that what it finds gets fixed. An entry
exists only when the fix genuinely cannot be made in this repository, and it carries
four properties that make it a gate rather than a mute button:

1. **Per-finding** — an exact CVE id scoped to an exact target path (`usr/bin/caddy`),
   never a bare id, never a wildcard.
2. **Dated and owned** — a `statement` with the owner and why the CVE is not fixable
   here, next to a review date.
3. **Self-expiring** — Trivy enforces `expired_at`: the day it passes, the entry stops
   applying and the CVE fails the check again. Silence is temporary by design.
4. **Never for a fixable finding** — if a one-line change clears it, the change lands
   and no entry is written.

Baseline at this ticket's landing (2026-10-29/30, Trivy 0.76.1, both images fresh):
**zero entries for the API image.** The web image first came back red with **three
fixable Alpine findings** — a CRITICAL `brotli` out-of-bounds write and a HIGH `curl`
use-after-free among them — which were **fixed in `deploy/caddy/Dockerfile`** with
`RUN apk upgrade --no-cache` (the `caddy:2` base layer lags the Alpine security repo
between upstream rebuilds), not baselined. What remains is **seven HIGH findings inside
the prebuilt Caddy binary itself** (Go stdlib 1.26.8 `crypto/x509`, `net/http`,
`crypto/tls`, `golang.org/x/net`, `go-jose`), which needs an upstream Caddy rebuild on
Go 1.26.10+/1.27.3+ and cannot be changed from here — those five CVE ids are the only
entries, expiring **2026-12-31**, after which the check goes red again until Caddy
ships the rebuild.

Re-check procedure: `docker pull caddy:2`, rebuild, scan, and delete every entry that
no longer reports.

**Monitoring the other direction:** [`.github/dependabot.yml`](../.github/dependabot.yml)
opens weekly upgrade PRs for NuGet, both npm roots and the workflow actions, so an
advisory against an already-pinned package arrives as a reviewable PR instead of
waiting for someone to open a branch. Note it does not manage base-image tags; the
`apk upgrade` layer above is what keeps the pinned `caddy:2` current between rebuilds.

---

## Secrets

All secrets live **only** on the VPS (`.env`) and in GitHub Actions secrets — never in
the repo or the images (`.env.example` is the template; architecture §6.3).

| Where | Keys |
|---|---|
| GitHub Actions secrets | `SSH_PRIVATE_KEY`, `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_HOST_KEY` |
| GitHub Actions variables | `DEPLOY_PATH`, `PUBLIC_URL` |
| VPS `.env` (compose) | `DOMAIN`, `POSTGRES_*`, `ConnectionStrings__Default`, `Smtp__*`, `Evren__ApiKey`, `APP_URL`, `ALERT_EMAIL` |
| VPS `/etc/degerli/backup.env` | `BACKUP_PASSPHRASE`, `RCLONE_REMOTE` |

`DEPLOY_HOST_KEY` is the deploy host's public host-key line exactly as it appears in
`known_hosts` (e.g. `degerli.example ssh-ed25519 AAAA…`). Capture it once from a
trusted network, verify the fingerprint out-of-band, and store it as a repository
secret — the deploy job pins it and connects with `StrictHostKeyChecking=yes`, so a
rogue/on-path host key aborts the deploy instead of being trusted silently (no TOFU).

---

## KAP public-site access: the relay fallback

The KAP public-site lane reaches `https://www.kap.org.tr` through the adapter's own
browser-shaped TLS handler plus 429-aware pacing (TKT-foundation-100, architecture
§10.9.19). When the edge refuses that stack at the **transport** level — or enforces its
export quota harder than the adapter's floor — a local TLS relay can stand in front of the
source while keeping every politeness rule. It is **ops posture, not a product path**:
[`kap-relay/README.md`](kap-relay/README.md) states when to use it, the
`Ingestion:KapPublic:BaseUrl` seam it rides, and the live criterion for standing it down.

---

## Manual / operational ledger

These acceptance items are **not** automatable from CI and are recorded as builder-run
checklists (test strategy §5.6/§9.4):

- first production deploy + `/health` smoke on the real VPS (per release);
- backup restore drill (once before launch);
- UptimeRobot monitor live and green (once before launch);
- README quickstart verified on a clean machine (≤ 10 commands, NFR-MDF-005).
