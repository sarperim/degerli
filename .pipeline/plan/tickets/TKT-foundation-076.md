---
scope:
  - deploy/backup/restore.sh
  - deploy/README.md
  - docs/restore-drill-record.md
---

# TKT-foundation-076: Restore drill — exercise the backup restore for real (LOW priority)

- Status: todo
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
