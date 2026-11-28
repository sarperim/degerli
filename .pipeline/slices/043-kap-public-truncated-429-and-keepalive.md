---
id: SLICE-043
status: done
scope:
  - .pipeline/slices/043-kap-public-truncated-429-and-keepalive.md
  - src/Ingestion/KapPublic/KapPublicHttpSourceClient.cs
  - tests/Api.IntegrationTests/KapPublicSourceTests.cs
---

# SLICE-043 — Handle KAP Truncated 429 (`ResponseEnded`) & Keep-Alive Idle Timeout

## Context & Goal

Live raw TLS inspection against `https://www.kap.org.tr/tr/api/notification/export/excel/{index}` revealed two transport issues causing ~50%+ of export fetches during multi-year backfills to fail as `SOURCE_FETCH_FAILED` (`DISCLOSURE_DETAIL_UNFETCHABLE`) and burn one-shot replay tombstones:

1. **Truncated HTTP 429 frame:** When KAP's edge rate limiter triggers, it sends raw bytes `HTTP/1.1 429 Request Limit Exceeded\r\n` **without** the trailing `\r\n` header terminator and immediately closes the TCP socket. `.NET`'s RFC-strict `SocketsHttpHandler` throws `HttpRequestException` (`HttpRequestError.ResponseEnded` / `IOException: The response ended prematurely.`) instead of returning `HttpStatusCode.TooManyRequests`. This completely bypassed `KapPublicHttpSourceClient`'s `429` quota backoff and `WalkQuotaBackoff` deferral logic, causing the walk to keep hammering KAP every 12s, quarantine every report, and exhaust replay tombstones.
2. **Stale pooled keep-alive sockets:** KAP responds with `keep-alive: timeout=5`, whereas `KapPublicHttpHandlerFactory.Create` left `PooledConnectionIdleTimeout` at .NET's 1-minute default while `ExportRequestDelayMs` is `12000ms`.

## Rules & Invariants

- **Honest quota obedience (no retry ladder):** On `HttpRequestError.ResponseEnded` (or `IOException` `"ended prematurely"`), wait at most one bounded `QuotaBackoff` and re-issue once (re-paced). If the wait does not fit in the remaining per-item timeout OR the second attempt also hits `ResponseEnded`, throw `KapPublicQuotaException` with `ReasonCode = KapPublicReasonCodes.WalkQuotaBackoff` and `Deferred = true` so `KapPublicStatementsWalker` cleanly stops at the last completed unit without quarantining the report or burning a replay tombstone.
- **Keep existing explicit HTTP 429 & non-ResponseEnded transport behavior unchanged:** Existing `KapPublicSourceTests` for explicit `429` (`QuotaRateLimited` vs `WalkQuotaBackoff`) and ordinary `HttpRequestException` (`SourceFetchException`) must pass untouched.
- **Keep-alive hygiene:** Set `PooledConnectionIdleTimeout = TimeSpan.FromSeconds(4)` on `KapPublicHttpHandlerFactory.Create`.
- **Zero live calls in tests:** All new tests use in-memory `HttpMessageHandler` doubles.

## Blockers & Dependencies

- None.

## Verification

- `dotnet test tests/Api.IntegrationTests/Degerli.Api.IntegrationTests.csproj --filter "KapPublicSourceTests"` passes inside the SDK container.
- Live replay passes against `kap.org.tr` recover quarantined filings cleanly without burning tombstones on rate-limit cooldowns, and recovered companies pass balance-sheet identity + external KAP spot-checks.

## Result

- Updated [KapPublicHttpSourceClient.cs](../../src/Ingestion/KapPublic/KapPublicHttpSourceClient.cs): set `PooledConnectionIdleTimeout = TimeSpan.FromSeconds(4)` (below KAP's `keep-alive: timeout=5`) and mapped `HttpRequestError.ResponseEnded` / premature-response-end `IOException` (KAP's truncated `HTTP/1.1 429 Request Limit Exceeded\r\n`) to bounded `QuotaBackoff` + clean `WalkQuotaBackoff` deferral (`deferred: true`) so walks stop without quarantining or burning replay tombstones.
- Added 4 tests (`SLICE_043_a`..`SLICE_043_d`) in [KapPublicSourceTests.cs](../../tests/Api.IntegrationTests/KapPublicSourceTests.cs); `dotnet test --filter "KapPublicSourceTests"` passed `29/29`.
