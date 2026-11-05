using System.Diagnostics.Metrics;

namespace Degerli.Ingestion.Telemetry;

/// <summary>
/// The ingestion lane's <see cref="System.Diagnostics.Metrics"/> instruments (SLICE-014).
/// <para>
/// <b>Instrumentation is unconditional, export is gated.</b> These instruments always exist,
/// but <see cref="System.Diagnostics.Metrics"/> instruments are no-ops without a listener, so
/// they cost effectively nothing until a meter provider (the API's OpenTelemetry pipeline,
/// <c>src/Api/Infrastructure/OpenTelemetrySetup.cs</c>) subscribes to
/// <see cref="MeterName"/> — off by default in production. L2 tests attach an in-process
/// <c>MeterListener</c> instead, so no test ever exports over OTLP (zero-live-calls).
/// </para>
/// <para>
/// Recorded in exactly one place: <c>IngestionJobRunner.RunCoreAsync</c>
/// (<c>src/Ingestion/Jobs/Jobs.cs</c>) — the per-job seam every scheduler and admin trigger
/// already rides. No individual job class is instrumented.
/// </para>
/// <para>
/// Tag values are bounded and non-sensitive by construction (<c>job_code</c>,
/// <c>outcome</c>, <c>disposition</c>): connection strings, credentials and payloads never
/// reach a tag. There is no investment-advice vocabulary anywhere in this file.
/// </para>
/// </summary>
public static class DegerliIngestionMetrics
{
    /// <summary>The meter name the API's OpenTelemetry pipeline subscribes to.</summary>
    public const string MeterName = "Degerli.Ingestion";

    /// <summary>The <c>job_code</c> tag: the job's stable <see cref="Jobs.IIngestJob.JobCode"/>.</summary>
    public const string JobCodeTag = "job_code";

    /// <summary>The <c>outcome</c> tag: the run's <see cref="Jobs.IngestResult.Status"/>.</summary>
    public const string OutcomeTag = "outcome";

    /// <summary>The <c>disposition</c> tag: which row count a measurement reports.</summary>
    public const string DispositionTag = "disposition";

    /// <summary>A fact was written.</summary>
    public const string Written = "written";

    /// <summary>A fact was already present and unchanged (idempotent no-op).</summary>
    public const string Unchanged = "unchanged";

    /// <summary>A fact was quarantined instead of written.</summary>
    public const string Quarantined = "quarantined";

    /// <summary>A fact/entity was deliberately excluded by a scope bound.</summary>
    public const string Skipped = "skipped";

    /// <summary>The shared meter for the ingestion lane.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Wall-clock duration of one job run, in seconds, tagged <c>job_code</c> and
    /// <c>outcome</c> (the terminal status — <c>failed</c> for a thrown run).
    /// </summary>
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "degerli.ingestion.job.duration",
        unit: "s",
        description: "Duration of one ingestion job run, tagged by job code and outcome.");

    /// <summary>
    /// Rows a job run accounted for, tagged <c>job_code</c> and <c>disposition</c>
    /// (<see cref="Written"/> / <see cref="Unchanged"/> / <see cref="Quarantined"/> /
    /// <see cref="Skipped"/>). Only non-zero dispositions are reported, so a "nothing to do"
    /// run is visible as the absence of a row measurement rather than as a zero.
    /// </summary>
    public static readonly Counter<long> JobRows = Meter.CreateCounter<long>(
        "degerli.ingestion.job.rows",
        unit: "{row}",
        description: "Rows accounted for by an ingestion job run, tagged by job code and disposition.");
}
