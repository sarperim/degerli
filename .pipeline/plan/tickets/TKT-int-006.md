# TKT-int-006: Deployment pipeline & ops (deploy.yml, Caddy, backups, README)

- Status: todo
- Size: M
- Scope: production deployment per architecture §6.2: `deploy.yml` (build + push `ghcr.io/sarperim/degerli/api` images on main after CI; SSH deploy = compose pull → `docker compose run --rm api migrate` → `up -d` → `GET /health` smoke, non-200 fails the job); `compose.prod.yml` + Caddy config (auto-TLS, SPA statics, `/api` proxy, HTTPS redirect); nightly encrypted `pg_dump` → Backblaze B2 (30-day retention, rclone) with the documented restore drill (RTO 4h / RPO 24h); UptimeRobot setup doc; finalize the repo README's quickstart against the real stack (the project README exists since 2026-10-07 — this ticket completes and verifies the ≤ 10-command quickstart + public-repo portfolio framing, SC-007).
- Traces to: SC-001 (deployment vehicle), SC-007 (public repo + README); OBJ-004; `01` §6.2, §10.8
- Acceptance (explicit, no TCs — ops work): a main-branch merge after CI green deploys automatically and the smoke-check passes; rollback = re-deploy of the prior SHA tag (documented, tag-pinned compose); the backup job runs on-box and the restore drill is documented (execution pre-launch is a manual-ledger item per test strategy §5.6); README quickstart verified by the builder (≤ 10 commands — manual ledger); no secrets in repo or images (`.env.example` only).
- Architecture refs: `01-system-architecture.md` §6.2 (deploy.yml), §6.3 (secrets policy), §3 (C7), §10.8 (backup/DR), §5 (reverse proxy, backups, monitoring rows); `02-data-model.md` §8 (migration policy)
- UX refs: —
- Dependencies: TKT-foundation-003, TKT-int-001, TKT-int-002, TKT-int-003, TKT-int-004, TKT-int-005
- Parallel group: P-19
