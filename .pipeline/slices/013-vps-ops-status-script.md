---
id: SLICE-013
status: done
scope:
  - deploy/status.sh
  - deploy/README.md
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
   - **Health** — `curl` `GET /health` through Caddy on `localhost` (bounded:
     `--max-time 5 --connect-timeout 3`, mirroring the deploy smoke-check), reporting
     the HTTP code and latency.
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
  backup-marker refusal plus the `pg-backup.sh` remedy; and the opt-in `--logs [N]`
  (default 50) / `--errors` (last 24 h, `--tail 2000` in, `tail -n 200` out,
  case-insensitive `error|fail|exception`). Exit 0 for every completed report, 2 only for
  a usage error.
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

