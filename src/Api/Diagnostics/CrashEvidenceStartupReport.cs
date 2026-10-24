using Microsoft.Extensions.Logging;

namespace Degerli.Api.Diagnostics;

/// <summary>
/// Startup reporting for crash evidence (`01` §13.1 v1.5.26). On every boot the host looks
/// at the evidence directory and says — in the structured log — whether a <b>previous
/// run</b> left an artifact behind, and states the native-abort posture honestly. Without
/// this line an artifact is only findable by an operator who already suspects a crash;
/// with it, the revival after an abort is itself the notification.
/// </summary>
public static class CrashEvidenceStartupReport
{
    /// <summary>Environment knob the .NET runtime reads to write a dump on a native abort.</summary>
    public const string RuntimeDumpKnob = "DOTNET_DbgEnableMiniDump";

    private const int MaxArtifactsLogged = 5;

    /// <summary>
    /// Logs the previous-run artifacts (newest first) and the capture posture. Never throws:
    /// a diagnostics report must not be able to fail a boot.
    /// </summary>
    public static IReadOnlyList<string> Report(CrashEvidenceOptions options, ILogger logger)
        => Report(options, logger, Environment.GetEnvironmentVariable);

    /// <summary>
    /// Overload with an injected environment lookup so both dump-posture branches are testable
    /// deterministically, without mutating the test process's environment.
    /// </summary>
    public static IReadOnlyList<string> Report(
        CrashEvidenceOptions options,
        ILogger logger,
        Func<string, string?> readEnvironment)
    {
        try
        {
            var artifacts = FindArtifacts(options);

            foreach (var artifact in artifacts.Take(MaxArtifactsLogged))
            {
                logger.LogInformation(
                    "Previous-run crash evidence found: {ArtifactPath} ({ArtifactKind}) — read it before assuming this boot's state.",
                    artifact.Path,
                    artifact.Kind);
            }

            if (artifacts.Count > MaxArtifactsLogged)
            {
                logger.LogInformation(
                    "Previous-run crash evidence found: {AdditionalCount} further artifact(s) in {EvidenceDirectory}.",
                    artifacts.Count - MaxArtifactsLogged,
                    Path.GetFullPath(options.Directory));
            }

            logger.LogInformation(
                "Crash-evidence capture active: managed hooks ({UnhandledHook}, {UnobservedHook}) write to {EvidenceDirectory}; " +
                "native-abort crash dumps {DumpPosture}.",
                CrashEvidenceSources.AppDomainUnhandledException,
                CrashEvidenceSources.TaskSchedulerUnobserved,
                Path.GetFullPath(options.Directory),
                DumpPosture(readEnvironment));

            return artifacts.Select(artifact => artifact.Path).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<(string Path, string Kind)> FindArtifacts(CrashEvidenceOptions options)
    {
        try
        {
            var directory = Path.GetFullPath(options.Directory);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<(string, string)>();
            }

            return new DirectoryInfo(directory)
                .GetFiles("crash-*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .Select(file => (file.FullName, KindOf(file.Name)))
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>
    /// The artifact name carries the kind (<c>crash-…-{pid}-{kind}.json</c>); a cheap parse
    /// keeps the log line useful without reading the file at boot.
    /// </summary>
    private static string KindOf(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Contains(CrashEvidenceKinds.UnobservedTaskException, StringComparison.Ordinal)
            ? CrashEvidenceKinds.UnobservedTaskException
            : CrashEvidenceKinds.UnhandledException;
    }

    /// <summary>
    /// Honest posture. The managed hooks cannot run for SIGABRT/heap-corruption/external
    /// kills; only the runtime's own dump knobs cover that class. Those knobs are read by
    /// the runtime from the process environment at start-up, so this line reports what is
    /// actually set rather than implying coverage that is not there.
    /// </summary>
    private static string DumpPosture(Func<string, string?> readEnvironment)
    {
        var value = readEnvironment(RuntimeDumpKnob);
        var enabled = value is not null
                      && (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));

        if (enabled)
        {
            return $"enabled ({RuntimeDumpKnob}={value}) — a native abort writes a runtime dump, and the next boot reports it";
        }

        return $"not configured ({RuntimeDumpKnob} {(value is null ? "unset" : $"set to '{value}'")}) — a native abort " +
               "(SIGABRT / exit 134) cannot run managed hooks and will leave no dump until the composition sets it; " +
               "see 01 §13.1 v1.5.26";
    }
}
