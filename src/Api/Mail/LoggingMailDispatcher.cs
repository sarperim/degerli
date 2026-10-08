using Microsoft.Extensions.Logging;

namespace Degerli.Api.Mail;

/// <summary>
/// Default mail dispatcher: records the outbound message in the structured log. The
/// real SMTP/Brevo adapter is a configuration step deferred by FLG-04 (domain pending,
/// sender-not-yet-verified), so V1 ships the seam plus this honest local sink rather
/// than pretending delivery happened. Message bodies are deliberately not logged —
/// transactional bodies carry single-use verification/reset tokens.
/// </summary>
public sealed class LoggingMailDispatcher : IMailDispatcher
{
    private readonly ILogger<LoggingMailDispatcher> _logger;

    public LoggingMailDispatcher(ILogger<LoggingMailDispatcher> logger) => _logger = logger;

    public Task SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Outbound e-mail queued for {Recipient}: {Subject} (bilingual TR/EN).",
            mail.To,
            mail.Subject);
        return Task.CompletedTask;
    }
}
