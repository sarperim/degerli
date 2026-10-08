using System.Collections.Concurrent;

namespace Degerli.Api.IntegrationTests.Harness.Mail;

/// <summary>
/// In-process mail dispatcher double (test strategy §7): records every dispatch and
/// never touches the network. Registered in the test host so tests assert exactly
/// what the application tried to send. Implements both the harness alias and, through
/// it, the application seam (<c>Degerli.Api.Mail.IMailDispatcher</c>).
/// </summary>
public sealed class RecordingMailDispatcher : IMailDispatcher
{
    private readonly ConcurrentQueue<Degerli.Api.Mail.OutboundMail> _sent = new();

    public IReadOnlyList<Degerli.Api.Mail.OutboundMail> Sent => _sent.ToArray();

    public Task SendAsync(Degerli.Api.Mail.OutboundMail mail, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(mail);
        return Task.CompletedTask;
    }
}
