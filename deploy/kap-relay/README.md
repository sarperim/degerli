# KAP public-site TLS relay — when to use it

A supported, documented fallback for one specific failure: **the KAP edge refuses the
application's own HTTP stack while accepting another one from the same host.** It is
*ops posture*, not a product path — the product's access fix lives in the adapter
(`src/Ingestion/KapPublic/`, TKT-foundation-100), and this relay is what an operator
reaches for when the edge shifts in a way that fix cannot absorb.

## What it does

`kap-tls-relay.py` puts a local HTTP listener in front of `https://www.kap.org.tr` and
forwards each request upstream through **python `urllib`** — whose TLS ClientHello the
edge accepts — while keeping every politeness rule the lane already ships:

| Behaviour | Why | Default |
|---|---|---|
| forward the app's headers verbatim (User-Agent included) | the relay presents a *fingerprint*, it never launders an identity | — |
| serialize export fetches behind one lock | the export route's quota is per-IP and accumulates | — |
| export pacing floor | verified live 2026-10-28/29: serialized 12 s export fetches stopped tripping the quota | `--export-min-interval 12` |
| bounded 429 backoff (brief) | the app's per-item bound is 30 s (60 s detail); a longer wait outlives its caller and stacks uselessly | `--backoff-429 8`, one re-issue (`--max-429-attempts 2` — the adapter's `MaxQuotaAttempts`, never a ladder), the re-issue paced at the lane's ≥ 3 s general floor |
| one upstream request per inbound request | no parallel clients, no address rotation, no quota workaround | — |

**The quota is authoritative.** The relay waits it out; it never rotates addresses,
opens parallel clients or otherwise tries to get around the limit. If a 429 persists past
the bounded attempts, the relay hands the 429 back to the app, which names it
(`QUOTA_RATE_LIMITED`) or truncates its walk honestly (`WALK_QUOTA_BACKOFF`) and resumes
on the next pass.

## When to use it

Use it **only** when a live run shows the fingerprint block or the export quota biting:

- transport-level failures (`connection reset` / timeout / "An error occurred while
  sending the request") from the API container after a handful of fetches, **while**
  `curl` and `python3 -c 'urllib.request.urlopen(...)'` succeed from the same host;
- sustained `HTTP 429 "Request Limit Exceeded"` on
  `/tr/api/notification/export/excel/{index}` even at the shipped export floor.

Do **not** use it as a default. With the adapter's browser-shaped TLS handler in place
the app is expected to reach the source directly; the relay existing in the repository is
not a reason to run it.

## The seam it rides

`Ingestion:KapPublic:BaseUrl` is the supported seam: the lane addresses its own
`HttpClient` from that value, so pointing it at the relay is **configuration**, and
switching back is configuration too — no code fork, no rebuild of the lane.

A dev-stack override (compose, git-ignored outside the repo's own override file):

```yaml
# compose.dev.yml (fragment) — the relay runs beside the API and the app addresses it.
services:
  kap-relay:
    image: python:3.12-slim
    # `--bind 0.0.0.0` is deliberate: inside the compose network the relay must accept
    # connections from the api container, and the port is not published to the host or the
    # internet. Standalone from the host the default (`127.0.0.1`) applies — see below.
    command: ["python", "/relay/kap-tls-relay.py", "--bind", "0.0.0.0", "--port", "8899"]
    volumes:
      - ./deploy/kap-relay:/relay:ro
    restart: unless-stopped

  api:
    environment:
      Ingestion__KapPublic__BaseUrl: "http://kap-relay:8899"
    depends_on:
      - kap-relay
```

Run it standalone when debugging from the host:

```bash
python3 deploy/kap-relay/kap-tls-relay.py --port 8899
# and point the API at http://localhost:8899
```

The listener binds `127.0.0.1` by default. The relay forwards `Authorization` headers and
adds no authentication of its own, so widening the listener is an explicit choice: pass
`--bind` only for a network you control (the compose fragment above does exactly that).

## Standing it down

The relay is stood down once a **paced live probe through the adapter itself** (never
through the relay) sustains export fetches without the transport-failure burst and
handles a 429 by naming, backing off and resuming — the live criterion recorded on
TKT-foundation-100. Revert `Ingestion:KapPublic:BaseUrl` to `https://www.kap.org.tr` and
stop the relay service.

## Scope and safety notes

- **Never in CI.** No test, workflow or automated job may call `kap.org.tr` — through this
  relay or otherwise. The lane's tests use recorded doubles.
- **Dev/ops only.** The relay adds no authentication, so it binds `127.0.0.1` by default: keep
  it there, or bind it to the compose network's own address (the fragment above passes
  `--bind 0.0.0.0` without publishing the port) — never to a public interface.
- **It is not a caching proxy.** It adds no store, no rewriting and no content handling:
  status, body and content type are forwarded as the source sent them.
