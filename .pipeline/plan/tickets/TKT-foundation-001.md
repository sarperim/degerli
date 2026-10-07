# TKT-foundation-001: Backend solution scaffold & dev environment

- Status: done
- PR: https://github.com/sarperim/degerli/pull/2
- Size: M
- Scope: create the .NET 10 monorepo backend: solution file + `/src/Api` (ASP.NET Core Minimal API host), `/src/Core` (shared quantitative class lib), `/src/Ingestion` (worker class lib), `/src/ContentPipeline` (console app, alternate entrypoint of the same image); Serilog structured JSON logging (rolling files + stdout); `/health` liveness endpoint; `compose.dev.yml` (Postgres 17, MailPit, API with hot-reload mounts); `.env.example`; extend the repo README (which exists since 2026-10-07) with the local-dev quickstart section. Must NOT touch `/src/Web` or `.github/`.
- Traces to: foundation (architecture repo-layout decision; AD-04, AD-11)
- Acceptance (explicit, no TCs — foundation): fresh clone → `docker compose -f compose.dev.yml up` starts Postgres + MailPit + API; `GET /health` returns 200; `dotnet build` succeeds warning-clean; Serilog emits structured JSON to stdout and a rolling file; `.env.example` documents every secret the architecture names (`ConnectionStrings__Default`, `Smtp__*`, `Evren__ApiKey`, `APP_URL`, `ALERT_EMAIL`).
- Architecture refs: `01-system-architecture.md` §2 (deployable units), §3 (C2/C3/C4/C5), §5 (tech stack rows: runtime, ORM, jobs, logging, containerization, repo layout), §10.4 (configuration)
- UX refs: — (not UI-touching)
- Dependencies: none
- Parallel group: P-1
