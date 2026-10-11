using System.Net;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog.Events;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// Sample L2 integration test (TKT-foundation-006 acceptance): the harness boots the
/// real API in-process against a fresh, migrated Testcontainers PostgreSQL, swaps in
/// the deterministic doubles (fake clock, recording mail dispatcher, Serilog test
/// sink) and offers fixture-builder helpers so each test creates its own mutable
/// state. Everything here is a harness proof, not a domain test.
/// </summary>
public sealed class HarnessSampleTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public HarnessSampleTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Api_host_serves_health_against_the_migrated_postgres_container()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        // 200 can only be returned when the harness-applied migration created the
        // schema and the real database dependency check succeeded.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());

        await using var db = _postgres.CreateContext();
        Assert.True(await db.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Fixture_builder_creates_user_screen_and_scenario_against_real_postgres()
    {
        await using var db = _postgres.CreateContext();
        var builder = new FixtureBuilder(db);

        var user = await builder.CreateUserAsync("harness-user@degerli.test");
        var instrument = await builder.CreateInstrumentAsync("HRNST");
        var screen = await builder.CreateScreenAsync(user.Id, "Harness Screen", "[{\"metricCode\":\"pe\"}]");
        var scenario = await builder.CreateScenarioAsync(user.Id, instrument.Id, "Harness Scenario");

        await using var verify = _postgres.CreateContext();
        Assert.True(await verify.Users.AnyAsync(u => u.Id == user.Id && u.EmailConfirmed));
        Assert.True(await verify.SavedScreens.AnyAsync(s => s.Id == screen.Id && s.UserId == user.Id));
        Assert.True(await verify.DcfScenarios.AnyAsync(s => s.Id == scenario.Id && s.InstrumentId == instrument.Id));
    }

    [Fact]
    public async Task Mail_double_captures_a_dispatched_message()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        _ = factory.CreateClient(); // build the host so DI is live

        var dispatcher = factory.Services.GetRequiredService<IMailDispatcher>();
        await dispatcher.SendAsync(new OutboundMail(
            To: "builder@degerli.local",
            Subject: "Harness probe",
            BodyTr: "Merhaba",
            BodyEn: "Hello"));

        var sent = Assert.Single(factory.Mail.Sent);
        Assert.Equal("builder@degerli.local", sent.To);
        Assert.Equal("Harness probe", sent.Subject);
        Assert.Equal("Merhaba", sent.BodyTr);
        Assert.Equal("Hello", sent.BodyEn);
    }

    [Fact]
    public async Task Fake_clock_is_wired_into_the_test_host()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        _ = factory.CreateClient();

        var clock = Assert.IsType<FakeTimeProvider>(factory.Services.GetRequiredService<TimeProvider>());
        var before = clock.GetUtcNow();

        clock.Advance(TimeSpan.FromHours(48));

        Assert.Equal(before.AddHours(48), factory.Services.GetRequiredService<TimeProvider>().GetUtcNow());
    }

    [Fact]
    public async Task Serilog_test_sink_captures_application_logs()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        // The real flake (TKT-foundation-012) was not pure timing: Serilog's
        // request-logging middleware defaults to the process-static Log.Logger, which
        // every parallel in-process host overwrites at boot, so the /health completion
        // event could be routed to another (possibly disposed) host's sink and never
        // reach this factory. DegerliAppFactory now pins RequestLoggingOptions.Logger
        // to this host's own logger, making the event deterministic. The bounded wait
        // below remains as defence for the event's genuine post-response timing.
        var captured = await WaitForLogAsync(
            factory.Logs,
            evt => evt.MessageTemplate.Text.Contains("HTTP", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));

        Assert.True(
            captured,
            "Expected the request-logging middleware event in the Serilog test sink.");
    }

    /// <summary>
    /// Polls the Serilog test sink until <paramref name="predicate"/> matches or
    /// <paramref name="timeout"/> elapses. Used for events emitted after the HTTP
    /// response is returned (e.g. request-logging completion), whose timing is not
    /// synchronized with the client call.
    /// </summary>
    private static async Task<bool> WaitForLogAsync(
        InMemorySerilogSink sink,
        Func<LogEvent, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!sink.Contains(predicate))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return true;
    }
}
