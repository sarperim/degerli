using System.Collections.Concurrent;

namespace Degerli.Api.IntegrationTests.Harness.Mail;

/// <summary>
/// In-process mail dispatcher double (test strategy §7): records every dispatch and
/// never touches the network. Registered in the test host so tests assert exactly
/// what the application tried to send.
/// </summary>
public sealed class RecordingMailDispatcher : IMailDispatcher
{
    private readonly ConcurrentQueue<OutboundMail> _sent = new();

    public IReadOnlyList<OutboundMail> Sent => _sent.ToArray();

    public Task SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(mail);
        return Task.CompletedTask;
    }
}
