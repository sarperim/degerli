# Restore drill record

The dated record of the backup **restore** drill — the manual-ledger item (test strategy
§5.6, architecture `01` §10.8, NFR-MOV-003). One entry per execution; the newest entry
first. A drill that was not run is **not** an entry, and nothing here is inferred from a
script having been read.

What this file answers, in the order the ticket asks for it: which archive, how old, how
long the restore took, what landed, what was spot-checked, what went wrong, and which of
the RTO/RPO claims are actually supported.

---

## 2026-10-30 — rehearsal drill on the production compose stack (not on the VPS)

**Class: `manual` · rehearsal substitute — a real restore, on a scratch host.** The
builder/VPS run with a real B2 archive is **still open** (see *What this did not
exercise*). This entry is not a claim that the drill has been performed in production.

| | |
|---|---|
| Date (UTC) | 2026-10-30, 14:05–14:38 |
| Executed by | coder session for `TKT-foundation-076` (D6 unpark of 056 item 13) |
| Environment | scratch Docker host, **not** the VPS; Docker 28.3.3 / Compose v2.39.2 |
| Stack | this repo's `compose.prod.yml`, unmodified, one project per rehearsal |
| Images | `postgres:17` (as pinned) and the API image built from this tree (`degerli-api-rehearsal:t1`) |
| Data | **not production data** — a production-shaped seeded dataset: 100 instruments, 25,000 daily prices, 800 financial statements with 16,000 line items, 2,088 macro values, one index with 250 levels and 100 constituents, 250 market snapshots, 20 ingest-run rows, 12 EF migrations applied |
| Encryption | the real path: GnuPG symmetric AES-256, passphrase via fd 3, exactly as `pg-backup.sh` writes it |
| Remote | **a local-directory rclone remote standing in for Backblaze B2** (no B2 credentials in this environment) |

The harness — seed SQL, the per-table fingerprint queries, the timed runner — was
**throwaway, written in `/tmp` and not committed**: this ticket's scope is the deploy
scripts plus this record. The dataset's shape is described above so the exercise can be
repeated in kind; there is deliberately no reusable drill harness in the repo.

### The archive

| | |
|---|---|
| Archive | `degerli-20261030T143654Z.dump.gpg` |
| Size | 284,604 bytes (278 KiB) — of a 15 MB database |
| SHA-256 | `e1ac69b82aa1ba75d1d93c241bdcf8fc73b6a9f0c3c8c406468980e34d27986c` |
| Age at restore | 0.0 h (taken minutes before; RPO target ≤ 24 h) |
| Uploaded copy | byte-identical SHA-256 at the remote — the archive under test is the one the upload leg produced, not the local one |
| Backup leg | `pg-backup.sh` exit 0 in **0.65 s and 1.08 s** (the two timed runs of the fixed script: dump → encrypt → upload → retention) |

### The restore

```text
restore: archive degerli-20261030T143654Z.dump.gpg — 280K, 0.0h old (RPO target ≤ 24h)
restore: decrypting
restore: restoring 280K into scratch database 'degerli_restore'
restore: verifying
restore: verified db_size=15 MB tables=41 rows=44766 ef_migrations=12 — elapsed 3s (RTO target ≤ 4h)
restore: done.
```

**Elapsed (RTO measurement): 1–5 s** across five runs of an archive taken from this
dataset (1 s, 2 s, 2 s, 3 s, 5 s — the drift is the shared rehearsal host, which had up to
four copies of the database live at once; the clean single-run measurement was **1 s** of
script time, 1.7 s wall including container start-up). The script's `elapsed` clock starts
after decryption begins and stops when the verification query returns.

### What landed — counts and checks

Every check below was run against the restored database; the comparison is against the
source database the archive was taken from, in the same session.

1. **Schema.** `pg_dump --schema-only` of source vs restored are **identical**: 41 tables,
   25 indexes, 75 constraints (the only differing bytes in the raw dumps are the
   per-dump `\restrict` nonce, which is not schema).
2. **Data, table by table.** For all **40** application tables: row count **and** a
   content `md5` over the table's rows sorted by their text form. Source and restored
   fingerprints are **identical** (44,754 rows across those 40 tables; the 41st table in
   the script's count is `__EFMigrationsHistory`, whose 12 rows make the 44,766 total).
3. **Migrations.** `__EFMigrationsHistory` carries all 12 rows, and `api migrate` against
   the *restored* database answers `migrate: database schema is up to date.` — the archive
   is a post-migration database, so a restore followed by a deploy's `migrate` is a no-op
   rather than a re-application.
4. **Spot rows** (source | restored, identical on both):
   - `daily_prices` TST001 @ 2026-10-16 → close `4.2800`, adjusted `4.3158`, volume `100137`;
   - `daily_prices` TST050 @ 2026-10-16 → close `4.2700`, adjusted `4.3057`, volume `106850`;
   - `financial_statements` TST001 annual → version `1`, published `2026-02-14`, 20
     `fin_line_items` rows;
   - `financial_statements` TST050 annual → the same shape, 20 line items;
   - `asp_net_users` → the bootstrap builder account (`builder@degerli.local`,
     `email_confirmed = true`, password hash preserved).
5. **Identity sequences.** `instruments_id_seq`, `financial_statements_id_seq`,
   `index_constituents_id_seq` are at the same values in both databases (`200`, `1600`,
   `200`) — a restored database can accept new rows without colliding with restored ones.

### The restored database actually serves

The stronger check than row counts: a **fresh compose project** (no containers, no
volumes), then the recovery sequence by hand, with the shipped images.

| Step | Cumulative |
|---|---|
| start (empty project; images already local) | 0 s |
| `postgres` healthy | 7 s |
| live restore of the archive into the production database name (`ALLOW_LIVE_RESTORE=1`) | 10 s |
| `api migrate` → `schema is up to date` | 15 s |
| `api` healthy (its `/health` probe includes the database) | 22 s |

Probes through the running API container:

| Request | Result |
|---|---|
| `GET /health` | `200`, body `Healthy` |
| `GET /api/v1/stocks` | `200`, **100** instruments, `asOf` `2026-10-16` |
| `GET /api/v1/stocks/TST001/overview` | `200`, `lastPrice 4.2800`, `prevClose 4.2700` — the restored price |
| `GET /api/v1/market/overview` | `200`, breadth 55/40/5 from the restored `market_snapshots` |

**So: 21–22 s from an empty Docker project to an API answering real queries out of a
restored database**, 100 instruments and 25,000 price rows included (two runs: 21 s and
22 s). The number is a *rehearsal* number: the images were already on the host and no
TLS/domain work was done.

### The RTO / RPO claims, against what was observed

| Claim (`01` §10.8) | Observed | Verdict |
|---|---|---|
| **RTO ≤ 4 h** (restore of an archive to a serving stack) | restore leg **1–5 s**; empty-project-to-serving-API **22 s** on a host with images local | **Supported with a very large margin** — the restore leg is four orders of magnitude inside the bound. What is *not* measured: pulling the three images over the VPS's link and any host provisioning; those are minutes, not hours, but they are an extrapolation, not a measurement (see *not exercised*). |
| **RPO ≤ 24 h** (nightly archive; the loss window after a restore) | not a property of a single restore: it is the age of the newest archive, enforced by the deploy's restore-point marker (`01` §6.2 phase 3) and by `pg-backup.sh`'s nightly schedule | **Supported as a schedule claim**: the drill restored a 0.0 h-old archive, and an archive aged past 24 h restores fine but is flagged by the script's `WARNING` (and refused by the deploy marker). What is *not* measured: a real 24 h cadence on the VPS (the job has never run there). |

**Scale caveat, stated as an estimate:** the architecture sizes the production database
at 1–5 GB (`01` §5). The rehearsal restored 15 MB of database in ~2 s ≈ **7.5 MB/s of
restored database size**; at that rate 5 GB is ≈ **11 minutes**, still far inside 4 h —
but that is a linear extrapolation from a 15 MB database, not a measurement of a 5 GB one,
and `pg_restore` throughput on indexes does not scale perfectly linearly.

### Gaps and defects this drill found

1. **The nightly backup never uploaded on a current rclone — fixed.** `pg-backup.sh`
   passed `--no-progress` to `rclone copy`; that flag was **removed upstream**, so a
   current rclone (v1.75.2 here) exits 2 with `unknown flag: --no-progress` *after*
   encrypting, leaving the archive on the host only and nothing in B2. The deploy's
   restore-point marker would then refuse every deploy for having no archive, and a
   restore would be attempted from a host-local file that a rebuilt box does not have.
   This is a backup-path defect (worse than a restore-path one: it makes the RPO claim
   false in the direction that matters). Fixed in the same PR by dropping the flag —
   rclone prints no progress unless `-P` is asked for, so nothing is lost.
2. **The restore gave no verification and no numbers — fixed.** The old script restored
   and then told the operator to go count rows by hand, so a drill record could not be
   produced from its output. It now prints the archive's size and age, and after the
   restore the database size, exact table/row counts and the EF migration count, plus the
   elapsed seconds.
3. **A failed restore left a database that looks restored — fixed.** On a truncated
   archive the old script exited 1 *after* leaving a `degerli_restore` containing the
   schema and the seed rows but none of the data. It now names the leftover database and
   prints the `dropdb` line. `pg_restore` also gained `--exit-on-error` (measured on a
   duplicate-object restore: 1 error line instead of 182; the exit code was already 1
   either way, so this is about failing where it broke, not about detecting failure).
4. **The script could be aimed at the production database — fixed.** `restore.sh <archive>
   degerli` (a plausible mistype, and exactly what a real DR promote needs) dropped and
   recreated the live database with no confirmation. It now reads `POSTGRES_DB` from the
   running container and **refuses** that name with exit 2 unless
   `ALLOW_LIVE_RESTORE=1` is set, which is the documented, deliberate promote leg.
   Exercised in both directions on the rehearsal: refused (exit 2, nothing dropped) and
   permitted (restored into `degerli` on the throwaway project).
5. **Failure shapes are loud** (all exercised, scratch targets only):

   | Shape | Behaviour |
   |---|---|
   | truncated archive (150 KB of the dump) | `pg_restore: could not read from input file: end of file`, exit 1, leftover DB named + drop instruction printed |
   | wrong passphrase | `gpg: decryption failed: Bad session key`, exit 2 — **no database created** (decryption precedes it) |
   | empty archive | `gpg: decrypt_message failed`, exit 2, no database created |
   | missing archive | `restore: no such archive: …`, exit 1 |
   | `BACKUP_PASSPHRASE` unset | named error from the `:?` guard, exit 1 |
   | archive 26 h old | restore **succeeds**; `WARNING` explains the RPO breach and that the nightly job has been failing |
   | archive 20 h old | restore succeeds, no warning |
   | custom scratch target | restores into the named database |
   | target = production DB name | refused, exit 2, unless `ALLOW_LIVE_RESTORE=1` |

6. **Disk space: no precheck, deliberately.** A restore needs room for the archive plus a
   second full copy of the database. Neither `createdb` nor `pg_restore` proceeds quietly
   when the filesystem is full — they fail with a `No space left on device` that the new
   failure path reports — so a size heuristic in the script would add a guess (dump ratio)
   without adding a guarantee. Recorded here as a considered non-change rather than an
   omission.

### What this drill did **not** exercise (never inferred from a passing rehearsal)

- **The VPS itself.** No SSH access in this environment: host provisioning, the real
  `/opt/degerli` checkout, `/etc/degerli/backup.env`, the systemd/cron line and the
  `docker compose` project on the box are untouched by this run.
- **Backblaze B2.** The upload and the restore read a **local-directory rclone remote**.
  The B2 leg (credentials, bucket policy, `rclone copy` over the real transport, retention
  deletion on the remote) is exercised to the extent that the rclone command line is the
  same one — which is exactly where gap 1 lived, so do not read this as "B2 works".
- **A real 24 h cadence.** One archive, taken minutes before the restore. The nightly
  timer has never run.
- **Production data volume.** 15 MB of a synthetic dataset, not the 1–5 GB estimate; the
  RTO extrapolation is labelled as an estimate above.
- **Caddy, TLS and the public URL.** The rehearsal probed `/health` and the data
  endpoints *inside* the `api` container; the `caddy` service was not started (it needs a
  real `DOMAIN` and Let's Encrypt), so the internet-facing half of "the stack serves
  again" is unproven here.
- **A rollback after a real contract migration.** Not this drill's subject (that is
  067's), recorded because the recovery ladder leans on it.
- **A 5 GB restore's wall clock**, and `pg_restore` parallelism — the script runs a single
  `pg_restore` with no `-j`, deliberately: one stream out of the container's stdin.

### Contract consequences

- **No architecture amendment.** `01` §10.8's claims (RTO ≤ 4 h, RPO ≤ 24 h) were
  **not** contradicted — nothing needed correcting, and the section is out of this
  ticket's scope. Its "restore drill documented in README (target: exercised once before
  launch)" sentence stays true: this was a rehearsal, not the pre-launch run.
- **The pre-launch manual-ledger item narrows** (test strategy §5.6): the script is no
  longer untested; what remains is the builder running the same command on the box with a
  real B2 archive and filling in an entry here.
- **Cross-reference:** 067 recorded an open evidence item — "an executed rollback variant
  (… and/or `restore.sh` into the scratch DB) remains open". The *restore* half of that
  item is now executed and recorded above (including a live restore into the production
  database name on a throwaway project); the previous-tag re-deploy half still belongs to
  that lane.

### Cleanup — no database left behind, no production data touched

- Every rehearsal restore targeted a **scratch** database (`degerli_restore`,
  `degerli_restore_alt`) on a scratch compose project, or the *throwaway* project's own
  `degerli` database while standing in for a fresh host; **no production database — none
  exists in this environment — was modified.**
- All scratch databases were dropped at the end of the session, and the throwaway
  compose project was removed with its volume (`docker compose down -v`). Verified after
  the fact: no `degerli_restore*` database remains, and the rehearsal project has no
  containers or volumes.
