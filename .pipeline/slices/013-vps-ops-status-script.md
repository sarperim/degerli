---
id: SLICE-013
status: blocked
blocked_reason: >
  Review round 3 ESCALATED (PR #183): the slice's code is review-clean (all DR-1..DR-6,
  DR2-1..DR2-4 findings verified closed; facet gate clean), but the required Containers
  (Trivy) CI check is red on a newly published upstream CVE — CVE-2026-109518 (apt /
  libapt-pkg6.0t64, HIGH, no fixed version) in the dotnet aspnet base image. It fails
  identically on dev, blocking every PR repo-wide, and no commit on this branch touches
  an image or dependency. Remedy: a separate hygiene slice adding a dated, path-scoped,
  self-expiring .trivyignore.yaml entry per deploy/README.md §Container baseline; this
  PR needs no further code review and goes green on rebase after that lands. Artifacts:
  .pipeline/slices/reviews/SLICE-013-round-{1,2,3}.md
scope:
  - deploy/status.sh
  - deploy/README.md
  - .gitignore
  - .pipeline/slices/013-vps-ops-status-script.md
---

# SLICE-013 — VPS Ops Insight: One-Command Status Script

## Context & Goal

The production runbook (`deploy/README.md`) documents the deploy, rollback, and backup
posture thoroughly, but day-to-day insight on the VPS is a handful of memorized commands:
`docker compose -f compose.prod.yml ps`, `docker stats`, `docker compose logs`, and a
hand-written check of backup freshness. The UptimeRobot monitor (§Monitoring) is the
outside-in liveness check and stays a manual-ledger item (an external account cannot be
created from CI) — what is missing is the **on-box, one-command** counterpart.

This slice adds `deploy/status.sh`, a **read-only** script that assembles the whole
operational picture in one shot, and documents it in the runbook:

1. **`deploy/status.sh` (new)** — run from anywhere (it resolves the repo root relative
   to itself, like `deploy/backup/pg-backup.sh` and `deploy/backup/restore.sh` do):
   - **Compose state** — `docker compose ps` for the stack (compose file overridable via
     `COMPOSE_FILE` env, default `compose.prod.yml`, so the script is also usable against
     the CI stack locally).
   - **Health** — `curl` `GET https://${DOMAIN}/health` **resolved to loopback**
     (`curl --resolve "${DOMAIN}:443:127.0.0.1"`), bounded (`--max-time 5
     --connect-timeout 3`, mirroring the deploy smoke-check), reporting the HTTP code
     and latency. `DOMAIN` is read from the VPS `.env` by **single-key extraction**
     (the same `sed -n 's/^DOMAIN=//p' .env | tail -n1` shape deploy.yml uses for the
     builder password — never `cat .env`), then **normalised the way Compose reads
     `.env`**: a surrounding quote pair, trailing whitespace and an inline `#comment`
     are stripped (deploy.yml:296-297, "last assignment wins, optional quotes"), so a
     quoted `DOMAIN="degerli.example"` probes `degerli.example`. `HEALTH_URL` remains as
     an explicit override. Rationale (review round 1, DR-1): Caddy's only site block is
     `{$DOMAIN}` — it holds no certificate for `localhost`, so an `https://localhost`
     or `Host: localhost` probe cannot succeed on a healthy production box; probing
     the real domain over loopback mirrors the deploy smoke-check
     (`${PUBLIC_URL}/health`) without leaving the box.
   - **Resource usage** — `docker stats --no-stream` for the project's containers only,
     plus `docker system df` and the size of the `postgres-data` volume.
   - **Backup freshness vs RPO** — newest `backups/degerli-*.dump.gpg` age compared to
     the 24 h RPO, printed as `OK` or `WARNING: older than the 24h RPO — the next deploy
     will refuse at the backup-marker phase`. Age **must** be computed from the timestamp
     embedded in the archive name (`degerli-%Y%m%dT%H%M%SZ`, the way `restore.sh` does
     it), never from mtime — a copied archive has a fresh mtime.
   - **Logs (opt-in flags)** — `--logs [N]` prints the last N lines per service
     (default 50 when the flag is given without a value); `--errors` greps the last 24 h
     of logs for `error|fail|exception` (case-insensitive, tail-bounded).
   - **Graceful degradation** — every section reports honestly when its prerequisite is
     missing (docker down, no backups dir, no running containers) instead of aborting
     the report. Exit code is 0 for a completed report (a status report is not a gate);
     nonzero only for usage errors.
2. **`deploy/README.md`** — new **"Ops commands"** subsection (placed beside
   §Monitoring) documenting `deploy/status.sh`, its flags, and how it complements the
   UptimeRobot monitor (outside-in liveness vs on-box state). The manual-ledger line
   "UptimeRobot monitor live and green (once before launch)" is unchanged — the slice
   does not automate account creation.
3. **`.gitignore`** — add `.pipeline/slices/reviews/` (review round artifacts;
   `.pipeline/plan/reviews/` is already ignored, the slices twin was missed) — folded
   in by user decision during review round 1.

## Rules & Invariants

- **Read-only:** the script must not mutate any state — no `up`/`down`/`restart`, no
  writes outside stdout. It is a viewer, not an operator.
- **No secrets in output:** never `cat .env`, never plain `docker compose config`
  (which interpolates and dumps every secret — the runbook's §Deploying warning); only
  `config -q` if validation is ever needed.
- **Fail-honest, not fail-loud:** a missing piece is reported in the output ("no backup
  archive found"), never hidden and never fatal to the rest of the report.
- **RPO arithmetic mirrors the deploy** (`deploy.yml` phase 3 / `PRE_DEPLOY_MAX_BACKUP_AGE_HOURS`
  default 24) and `restore.sh`'s archive-name timestamp convention.
- `bash` with `set -euo pipefail` and `set -o pipefail`-safe constructs; no bashisms
  beyond what the other `deploy/` scripts already use.

## Blockers & Dependencies

- None. Independent of the in-flight SLICE-011/012 work (no overlapping files).

## Verification

- `bash -n deploy/status.sh` — parses clean.
- `deploy/status.sh --help` — exits 0 with usage text on a machine without Docker
  (proves graceful degradation).
- Against a real stack (Docker available): `docker compose -f compose.ci.yml up --build --wait`
  then `COMPOSE_FILE=compose.ci.yml bash deploy/status.sh` — exits 0, reports compose ps,
  `/health` 200, and honestly notes the absence of a backup archive (the CI stack has
  none). Tear the stack down afterwards (`docker compose -f compose.ci.yml down -v`).
- `git diff --stat` shows only the two scoped files plus the slice file.

## Result

Implemented and verified locally on 2026-11-05 (branch `SLICE-013`, forked from `dev`).

**What landed**

- `deploy/status.sh` (new, `set -euo pipefail`, read-only) — resolves the repo root from
  `BASH_SOURCE`, honours `COMPOSE_FILE` / `HEALTH_URL` / `BACKUP_DIR`, and prints five
  sections, each degrading on its own: `compose ps`; bounded `GET /health` (`--max-time 5
  --connect-timeout 3`) reported as HTTP code + latency and mapped to a named failure mode
  (timeout / refused / DNS / TLS); `docker stats --no-stream` scoped to the project's own
  container IDs, `docker system df`, and the `<project>_postgres-data` size read out of
  `docker system df -v` (no throwaway container, no writes); backup freshness from the
  newest `degerli-*.dump.gpg` by **name** with the age computed from the
  `degerli-%Y%m%dT%H%M%SZ` timestamp, `OK` vs the 24 h RPO or a `WARNING` naming the
  backup-marker gate plus the `pg-backup.sh` remedy; and the opt-in `--logs [N]`
  (default 50) / `--errors` (last 24 h, `--tail 2000` in, `tail -n 200` out,
  case-insensitive `error|fail|exception`). Exit 0 for every completed report, 2 only for
  a usage error. **Since review round 1** the health probe is
  `GET https://${DOMAIN}/health` with `--resolve "${DOMAIN}:443:127.0.0.1"` (DOMAIN read
  from the compose project's `.env` by single-key `sed`; `HEALTH_URL` remains the explicit
  override) — see "Review round 1 fixes" below.
- `deploy/README.md` — new **§Ops commands — one-shot status report** between §Backups and
  §Monitoring (section table, the viewer-not-operator and no-secrets rules, the two
  deliberate "the naive version lies" details, exit-code policy, tunables), plus a
  cross-reference from §Monitoring stating it is the on-box half of the same question. The
  manual-ledger line "UptimeRobot monitor live and green (once before launch)" is
  unchanged.

**Verification**

| Command | Outcome |
|---|---|
| `bash -n deploy/status.sh` | parses clean |
| `shellcheck -S warning deploy/status.sh` | no findings (exit 0) |
| `bash deploy/status.sh --help` | exit 0, full usage text, no Docker needed |
| `bash deploy/status.sh --bogus` | exit 2 + usage on stderr (usage errors are the only non-zero) |
| `bash deploy/status.sh` (no Docker stack, no `backups/`) | exit 0 — `compose ps` reports the interpolation error verbatim, `/health` DOWN, resources skip, backup section honestly says "no backup directory … RPO status unknown" |
| `BACKUP_DIR=<tmp> bash deploy/status.sh` with a fresh + a 2020 archive | prints the fresh one, `age: 0.0h`, `OK — inside the 24h RPO` |
| `BACKUP_DIR=<tmp> bash deploy/status.sh` with only the 2020 archive | `age: 60000.8h`, `WARNING: older than the 24h RPO — the next deploy will refuse at the backup-marker phase`, plus the remedy line — **named, not mtime-derived** (both files had "now" as mtime) |
| `COMPOSE_FILE=<ci stack> bash deploy/status.sh` against a running CI stack | exit 0 — `compose ps` lists api/mailpit/postgres healthy, `GET http://127.0.0.1:18080/health` → `OK — HTTP 200 in 0.006927s`, `docker stats` for the three project containers, volume `50.63MB`, backup section honestly reports the absence of `backups/` |
| `… --logs 3 --errors` | per-service tails printed (empty api tail reported as "(no log output in the tail window)"), `--errors` surfaced the postgres `ERROR: relation "__EFMigrationsHistory" does not exist` line |
| stack teardown | `docker compose down -v` — no leftover containers/volumes/networks |

**Deviation in the stack check (environment, not code):** the literal
`docker compose -f compose.ci.yml up` cannot run in this sandbox — another agent's
`degerli-dev-*` stack already owns `127.0.0.1:8025` and `:8080`, and the `mailpit` bind
fails with "port is already allocated". The stack check was therefore run against a
`/tmp`-only copy of `compose.ci.yml` with **only** the project name and the two host ports
remapped (`18025`/`18080`) and the build context made absolute; no repo file was modified
and the copy was deleted afterwards. The script's own behaviour is unchanged by this — it
was pointed at a stack via `COMPOSE_FILE` exactly as the slice intends.

**Scope:** `git diff --stat` touches only `deploy/status.sh`, `deploy/README.md`, and this
slice doc. No tests were added; the slice adds no application behaviour and the repo's
targeted gates for a shell script are its parse + lint + the commands above.

### Review round 1 fixes (round 2, same branch)

Fixed DR-1..DR-6 from `.pipeline/slices/reviews/SLICE-013-round-1.md`, plus the folded
`.gitignore` hygiene item. `deploy.yml` was **not** changed (DR-3's mtime/name divergence is
a recorded follow-up, out of this slice's scope).

| Finding | Change |
|---|---|
| **DR-1** (blocker) | Default production probe is now `GET https://${DOMAIN}/health` with `curl --resolve "${DOMAIN}:443:127.0.0.1"`, still bounded `--max-time 5 --connect-timeout 3`. `DOMAIN` is extracted from `${COMPOSE_PROJECT_DIR}/.env` with `sed -n 's/^[[:space:]]*DOMAIN[[:space:]]*=[[:space:]]*//p' … \| tail -n1 \| tr -d '\r'` — the deploy.yml builder-password shape; the `.env` is never catted or sourced. `HEALTH_URL` stays as the explicit override (no `--resolve` applied to it). README health row, tunables paragraph and usage text updated. |
| **DR-2** (major) | The `name:` extraction now carries `\|\| true`, and the volume block is gated on `[ -r "$COMPOSE_FILE" ]` — an unreadable compose file reports `volume: unknown (compose file … is not readable)` instead of aborting the report. |
| **DR-3** (minor) | The RPO `WARNING` no longer asserts "the next deploy will refuse": it says a restore point this old would not satisfy the backup-marker gate and states explicitly that the gate selects by mtime while this report ages the NAME, so a copied archive can disagree. README bullet reworded the same way. The no-archive messages now say "a non-bootstrap deploy", matching deploy.yml. |
| **DR-4** (nit) | Every RPO message derives from `${RPO_MAX_HOURS}` (no hardcoded "24h"); the `--errors` 24 h window is a separate tunable and unchanged. |
| **DR-5** (nit) | Header comment and README now state the repo root is resolved from `BASH_SOURCE` and that this is *deliberately different* from `deploy/backup/*.sh`, which default to a fixed `/opt/degerli` path. |
| **DR-6** (nit) | `HEALTH_URL` documented in the README ops section as an explicit override of the derived probe. |
| **hygiene** | `.gitignore` gains `.pipeline/slices/reviews/` beside `.pipeline/plan/reviews/`. |

**Round-2 verification** (targeted only):

| Command | Outcome |
|---|---|
| `bash -n deploy/status.sh` | parses clean |
| `shellcheck -S warning deploy/status.sh` | no findings |
| `bash deploy/status.sh --help` | exit 0 |
| `bash deploy/status.sh --bogus` | exit 2 + usage |
| `bash deploy/status.sh` (no stack) | exit 0, every section degrades honestly; health reports "DOMAIN is not set in …/.env" |
| `COMPOSE_FILE=/tmp/opencode/nope.yml bash deploy/status.sh` | **exit 0** (was 2), report completes, `volume: unknown (compose file … is not readable)` |
| DR-1 mechanism, in `unshare -rn` netns: self-signed `degerli.test` TLS server on 127.0.0.1:443 | `curl --resolve degerli.test:443:127.0.0.1 --cacert cert.pem https://degerli.test/health` → `http_code=200 remote=127.0.0.1`; the same URL **without** `--resolve` → exit 6 (DNS) — proves the resolve pins the real name to loopback and the Host/SNI is the domain |
| Same netns, the script itself with a test compose file (`caddy:` service) + `DOMAIN=degerli.test` in `.env` and `CURL_CA_BUNDLE` set | `GET https://degerli.test/health (resolved to 127.0.0.1)` → `OK — HTTP 200 in 0.005292s`; with the TLS server stopped → `DOWN — connection refused (curl exit 7)` |
| `HEALTH_URL=http://127.0.0.1:18099/health` against a local plain-HTTP server | `GET http://127.0.0.1:18099/health` → `OK — HTTP 200` (override bypasses `--resolve`) |
| Backup fixtures, both archives mtime "now" | fresh name → `newest: degerli-20261105T000000Z…`, `age: 2.1h`, `OK`; only the 2020 name → `age: 60002.1h`, new RPO wording, exit 0 |
| `git check-ignore -v .pipeline/slices/reviews/test` | matched by `.gitignore:4` |

**Scope (round 2):** `git diff --stat dev...HEAD` — `deploy/status.sh`, `deploy/README.md`,
`.gitignore`, this slice doc.

### Review round 2 fixes (round 3, same branch)

Fixed DR2-1..DR2-4 from `.pipeline/slices/reviews/SLICE-013-round-2.md`. DR2-5 (RPO constant
vs `deploy.yml`, `%.1f` rounding) stays deferred to a follow-up slice — unchanged here. The
red Trivy gate finding (CVE-2026-109518 in the `dotnet/aspnet` base image) is out of this
slice's scope and left to the user's decision; no `.trivyignore.yaml` entry was added.

| Finding | Change |
|---|---|
| **DR2-1** (blocker) | The `else` glued onto the end of the DOMAIN-absent `echo` string now starts its own line, so the inner `if [ "$caddy_present" -eq 1 ]` has a real `else` again. Both branches verified below. |
| **DR2-2** (major) | The extracted `DOMAIN` is now normalised the way Compose reads `.env` and the way deploy.yml:296-297 does: trailing whitespace and an inline `#comment` are stripped, then a surrounding `"`/`'` pair — so quotes never reach the URL or the `--resolve` argument. A quoted end-to-end value is left alone (a `#` inside it is literal). |
| **DR2-3** (minor) | README health "degrades to" cell now names both cases (DOMAIN absent / no readable `.env`; no `caddy` service and no published `api` port) and the tunables paragraph documents the normalisation. This table records the output verbatim, not a summary. |
| **DR2-4** (nit) | The DOMAIN-absent message distinguishes the two states: `DOMAIN is not set in …/.env` when the file is readable, `DOMAIN is not set and there is no readable …/.env` when it is not (`[ -r ]` test). |

**Round-2 verification** (targeted only, `/tmp` fixtures, no full suite):

`bash -n deploy/status.sh` → parses clean; `shellcheck -S warning deploy/status.sh` → no
findings (the pre-existing `SC2012` *info* on the `ls` of backup archives is unchanged).

DR2-1 — both degraded branches now print their own message and nothing else:

```
$ COMPOSE_FILE=compose.ci.yml bash deploy/status.sh        # caddy absent, no api port derivable
== health ==
  no health endpoint could be derived from /…/compose.ci.yml (no caddy service and
  no published api port). Set HEALTH_URL explicitly to probe one.

== resources ==
```

```
$ # fixture compose with a caddy service, .env present but no DOMAIN
== health ==
  no health endpoint could be derived: DOMAIN is not set in
  /tmp/opencode/slice013-fix/.env, so Caddy's site name is unknown. Set DOMAIN there, or
  set HEALTH_URL explicitly to probe a target you name.

== resources ==
```

```
$ # same fixture, no .env at all
== health ==
  no health endpoint could be derived: DOMAIN is not set and there is no readable
  /tmp/opencode/slice013-fix/.env, so Caddy's site name is unknown. Create one with DOMAIN, or
  set HEALTH_URL explicitly to probe a target you name.

== resources ==
```

DR2-2 — every `.env` form yields exactly the same clean probe target:

| `.env` content | health line |
|---|---|
| `DOMAIN=degerli.example` | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| `DOMAIN="degerli.example"` | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| `DOMAIN='degerli.example'` | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| `  DOMAIN  =  degerli.example  ` | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| `DOMAIN=degerli.example   # public site` | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| `DOMAIN="degerli.example"   # public site` | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| `DOMAIN=old.example` + `DOMAIN="degerli.example"` (last wins) | `GET https://degerli.example/health (resolved to 127.0.0.1)` |
| CRLF (`DOMAIN=degerli.example\r\n`) | `GET https://degerli.example/health (resolved to 127.0.0.1)` |

**Scope (round 3):** unchanged — `deploy/status.sh`, `deploy/README.md`, `.gitignore`, this
slice doc.
