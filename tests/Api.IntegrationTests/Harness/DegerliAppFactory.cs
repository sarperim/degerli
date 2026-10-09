using Degerli.Api.IntegrationTests.Harness.Mail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// In-process API host for L2 integration tests that need the real database
/// (test strategy §5.2). Unlike <see cref="ApiFactory"/> (which stubs the DB health
/// check for pure mechanism tests), this factory points the application at a live
/// Testcontainers PostgreSQL instance and swaps in the deterministic test doubles:
/// the fake <see cref="TimeProvider"/>, the recording mail dispatcher and the
/// Serilog test sink. Tests advance <see cref="Clock"/> to drive time-dependent
/// behavior with no wall-clock wait.
/// </summary>
public sealed class DegerliAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public DegerliAppFactory(string connectionString) => _connectionString = connectionString;

    /// <summary>Fixed, controllable clock registered as the application's TimeProvider.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero));

    /// <summary>Captures every Serilog event emitted by the in-process application.</summary>
    public InMemorySerilogSink Logs { get; } = new();

    /// <summary>The mail dispatcher double the application resolves in tests.</summary>
    public RecordingMailDispatcher Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                // The in-process API host would otherwise run the C3c scheduler (TKT-mdf-005)
                // as a live background worker; tests drive the scheduler explicitly instead.
                ["Ingestion:Scheduler:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Registered after the app's own composition, so these win on resolution.
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IMailDispatcher>(Mail);
            // The application resolves its own mail seam (Degerli.Api.Mail); the same
            // recording double satisfies both the harness alias and the app interface.
            services.AddSingleton<Degerli.Api.Mail.IMailDispatcher>(Mail);
            // The API's Serilog pipeline calls ReadFrom.Services; registered sinks are
            // discovered and receive every emitted event.
            services.AddSingleton<ILogEventSink>(Logs);
        });
    }
}
