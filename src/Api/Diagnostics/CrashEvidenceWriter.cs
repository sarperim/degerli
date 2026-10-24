using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Degerli.Api.Diagnostics;

/// <summary>
/// Writes the bounded crash artifact for a <see cref="CrashEvidenceReport"/> (`01` §13.1
/// v1.5.26). The contract is deliberately narrow:
/// <list type="bullet">
/// <item><b>It never throws.</b> A writer failure (unwritable directory, full disk, a
/// failing log read) returns <c>null</c> — it must never mask the original fault or
/// replace the process's exit path with a diagnostics error.</item>
/// <item><b>It is bounded.</b> The exception message and stack trace are clipped, and only
/// the last <see cref="CrashEvidenceOptions.LogTailLines"/> lines of the newest log file
/// are read through a bounded end-of-file read.</item>
/// <item><b>It is redacted.</b> Everything textual passes through
/// <see cref="CrashEvidenceRedactor"/> before a byte reaches disk.</item>
/// </list>
/// </summary>
public sealed class CrashEvidenceWriter : ICrashEvidenceSink
{
    private const string TruncationMarker = "… [truncated]";
    private const string CaptureNote =
        "Managed evidence only. A native abort (SIGABRT / exit 134, heap or GC corruption, an external kill) " +
        "cannot run these managed hooks, so this artifact cannot exist for that class unless the runtime crash-dump " +
        "knobs (DOTNET_DbgEnableMiniDump) are enabled in the process environment — see 01 §13.1 v1.5.26.";

    private readonly CrashEvidenceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly CrashEvidenceRedactor _redactor;

    public CrashEvidenceWriter(CrashEvidenceOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _redactor = new CrashEvidenceRedactor(options.Secrets);
    }

    /// <inheritdoc />
    public string? Capture(CrashEvidenceReport report)
    {
        try
        {
            return CaptureCore(report);
        }
        catch (Exception)
        {
            // Deliberate: the crash writer is never allowed to throw. A missing artifact is
            // a diagnosability loss; a thrown writer would corrupt the original failure.
            return null;
        }
    }

    private string CaptureCore(CrashEvidenceReport report)
    {
        var capturedAt = _timeProvider.GetUtcNow();
        var tail = CrashEvidenceLogTail.Read(_options, _redactor);

        var directory = Path.GetFullPath(_options.Directory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, NextFileName(directory, capturedAt, report.Kind));
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", 1);
            json.WriteString("kind", report.Kind);
            json.WriteString("source", report.Source);
            json.WriteString("capturedAt", capturedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteBoolean("isTerminating", report.IsTerminating);
            json.WriteString("exitPath", report.ExitPath);

            if (report.Exception is { } exception)
            {
                json.WriteStartObject("exception");
                json.WriteString("type", _redactor.Redact(exception.Type));
                WriteBounded(json, "message", exception.Message, _options.MaxExceptionMessageChars, "messageTruncated");
                WriteBounded(json, "stackTrace", exception.StackTrace, _options.MaxStackTraceChars, "stackTraceTruncated");
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("exception");
            }

            WriteProcess(json);
            WriteLogs(json, tail);

            json.WriteString("captureNote", CaptureNote);
            json.WriteEndObject();
        }

        var bytes = buffer.ToArray();
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path);
        return path;
    }

    private void WriteProcess(Utf8JsonWriter json)
    {
        json.WriteStartObject("process");
        json.WriteNumber("pid", Environment.ProcessId);
        json.WriteString("runtimeVersion", Environment.Version.ToString());
        json.WriteString("appVersion", AppVersion());
        json.WriteString("environment", _options.Environment);
        json.WriteString("os", RuntimeInformation.OSDescription);
        json.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
        json.WriteString("machineName", Environment.MachineName);
        json.WriteEndObject();
    }

    private void WriteLogs(Utf8JsonWriter json, CrashEvidenceLogTail.Tail tail)
    {
        json.WriteStartObject("logs");
        if (tail.File is null)
        {
            json.WriteNull("file");
        }
        else
        {
            json.WriteString("file", tail.File);
        }

        json.WriteBoolean("truncated", tail.Truncated);
        json.WriteStartArray("lines");
        foreach (var line in tail.Lines)
        {
            json.WriteStringValue(line);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private void WriteBounded(Utf8JsonWriter json, string property, string? value, int limit, string truncatedProperty)
    {
        var redacted = _redactor.Redact(value);
        var truncated = redacted.Length > limit;
        var bounded = truncated ? redacted[..limit] + TruncationMarker : redacted;
        json.WriteString(property, bounded);
        json.WriteBoolean(truncatedProperty, truncated);
    }

    /// <summary>
    /// Collision-safe name: two faults inside the same second must both leave an artifact
    /// (the second one is what an operator will actually be reading).
    /// </summary>
    private static string NextFileName(string directory, DateTimeOffset capturedAt, string kind)
    {
        var stamp = capturedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var safeKind = new string(kind.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray());
        var stem = $"crash-{stamp}-{Environment.ProcessId}-{safeKind}";
        var candidate = $"{stem}.json";
        for (var suffix = 1; File.Exists(Path.Combine(directory, candidate)); suffix++)
        {
            candidate = $"{stem}-{suffix}.json";
        }

        return candidate;
    }

    private static string AppVersion()
    {
        var assembly = typeof(CrashEvidenceWriter).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}

/// <summary>
/// Reads the last N lines of the newest Serilog rolling file with a bounded end-of-file
/// read. A missing directory, a locked file or a partial final line is a normal (not
/// exceptional) outcome for a crash: the artifact still records what it could see.
/// </summary>
internal static class CrashEvidenceLogTail
{
    internal readonly record struct Tail(string? File, IReadOnlyList<string> Lines, bool Truncated);

    internal static Tail Read(CrashEvidenceOptions options, CrashEvidenceRedactor redactor)
    {
        try
        {
            var directory = Path.GetFullPath(options.LogDirectory);
            if (!Directory.Exists(directory))
            {
                return new Tail(null, Array.Empty<string>(), false);
            }

            var file = new DirectoryInfo(directory)
                .GetFiles("*.log")
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (file is null)
            {
                return new Tail(null, Array.Empty<string>(), false);
            }

            using var stream = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var take = (int)Math.Min(length, Math.Max(options.MaxLogReadBytes, 1));
            var skippedFromStart = take < length;
            stream.Seek(-take, SeekOrigin.End);

            var buffer = new byte[take];
            var read = 0;
            while (read < take)
            {
                var chunk = stream.Read(buffer, read, take - read);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var rawLines = text.Split('\n');
            var firstIndex = skippedFromStart && rawLines.Length > 1 ? 1 : 0;
            var lines = rawLines
                .Skip(firstIndex)
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0)
                .ToList();

            var truncated = skippedFromStart || lines.Count > options.LogTailLines;
            var limit = Math.Max(options.LogTailLines, 1);
            var tail = lines.Count > limit
                ? lines.Skip(lines.Count - limit).ToList()
                : lines;

            return new Tail(file.FullName, tail.Select(redactor.Redact).ToArray(), truncated);
        }
        catch (Exception)
        {
            return new Tail(null, Array.Empty<string>(), false);
        }
    }
}
