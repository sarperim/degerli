namespace Degerli.Api.Diagnostics;

/// <summary>The crash classes the API host can capture (`01` §13.1 v1.5.26).</summary>
public static class CrashEvidenceKinds
{
    public const string UnhandledException = "unhandled-exception";
    public const string UnobservedTaskException = "unobserved-task-exception";
}

/// <summary>The runtime hook each report came from — recorded verbatim in the artifact.</summary>
public static class CrashEvidenceSources
{
    public const string AppDomainUnhandledException = "AppDomain.UnhandledException";
    public const string TaskSchedulerUnobserved = "TaskScheduler.UnobservedTaskException";
}

/// <summary>The exception detail recorded in an artifact (already bounded by the writer).</summary>
public sealed class CrashEvidenceException
{
    public CrashEvidenceException(string type, string? message, string? stackTrace)
    {
        Type = type;
        Message = message;
        StackTrace = stackTrace;
    }

    /// <summary>The exception's full type name, e.g. <c>System.InvalidOperationException</c>.</summary>
    public string Type { get; }

    /// <summary>The exception message (redacted and bounded before it is written).</summary>
    public string? Message { get; }

    /// <summary>The exception's stack trace (redacted and bounded before it is written).</summary>
    public string? StackTrace { get; }
}

/// <summary>
/// One crash observation handed to a <see cref="ICrashEvidenceSink"/>. Keeping the report
/// a plain value is what makes the capture seam injectable: a test raises the same shape
/// the real <see cref="AppDomain.UnhandledException"/> hook raises, with no process kill.
/// </summary>
public sealed class CrashEvidenceReport
{
    /// <summary>One of <see cref="CrashEvidenceKinds"/>.</summary>
    public string Kind { get; init; } = CrashEvidenceKinds.UnhandledException;

    /// <summary>One of <see cref="CrashEvidenceSources"/>.</summary>
    public string Source { get; init; } = CrashEvidenceSources.AppDomainUnhandledException;

    /// <summary>
    /// What happens to the process after the hook returns — the "exit path" an operator
    /// reads to know whether the fault terminated the host.
    /// </summary>
    public string ExitPath { get; init; } = string.Empty;

    /// <summary>True when the runtime is terminating the process after this hook.</summary>
    public bool IsTerminating { get; init; }

    /// <summary>The fault itself, when one is available.</summary>
    public CrashEvidenceException? Exception { get; init; }
}

/// <summary>
/// The crash-evidence destination. The single seam the hooks depend on, so a test can
/// prove a failing sink never masks the original fault.
/// </summary>
public interface ICrashEvidenceSink
{
    /// <summary>
    /// Captures <paramref name="report"/>; returns the artifact path, or <c>null</c> when
    /// the evidence could not be written. Implementations must never throw.
    /// </summary>
    string? Capture(CrashEvidenceReport report);
}
