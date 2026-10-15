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
3. fails the job (and alerts) if `/health` never returns `200` within ~150 s.

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

## Secrets

All secrets live **only** on the VPS (`.env`) and in GitHub Actions secrets — never in
the repo or the images (`.env.example` is the template; architecture §6.3).

| Where | Keys |
|---|---|
| GitHub Actions secrets | `SSH_PRIVATE_KEY`, `DEPLOY_HOST`, `DEPLOY_USER` |
| GitHub Actions variables | `DEPLOY_PATH`, `PUBLIC_URL` |
| VPS `.env` (compose) | `DOMAIN`, `POSTGRES_*`, `ConnectionStrings__Default`, `Smtp__*`, `Evren__ApiKey`, `APP_URL`, `ALERT_EMAIL` |
| VPS `/etc/degerli/backup.env` | `BACKUP_PASSPHRASE`, `RCLONE_REMOTE` |

---

## Manual / operational ledger

These acceptance items are **not** automatable from CI and are recorded as builder-run
checklists (test strategy §5.6/§9.4):

- first production deploy + `/health` smoke on the real VPS (per release);
- backup restore drill (once before launch);
- UptimeRobot monitor live and green (once before launch);
- README quickstart verified on a clean machine (≤ 10 commands, NFR-MDF-005).
