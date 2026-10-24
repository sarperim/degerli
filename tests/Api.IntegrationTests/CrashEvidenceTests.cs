using System.Reflection;
using System.Text.Json;
using Degerli.Api.Diagnostics;
using Degerli.Api.IntegrationTests.Harness;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-045 acceptance — <b>crash-evidence capture</b> for the API host
/// (<c>01</c> §13.1 v1.5.26, §10.3/§10.4). The dev stack's API process exited with
/// SIGABRT (exit 134) on 2026-10-20 with <em>no</em> managed exception, no log tail and
/// no artifact at all, so the abort was undiagnosable. The deliverable pinned here is
/// evidence, never a root-cause claim: the managed hooks (<see cref="AppDomain.UnhandledException"/>,
/// <see cref="TaskScheduler.UnobservedTaskException"/>) hand the fault to a <b>bounded</b>,
/// <b>redacted</b>, never-throwing writer that lands an artifact next to the logs, and a
/// startup line reports any artifact a previous run left behind.
/// <para>
/// Everything is exercised through the injected seam — no test kills its own process
/// and no test needs a database. A native SIGABRT cannot run managed hooks at all; that
/// honest limit is pinned by the posture line (see the last case) and stated in the
/// artifact's own <c>captureNote</c>.
/// </para>
/// </summary>
public sealed class CrashEvidenceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 20, 20, 12, 53, TimeSpan.Zero);

    // ---- (i) an unhandled managed exception leaves a bounded artifact -----------------

    [Fact]
    public void TKT_foundation_045_unhandled_exception_writes_an_artifact_with_the_expected_fields()
    {
        using var workspace = new CrashEvidenceWorkspace();
        var logFile = workspace.WriteLog("degerli-20261020.log", Lines(40));
        var writer = workspace.Writer(secrets: Array.Empty<string>());

        var report = new CrashEvidenceReport
        {
            Kind = CrashEvidenceKinds.UnhandledException,
            Source = CrashEvidenceSources.AppDomainUnhandledException,
            ExitPath = "AppDomain.UnhandledException (terminating): the runtime terminates the process after this handler",
            IsTerminating = true,
            Exception = new CrashEvidenceException(
                "System.InvalidOperationException",
                "probe: unhandled exception for crash-evidence verification.",
                "   at Degerli.Api.Program.ThrowProbe() in /app/src/Api/Program.cs:line 151"),
        };

        var path = writer.Capture(report);

        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"artifact not found at {path}");
        Assert.StartsWith("crash-20261020T201253Z", Path.GetFileName(path), StringComparison.Ordinal);
        Assert.Equal(workspace.EvidenceDirectory, Path.GetDirectoryName(path));

        using var artifact = JsonDocument.Parse(File.ReadAllText(path));
        var root = artifact.RootElement;

        Assert.Equal(CrashEvidenceKinds.UnhandledException, root.GetProperty("kind").GetString());
        Assert.Equal(CrashEvidenceSources.AppDomainUnhandledException, root.GetProperty("source").GetString());
        Assert.True(root.GetProperty("isTerminating").GetBoolean());
        Assert.Contains("terminates the process", root.GetProperty("exitPath").GetString(), StringComparison.Ordinal);
        Assert.Equal(FixedNow, root.GetProperty("capturedAt").GetDateTimeOffset());

        var exception = root.GetProperty("exception");
        Assert.Equal("System.InvalidOperationException", exception.GetProperty("type").GetString());
        Assert.Equal(
            "probe: unhandled exception for crash-evidence verification.",
            exception.GetProperty("message").GetString());
        Assert.Contains("Program.ThrowProbe", exception.GetProperty("stackTrace").GetString(), StringComparison.Ordinal);
        Assert.False(exception.GetProperty("messageTruncated").GetBoolean());
        Assert.False(exception.GetProperty("stackTraceTruncated").GetBoolean());

        // Environment/version context survives the crash: an operator can tell what died.
        var process = root.GetProperty("process");
        Assert.Equal(Environment.ProcessId, process.GetProperty("pid").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(process.GetProperty("runtimeVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(process.GetProperty("os").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(process.GetProperty("architecture").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(process.GetProperty("appVersion").GetString()));
        Assert.Equal("Test", process.GetProperty("environment").GetString());

        // The last-N log tail travels with the artifact (the abort's own context).
        var logs = root.GetProperty("logs");
        Assert.Equal(logFile, logs.GetProperty("file").GetString());
        var tail = logs.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).ToArray();
        Assert.Equal(40, tail.Length);
        Assert.Equal("line-0040", tail[^1]);

        // Honesty: the artifact states what it cannot cover (native aborts).
        Assert.Contains("SIGABRT", root.GetProperty("captureNote").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TKT_foundation_045_terminating_flag_is_recorded_from_the_real_event_args()
    {
        using var workspace = new CrashEvidenceWorkspace();
        var hooks = workspace.Hooks();

        hooks.RecordUnhandledException(
            null,
            new UnhandledExceptionEventArgs(new InvalidOperationException("terminating fault"), isTerminating: true));

        var root = workspace.SingleArtifact();
        Assert.True(root.GetProperty("isTerminating").GetBoolean());
        Assert.Contains("terminating", root.GetProperty("exitPath").GetString(), StringComparison.OrdinalIgnoreCase);

        var nonTerminatingHooks = workspace.Hooks();
        nonTerminatingHooks.RecordUnhandledException(
            null,
            new UnhandledExceptionEventArgs(new InvalidOperationException("non-terminating fault"), isTerminating: false));

        var nonTerminating = workspace.Artifacts()
            .Select(File.ReadAllText)
            .Select(text => JsonDocument.Parse(text).RootElement)
            .Single(element => element.GetProperty("exception").GetProperty("message").GetString() == "non-terminating fault");
        Assert.False(nonTerminating.GetProperty("isTerminating").GetBoolean());
    }

    [Fact]
    public void TKT_foundation_045_unobserved_task_exception_is_recorded_and_does_not_terminate()
    {
        using var workspace = new CrashEvidenceWorkspace();
        var hooks = workspace.Hooks();
        var fault = new AggregateException(new InvalidOperationException("background fault"));

        var args = new UnobservedTaskExceptionEventArgs(fault);
        hooks.RecordUnobservedTaskException(null, args);

        var root = workspace.SingleArtifact();
        Assert.Equal(CrashEvidenceKinds.UnobservedTaskException, root.GetProperty("kind").GetString());
        Assert.Equal(CrashEvidenceSources.TaskSchedulerUnobserved, root.GetProperty("source").GetString());
        Assert.False(root.GetProperty("isTerminating").GetBoolean());
        Assert.Contains("continues", root.GetProperty("exitPath").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background fault", root.GetProperty("exception").GetProperty("message").GetString(), StringComparison.Ordinal);

        // No behaviour change: the hook records and leaves the policy decision alone.
        Assert.False(args.Observed);
    }

    [Fact]
    public async Task TKT_foundation_045_concurrent_captures_in_the_same_second_all_land_distinct_artifacts()
    {
        using var workspace = new CrashEvidenceWorkspace();
        workspace.WriteLog("degerli-20261020.log", Lines(3));
        var writer = workspace.Writer(secrets: Array.Empty<string>());
        const int captures = 8;

        // Both process hooks can fire at once (the faulting thread for AppDomain.UnhandledException,
        // the finalizer thread for TaskScheduler.UnobservedTaskException), and the fixed clock
        // forces every capture onto the identical name stem: the collision the naming must survive.
        var attempts = Enumerable.Range(0, captures)
            .Select(index => Task.Run(() =>
                writer.Capture(Report(new InvalidOperationException($"concurrent fault {index}")))))
            .ToArray();
        var paths = await Task.WhenAll(attempts);

        Assert.All(paths, path => Assert.NotNull(path));
        Assert.Equal(captures, paths.Distinct(StringComparer.Ordinal).Count());

        // Every fault left its own artifact — in the same second, under the same pid.
        var artifacts = workspace.Artifacts();
        Assert.Equal(captures, artifacts.Count);
        var messages = artifacts
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement
                .GetProperty("exception").GetProperty("message").GetString())
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            Enumerable.Range(0, captures).Select(index => $"concurrent fault {index}").OrderBy(m => m, StringComparer.Ordinal),
            messages);

        // A failed or overlapping landing must leave no temp debris behind.
        Assert.Empty(Directory.GetFiles(workspace.EvidenceDirectory, "*.tmp"));
    }

    // ---- bounded + never-throwing ------------------------------------------------------

    [Fact]
    public void TKT_foundation_045_log_tail_is_bounded_to_the_configured_line_count()
    {
        using var workspace = new CrashEvidenceWorkspace();
        workspace.WriteLog("degerli-20261020.log", Lines(500));
        var writer = workspace.Writer(o => o.LogTailLines = 25);

        var path = writer.Capture(Report(new InvalidOperationException("bounded tail")));

        Assert.NotNull(path);
        using var artifact = JsonDocument.Parse(File.ReadAllText(path!));
        var logs = artifact.RootElement.GetProperty("logs");
        var tail = logs.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).ToArray();

        Assert.Equal(25, tail.Length);
        Assert.Equal("line-0500", tail[^1]);
        Assert.DoesNotContain("line-0400", tail);
        Assert.True(logs.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void TKT_foundation_045_oversized_exception_text_is_truncated()
    {
        using var workspace = new CrashEvidenceWorkspace();
        var writer = workspace.Writer(o =>
        {
            o.MaxExceptionMessageChars = 200;
            o.MaxStackTraceChars = 400;
        });

        var huge = new string('x', 50_000);
        var path = writer.Capture(new CrashEvidenceReport
        {
            Kind = CrashEvidenceKinds.UnhandledException,
            Source = CrashEvidenceSources.AppDomainUnhandledException,
            ExitPath = "terminating",
            IsTerminating = true,
            Exception = new CrashEvidenceException("System.OutOfMemoryException", huge, huge),
        });

        Assert.NotNull(path);
        using var artifact = JsonDocument.Parse(File.ReadAllText(path!));
        var exception = artifact.RootElement.GetProperty("exception");

        Assert.True(exception.GetProperty("messageTruncated").GetBoolean());
        Assert.True(exception.GetProperty("stackTraceTruncated").GetBoolean());
        Assert.True(exception.GetProperty("message").GetString()!.Length < 1_000);
        Assert.True(exception.GetProperty("stackTrace").GetString()!.Length < 1_500);
        Assert.True(new FileInfo(path!).Length < 100_000, "the artifact must stay bounded");
    }

    [Fact]
    public void TKT_foundation_045_a_failing_writer_returns_null_and_never_throws()
    {
        using var workspace = new CrashEvidenceWorkspace();
        // The evidence directory is a *file*, so CreateDirectory fails — the writer must
        // swallow it rather than mask the original fault.
        var blocked = Path.Combine(workspace.Root, "not-a-directory");
        File.WriteAllText(blocked, "occupied");

        var writer = new CrashEvidenceWriter(
            new CrashEvidenceOptions
            {
                Directory = Path.Combine(blocked, "crash-evidence"),
                LogDirectory = workspace.LogDirectory,
                Environment = "Test",
            },
            new FakeTimeProvider(FixedNow));

        var path = writer.Capture(Report(new InvalidOperationException("original fault")));

        Assert.Null(path);
    }

    [Fact]
    public void TKT_foundation_045_a_throwing_sink_never_propagates_out_of_the_hooks()
    {
        var hooks = new CrashEvidenceHooks(new ThrowingSink());

        hooks.Install();
        try
        {
            hooks.RecordUnhandledException(
                null,
                new UnhandledExceptionEventArgs(new InvalidOperationException("fault"), isTerminating: true));
            hooks.RecordUnobservedTaskException(
                null,
                new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("fault"))));
        }
        finally
        {
            hooks.Dispose();
        }

        Assert.Null(hooks.LastArtifactPath);
    }

    // ---- the hooks are actually installed on the process -------------------------------

    [Fact]
    public void TKT_foundation_045_install_subscribes_the_process_hooks_idempotently_and_dispose_removes_them()
    {
        // Every other case calls the record seams directly, so this is the only proof that
        // Install() really wires the process-level events: deleting the two `+=` lines in
        // CrashEvidenceHooks.Install fails exactly here.
        var hooks = new CrashEvidenceHooks(new ThrowingSink());
        var appDomainHandler = HandlerOf<UnhandledExceptionEventHandler>(hooks, "OnAppDomainUnhandledException");
        var taskHandler = HandlerOf<EventHandler<UnobservedTaskExceptionEventArgs>>(hooks, "OnTaskSchedulerUnobservedTaskException");

        Assert.False(IsSubscribedToAppDomain(appDomainHandler));
        Assert.False(IsSubscribedToTaskScheduler(taskHandler));

        hooks.Install();
        try
        {
            Assert.True(IsSubscribedToAppDomain(appDomainHandler));
            Assert.True(IsSubscribedToTaskScheduler(taskHandler));

            // Idempotent: a second Install must not add a duplicate subscription.
            hooks.Install();
            Assert.Single(SubscribersOfAppDomain(appDomainHandler));
            Assert.Single(SubscribersOfTaskScheduler(taskHandler));
        }
        finally
        {
            hooks.Dispose();
        }

        Assert.False(IsSubscribedToAppDomain(appDomainHandler));
        Assert.False(IsSubscribedToTaskScheduler(taskHandler));
    }

    // ---- redaction ---------------------------------------------------------------------

    [Fact]
    public void TKT_foundation_045_configured_secrets_and_credential_patterns_are_redacted()
    {
        using var workspace = new CrashEvidenceWorkspace();
        var secret = "s3cr3t-kap-value";
        workspace.WriteLog(
            "degerli-20261020.log",
            new[]
            {
                "line-0001 GET /disclosures Authorization: Basic " + Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("key:secret")),
                "line-0002 upstream failed: password=hunter2 api_key=" + secret,
                // The repository's own Serilog format is CompactJsonFormatter, so a real tail
                // line is JSON: the closing quote sits between the keyword and the separator.
                "{\"Password\":\"hunter2\"}",
                "line-0004 {\"ApiKey\":\"json-api-key-value\",\"status\":\"failed\"}",
            });
        var writer = workspace.Writer(secrets: new[] { secret });

        var path = writer.Capture(new CrashEvidenceReport
        {
            Kind = CrashEvidenceKinds.UnhandledException,
            Source = CrashEvidenceSources.AppDomainUnhandledException,
            ExitPath = "terminating",
            IsTerminating = true,
            Exception = new CrashEvidenceException(
                "System.Net.Http.HttpRequestException",
                $"request to evds3.tcmb.gov.tr failed: key={secret}",
                $"   at X.Bearer() --- Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig --- key={secret}"),
        });

        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        using var artifact = JsonDocument.Parse(text);
        var exception = artifact.RootElement.GetProperty("exception");

        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("json-api-key-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("key:secret")), text, StringComparison.Ordinal);
        Assert.Contains("***", exception.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Contains("***", exception.GetProperty("stackTrace").GetString()!, StringComparison.Ordinal);

        var tail = artifact.RootElement.GetProperty("logs").GetProperty("lines").EnumerateArray()
            .Select(l => l.GetString()!).ToArray();
        Assert.All(tail, line => Assert.Contains("***", line, StringComparison.Ordinal));

        // The JSON-quoted shape is actually masked, not merely skipped: the keyword survives
        // (the operator still sees which field leaked) and the value is gone.
        Assert.Contains("{\"Password=***\"}", tail);
        Assert.Contains(tail, line => line.Contains("ApiKey=***", StringComparison.Ordinal));
    }

    // ---- (ii) startup report over a previous run's artifact ---------------------------

    [Fact]
    public void TKT_foundation_045_startup_reports_a_previous_run_crash_artifact()
    {
        using var workspace = new CrashEvidenceWorkspace();
        workspace.WriteLog("degerli-20261020.log", Lines(3));
        var artifact = workspace.Writer().Capture(Report(new InvalidOperationException("previous run abort")));
        Assert.NotNull(artifact);

        using var host = workspace.Host();
        _ = host.Services; // boots the real Program.cs pipeline

        var messages = host.Events();
        Assert.Contains(
            messages,
            message => message.Contains("previous-run crash evidence", StringComparison.OrdinalIgnoreCase)
                       && message.Contains(Path.GetFileName(artifact)!, StringComparison.Ordinal));
    }

    [Fact]
    public void TKT_foundation_045_startup_writes_no_artifact_and_passes_the_happy_path_silently()
    {
        using var workspace = new CrashEvidenceWorkspace();

        using var host = workspace.Host();
        _ = host.Services;

        var messages = host.Events();
        Assert.DoesNotContain(
            messages,
            message => message.Contains("previous-run crash evidence", StringComparison.OrdinalIgnoreCase));
        // Passive hooks: a healthy boot leaves no evidence file behind (and does not even
        // create the evidence directory — nothing is written unless something crashed).
        Assert.Empty(Directory.Exists(workspace.EvidenceDirectory)
            ? Directory.GetFiles(workspace.EvidenceDirectory, "crash-*.json")
            : Array.Empty<string>());
    }

    [Fact]
    public void TKT_foundation_045_startup_reports_the_native_abort_posture_honestly()
    {
        using var workspace = new CrashEvidenceWorkspace();

        // The posture reports the real process environment, so the assertion follows whatever
        // this host actually has instead of assuming the knob is unset (an operator's machine
        // may set it); the deterministic both-branch coverage is the next case.
        var knob = Environment.GetEnvironmentVariable(CrashEvidenceStartupReport.RuntimeDumpKnob);
        var dumpsEnabled = knob is not null
                           && (knob.Equals("1", StringComparison.Ordinal)
                               || knob.Equals("true", StringComparison.OrdinalIgnoreCase));

        using (var host = workspace.Host())
        {
            _ = host.Services;

            // The managed hooks cannot run for a SIGABRT; the startup line must state the real
            // dump posture rather than imply coverage the host does not have.
            var expected = dumpsEnabled ? "enabled" : "not configured";
            Assert.Contains(
                host.Events(),
                message => message.Contains("native-abort crash dumps", StringComparison.OrdinalIgnoreCase)
                           && message.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TKT_foundation_045_dump_posture_covers_both_branches_deterministically()
    {
        using var workspace = new CrashEvidenceWorkspace();
        var logger = new RecordingLogger();

        // The environment lookup is an injected seam, so both branches are pinned without
        // mutating the process-wide environment the rest of the suite shares.
        CrashEvidenceStartupReport.Report(workspace.Options(), logger, _ => "1");
        CrashEvidenceStartupReport.Report(workspace.Options(), logger, _ => "true");
        CrashEvidenceStartupReport.Report(workspace.Options(), logger, _ => null);
        CrashEvidenceStartupReport.Report(workspace.Options(), logger, _ => "0");

        Assert.Contains(
            logger.Messages,
            message => message.Contains("native-abort crash dumps enabled (DOTNET_DbgEnableMiniDump=1)", StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("native-abort crash dumps enabled (DOTNET_DbgEnableMiniDump=true)", StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("not configured (DOTNET_DbgEnableMiniDump unset)", StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("not configured (DOTNET_DbgEnableMiniDump set to '0')", StringComparison.Ordinal));
    }

    // ---- helpers ----------------------------------------------------------------------

    private static CrashEvidenceReport Report(Exception exception) => new()
    {
        Kind = CrashEvidenceKinds.UnhandledException,
        Source = CrashEvidenceSources.AppDomainUnhandledException,
        ExitPath = "AppDomain.UnhandledException (terminating)",
        IsTerminating = true,
        Exception = new CrashEvidenceException(exception.GetType().FullName!, exception.Message, exception.ToString()),
    };

    private static IReadOnlyList<string> Lines(int count)
        => Enumerable.Range(1, count).Select(i => $"line-{i:0000}").ToArray();

    // ---- reflection helpers for the process-level event subscriptions -------------------

    private const BindingFlags EventBackingFieldFlags =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// The delegate <see cref="CrashEvidenceHooks.Install"/> subscribes: a delegate bound to the
    /// hook's private handler compares equal to the subscribed instance by target + method, so
    /// other subscribers already on the process event (other test hosts) cannot mask a match.
    /// </summary>
    private static TDelegate HandlerOf<TDelegate>(CrashEvidenceHooks hooks, string methodName)
        where TDelegate : Delegate
    {
        var method = typeof(CrashEvidenceHooks).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"CrashEvidenceHooks.{methodName} not found");
        return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), hooks, method);
    }

    private static IReadOnlyList<Delegate> SubscribersOfAppDomain(UnhandledExceptionEventHandler handler)
    {
        // .NET 10 forwards `AppDomain.UnhandledException` to `AppContext.UnhandledException`,
        // so the backing delegate field lives on AppContext; fall back to AppDomain itself if a
        // runtime keeps it there.
        var subscribers = Subscribers(typeof(AppDomain), AppDomain.CurrentDomain, handler);
        if (subscribers.Count > 0)
        {
            return subscribers;
        }

        var appContext = typeof(AppDomain).Assembly.GetType("System.AppContext")
                         ?? throw new InvalidOperationException("System.AppContext not found");
        return Subscribers(appContext, null, handler);
    }

    private static IReadOnlyList<Delegate> SubscribersOfTaskScheduler(EventHandler<UnobservedTaskExceptionEventArgs> handler)
        => Subscribers(typeof(TaskScheduler), null, handler);

    private static IReadOnlyList<Delegate> Subscribers(Type hostType, object? instance, Delegate expected)
        => hostType.GetFields(EventBackingFieldFlags)
            .Where(field => field.FieldType == expected.GetType())
            .Select(field => field.GetValue(field.IsStatic ? null : instance) as Delegate)
            .Where(value => value is not null)
            .SelectMany(value => value!.GetInvocationList())
            .Where(subscriber => subscriber.Equals(expected))
            .ToArray();

    private static bool IsSubscribedToAppDomain(UnhandledExceptionEventHandler handler)
        => SubscribersOfAppDomain(handler).Count > 0;

    private static bool IsSubscribedToTaskScheduler(EventHandler<UnobservedTaskExceptionEventArgs> handler)
        => SubscribersOfTaskScheduler(handler).Count > 0;

    /// <summary>Collects rendered log messages for the startup-report assertions.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class ThrowingSink : ICrashEvidenceSink
    {
        public string? Capture(CrashEvidenceReport report)
            => throw new InvalidOperationException("sink is down");
    }

    /// <summary>A throwaway evidence/log directory under <c>/tmp</c>.</summary>
    private sealed class CrashEvidenceWorkspace : IDisposable
    {
        public CrashEvidenceWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "degerli-crash-evidence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            EvidenceDirectory = Path.Combine(Root, "crash-evidence");
            LogDirectory = Path.Combine(Root, "logs");
            Directory.CreateDirectory(LogDirectory);
        }

        public string Root { get; }

        public string EvidenceDirectory { get; }

        public string LogDirectory { get; }

        public CrashEvidenceOptions Options(Action<CrashEvidenceOptions>? configure = null)
        {
            var options = new CrashEvidenceOptions
            {
                Directory = EvidenceDirectory,
                LogDirectory = LogDirectory,
                Environment = "Test",
            };
            configure?.Invoke(options);
            return options;
        }

        public CrashEvidenceWriter Writer(Action<CrashEvidenceOptions>? configure = null)
            => new(Options(configure), new FakeTimeProvider(FixedNow));

        public CrashEvidenceWriter Writer(IReadOnlyList<string> secrets)
            => new(Options(o => o.Secrets = secrets), new FakeTimeProvider(FixedNow));

        public CrashEvidenceHooks Hooks(IReadOnlyList<string>? secrets = null)
        {
            var options = Options(o => o.Secrets = secrets ?? Array.Empty<string>());
            return new CrashEvidenceHooks(new CrashEvidenceWriter(options, new FakeTimeProvider(FixedNow)));
        }

        public string WriteLog(string fileName, IEnumerable<string> lines)
        {
            var path = Path.Combine(LogDirectory, fileName);
            File.WriteAllLines(path, lines);
            return path;
        }

        public IReadOnlyList<string> Artifacts()
            => Directory.Exists(EvidenceDirectory)
                ? Directory.GetFiles(EvidenceDirectory, "crash-*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

        public JsonElement SingleArtifact()
        {
            var path = Assert.Single(Artifacts());
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        }

        public CrashEvidenceHost Host() => new(EvidenceDirectory, LogDirectory);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
                // A best-effort cleanup; a leaked /tmp directory must not fail a test.
            }
        }
    }

    /// <summary>
    /// Boots the real <c>Program.cs</c> pipeline (no database: the health check is never
    /// called) with the crash-evidence paths redirected and the Serilog test sink attached,
    /// so the startup lines are asserted exactly as the production host emits them.
    /// </summary>
    private sealed class CrashEvidenceHost : WebApplicationFactory<Program>
    {
        private readonly string _evidenceDirectory;
        private readonly string _logDirectory;
        private readonly InMemorySerilogSink _sink = new();

        public CrashEvidenceHost(string evidenceDirectory, string logDirectory)
        {
            _evidenceDirectory = evidenceDirectory;
            _logDirectory = logDirectory;
        }

        public IReadOnlyList<string> Events()
            => _sink.Events.Select(evt => evt.RenderMessage()).ToArray();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] =
                        "Host=127.0.0.1;Port=5432;Database=degerli;Username=degerli;Password=degerli;Timeout=1",
                    ["Ingestion:Scheduler:Enabled"] = "false",
                    ["Diagnostics:CrashEvidence:Directory"] = _evidenceDirectory,
                    ["Diagnostics:CrashEvidence:LogDirectory"] = _logDirectory,
                });
            });

            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(_sink));
        }
    }
}
