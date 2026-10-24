---
scope:
  - src/Api/Program.cs
  - src/Api/Diagnostics/
  - tests/Api.IntegrationTests/CrashEvidenceTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-045: API process SIGABRT (exit 134) with zero captured evidence — add crash-evidence capture so an abort is never again undiagnosable

- Status: in-review
- PR: https://github.com/sarperim/degerli/pull/131 (base `dev`, head `feature/TKT-foundation-045`)
- Size: S
- **Priority: P2 — dispatch-ready, investigation-first.**
- Verified evidence (dev stack, 2026-10-20): the API process **exited with code 134 (SIGABRT) at 20:12:53 UTC (23:12:53 Istanbul)** during routine log-file churn — the last app-log lines are benign ("File updated: logs/degerli-2026-10-20.log", "No managed code changes to apply"), then exit; **no stack, no unhandled-exception log, nothing at the abort** — the log file wrote nothing. `dotnet watch` revived it ~2.5 min later on a file change ("Build succeeded", listening 20:15:16). Root cause unknown: candidates include a `dotnet watch`/hot-reload interaction (the dev stack runs under watch — the churn lines are watch artifacts), a runtime abort (GC/heap corruption class), or an environment-level kill. NOTE: this is 2.5 h AFTER the EOD-chain stall began (17:37) — finding F is NOT the stall's cause (041's lane); it stands alone.
- Scope (evidence-first, minimal): **(a) crash-evidence capture in the API host:** wire the .NET crash/diagnostic hooks — `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, and (for native/SIGABRT-class exits where managed hooks cannot run) the runtime's crash-dump/gc heap dump knobs (`DOTNET_DbgEnableMiniDump` / `DOTNET_EnableCrashReport` posture via `compose.dev.yml` env, per the §10.4 config doctrine) plus a startup line that logs any crash artifact found from the previous run — so the NEXT abort leaves a stack, a dump, or at minimum a marker. **(b) Distinguish dev-watch from product:** record honestly that the observed abort happened under `dotnet watch` in the dev stack; the capture must work both under watch and in the composed product container (the INT-006 deploy posture) — if the evidence later shows a watch-only artifact, that finding is recorded and the product-side capture still stands. **(c) A short investigation note on the 20:12:53 abort itself** (watch logs, container events, dmesg if available) — attempt only; no root cause is claimed or fabricated. **(d) Docs:** `01` §13.1 **v1.5.26** (diagnostics posture). No attempt to "fix" an unexplained crash — the deliverable is that it can never again happen invisibly.
- Traces to: NFR-MDF-001/operational honesty (a crash with no evidence is an unauditable event); `01` §10.4 (config/env doctrine — the dump knobs), §3 C1/C2 (the API host); the INT-006 deploy precedent (crash-relevant hardening); foundation pseudo-domain convention (diagnostics/observability — explicit criteria; the 030 precedent).
- Acceptance: **explicit criteria; doubles/harness only.** **(i) Unhandled managed exception:** an unhandled exception in the host writes a structured log entry + (where the platform allows) a crash marker/dump before exit — verified in a test harness (a spawned process that throws; assert the artifact); **(ii) native-abort posture:** the shipped config/env enables the runtime crash-dump knobs for the dev + product compositions and a startup log line reports any previous-run crash artifact found (test: place a marker, start the host, assert the line); **(iii) watch vs product:** the capture is documented for both execution modes; **(iv) CI green** (no behavior change on the happy path; the hooks are passive). **Live criterion (orchestrator post-merge, honest):** on the dev stack, a deliberately-triggered abort (e.g. `kill -ABRT` the API container process — an operator action, not a test) leaves recoverable evidence (dump file or marker + startup report on revival); recorded verbatim on the ticket. Any FUTURE spontaneous abort now leaves evidence by construction — its diagnosis is a follow-up on that evidence, never this ticket's claim.
- Architecture refs: `01-system-architecture.md` §10.4 (config), §13.1 v1.5.26; `02-data-model.md` unchanged.
- UX refs: none.
- Dependencies: none hard (Api host lane; disjoint from 039/040/041/042/043/044 code lanes). **Serialize note:** `01` §13.1 v1.5.26 behind v1.5.15-20 — single-row append, last-lander rebases. `compose.dev.yml` env additions, if needed for the dump knobs, are an operational-config change — coordinate with the orchestrator (the dev stack runs from the main checkout; no docker/infra redesign, additive env only).
- Parallel group: none.

## Implementation record (2026-10-24)

**PR #131** (`feature/TKT-foundation-045`, base `dev` @ `3069bd2`). Scope: `src/Api/Program.cs`, `src/Api/Diagnostics/` (new), `tests/Api.IntegrationTests/CrashEvidenceTests.cs`, `01` §13.1 **v1.5.26** + a §10.3 diagnostics bullet — plus this ticket file. Nothing outside it was touched. **No root cause is claimed.**

### What was built (evidence, not diagnosis)

- **`src/Api/Diagnostics/CrashEvidenceHooks`** — two passive process-wide hooks: `AppDomain.UnhandledException` (the last managed code for an unhandled exception) and `TaskScheduler.UnobservedTaskException` (observation only; `SetObserved` is **never** called, so no exception-policy behaviour change). Both wrap the capture so a failure can never propagate; `LastArtifactPath` exposes the seam to tests. Installed from `Program.cs` **after** `builder.Build()` so the *merged* configuration (appsettings + env + composition overrides) decides the paths.
- **`CrashEvidenceWriter`** — one bounded JSON artifact at `logs/crash-evidence/crash-<UTC>-<pid>-<kind>.json` (`Diagnostics:CrashEvidence:Directory|LogDirectory|LogTailLines|MaxLogReadBytes|MaxExceptionMessageChars|MaxStackTraceChars`): exception type/message/stack (clipped, with `messageTruncated`/`stackTraceTruncated` flags), the last-N lines of the newest rolling log through a bounded end-of-file read (512 KB cap, partial first line dropped, `truncated` flag), the process envelope (pid, runtime version, app version, environment, OS, arch, machine) and an explicit **exit path** string (terminating — "signals, hosted-service shutdown and the graceful exit path do not run"; non-terminating; unobserved-task "process continues, policy untouched"). Collision-safe naming so two faults in one second both leave an artifact. **The writer never throws** — every failure path returns `null`.
- **`CrashEvidenceRedactor`** — the repo's existing client-failure posture applied to the crash surface: every configured secret (`ConnectionStrings:Default`, `Ingestion:Kap:ApiKey`/`ApiSecret`, `Ingestion:Macro:EvdsApiKey`, `Smtp:Password`, `Auth:UserRefHashKey`, `Evren:ApiKey`) plus `password=`/`passwd=`/`secret`/`api_key=`/`token=` shapes, `Bearer …` and `Basic …`, applied to the message, the stack and every log-tail line.
- **`CrashEvidenceStartupReport`** (called from `Program.cs`) — one Information line per artifact a **previous run** left behind (newest first, max 5 + a summary), and one honest **native-abort posture** line: `enabled (DOTNET_DbgEnableMiniDump=…)` when that knob is set, otherwise `not configured (… unset) — a native abort (SIGABRT / exit 134) cannot run managed hooks and will leave no dump …`. Documented in `01` §10.3 and §13.1 v1.5.26.

### Acceptance criteria — honest status

- **(i) unhandled managed exception → structured evidence: MET.** Injected-seam tests (the real `UnhandledExceptionEventArgs`/`UnobservedTaskExceptionEventArgs` shapes) plus a real `WebApplicationFactory<Program>` boot. No process kill, no database.
- **(ii) native-abort posture + previous-run startup report: PARTIALLY MET.** The startup report and its honest posture line are implemented and tested. The **dump knobs themselves are process environment**, so they belong to `compose.dev.yml` (`x-api-environment`) and `compose.prod.yml` — **outside this ticket's `scope:` list**, therefore not edited here and **flagged as an orchestrator action** in the PR and §13.1: `DOTNET_DbgEnableMiniDump=1`, `DOTNET_DbgMiniDumpType=2`, `DOTNET_DbgMiniDumpName=/app/logs/crash-evidence/dump.%p`, plus a durable evidence volume (the container filesystem is discarded by `--force-recreate` — exactly how the 2026-10-20 evidence was lost). Until those land, the host says `not configured` rather than implying coverage it does not have.
- **(iii) watch vs product documented: MET.** Capture is host-level, so it is identical under `dotnet watch` (the watch child *is* the host) and in the composed product container; the two honest limits — nothing managed runs on a native abort, and the artifact is only as durable as the container filesystem — are stated in the artifact's own `captureNote`, in `01` §10.3/§13.1 v1.5.26 and in the PR.
- **(iv) CI green, no happy-path behaviour change: MET locally.** `dotnet build Degerli.sln` 0 warnings / 0 errors; `CrashEvidenceTests` 11/11; targeted regressions 25/25 (`HealthEndpointTests`, `InternalErrorTests`, `DataEnvelopeTests`, `Foundation014MailDispatcher*`, `SchedulerTests`). No existing test was modified or weakened.
- **Live criterion (orchestrator post-merge, operator action, never a test):** `kill -ABRT` the dev API process → a recoverable artifact (or a dump once the knobs are wired) and the revived host's startup line naming it. **Not attempted here** (it would take down the shared dev stack mid-wave). Record the outcome verbatim on this ticket.

### Item (c) — investigation note on the 2026-10-20T20:12:53Z abort (attempt only, nothing fabricated)

Attempted 2026-10-24 against the local dev stack (`degerli-dev-api-1`):

- The dev API is the **`dotnet watch` child inside the container** (live: `dotnet watch ⌚ Polling file watcher is enabled`, hot reload enabled, ENC "restart is needed" prompts). Consistent with the ticket's watch/hot-reload candidate — **not proof of it**.
- **The container that aborted no longer exists.** The running container was created `2026-10-23T21:42:08Z` (three days after the abort) with `RestartCount=0`, so the 2026-10-20 container logs and any in-container file were discarded by this wave's force-recreates.
- `docker events --since 168h --filter container=degerli-dev-api-1` → **no events retained** for that period.
- `dmesg` is unreadable in this environment → the OOM/external-kill branch can be **neither confirmed nor excluded**.

**Conclusion: the abort remains undiagnosed, by evidence loss — which is exactly the hole this ticket closes.** No hypothesis is promoted to a finding.

### Test evidence

- **Red:** the test project failed to compile against the missing `Degerli.Api.Diagnostics` types (`CS0234`/`CS0246` on `CrashEvidenceReport`, `ICrashEvidenceSink`, `CrashEvidenceOptions`, `CrashEvidenceWriter`, `CrashEvidenceHooks`) — the not-implemented class.
- **Green:** `dotnet test … --filter CrashEvidenceTests` → **11 passed / 0 failed / 0 skipped** (one test-helper bug fixed during green: the "healthy boot writes nothing" case read a directory that is deliberately never created; the assertion was kept, the helper was made tolerant — no test weakened).
- **Targeted regressions:** 25/25 green; **no TC lane exists for this lane** (foundation explicit-criteria convention, 030 precedent), so the 11 cases carry the `TKT_foundation_045` tag in their names.

### Flagged items

1. `compose.dev.yml` / `compose.prod.yml` dump knobs + evidence volume — out of scope, orchestrator action (details in (ii) above).
2. The ticket's live criterion is **not attempted** here (shared dev stack; operator action post-merge).
3. `.pipeline/testing/` mirroring was not touched — this ticket's doc scope is `01` only.
