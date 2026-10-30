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
| `api` | `ghcr.io/sarperim/degerli/api` | API + hosted ingestion workers (AD-04); also the `migrate` entrypoint; `healthcheck: /health` is the readiness gate |
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
   # edit .env: POSTGRES_PASSWORD, ConnectionStrings__Default, DEGERLI_BUILDER_PASSWORD,
   #            Smtp__*, Evren__ApiKey, APP_URL, ALERT_EMAIL, DOMAIN
   ```
   `DEGERLI_BUILDER_PASSWORD` is required (**≥ 12 characters** — the minimum the seed
   enforces, `DegerliSeed.MinimumBuilderPasswordLength`): the `migrate` step seeds the
   builder bootstrap account, and in a deployed environment that password has no default
   by design (CWE-798). The deploy's `preflight` checks both presence **and** length and
   fails there with a named message, so a missing or too-short value never surfaces as a
   mid-migration abort. The variable is passed only to the one-shot `migrate` container —
   it is deliberately *not* in the serving `api` container's environment, which never
   reads it. (Note: the length guard in `DegerliSeed` only runs when the `InitialSchema`
   seed SQL is generated, i.e. on a first migration; the preflight check is what covers
   every deploy.)
5. **Make the GHCR packages public** — once, in the GitHub UI: *Packages → degerli/api*
   and *degerli/web* → *Package settings* → *Change visibility → Public*. The deploy
   host has no registry login, so it relies on public images.
6. **First deploy (bootstrap)** — the restore-point gate (phase 3) cannot be satisfied on
   a blank host: there is nothing to restore, and `pg-backup.sh` dumps out of a *running*
   `postgres` container while also requiring `RCLONE_REMOTE`. So the gate detects exactly
   that case — no `postgres-data` volume for the compose project, i.e. no database has
   ever existed on this host — and records `bootstrap=true` in the marker instead of
   requiring an archive. That carve-out applies **once**, while the volume is absent;
   every later deploy is gated unchanged. Either push to `main` (CI green → CD runs) or
   run the `Deploy` workflow manually from the Actions tab.
   *Optionally*, if `/etc/degerli/backup.env` and the rclone remote are already set up
   (§Backups), give even the first migration a restore point by creating the empty
   database first and taking a baseline archive — after which the normal gate applies:
   ```bash
   cd /opt/degerli
   docker compose -f compose.prod.yml up -d postgres     # no api/caddy yet
   set -a; . /etc/degerli/backup.env; set +a             # BACKUP_PASSPHRASE, RCLONE_REMOTE
   deploy/backup/pg-backup.sh                            # writes INTO /opt/degerli/backups
   ```
   Either way, from the moment the volume exists **every** later deploy — including one on
   a box whose stack is fully down (`up -d postgres` first, then `pg-backup.sh`) — must
   have an archive inside the RPO. There is no override.

---

## Deploying

### Automatic

`.github/workflows/deploy.yml` listens for the **CI** workflow completing on `main`
(`workflow_run`). On a green run it:

1. builds + pushes `ghcr.io/sarperim/degerli/api` and `.../web`, tagged `:<short-sha>`
   and `:latest`;
2. SSHes to the VPS and runs the deploy **in this order**, in the deploy directory
   (`TKT-foundation-067`). Each phase runs only if the previous one succeeded, so a
   failure stops the deploy *there* — the later, more destructive phase never starts:

   | # | Phase | What it does | Fails the deploy when |
   |---|---|---|---|
   | 1 | `preflight` | reads the tag currently serving (the rollback target), checks `compose.prod.yml`/`.env`/`docker`/`curl`, validates the compose file, and checks `DEGERLI_BUILDER_PASSWORD` is present and ≥ 12 chars | anything required is missing, the password is absent/too short, or the compose file does not validate |
   | 2 | `pull` | `docker compose -f compose.prod.yml pull` | an image cannot be pulled |
   | 3 | `backup-marker` | verifies the newest `degerli-*.dump.gpg` restore point is non-empty and **no older than the RPO (24 h)**, then records the tag + restore point + `outcome=attempt` in a `predeploy-<tag>-<utc>.marker` beside it. On a blank host (no `postgres-data` volume yet) it records `bootstrap=true` instead of requiring an archive | there is no restore point, it is empty, or it is older than the RPO |
   | 4 | `migrate` | `docker compose -f compose.prod.yml run --rm -e DEGERLI_BUILDER_PASSWORD=… api migrate` — a **one-shot** container; the serving stack is untouched | the migration fails |
   | 5 | `ready` | `docker compose -f compose.prod.yml up -d --wait --wait-timeout 180` — the readiness gate (below) | a service does not report running/healthy within 180 s |
   | 6 | `smoke` | `GET ${PUBLIC_URL}/health` through Caddy, 30 attempts × 5 s, each `curl` bounded at 5 s | `/health` never answers 200 |

3. on failure prints the phase, `compose ps`, and the recovery path for **that** phase
   (`§Rollback posture`). Nothing is rolled back automatically — see below.
4. There is no in-workflow alert step; runtime alerting is handled by the UptimeRobot
   monitor (see [Monitoring](#monitoring--uptimerobot)).

From the box, the same sequence, by hand:

```bash
cd /opt/degerli
export IMAGE_TAG=<sha>
docker compose -f compose.prod.yml pull
# phase 3 — see §Backups for what the marker checks
ls -1t backups/degerli-*.dump.gpg | head -1
# the builder password is a migrate-only input; read it from .env (never export it globally)
# and pass it by name, so it never appears in the host's argument list
export DEGERLI_BUILDER_PASSWORD="$(sed -n 's/^DEGERLI_BUILDER_PASSWORD=//p' .env | tail -n1)"
docker compose -f compose.prod.yml run --rm \
  -e DEGERLI_BUILDER_PASSWORD api migrate      # forward-only EF migrations
unset DEGERLI_BUILDER_PASSWORD
docker compose -f compose.prod.yml up -d --wait --wait-timeout 180
curl -fsS "$PUBLIC_URL/health"
```

> Validate the compose file by hand with `docker compose -f compose.prod.yml config -q`
> — `config -q` checks it **without printing** it. Plain `config` interpolates and dumps
> every secret (`ConnectionStrings__Default`, `Smtp__Password`, `Evren__ApiKey`) into
> your scrollback and shell logs; the workflow uses `-q` for exactly that reason.

> `workflow_run` requires `deploy.yml` to be on the **default branch** (`main`), so CD
> is inert until this workflow is merged to `main`.

### The readiness gate, and what it does *not* buy

`compose.prod.yml` gives the `api` service a `healthcheck:` on `/health` — the liveness
**and database** probe (`01` §10.3). That block is what makes Caddy's
`depends_on: api: condition: service_healthy` satisfiable: before it existed, the
condition could never become true and `up -d` was not a gate at all. With it,
`up -d --wait` blocks until the API reports healthy (bounded: ~30 s start period plus
6 × 10 s checks, under the 180 s `--wait-timeout`) and **fails** instead of returning
success on a stack that is still starting. A container that cannot reach Postgres is
therefore never reported as deployed.

**Honest limit — stated, not papered over:** this is a single-node stack with in-place
container replacement, so the *traffic switch* is the same `up -d` that stops the old
API container. The gate guarantees Caddy is never (re)started against an unready API,
and it bounds how long a bad release can sit there, but it cannot keep the old API
serving after `up -d` has begun — there is no second API behind a load balancer.

The two failure classes are **not** equally safe, and the difference matters:

- **A failure at `migrate` (phase 4) leaves the old stack running — but not necessarily
  the old schema.** `api migrate` applies *every* pending migration in one run, each
  committed in its own transaction: when migration *k* fails, migrations 1..*k−1* are
  already committed. The old image keeps serving, but against a **partially advanced**
  schema. Check `__EFMigrationsHistory` before choosing a path, and do not re-deploy the
  previous tag expecting the schema to be untouched — it is not undone by that. Recovery
  is a corrective (fix-forward) migration, or the restore point.
- **A failure at `ready`/`smoke` (phases 5–6) is a full outage.** `up -d` removes the old
  `caddy` before creating the new one, and `restart: unless-stopped` never starts a
  container that was created but never started — so when the API does not become ready,
  the public entrypoint is *down*, not merely "not ready". Every minute spent deciding is
  downtime; restore service first (previous tag if the change is still expand-phase, else
  the restore point).

Zero-downtime switching needs a blue/green pair (second API + Caddy upstream switching)
and is a recorded follow-up, not something this gate claims.

### Manual (same thing, on demand)

Actions → **Deploy** → *Run workflow*. This is also the rollback path.

---

## Rollback posture

Images are **tag-pinned** by SHA, so a code rollback is a re-deploy of the previous
tag — no rebuild:

1. Find the last good short SHA (Actions run history; the `predeploy-*.marker` files, but
   only those carrying `outcome=success` — see §Backups; or `gh api .../packages`).
2. Actions → **Deploy** → *Run workflow* → set **image_tag** to that SHA.
3. The job runs the full ordered sequence again: `pull` → `backup-marker` → `migrate`
   → readiness gate → smoke-check.
   **The marker gate applies to a rollback too**, and it is the one thing that can refuse
   one: an emergency rollback is stopped at phase 3 whenever the newest archive is older
   than the RPO — exactly when a rollback is most likely. It is not bypassable from the
   workflow; deploy from the box with the block below instead (still take a fresh archive
   first if the box can: `up -d postgres`, `pg-backup.sh`).

From the box, equivalently:

```bash
cd /opt/degerli
IMAGE_TAG=<previous-sha> docker compose -f compose.prod.yml pull
export DEGERLI_BUILDER_PASSWORD="$(sed -n 's/^DEGERLI_BUILDER_PASSWORD=//p' .env | tail -n1)"
IMAGE_TAG=<previous-sha> docker compose -f compose.prod.yml run --rm \
  -e DEGERLI_BUILDER_PASSWORD api migrate
unset DEGERLI_BUILDER_PASSWORD
IMAGE_TAG=<previous-sha> docker compose -f compose.prod.yml up -d --wait --wait-timeout 180
curl -fsS "$PUBLIC_URL/health"
```

### What an image rollback covers — and what it cannot

**An image re-deploy rolls back code. It cannot roll back a schema.** Migrations are
forward-only with no down-migrations (`02-data-model.md` §8), so a release that changed
the schema leaves the schema changed no matter which image runs next. Two rules follow,
and both are standing policy (`01` §6.2/§10.8):

1. **Backward-incompatible schema changes follow expand → migrate → contract.** The
   *expand* migration adds the new shape and nothing depends on it yet, so the previous
   image still works against the migrated schema and an image rollback is safe. Only
   once the new code has run for a full deploy cycle does a later release *contract*
   (drop the old column/table). One release never expands and contracts at once.
   **Consequence, stated plainly:** roll back the image freely *inside* the expand
   window; after a contract migration the previous image is not a rollback target any
   more — recovery is a fix-forward migration or a restore.
2. **Restore-based recovery is the last resort, and it is lossy.** If the schema moved
   incompatibly and no fix-forward is possible, restore the pre-deploy restore point
   (`deploy/backup/restore.sh` restores into a scratch database and refuses this stack's
   live database name unless `ALLOW_LIVE_RESTORE=1` says so explicitly — restore →
   verify → promote, with the `api` stopped). Everything written since that archive is
   lost: the RPO is **24 h** (`01` §10.8). This is why the deploy refuses to run a
   forward-only migration without a restore point younger than the RPO (phase 3 above).

**What the deploy does *not* do:** it never rolls back automatically. A migration
failure and a readiness failure have opposite recovery paths — the first is fix-forward
or restore, the second *may* be a plain image rollback — and the job cannot tell whether
a release's change was expand-phase or contract-phase. So it stops, names the phase and
the tag that was serving, and leaves the decision to the operator, who has the release
context. An automatic revert over a migrated schema is exactly the half-migrated stack
the ordered deploy exists to prevent.

**Recorded, not hidden:** the previous half of this posture — "rollback = re-deploy the
previous image tag" — was true only for code. Before TKT-foundation-067 there was no
expand/contract rule, so a backward-incompatible migration made the previous image
silently unsafe. The rule is now the standing one (`01` §6.2/§10.8); a future release
that needs a breaking change plans it as an expand/contract pair instead of relying on
a rollback that cannot work.

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

The upload runs `rclone copy` with **no progress flag** — rclone prints nothing unless
`-P/--progress` is asked for, and rclone has no `--no-progress` flag at all (only
`-P/--progress`). An older version of this script passed `--no-progress`; a current rclone
(v1.75.2) rejects it as an unknown flag and exits 2, so the upload never happens while the
local `.dump.gpg` still lands (so the job looked like it ran). Found by the 2026-10-30
restore drill, fixed in `pg-backup.sh`; if you ever see `Fatal error: unknown flag`, that
is the shape.

### Restore drill (run once before launch — manual ledger §5.6)

[`deploy/backup/restore.sh`](backup/restore.sh) restores a chosen archive into a
**scratch** database, so it is safe on the live box:

```bash
set -a; . /etc/degerli/backup.env; set +a
/opt/degerli/deploy/backup/restore.sh /opt/degerli/backups/degerli-<timestamp>.dump.gpg
```

It decrypts → recreates the scratch database → `pg_restore`s into it, and then **prints
the two numbers the drill exists for**:

```text
restore: archive degerli-20261030T143654Z.dump.gpg — 280K, 0.0h old (RPO target ≤ 24h)
restore: verified db_size=15 MB tables=41 rows=44766 ef_migrations=12 — elapsed 3s (RTO target ≤ 4h)
```

- **elapsed** is the RTO measurement: decrypt → restore → verified counts. Target ≤ 4 h
  (architecture §10.8).
- **archive age** is the RPO exposure of the archive you picked. It is computed from the
  creation time in the archive's own name (`degerli-%Y%m%dT%H%M%SZ`), not from the file's
  mtime — the copy onto a rebuilt host (`scp`/`curl`/`cp`) has a fresh mtime, which would
  report a stale archive as minutes old. Target ≤ 24 h; an older archive still restores (it
  just loses more than the RPO allows) and the script says so in a `WARNING` line instead
  of refusing — the refusal lives in the deploy's restore-point marker below, which will
  not let a stale archive near a migration.
- **tables / rows / ef_migrations** are exact `count(*)`s over every public table, not
  `pg_stat` estimates — a restore can only be called good if the counts are the ones you
  expect, so they are printed rather than left to a hand-written query.

`--exit-on-error` is on: a failing object stops the restore where it broke (measured:
1 error line instead of 182 on a duplicate-object restore) instead of writing over a
database already known to be bad.

**A failure is loud, and never looks like a restore.** If anything fails after the
scratch database was created, the script prints the leftover database's name and the
`dropdb` line to clean it up before re-running — a half-restored database that reads as
"restored" is exactly the lie this drill is supposed to prevent.

**The scratch target is enforced.** Restoring into this stack's live database name
(`POSTGRES_DB`, read from the running container) is refused with exit 2, because the
script drops and recreates whatever it is pointed at. A deliberate live recovery — the
last-resort, lossy-to-the-RPO path of §Rollback posture — is restore → verify →
promote, and the promote step is explicit:

```bash
ALLOW_LIVE_RESTORE=1 /opt/degerli/deploy/backup/restore.sh <archive> "$POSTGRES_DB"
```

Stop the `api` first. Afterwards **drop the scratch database** — a drill that leaves a
second copy of production on the disk is a finding in itself:

```bash
docker compose -f compose.prod.yml exec postgres dropdb -U degerli degerli_restore
```

**Status: rehearsed, not yet run on the VPS.** The drill was executed on 2026-10-30
against this repo's `compose.prod.yml` on a scratch Docker host, with a
production-shaped seeded dataset and a local-directory rclone remote standing in for B2 —
the archive round-tripped byte-identically and the restored database served the shipped
API image. What that rehearsal does **not** cover (no VPS, no B2 credentials here) is
listed in [`docs/restore-drill-record.md`](../docs/restore-drill-record.md), which is the
dated record. The pre-launch item therefore stays open in one narrow form: **run the same
command once on the box with a real B2 archive**, fill in the record and drop the scratch
DB. Execution before launch remains a builder-executed manual-ledger item (test strategy
§5.6) — it cannot be automated from CI.

### The pre-deploy restore-point marker

Every deploy verifies one thing about these archives before it touches anything
(phase 3 of §Deploying, `TKT-foundation-067`): that the newest
`/opt/degerli/backups/degerli-*.dump.gpg` is **non-empty** and **no older than the RPO
(24 h)**, and then writes a small `predeploy-<tag>-<utc>.marker` beside it recording the
tag, the archive it relied on, the archive's age, `bootstrap=false` and
`outcome=attempt`. When the smoke-check passes the same marker is flipped to
`outcome=success` and stamped with `smoke_ok_at` — so **a marker left at
`outcome=attempt` is a tag that was tried, not one that served**; only `outcome=success`
markers are rollback targets. The one exception is a **bootstrap** deploy (§First-time VPS
setup step 6): on a host where no `postgres-data` volume has ever existed there is nothing
to restore, so the phase records `bootstrap=true` and requires no archive — that carve-out
applies only while the volume is absent, and every later deploy is gated unchanged.

It is a gate, not a formality — a forward-only migration has no down path, so a restore
point is the only honest undo. The deploy **stops before it migrates** when:

- there is no archive at all — `no backup archive … refusing to run a forward-only
  migration with no restore point`;
- the newest archive is empty (an aborted dump is not a restore point);
- the newest archive is older than `PRE_DEPLOY_MAX_BACKUP_AGE_HOURS` (default 24) —
  i.e. the nightly job has been failing and the RPO claim would be a lie.

The remedy is the same in all three cases and it is deliberately manual: source
`/etc/degerli/backup.env` and run `deploy/backup/pg-backup.sh`, then re-run the deploy.
There is **no override switch** — an "I know what I'm doing" flag on this check would
turn the only real undo into an optional one, which is exactly what the ordered deploy
exists to prevent. The marker does not take a backup itself, and it does not verify the
archive restores; the drill above is the thing that proves that, and it stays in the
manual ledger.

Three configuration facts about this gate, each of which can look like a bug:

- **The directories must be the same one.** The deploy looks in
  `PRE_DEPLOY_BACKUP_DIR` (default `/opt/degerli/backups`) while `pg-backup.sh` writes to
  `BACKUP_DIR` from `/etc/degerli/backup.env` (default `/opt/degerli/backups`). If they
  differ, fresh archives pile up in one place while the deploy is refused for having none
  in the other — the error message names both. Both defaults agree; keep them equal.
- **The three tunables are fixed in the workflow.** `PRE_DEPLOY_BACKUP_DIR`,
  `PRE_DEPLOY_MAX_BACKUP_AGE_HOURS` and `READINESS_TIMEOUT_SECONDS` are defaults inside
  `deploy.yml`'s remote script, which is a quoted heredoc over `ssh … bash -s` and never
  sources the VPS `.env` — setting them there has **no effect**. Change them in
  `deploy.yml` (an explicit sourced override is a future change, not a current knob).
- **A rollback dispatch passes through this gate too** (§Rollback posture item 3): if the
  newest archive is stale, the rollback is refused at phase 3; deploy from the box instead.

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
| VPS `.env` (compose) | `DOMAIN`, `POSTGRES_*`, `ConnectionStrings__Default`, `DEGERLI_BUILDER_PASSWORD`, `Smtp__*`, `Evren__ApiKey`, `APP_URL`, `ALERT_EMAIL` |
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
- **ordered-deploy rehearsal** on any change to the deploy path: load
  `compose.prod.yml` on a local Docker host and run the phases by hand (restore-point
  marker → migrate → readiness gate → smoke), including **both** failing shapes of
  `migrate`:
  - a **single** deliberately failing migration (re-applied against existing objects) —
    confirms the deploy stops before the stack is replaced; and
  - a **migrate batch that fails partway** (several migrations pending, the last one
    broken, so earlier ones commit) — confirms the recovery message's claim that the
    schema can be *partially* advanced, and that `__EFMigrationsHistory` names how far it
    got. The batch shape is the one an operator actually meets, and a single-migration
    rehearsal cannot show it.
  Also exercise the `bootstrap` marker path on a host with no `postgres-data` volume
  (last run 2026-10-30, TKT-foundation-067 — see the ticket);
- backup restore drill (once before launch);
- UptimeRobot monitor live and green (once before launch);
- README quickstart verified on a clean machine (≤ 10 commands, NFR-MDF-005).
