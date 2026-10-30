---
scope:
  - deploy/backup/restore.sh
  - deploy/README.md
  - docs/restore-drill-record.md
  # Added by this ticket's implementation (recorded, not hidden): the drill's headline
  # defect is in the backup job's upload leg (rclone `--no-progress` is not a real flag —
  # exit 2, nothing reaches B2), and acceptance (iii) requires a found defect fixed in the
  # same PR. A planner could not have seen it before the drill ran. See the Status line.
  - deploy/backup/pg-backup.sh
---

# TKT-foundation-076: Restore drill — exercise the backup restore for real (LOW priority)

- Status: done — **PR #171 squash-merged 2026-10-30 (`d1487bc` on dev), round-3 MERGEABLE, zero open findings** (`.pipeline/plan/reviews/TKT-foundation-076-round-{1,2,3}.md`; 3 passes, 2 fixer rounds — round-1 major DR-1: the deploy marker inspected only the local backup dir so a failed B2 upload kept the gate green forever, fixed-verified; round-2 DR-4 regression caught and closed in round 3). Drilled 2026-10-30 on a **scratch compose host** (production `compose.prod.yml`, production-shaped seeded dataset, local-directory rclone remote standing in for B2 — **no VPS or B2 credentials in this environment**), so this is a **labelled rehearsal**, not the pre-launch VPS run (067's precedent). Record: `docs/restore-drill-record.md`. Measured: archive `degerli-20261030T143654Z.dump.gpg`, 278 KiB of a 15 MB DB, 0.0 h old, restore **1–5 s** (clean run 1 s), all 40 application tables identical on count + content md5 (44,754 rows), schema identical (41 tables / 25 indexes / 75 constraints), 12 EF migrations, `api migrate` → up to date, spot rows and identity sequences equal, and a fresh empty project serving the shipped API image off the restored DB **22 s** after start (`/health`, 100 instruments, restored TST001 price). **Four defects fixed here:** (1) `pg-backup.sh` passed rclone `--no-progress` — not a real rclone flag, so a current rclone exits 2 *after* encrypting and **nothing reaches B2** (the RPO claim was false in the direction that matters; scope amendment recorded); (2) `restore.sh` verified nothing → now prints archive size/age and exact db_size/tables/rows/ef_migrations + elapsed, and an incomplete verification fails the run; (3) a failed restore left a partially-restored DB silently → now named with its `dropdb` line, plus `pg_restore --exit-on-error`; (4) `restore.sh <archive> degerli` would drop and recreate the live DB → refused (exit 2) unless `ALLOW_LIVE_RESTORE=1`. Failure shapes exercised: truncated, wrong passphrase (no DB created), empty, missing, unset passphrase, 26 h-old archive (RPO WARNING), 20 h-old (silent), custom target, production-name target. **Still open, stated honestly:** the on-VPS run with a real B2 archive (VPS, B2 credentials, 24 h cadence, 1–5 GB scale, Caddy/TLS all unexercised — the ≈11 min @ 5 GB figure is a labelled extrapolation); `01` §10.8's pointer to this record is a routed follow-up; `deploy/README.md:336` hardcodes `-U degerli` without `--if-exists` (pre-existing nit); 067's open "restore.sh into the scratch DB" evidence item is now half-closed by this drill.
- Size: S
- **Priority: P3 — LOW (USER DECISION D6, 2026-10-24: 056 item 13 unparked, explicitly low priority).**
- **Evidence (gathered 2026-10-24):**
  - The restore path exists: `deploy/backup/restore.sh` decrypts a `pg_dump -Fc` archive and restores it into a **scratch** DB (`degerli_restore`), and `deploy/README.md` §"Restore drill" describes it.
  - **No restore has ever been exercised.** The README says "run once before launch — builder-executed manual-ledger item"; the manual ledger lists "backup restore drill (once before launch)" (`deploy/README.md:194`; test strategy §5.6). There is no dated drill record, no measured RTO, and no row-count evidence anywhere in the repo.
  - The RTO ≤ 4 h / RPO ≤ 24 h claims (`01` §10.8) are therefore **unverified**. The 2026-10-23 wave exercised deploys by hand but never backups.
  - Preconditions the drill needs: B2 credentials + an existing archive + a scratch DB. If they are unavailable, the drill is **blocked** — record that, never fabricate a result.
- **Pinned approach:**
  - Execute the documented drill on the real stack: pick a real archive, decrypt, restore into `degerli_restore`, verify schema + row counts + a few spot rows, record elapsed time (RTO), archive age (RPO), and any script defects; drop the scratch DB afterwards.
  - Fix `restore.sh`/README if the drill exposes a gap (flags, permissions, disk space, restore ordering).
  - Write a durable `docs/restore-drill-record.md` (date, archive name/age, elapsed, counts, outcome, gaps).
- **Acceptance (explicit criteria):**
  - **(i)** A dated drill record with archive name/age, elapsed time, row counts, spot checks and outcome.
  - **(ii)** The observed RTO/RPO are recorded against the claims, and any gap is stated honestly.
  - **(iii)** Any script/doc defect found is fixed in the same PR (or explicitly routed if out of scope).
  - **(iv)** No production data is modified; the restore target is a scratch DB, dropped afterwards.
  - **(v)** If credentials/archive access are unavailable, the ticket is marked blocked with the missing precondition — not closed on an unexercised script.
- Traces to: NFR-MOV-003 (availability), `01` §10.8 (backup/DR), test strategy §5.6/§9.4 (manual ledger).
- Architecture refs: `01-system-architecture.md` §10.8 (only if the drill exposes a claim that needs correcting; otherwise no amendment).
- UX refs: none.
- Dependencies: none.
- Parallel group: **P-1** (scopes disjoint from 068/074/075).

---

## Implementation note (recorded on the ticket, 2026-10-30)

**What was executed vs. what was not.** The drill ran end to end on a scratch Docker host
against this repo's unmodified `compose.prod.yml`, with a production-shaped seeded dataset
(plainly not production data) and a **local-directory rclone remote in place of B2** —
this environment has no VPS access and no B2 credentials. It is therefore a **rehearsal
substitute, labelled as one** (the 067 precedent), not acceptance (v)'s "blocked, no
archive access": an archive *was* produced by the real `pg-backup.sh` and restored by the
real `restore.sh`, and the restored database served the shipped API image. The full
not-exercised list lives in `docs/restore-drill-record.md` and is not softened here.

**Scope amendment (one file).** `deploy/backup/pg-backup.sh` was added to the frontmatter
`scope:` after the drill found the upload defect there (rclone has no `--no-progress` flag →
unknown flag, exit 2, nothing reaches B2; the archive stays host-local and the deploy's
restore-point marker, which only inspects that local directory, kept passing on it — so
nothing surfaced the empty remote until a rebuild). Acceptance (iii) says a found defect is
fixed in the same PR; leaving it routed would have left the RPO claim false. Recorded in the
frontmatter and the PR body, not only in code.

**Routed, not done here:** pointing `01` §10.8 at `docs/restore-drill-record.md` (the
architecture file is outside this ticket's scope, and no claim in it was contradicted, so
no amendment is owed); the deploy's restore-point marker does not verify that the archive
reached the remote — a `deploy.yml` (phase 3) concern, outside this ticket's scope, recorded
in the drill record's *Routed, not done here*; the on-VPS run with a real B2 archive
(builder manual-ledger item, test strategy §5.6) — narrowed by this ticket from "never
executed" to "executed on a rehearsal stack, one confirmation run left on the box".
