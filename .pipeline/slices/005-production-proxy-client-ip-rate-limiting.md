---
id: SLICE-005
status: done
scope:
  - .pipeline/slices/005-production-proxy-client-ip-rate-limiting.md
  - src/Api/Infrastructure/RateLimitingSetup.cs
  - tests/Api.IntegrationTests/Xc011RateLimitTests.cs
---

# SLICE-005: Honor Caddy `X-Forwarded-For` Hop for Production Client-IP Rate Limiting

## Context & Goal

In `compose.prod.yml`, `api` is an internal-only service (`expose: ["8080"]`) behind `caddy` (`reverse_proxy api:8080`). Every public request arrives at `api` over the Docker compose bridge network with `context.Connection.RemoteIpAddress` set to Caddy's container IP (e.g., `172.18.0.x` or `::ffff:172.18.0.x`) and the real visitor IP appended by Caddy to `X-Forwarded-For`.

Previously, `RateLimitingSetup.ResolveClientIp` (`src/Api/Infrastructure/RateLimitingSetup.cs`) ignored `X-Forwarded-For` whenever `environment.IsProduction()` was true and returned `context.Connection.RemoteIpAddress`. In production behind Caddy, that caused every visitor on the internet to share a single rate-limit key (`auth` 5 req/min globally, `global` 600 req/min globally).

**Goal:** Update `RateLimitingSetup.ResolveClientIp` so that in `Production`, when the immediate peer (`RemoteIpAddress`) is a trusted internal/private proxy address (loopback, RFC 1918 `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, IPv6 ULA `fc00::/7`, including IPv4-mapped IPv6 addresses, or `null` in-memory test hosts), it extracts the **rightmost** (last) hop of `X-Forwarded-For` appended by Caddy and validates it with `IPAddress.TryParse`.

## Rules & Invariants

1. **Non-production (`!environment.IsProduction()`) behavior is unchanged.** The existing test-controllable `X-Forwarded-For` behavior (reading the first token, including non-IP test tokens such as GUID strings) remains untouched.
2. **Spoof-resistant in `Production`:**
   - Only trusts `X-Forwarded-For` when `Connection.RemoteIpAddress` is `null` or a loopback / private network IP (unmapping `IsIPv4MappedToIPv6` first). If `Connection.RemoteIpAddress` is a public IP, `X-Forwarded-For` is ignored and the normalized `RemoteIpAddress` is used.
   - Reads the **last** (rightmost) comma-separated entry in `X-Forwarded-For` (the hop appended by Caddy) rather than the first entry, preventing clients from bypassing rate limits by sending a forged `X-Forwarded-For` header to Caddy.
   - Requires `IPAddress.TryParse` on that rightmost token (normalizing `IsIPv4MappedToIPv6` to IPv4); if missing or unparseable, falls back to the normalized `RemoteIpAddress` (or `"unknown"`).

## Blockers & Dependencies

- None.

## Verification

- [x] `Xc011RateLimitTests` verifies that in `Production` environment:
  - Distinct client IPs forwarded by Caddy get independent rate-limit budgets.
  - A client spoofing an rotating prefix in `X-Forwarded-For: <spoofed>, 203.0.113.88` is still rate-limited on the rightmost hop (`203.0.113.88`).
  - `RateLimitingSetup.ResolveClientIp` trusts `X-Forwarded-For` from private/loopback proxy IPs (including IPv4-mapped IPv6 `::ffff:172.18.0.4`) and ignores `X-Forwarded-For` when `RemoteIpAddress` is a public IP.
- [x] `dotnet test tests/Api.IntegrationTests --filter "FullyQualifiedName~Xc011RateLimitTests|FullyQualifiedName~Xc011CredentialHardeningTests"` passes.

## Result

- Updated `RateLimitingSetup.ResolveClientIp` to be `public static`, preserving non-production behavior while adding spoof-resistant Caddy proxy hop extraction in `Production`.
- Added `NormalizeIp` and `IsTrustedProxyPeer` to authenticate private/loopback network peers (RFC 1918, RFC 4193 ULA, loopback, and IPv4-mapped IPv6) before honoring `X-Forwarded-For`.
- Keyed production rate limiting on the rightmost hop validated with `IPAddress.TryParse`.
- Added integration and unit tests in `Xc011RateLimitTests` covering end-to-end production rate limiting with rotating spoofed prefixes and unit validation across trusted Docker bridge peers, IPv4-mapped IPv6 peers, and untrusted public peers.
- All 10 targeted integration tests pass.
