using Microsoft.Extensions.Configuration;

namespace Degerli.Api.Diagnostics;

/// <summary>
/// Bounded, non-secret configuration for <see cref="CrashEvidenceWriter"/> (`01` §13.1
/// v1.5.26, diagnostics posture). Everything here is operational config (§10.4): the
/// artifact directory, the log directory the tail is read from, and the hard bounds that
/// keep a crash artifact small and secret-free.
/// </summary>
public sealed class CrashEvidenceOptions
{
    /// <summary>The <c>Diagnostics:CrashEvidence</c> configuration section prefix.</summary>
    public const string SectionName = "Diagnostics:CrashEvidence";

    /// <summary>
    /// Environment keys whose values are never written to an artifact. This mirrors the
    /// repo's client-failure redaction posture (the KAP/EVDS adapters replace the
    /// configured credential before a message reaches a log); a crash artifact is a log
    /// surface like any other, so the same secrets are scrubbed from it.
    /// </summary>
    public static readonly IReadOnlyList<string> SecretConfigurationKeys = new[]
    {
        "ConnectionStrings:Default",
        "Ingestion:Kap:ApiKey",
        "Ingestion:Kap:ApiSecret",
        "Ingestion:Macro:EvdsApiKey",
        "Smtp:Password",
        "Auth:UserRefHashKey",
        "Evren:ApiKey",
    };

    /// <summary>
    /// Where the artifact lands. Defaults to <c>logs/crash-evidence</c> — beside the Serilog
    /// rolling files, the first place an operator looks for a dead process.
    /// </summary>
    public string Directory { get; set; } = Path.Combine("logs", "crash-evidence");

    /// <summary>The Serilog rolling-log directory whose newest file supplies the tail.</summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>How many trailing log lines ride along with the artifact.</summary>
    public int LogTailLines { get; set; } = 200;

    /// <summary>How many bytes of the log file are read from its end (a bounded read).</summary>
    public int MaxLogReadBytes { get; set; } = 512 * 1024;

    /// <summary>Bound on the recorded exception message.</summary>
    public int MaxExceptionMessageChars { get; set; } = 4_000;

    /// <summary>Bound on the recorded stack trace.</summary>
    public int MaxStackTraceChars { get; set; } = 16_000;

    /// <summary>Configured secrets replaced with <c>***</c> before anything is written.</summary>
    public IReadOnlyList<string> Secrets { get; set; } = Array.Empty<string>();

    /// <summary>The host environment name recorded in the artifact (never inferred in tests).</summary>
    public string Environment { get; set; } = "unknown";

    /// <summary>
    /// Reads the diagnostics section from the merged configuration, defaulting to the
    /// paths the shipped <c>appsettings.json</c> logging uses. Non-secret only; the
    /// secrets themselves are collected purely for redaction.
    /// </summary>
    public static CrashEvidenceOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new CrashEvidenceOptions
        {
            Directory = ValueOrDefault(configuration[$"{SectionName}:Directory"], "logs/crash-evidence"),
            LogDirectory = ValueOrDefault(configuration[$"{SectionName}:LogDirectory"], "logs"),
        };

        options.LogTailLines = PositiveOrDefault(configuration[$"{SectionName}:LogTailLines"], options.LogTailLines);
        options.MaxLogReadBytes = PositiveOrDefault(configuration[$"{SectionName}:MaxLogReadBytes"], options.MaxLogReadBytes);
        options.MaxExceptionMessageChars =
            PositiveOrDefault(configuration[$"{SectionName}:MaxExceptionMessageChars"], options.MaxExceptionMessageChars);
        options.MaxStackTraceChars =
            PositiveOrDefault(configuration[$"{SectionName}:MaxStackTraceChars"], options.MaxStackTraceChars);

        options.Secrets = SecretConfigurationKeys
            .Select(key => configuration[key])
            .Where(secret => !string.IsNullOrEmpty(secret))
            .Select(secret => secret!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return options;
    }

    private static string ValueOrDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;

    private static int PositiveOrDefault(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
