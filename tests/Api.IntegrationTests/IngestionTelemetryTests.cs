using System.Diagnostics.Metrics;
using Degerli.Api.Infrastructure;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Telemetry;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// SLICE-014 acceptance — the ingestion meters are recorded once, at the runner seam, with
/// bounded tags and no exporter.
/// <para>
/// Every test runs the real <c>prices</c> EOD job through the real
/// <see cref="IIngestionJobRunner"/> against a migrated Testcontainers PostgreSQL and the
/// FU §10 WireMock source double (the <see cref="PriceIngestionTests"/> harness), and
/// observes the <c>Degerli.Ingestion</c> meter with an in-process
/// <see cref="MeterListener"/>. No OTLP exporter is ever registered here: the meters are
/// inspected in memory, so the zero-live-calls invariant holds (the OpenTelemetry export
/// pipeline is off by default and untouched by this test).
/// </para>
/// </summary>
public sealed class IngestionTelemetryTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public IngestionTelemetryTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// SLICE-014 — a successful run records one duration sample tagged
    /// <c>job_code=prices, outcome=succeeded</c> and a <c>written</c> row count of 16.
    /// </summary>
    [Fact]
    public async Task SLICE_014_Successful_run_records_duration_and_written_rows()
    {
        await ResetAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk);
        await using var provider = BuildIngestion("/canned-sources/prices-ok");

        using var collector = new IngestionMeterCollector();
        var result = await RunAsync(provider, "prices");

        Assert.Equal("succeeded", result.Status);

        var duration = Assert.Single(collector.Durations);
        Assert.Equal("prices", duration.Tags[DegerliIngestionMetrics.JobCodeTag]);
        Assert.Equal(IngestResult.Succeeded, duration.Tags[DegerliIngestionMetrics.OutcomeTag]);
        Assert.True(duration.Value >= 0, "duration must be a non-negative number of seconds");

        var written = Assert.Single(collector.Rows, r => Disposition(r) == DegerliIngestionMetrics.Written);
        Assert.Equal("prices", written.Tags[DegerliIngestionMetrics.JobCodeTag]);
        Assert.Equal(16L, written.Value);
    }

    /// <summary>
    /// SLICE-014 — an idempotent re-run records the unchanged disposition, proving the row
    /// counter reports what the run actually did rather than a constant.
    /// </summary>
    [Fact]
    public async Task SLICE_014_Rerun_records_unchanged_rows()
    {
        await ResetAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk);
        await using var provider = BuildIngestion("/canned-sources/prices-ok");

        await RunAsync(provider, "prices");

        using var collector = new IngestionMeterCollector();
        var second = await RunAsync(provider, "prices");

        Assert.Equal(0, second.Written);
        Assert.Equal(16, second.Unchanged);

        var unchanged = Assert.Single(collector.Rows, r => Disposition(r) == DegerliIngestionMetrics.Unchanged);
        Assert.Equal("prices", unchanged.Tags[DegerliIngestionMetrics.JobCodeTag]);
        Assert.Equal(16L, unchanged.Value);

        // A no-op write emits no `written` measurement at all: zero-count dispositions are
        // not reported (the absence is the signal).
        Assert.DoesNotContain(collector.Rows, r => Disposition(r) == DegerliIngestionMetrics.Written);
    }

    /// <summary>
    /// SLICE-014 — the meters are recorded for a failing run too, with the
    /// <c>failed</c> outcome, so a source-down run is visible in the telemetry view.
    /// </summary>
    [Fact]
    public async Task SLICE_014_Failing_run_records_failed_outcome()
    {
        await ResetAsync();
        // No stub on the configured path: every fetch is a 404, which the job surfaces as a
        // failure (SOURCE_FETCH_FAILED / per-symbol transport failure). The runner still
        // emits its duration sample with the terminal outcome.
        await using var provider = BuildIngestion("/canned-sources/absent");

        using var collector = new IngestionMeterCollector();
        var result = await RunAsync(provider, "prices");

        Assert.NotEqual(IngestResult.Succeeded, result.Status);

        var duration = Assert.Single(collector.Durations);
        Assert.Equal("prices", duration.Tags[DegerliIngestionMetrics.JobCodeTag]);
        Assert.Equal(result.Status, duration.Tags[DegerliIngestionMetrics.OutcomeTag]);
    }

    /// <summary>
    /// SLICE-014 — the export pipeline is gated: with <c>OpenTelemetry:Enabled</c> false (the
    /// production default) the composition registers no OpenTelemetry service at all, so the
    /// host behaves exactly as before the slice. With it true, the pipeline descriptors are
    /// present. Nothing is built or exported here (zero live calls): only the registrations
    /// are inspected.
    /// </summary>
    [Fact]
    public void SLICE_014_OpenTelemetry_registration_is_gated_by_configuration()
    {
        static IServiceCollection Compose(bool enabled)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenTelemetry:Enabled"] = enabled ? "true" : "false",
                    ["OpenTelemetry:OtlpEndpoint"] = "http://localhost:18889",
                })
                .Build();

            return new ServiceCollection().AddDegerliOpenTelemetry(configuration);
        }

        static bool IsOpenTelemetry(ServiceDescriptor descriptor) =>
            (descriptor.ServiceType.FullName ?? string.Empty).Contains("OpenTelemetry", StringComparison.Ordinal)
            || (descriptor.ImplementationType?.FullName ?? string.Empty).Contains("OpenTelemetry", StringComparison.Ordinal);

        Assert.DoesNotContain(Compose(enabled: false), IsOpenTelemetry);
        Assert.Contains(Compose(enabled: true), IsOpenTelemetry);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The production ingestion composition retargeted at the Testcontainers database and
    /// the WireMock source double — identical to <see cref="PriceIngestionTests"/>. The
    /// telemetry path under test is the runner's unconditional metrics emission; the
    /// OpenTelemetry host pipeline (and its OTLP exporter) is never registered here.
    /// </summary>
    private TelemetryHost BuildIngestion(string pricesPath)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Prices:Path"] = pricesPath,
                ["Ingestion:Prices:UniverseIndex"] = "XU100",
                ["Ingestion:Prices:RequestDelayMs"] = "0",
            })
            .Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        return new TelemetryHost(services.BuildServiceProvider(), mail, logs);
    }

    private static async Task<IngestResult> RunAsync(TelemetryHost host, string jobCode)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

    /// <summary>Applies the FU universe and clears the ingestion tables.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
    }

    private static string? Disposition(RecordedRow row) =>
        row.Tags.TryGetValue(DegerliIngestionMetrics.DispositionTag, out var value) ? value : null;

    /// <summary>One recorded histogram/counter measurement with its tag snapshot.</summary>
    private sealed record RecordedMeasurement(double Value, IReadOnlyDictionary<string, string?> Tags);

    private sealed record RecordedRow(long Value, IReadOnlyDictionary<string, string?> Tags);

    /// <summary>
    /// In-process listener on the <c>Degerli.Ingestion</c> meter. It snapshots every
    /// measurement synchronously (the SDK forbids holding measurement objects past the
    /// callback) and never exports anything — the zero-live-calls posture.
    /// </summary>
    private sealed class IngestionMeterCollector : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<RecordedMeasurement> _durations = [];
        private readonly List<RecordedRow> _rows = [];

        public IngestionMeterCollector()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == DegerliIngestionMetrics.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            {
                if (instrument.Name == "degerli.ingestion.job.duration")
                {
                    _durations.Add(new RecordedMeasurement(measurement, Snapshot(tags)));
                }
            });

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                if (instrument.Name == "degerli.ingestion.job.rows")
                {
                    _rows.Add(new RecordedRow(measurement, Snapshot(tags)));
                }
            });

            _listener.Start();
        }

        public IReadOnlyList<RecordedMeasurement> Durations => _durations;

        public IReadOnlyList<RecordedRow> Rows => _rows;

        public void Dispose() => _listener.Dispose();

        private static IReadOnlyDictionary<string, string?> Snapshot(
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                snapshot[tag.Key] = tag.Value?.ToString();
            }

            return snapshot;
        }
    }

    /// <summary>The active ingestion provider plus its alert/mail and log doubles.</summary>
    private sealed class TelemetryHost : IAsyncDisposable
    {
        public TelemetryHost(ServiceProvider provider, RecordingMailDispatcher mail, InMemorySerilogSink logs)
        {
            Provider = provider;
            Mail = mail;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>Funnels the ingestion alert seam into the shared recording mail double.</summary>
    private sealed class HarnessMailAlerter : IIngestionAlerter
    {
        private readonly RecordingMailDispatcher _mail;

        public HarnessMailAlerter(RecordingMailDispatcher mail) => _mail = mail;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            _mail.SendAsync(
                new OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
