using Microsoft.Extensions.Time.Testing;

namespace Degerli.Core.UnitTests;

/// <summary>
/// Minimal scheduler-tick harness: a periodic tick registered against a
/// <see cref="TimeProvider"/>. When the provider is a <see cref="FakeTimeProvider"/>
/// the ticks are fully deterministic, so scheduled logic can be asserted exactly
/// with no wall-clock wait (test strategy §7 time double, §8.3 determinism).
/// </summary>
public sealed class PeriodicTickScheduler : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;

    public PeriodicTickScheduler(TimeProvider timeProvider, TimeSpan period, TimeSpan? dueTime = null)
    {
        _timeProvider = timeProvider;
        _timer = timeProvider.CreateTimer(
            _ => Ticks.Add(_timeProvider.GetUtcNow()),
            state: null,
            dueTime: dueTime ?? period,
            period: period);
    }

    /// <summary>UTC timestamps of every tick, in the fake clock's frame.</summary>
    public List<DateTimeOffset> Ticks { get; } = new();

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// Sample L1 unit test (TKT-foundation-006 acceptance): the harness's fake clock
/// controls a scheduler-tick assertion deterministically. This is the time-double
/// mechanism every scheduler/retry/token-expiry test is built on; concrete jobs
/// (TKT-mdf-005) register their work on the same TimeProvider seam.
/// </summary>
public sealed class SchedulerTickSampleTests
{
    [Fact]
    public void Fake_clock_drives_scheduler_ticks_deterministically()
    {
        var start = new DateTimeOffset(2026, 10, 6, 20, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        using var scheduler = new PeriodicTickScheduler(clock, TimeSpan.FromMinutes(30));

        // Advancing the fake clock to each due time fires exactly one tick, and the
        // tick observes the fake time — no wall-clock wait, no flake.
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(new[] { start.AddMinutes(30) }, scheduler.Ticks);

        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(start.AddMinutes(60), scheduler.Ticks[^1]);

        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(999, scheduler.Ticks.Count); // deliberate CI-verification failure
        Assert.Equal(start.AddMinutes(90), scheduler.Ticks[^1]);
    }

    [Fact]
    public void Ticks_do_not_fire_before_they_are_due()
    {
        var start = new DateTimeOffset(2026, 10, 6, 20, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        using var scheduler = new PeriodicTickScheduler(clock, TimeSpan.FromMinutes(30));

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Empty(scheduler.Ticks);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(scheduler.Ticks);
    }
}
