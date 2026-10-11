# TKT-foundation-012: Stabilize the Serilog test-sink harness (fix flaky HarnessSampleTests)

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/40
- Size: S
- Scope: `tests/Api.IntegrationTests/Harness/HarnessSampleTests.cs` (and, only if needed, `tests/Api.IntegrationTests/Harness/InMemorySerilogSink.cs` or `DegerliAppFactory.cs`): make `Serilog_test_sink_captures_application_logs` deterministic by **waiting for the request-logging event with a bounded timeout** instead of a single immediate check. Optionally reduce host-boot parallelism for factory-booting classes so the runner is not booting many in-process hosts at once. Must NOT change product code (`/src/**`) and must NOT weaken any existing assertion.
- Traces to: TKT-foundation-006 (backend test harness); unblocks TKT-res-007 and any PR whose CI spuriously reddens on this flake
- Acceptance (explicit, no TCs): `HarnessSampleTests` — all five facts, including `Serilog_test_sink_captures_application_logs` — passes deterministically; demonstrated by running the full `Api.IntegrationTests` suite to green repeatedly (and, if feasible, with parallel host boots). No product code changed; no other test weakened or skipped.
- Architecture refs: —
- UX refs: —
- Dependencies: none (TKT-foundation-006 is done)
- Parallel group: P-20

## Notes

**Root cause (race, not a product bug).** `app.UseSerilogRequestLogging()` (`src/Api/Program.cs:41`) emits the completion event (`HTTP {RequestMethod} {RequestPath} responded {StatusCode}`) at the very end of the request pipeline, *after* the response is handed back to the client. `HarnessSampleTests.cs:100` does `client.GetAsync("/health")` and then asserts `factory.Logs.Contains(...)` **once, immediately, with no wait**. Under CI's parallel xUnit host boots (many `DegerliAppFactory` instances booting concurrently on a shared runner) the event occasionally lands after the assertion → intermittent failure. The sink is a `ConcurrentQueue` (`InMemorySerilogSink.cs:15`), so the fix is a bounded poll.

**Evidence.** CI run `38084499798` (PR #36, head `8a5cc59`):
`Degerli.Api.IntegrationTests.HarnessSampleTests.Serilog_test_sink_captures_application_logs [FAIL] — "Expected the request-logging middleware event in the Serilog test sink." at HarnessSampleTests.cs:line 100`, `Failed: 1, Passed: 162, Total: 163`; **passes in isolation and on a plain CI re-run**. The same flake was observed earlier on `TKT-acc-004` and `TKT-res-003`.

**Fix direction.** Replace the immediate `Assert.True(Contains(...))` with a bounded wait (e.g. poll up to ~2 s for `Logs.Contains(...)`, failing with the same message on timeout). Optionally group the factory-booting test classes in one xUnit collection to serialize host boots.

**Impact.** Because the backend job is a prerequisite for the E2E job, this flake reddens the whole pipeline and skips E2E (where web acceptance TCs run) — so it can spuriously block unrelated web-only PRs. Fixing it removes a cross-cutting blocker.

## Resolution (in-review — PR #40)

**Root cause corrected.** The flake was not a pure timing race. `app.UseSerilogRequestLogging()` writes its completion event through `RequestLoggingOptions.Logger`, which defaults to the **process-static `Serilog.Log.Logger`**. Every parallel in-process `DegerliAppFactory` host overwrites that static logger at boot, so a `/health` completion event could be written to another (possibly disposed) host's sink and **never** reach the failing factory. Diagnosed with a 60 s poll: the `HTTP {RequestMethod} {RequestPath} responded {StatusCode}` event never arrived while host-startup events did (so a bounded wait alone provably cannot fix it).

**Changes (test harness only; no `/src/**` change, no assertion weakened).**
- `Harness/DegerliAppFactory.cs` — pin `RequestLoggingOptions.Logger` to this host's DI `Serilog.ILogger` (within the ticket's "only if needed" allowed scope).
- `HarnessSampleTests.cs` — bounded 2 s poll for the request-logging event instead of a single immediate assertion (retains the ticket's fix direction as defence).

**Evidence.** `HarnessSampleTests` filtered 10/10 green; full `Api.IntegrationTests` suite 11/11 consecutive green (`Passed: 181, Failed: 0`).

