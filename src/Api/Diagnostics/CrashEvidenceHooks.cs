namespace Degerli.Api.Diagnostics;

/// <summary>
/// The managed crash hooks the API host installs (`01` §13.1 v1.5.26, §10.3). Both hooks
/// are <b>passive</b>: they observe, hand a <see cref="CrashEvidenceReport"/> to the
/// injected <see cref="ICrashEvidenceSink"/> and return. They never rethrow, never
/// re-raise, never call <see cref="UnobservedTaskExceptionEventArgs.SetObserved"/> and
/// never touch a healthy request path.
/// <para>
/// <see cref="AppDomain.UnhandledException"/> is the last managed code that runs for an
/// <b>unhandled managed exception</b> — its artifact is the only chance to learn what died.
/// It does <b>not</b> run for a native abort (SIGABRT / exit 134), a runtime heap/GC abort
/// or an external kill: that class is only covered where the runtime crash-dump knobs are
/// enabled in the process environment (the honest limit stated in the artifact and in the
/// startup posture line).
/// </para>
/// </summary>
public sealed class CrashEvidenceHooks : IDisposable
{
    private readonly ICrashEvidenceSink _sink;
    private bool _installed;

    public CrashEvidenceHooks(ICrashEvidenceSink sink) => _sink = sink;

    /// <summary>The artifact path of the most recent capture, or <c>null</c> if none was written.</summary>
    public string? LastArtifactPath { get; private set; }

    /// <summary>Subscribes to the process-wide hooks (idempotent).</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
        _installed = true;
    }

    /// <summary>Unsubscribes; safe to call without a prior <see cref="Install"/>.</summary>
    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;
        _installed = false;
    }

    /// <summary>
    /// Captures a terminating/non-terminating unhandled exception. Public and
    /// argument-driven so the capture seam is exercised directly with an injected fault —
    /// no test has to kill its own process.
    /// </summary>
    public void RecordUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        Record(() =>
        {
            var exception = Describe(args.ExceptionObject);
            return new CrashEvidenceReport
            {
                Kind = CrashEvidenceKinds.UnhandledException,
                Source = CrashEvidenceSources.AppDomainUnhandledException,
                IsTerminating = args.IsTerminating,
                ExitPath = args.IsTerminating
                    ? "AppDomain.UnhandledException (terminating): the runtime unloads the AppDomain and terminates the " +
                      "process after this handler returns — signals, hosted-service shutdown and the graceful exit path " +
                      "do not run"
                    : "AppDomain.UnhandledException (non-terminating): the runtime continues after this handler",
                Exception = exception,
            };
        });
    }

    /// <summary>Captures an unobserved background task fault without changing its policy.</summary>
    public void RecordUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Record(() =>
        {
            var flattened = args.Exception.Flatten();
            var innermost = flattened.InnerExceptions.Count > 0 ? flattened.InnerExceptions[0] : flattened;
            return new CrashEvidenceReport
            {
                Kind = CrashEvidenceKinds.UnobservedTaskException,
                Source = CrashEvidenceSources.TaskSchedulerUnobserved,
                IsTerminating = false,
                ExitPath =
                    "TaskScheduler.UnobservedTaskException (observation only): the process continues — the exception's " +
                    "policy is left untouched (SetObserved is deliberately not called)",
                Exception = new CrashEvidenceException(
                    innermost.GetType().FullName ?? innermost.GetType().Name,
                    flattened.InnerExceptions.Count > 1
                        ? $"{flattened.Message} [{flattened.InnerExceptions.Count} faulted tasks]"
                        : innermost.Message,
                    flattened.ToString()),
            };
        });
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        => RecordUnhandledException(sender, args);

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
        => RecordUnobservedTaskException(sender, args);

    /// <summary>
    /// The never-throwing envelope around every capture: a fault in the evidence path must
    /// not become the reason the process dies differently than it would have.
    /// </summary>
    private void Record(Func<CrashEvidenceReport> buildReport)
    {
        try
        {
            LastArtifactPath = _sink.Capture(buildReport());
        }
        catch (Exception)
        {
            // Swallowed by design — see class remarks.
        }
    }

    private static CrashEvidenceException Describe(object? exceptionObject)
    {
        return exceptionObject switch
        {
            Exception exception => new CrashEvidenceException(
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.ToString()),
            null => new CrashEvidenceException("unknown", "the runtime reported no exception object", null),
            _ => new CrashEvidenceException(
                exceptionObject.GetType().FullName ?? exceptionObject.GetType().Name,
                exceptionObject.ToString(),
                null),
        };
    }
}
