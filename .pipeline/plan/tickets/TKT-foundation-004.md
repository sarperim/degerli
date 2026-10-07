# TKT-foundation-004: Shared API infrastructure (errors, envelope, auth plumbing, headers, limits)

- Status: done
- Size: M
- PR: https://github.com/sarperim/degerli/pull/4
- Scope: `/src/Api` shared infrastructure: RFC 7807 ProblemDetails middleware with the stable error-code catalog (`03` §7); honest-data envelope types (`asOf`, `stale`, `adjusted`, per-figure `restated`, `coverage`/`state` — `01` §10.7); `/api/v1` route group; `GET /health` upgraded to liveness + DB check; OpenAPI document served anonymously at `GET /api/v1/openapi.json`; CSRF antiforgery (`GET /api/v1/auth/csrf-token` + `X-CSRF-Token` enforcement on unsafe methods); rate-limit middleware (5 req/min/IP on `/api/v1/auth/*`, 600 global, 60 `/admin`; test-controllable client-IP keying per test-plan I-XC-2); security headers (`X-Content-Type-Options: nosniff`, CSP `default-src 'self'`, `frame-ancestors 'none'`); caching-policy helpers per `03` §13. Must NOT touch domain feature modules or Migrations/.
- Traces to: foundation (cross-cutting contracts `01` §10)
- Acceptance (explicit): anonymous `GET /api/v1/openapi.json` returns a valid document; an unhandled exception returns ProblemDetails `INTERNAL` with `traceId` and no stack trace; a probe POST without `X-CSRF-Token` → 400, with token → proceeds; the 6th request inside the window on a probe auth route → 429 `RATE_LIMITED` with `params.retryAfter` under test config; representative responses carry the security headers; envelope types are the only response shape helpers domain modules may use.
- Architecture refs: `01-system-architecture.md` §10.1–§10.5, §10.7; `03-api-design.md` §1, §2, §7, §12, §13
- UX refs: — (mechanism layer; the UXRs it serves are tested in TKT-int-003/int-004)
- Dependencies: TKT-foundation-001
- Parallel group: P-2
