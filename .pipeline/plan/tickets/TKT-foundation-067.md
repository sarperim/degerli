---
scope:
  - .github/workflows/deploy.yml
  - compose.prod.yml
  - deploy/README.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-067: Deploy migration safety — a real readiness gate and a defined rollback posture

- Status: todo
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 2 unparked).**
- **Evidence (gathered 2026-10-24):**
  - **`migrate` is run by hand today.** `deploy.yml` contains a `migrate` step (`:194-201`), but the production compose path has never been exercised end-to-end; the 2026-10-23 dev deploy was manual: stop the API, pull, force-recreate, then `dotnet run --project src/Api … -- migrate` — with the explicit note that migrations are **not** applied at API startup, only via the `migrate` token (`run-report.md:177`). The operational recipe is the orchestrator's hand-run sequence, not a repeatable gate.
  - **The prod stack cannot satisfy its own readiness contract.** `compose.prod.yml`'s `api` service has **no `healthcheck:` block**, while `caddy` declares `depends_on: api: condition: service_healthy` (`compose.prod.yml:50-67`). The API is therefore not a health-gated dependency; `up -d` is not a readiness gate. (The CI stack does define healthchecks — `compose.ci.yml:41/91`.)
  - **Rollback is only an image re-deploy.** Migrations are forward-only with no down-migrations (`02-data-model.md:319`, §8); the documented rollback is "re-deploy the previous image tag" (`deploy.yml:10-12`, `deploy/README.md` §Rollback). That cannot undo a schema change, and there is no stated expand/contract rule for a backward-incompatible migration.
  - **`/health` is a readiness probe used as liveness**: it includes the DB check (`Program.cs:71`, `DatabaseHealthCheck` tagged `ready`). There is no separate `/health/live`.
  - The deployed smoke-check retries `GET /health` for ~150 s **after** `up -d` (`deploy.yml:203-215`) — a failure is detected only after the new stack may already be serving.
- **Pinned approach:**
  1. **Readiness gate in prod compose** — add an `api` `healthcheck:` (`/health`, bounded `start_period`) so Caddy's `service_healthy` dependency is real; `up -d` then blocks on readiness or fails within a bounded window.
  2. **Ordered deploy** — pull → **take/verify a pre-deploy backup marker** (the backup path is TKT-foundation-076's drill; this ticket only requires the marker step and its failure posture) → run `migrate` as a one-shot → readiness gate → switch traffic → smoke. On any failure, stop and follow the recorded rollback; never leave a half-migrated stack serving.
  3. **Rollback decision (record it, do not invent silently):** adopt **expand → migrate → contract** as the standing rule for backward-incompatible schema changes (recommended), or explicitly accept a restore-based recovery (item 13). The chosen rule is written into `01` §6.2/§10.8 and `deploy/README.md`.
  4. **Readiness endpoint:** reuse `/health` in this ticket. Splitting `/health/live` vs `/health/ready` touches `src/Api/Program.cs`, which is **TKT-foundation-045's active scope** — record the split as a follow-up and do not collide here.
- **Acceptance (explicit criteria):**
  - **(i)** `docker compose -f compose.prod.yml config` shows the API healthcheck; Caddy's `service_healthy` dependency is satisfiable; `up -d` blocks until the API is healthy or fails within the bounded window.
  - **(ii)** A deliberately failing migration on a test stack stops the deploy **before** traffic switches, the old image keeps serving, and the documented rollback path is executed and recorded. (If a full prod-compose rehearsal is not possible, a scripted compose-level rehearsal with the same services and a bad migration is acceptable — state which was run.)
  - **(iii)** The pre-deploy backup marker step exists and its failure stops the deploy.
  - **(iv)** `deploy/README.md` and `01` §6.2/§10.8 state the ordered sequence, the readiness gate and the chosen rollback rule; §13.1 row appended (**soft reservation v1.5.46**; re-pin at merge).
  - **(v)** No `src/Api/Program.cs` change (the readiness split is a recorded follow-up).
- Traces to: FR-MDF-016 (pipeline health — deploy does not reveal a broken stack), NFR-MOV-003 (availability), `01` §6.2/§10.8; `02` §8 (forward-only migrations).
- Architecture refs: `01-system-architecture.md` §6.2 (deploy), §10.3 (`/health`), §10.8 (DR), §13.1; `02-data-model.md` §8.
- UX refs: none.
- Dependencies: none.
- Parallel group: none (touches `01`, which the in-flight 044/045/048/064 also touch).
