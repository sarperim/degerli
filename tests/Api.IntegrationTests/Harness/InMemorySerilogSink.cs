using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// Serilog test sink. Registered as an <see cref="ILogEventSink"/> in the test host;
/// the API's Serilog pipeline already calls <c>ReadFrom.Services</c>, so every log
/// event the application emits is captured here for assertion (failure forensics,
/// test strategy §6).
/// </summary>
public sealed class InMemorySerilogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }

    public bool Contains(Func<LogEvent, bool> predicate) => Events.Any(predicate);
}
