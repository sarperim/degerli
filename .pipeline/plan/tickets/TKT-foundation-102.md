---
scope:
  - .github/workflows/deploy.yml
  - .env.example
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/architecture/02-data-model.md
  # Was `.pipeline/architecture/03-api-contracts.md`, which does not exist in this package —
  # the `03` document is `03-api-design.md` (what item (b)6 means by "`03` §8"). Amended
  # 2026-11-01 by the implementation session, recorded here and in the PR body (v1.5.87).
  - .pipeline/architecture/03-api-design.md
  - deploy/README.md
---

# TKT-foundation-102: Pipeline-closing sweep — 067's R1 bootstrap fail-open fix + the recorded mechanical doc/config nits

- Status: done — **PR #172 squash-merged 2026-11-01 (`4ca96d4` on dev), round-2 MERGEABLE** (`.pipeline/plan/reviews/TKT-foundation-102-round-{1,2}.md`; 2 passes, 1 authorized fixer round — DR-1 availability defect closed with the state matrix verified safe, rehearsal red 4/5 → green 9/9; DR-2 wording; SEC-1 claims bounded to the rename limit). **The final ticket of the plan→ticket→code pipeline** — user switches to vertical slices next; this file set is the frozen archive. **Accepted non-blocking debt:** SEC-2/SEC-3 low (label-echo workflow-command forging needs root-equivalent socket access; tenant-name in CI-retained logs — nil on the single-tenant VPS); N-1..N-4 polish nits (§6.2 qualifier, refusal-text ordering, round-2 evidence citation, isolated "can no longer fail open" openers). **Routed (carries into the new model, do not lose):** R-1 — the gate's bootstrap carve-out recognizes only `*_postgres-data` volumes: unattributable/arbitrary-name data volumes and bind mounts still bootstrap, and a compose-project rename on a data-bearing host reads as "another tenant" and comes up empty behind the gate — a real documented boundary, cheap to close in a future ticket; R-2 — rehearsal harness not committed (067/076 precedent). Scope amendment recorded: `03-api-contracts.md` does not exist, the file is `03-api-design.md` (planner defect noted).
- Size: S
- **USER-AUTHORIZED 2026-11-01 (verbatim instruction: "ok lets do it", in answer to the orchestrator's offer to "knock out 067-R1 and the mechanical doc nits before you retire the pipeline").** This is the final ticket of the plan→ticket→code pipeline before the user switches to vertical slices; everything routed here is already recorded as non-blocking on landed tickets.
- **(a) The one code fix — 067 round-2 finding R1 (review artifact `.pipeline/plan/reviews/TKT-foundation-067-round-2.md`):** the deploy workflow's `bootstrap=true` detection can **fail open** — `docker compose` honours `COMPOSE_PROJECT_NAME` from `.env` (which outranks the compose `name: degerli`), while the remote shell env is never populated, so a `.env` carrying that key makes the volume filter match nothing and the restore-point gate is skipped on a data-bearing host. Command failure is fail-closed; the false negative is not. **Suggested contained fix (review round 2):** resolve the project name the way compose does (or probe `docker volume inspect "${project}_postgres-data"` directly) so the gate's bootstrap carve-out keys on the real volume, never on an empty filter result. Bounded: no deploy-phase redesign, no new phases.
- **(b) The mechanical doc/config nits (each already recorded on its landed ticket):**
  1. 084 DR-12 — `01` §13.1 v1.5.85 row still says the watermark is "never backwards" beside its own round-2 "re-anchor" addendum; one-line dated amendment so the row tells the truth (the code pulls a future watermark back, logged).
  2. `02-data-model.md:399` — stale `run --rm api migrate` command; align with 067's landed one-shot migrate phase.
  3. `.env.example` — add `DEGERLI_BUILDER_PASSWORD` (067 routing item: the next VPS deploy stops at preflight until the key exists).
  4. `01` §10.8 — pointer to `docs/restore-drill-record.md` (076 routing item).
  5. `deploy/README.md:336` — hardcoded `-U degerli` without `--if-exists` (076 pre-existing nit).
  6. 035 routing item — `03` §8 contract row for `POST /api/v1/admin/quarantine/conflicting/resolve` (shape recorded in `01` §10.10 / §13.1; the row documents shipped reality, no contract change).
- Traces to: 067 (R1), 084 (DR-12), 076 (README/§10.8), 035 (`03` §8), 067 (`.env.example`).
- Acceptance: **(i)** the bootstrap carve-out can no longer be skipped by a `.env`-set `COMPOSE_PROJECT_NAME` — demonstrated with a rehearsal-shaped check (a throwaway compose project per the 067/076 precedent; no VPS, no production data), red→green where the check is scripted; **(ii)** each doc/config nit above corrected in place with dated markers where the docs convention requires; **(iii)** CI 6/6 green; **(iv)** `01` §13.1 gains one row, next-free-above-max (verify in-file).
- Dependencies: none hard (067/076/084/035 all merged).
- Parallel group: none (final ticket).

---

## Implementation note (recorded on the ticket, 2026-11-01)

**(a) The R1 fix — the bootstrap carve-out can no longer fail open.** `deploy.yml`'s
phase 3 now (1) takes the project name from `compose.prod.yml`'s own `name:`, reads back
the name compose **actually** resolves (`docker compose config`), and **refuses** a
mismatch — a `.env` `COMPOSE_PROJECT_NAME` can no longer move the deploy to another
project (a fresh, empty database) past the gate; and (2) decides "blank host" only from
positive evidence: the project's `<project>_postgres-data` volume by name, any volume
carrying the project's compose labels (re-keyed/renamed/removed data volume), and a
host-wide `*_postgres-data` scan that refuses when such a volume belongs to this project
under another name or to no compose project at all (another project's database is named in
the log and not counted). A docker failure while deciding still aborts. The carve-out's
intent is unchanged — it fires only when the project has no volumes at all.

**Rehearsal evidence (red→green, throwaway harness in `/tmp`, not committed — the 067/076
precedent).** The `backup-marker` block is extracted **verbatim** from the workflow and run
against real Docker (28.3.3 / Compose v2.39.2) with the repo's real `compose.prod.yml` in a
throwaway directory and throwaway volumes; no VPS, no production data, and the pre-existing
`degerli-dev` / `degerli-ci` stacks untouched. Seven scenarios; the pre-fix workflow was
taken from `git show HEAD:.github/workflows/deploy.yml` so the red is the old code, not a
reconstruction:

| scenario | pre-fix (`HEAD`) | this PR |
|---|---|---|
| S1 `.env` sets `COMPOSE_PROJECT_NAME`, data volume exists under that project | **`bootstrap=true`, exit 0 — the fail-open** | refused by name (project != declared `name:`) |
| S2 data + no archive | refused (no archive) | refused (no archive) |
| S2b data + fresh archive | `bootstrap=false`, archive recorded | `bootstrap=false`, archive recorded |
| S3 `pg_postgres-data` created outside Compose (no labels) | **`bootstrap=true`** | refused ("cannot be proven") |
| S4 volume key renamed (`degerli_pgdata`) | **`bootstrap=true`** | refused → gated like any deploy |
| S5 genuinely blank project | `bootstrap=true` | `bootstrap=true` (carve-out preserved) |
| S6 docker failure deciding | refused to guess | refused to guess |

**Red 3 failed / 4 passed; green 7/7.** The rehearsal found a real defect in the first cut
of the fix — the host-wide probe counted the project's own data volume, so every ordinary
deploy would have been refused — fixed before the PR; the harness, not a reading, caught it.
Also: `bash -n` and `shellcheck -s bash -S warning` clean on the extracted remote script,
`yaml.safe_load` clean on the workflow, and `gitleaks dir` (8.30.1, the CI version) clean
over the tree with the new `.env.example` line.

**Honesty limit, stated:** the rehearsal exercises the decision block, not a whole SSH
deploy, and never touches the VPS — the first real deploy on the box remains the builder's.
The fail-open guarantee is bounded by the vector this ticket closes (a `.env`
`COMPOSE_PROJECT_NAME` / an empty lookup asking about the wrong project) and by what the
three probes can read (volume names and compose labels). It does not cover an operator
**rename** of the compose project — `compose.prod.yml`'s `name:`, or a move to a
differently-named checkout directory — while an old project's `*_postgres-data` volume
survives (its label then names a different project, so it reads as another project's data;
review round 1's SEC-1), nor this project's data in a volume created outside Compose under
an arbitrary name, nor a bind-mounted data path. Those boundaries are routed as shared-host
architecture/ops decisions (round 1's R-1), not fixed here.

**(b) The six nits** — all corrected in place: (1) `01` §13.1 v1.5.85's "never backwards"
watermark claim corrected in place with a dated amendment (the code pulls a future watermark
back and logs the re-anchor); (2) `02-data-model.md` §8's stale `run --rm api migrate` now
the landed one-shot `run --rm -e DEGERLI_BUILDER_PASSWORD api migrate` before `up -d`;
(3) `.env.example` gains `DEGERLI_BUILDER_PASSWORD=` with its ≥ 12-char rule and
migrate-only posture; (4) `01` §10.8 points at `docs/restore-drill-record.md`; (5)
`deploy/README.md`'s printed cleanup hint is `dropdb --if-exists -U degerli …`; (6) `03` §8
gains the row **and** a response sketch for `POST /api/v1/admin/quarantine/conflicting/resolve`,
documenting the shipped shape (no contract change). `01` §13.1 gains one row, **v1.5.87**
(next free above the landed max v1.5.86, verified in-file).

**Scope amendment (one path, recorded not hidden).** The frontmatter listed
`.pipeline/architecture/03-api-contracts.md`, which does not exist; the `03` document is
`03-api-design.md`, so the scope was amended to that path (see the frontmatter comment). No
file outside the amended list was touched, and the ticket file itself is added to the branch
(it existed only untracked in the main checkout).

**Nothing else**: no new deploy phase, no new workflow, no contract change, no product
source, no schema, no migration, no dependency, and no override switch (§10.8's "no
override" rule stands).
