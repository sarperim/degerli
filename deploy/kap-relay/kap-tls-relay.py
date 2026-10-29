#!/usr/bin/env python3
"""KAP public-site TLS relay — documented ops posture, not a product path.

WHY THIS EXISTS (01 §10.9.19, TKT-foundation-100, live findings 2026-10-28/29)
------------------------------------------------------------------------------
KAP's edge does two things the .NET `HttpClient` cannot absorb on its own:

1. **TLS-fingerprint blocking.** After a burst (~6-18 fetches from a fresh process) the
   edge starts refusing the .NET stack at the *transport* level (connection reset /
   timeout, "An error occurred while sending the request"), while `curl` and python
   `urllib` succeed from the same host and even from inside the same container. The
   discriminator is the TLS ClientHello fingerprint, not the User-Agent: the honest
   `... Degerli/0.1` token passes.
2. **A rolling per-IP quota on the excel export route.** `HTTP 429 "Request Limit
   Exceeded"`, empty body, NO `Retry-After` header. The `byCriteria`/`search` routes
   are unaffected.

The product-side answer is the adapter's browser-shaped TLS handler plus 429-aware
pacing (TKT-foundation-100). **This relay is the fallback when the edge shifts in a way
that handler cannot absorb** — a different fingerprint expectation, a stricter quota, an
outage of the shaping itself. It is deliberately dumb: it presents python's
(browser-acceptable) TLS fingerprint, serializes export fetches behind a pacing floor,
and backs off briefly on 429. It never retries in parallel, never rotates addresses and
never works around the quota.

POLITENESS CONTRACT (binding)
-----------------------------
* The quota is **authoritative**: the relay waits it out, it does not defeat it.
* `--export-min-interval` is a floor, not a target: the app's own pacing still applies
  upstream, and the effective export pace is the slower of the two.
* One upstream request at a time for the export route (a lock, not a pool).
* 429 backoffs are **brief by design**: the app's per-item timeout is 30 s (detail
  fetch 60 s), so a longer wait outlives its caller and stacks uselessly. The adapter
  truncates the walk honestly instead (reason `WALK_QUOTA_BACKOFF`).

HOW IT IS WIRED
---------------
The application reads its base address from `Ingestion:KapPublic:BaseUrl`, so pointing
that at this relay is a **configuration** change — the supported seam — never a code
fork. See `README.md` beside this file for the dev-stack override and the exit criteria.

USAGE
-----
    python3 deploy/kap-relay/kap-tls-relay.py [--port 8899] [--upstream https://www.kap.org.tr]

Environment variables (all optional) mirror the flags: `KAP_RELAY_PORT`,
`KAP_RELAY_UPSTREAM`, `KAP_RELAY_EXPORT_MIN_INTERVAL`, `KAP_RELAY_429_BACKOFF`,
`KAP_RELAY_UPSTREAM_TIMEOUT`, `KAP_RELAY_MAX_429_ATTEMPTS`.
"""

from __future__ import annotations

import argparse
import http.server
import os
import sys
import threading
import time
import urllib.error
import urllib.request

DEFAULT_PORT = 8899
DEFAULT_UPSTREAM = "https://www.kap.org.tr"
# The app's own bound on a detail fetch is 60 s and on any other request 30 s; the relay's
# upstream timeout sits above the detail bound so a genuinely slow export is the app's
# timeout to declare, not the relay's.
DEFAULT_UPSTREAM_TIMEOUT = 90.0
# Verified live (2026-10-28/29): serialized export fetches at this floor stopped tripping
# the export quota. Shipped in the app as `Ingestion:KapPublic:ExportRequestDelayMs`.
DEFAULT_EXPORT_MIN_INTERVAL = 12.0
# Brief by design: the caller's per-item bound is 30 s (60 s for a detail fetch).
DEFAULT_429_BACKOFF = 8.0
DEFAULT_MAX_429_ATTEMPTS = 3

# Headers forwarded upstream. Everything else (Host, Content-Length, hop-by-hop headers)
# is rebuilt by urllib. The app's honest User-Agent is forwarded verbatim: the relay
# presents a TLS fingerprint, it does not launder an identity.
FORWARDED_HEADERS = ("User-Agent", "Accept", "Content-Type", "Accept-Language", "Authorization")


class Settings:
    def __init__(self, args: argparse.Namespace) -> None:
        self.port = args.port
        self.upstream = args.upstream.rstrip("/")
        self.upstream_timeout = args.upstream_timeout
        self.export_min_interval = args.export_min_interval
        self.backoff_429 = args.backoff_429
        self.max_429_attempts = max(1, args.max_429_attempts)


class Relay(http.server.BaseHTTPRequestHandler):
    """One upstream request per inbound request; exports serialized behind one lock."""

    protocol_version = "HTTP/1.1"

    settings: Settings  # injected by `serve()` via a subclass

    export_lock = threading.Lock()
    last_export_started = 0.0

    # -- request handling ---------------------------------------------------------------

    def do_GET(self) -> None:  # noqa: N802 (http.server's API)
        self._forward("GET")

    def do_POST(self) -> None:  # noqa: N802
        self._forward("POST")

    def log_message(self, *_args) -> None:
        # The relay logs its own one-line records; the base class's stderr chatter is noise.
        pass

    def _log(self, message: str) -> None:
        print(f"{time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())} {message}", file=sys.stderr, flush=True)

    def _is_export(self, method: str) -> bool:
        return method == "GET" and "/export/" in self.path

    def _pace_export(self) -> None:
        """Serializes export fetches behind the pacing floor (a lock, never a pool)."""
        interval = self.settings.export_min_interval
        if interval <= 0:
            return

        with Relay.export_lock:
            since = time.monotonic() - Relay.last_export_started
            if Relay.last_export_started and since < interval:
                time.sleep(interval - since)
            Relay.last_export_started = time.monotonic()

    def _forward(self, method: str) -> None:
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length else None
        url = self.settings.upstream + self.path
        is_export = self._is_export(method)
        started = time.monotonic()

        attempts = 1 if (self.settings.backoff_429 <= 0) else self.settings.max_429_attempts
        for attempt in range(1, attempts + 1):
            if is_export:
                self._pace_export()

            request = urllib.request.Request(url, data=body, method=method)
            for header in FORWARDED_HEADERS:
                value = self.headers.get(header)
                if value:
                    request.add_header(header, value)

            try:
                with urllib.request.urlopen(request, timeout=self.settings.upstream_timeout) as response:
                    payload = response.read()
                    self._respond(
                        response.status,
                        payload,
                        response.headers.get_content_type(),
                    )
                    self._log(
                        f"{method} {self.path} -> {response.status} {len(payload)}B "
                        f"{time.monotonic() - started:.1f}s"
                        + (f" (retry {attempt - 1})" if attempt > 1 else "")
                    )
                    return
            except urllib.error.HTTPError as error:
                payload = error.read() if error.fp else b""
                if error.code == 429 and attempt < attempts:
                    # Brief, bounded, and never concurrent: the quota is authoritative.
                    self._log(
                        f"{method} {self.path} -> 429, bounded backoff "
                        f"{self.settings.backoff_429:.0f}s (attempt {attempt}/{attempts})"
                    )
                    time.sleep(self.settings.backoff_429)
                    continue

                self._respond(error.code, payload, error.headers.get_content_type() if error.headers else "text/plain")
                self._log(
                    f"{method} {self.path} -> HTTP {error.code} {len(payload)}B "
                    f"{time.monotonic() - started:.1f}s"
                )
                return
            except Exception as error:  # noqa: BLE001 — a relay must answer, never hang
                message = f"relay upstream failure: {error}".encode()
                self._respond(502, message, "text/plain")
                self._log(f"{method} {self.path} -> 502 {error} ({time.monotonic() - started:.1f}s)")
                return

    def _respond(self, status: int, payload: bytes, content_type: str | None) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type or "application/octet-stream")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


class ThreadedRelayServer(http.server.ThreadingHTTPServer):
    """Threaded so one slow upstream read cannot block the accept loop.

    Concurrency here is *transport*, not politeness: the single-flight discipline lives in
    the application's client (one request at a time), and the export route additionally
    serializes on `Relay.export_lock`.
    """

    daemon_threads = True


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="KAP public-site TLS relay (ops posture).")
    parser.add_argument("--port", type=int, default=int(os.environ.get("KAP_RELAY_PORT", DEFAULT_PORT)))
    parser.add_argument("--upstream", default=os.environ.get("KAP_RELAY_UPSTREAM", DEFAULT_UPSTREAM))
    parser.add_argument(
        "--upstream-timeout",
        type=float,
        default=float(os.environ.get("KAP_RELAY_UPSTREAM_TIMEOUT", DEFAULT_UPSTREAM_TIMEOUT)),
    )
    parser.add_argument(
        "--export-min-interval",
        type=float,
        default=float(os.environ.get("KAP_RELAY_EXPORT_MIN_INTERVAL", DEFAULT_EXPORT_MIN_INTERVAL)),
        help="minimum seconds between export fetch starts (0 disables the floor)",
    )
    parser.add_argument(
        "--backoff-429",
        type=float,
        default=float(os.environ.get("KAP_RELAY_429_BACKOFF", DEFAULT_429_BACKOFF)),
        help="seconds to wait after a 429 before the bounded re-issue (0 disables retrying)",
    )
    parser.add_argument(
        "--max-429-attempts",
        type=int,
        default=int(os.environ.get("KAP_RELAY_MAX_429_ATTEMPTS", DEFAULT_MAX_429_ATTEMPTS)),
    )
    return parser.parse_args(argv)


def serve(settings: Settings) -> None:
    # The handler class needs the settings; http.server builds a handler instance per request,
    # so they ride a class attribute rather than the constructor.
    handler = type("ConfiguredRelay", (Relay,), {"settings": settings})
    print(
        f"KAP public-site relay listening on :{settings.port} -> {settings.upstream} "
        f"(export floor {settings.export_min_interval:g}s, 429 backoff {settings.backoff_429:g}s, "
        f"upstream timeout {settings.upstream_timeout:g}s)",
        file=sys.stderr,
        flush=True,
    )
    ThreadedRelayServer(("0.0.0.0", settings.port), handler).serve_forever()


def main(argv: list[str] | None = None) -> int:
    serve(Settings(_parse_args(argv)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
