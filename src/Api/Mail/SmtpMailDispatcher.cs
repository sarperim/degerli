using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Mail;

/// <summary>
/// SMTP implementation of the <see cref="IMailDispatcher"/> seam, selected by
/// configuration (FLG-04: the real relay is a configuration step; MailPit is the
/// SMTP double in CI/local, Brevo the production relay). Uses the BCL
/// <see cref="System.Net.Mail.SmtpClient"/> so no new dependency is introduced.
///
/// Bounded by a short send timeout so an unreachable relay can never stall an API
/// request past the e2e suite's 5-minute budget (test strategy §15). Failures are
/// logged honestly but scrubbed of message content: neither the bilingual body nor
/// its single-use verification/reset token is ever emitted (acc-005 review S2 /
/// FLG-04 caveat).
/// </summary>
public sealed class SmtpMailDispatcher : IMailDispatcher
{
    /// <summary>
    /// Hard upper bound on a single SMTP send (10 s). Bounds the request path and
    /// keeps the e2e budget gate meaningful when the relay is slow or unreachable.
    /// </summary>
    private const int SendTimeoutMilliseconds = 10_000;

    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpMailDispatcher> _logger;

    public SmtpMailDispatcher(IOptions<SmtpOptions> options, ILogger<SmtpMailDispatcher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        using var message = BuildMessage(mail);
        using var client = CreateClient();

        try
        {
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Honest on failure, scrubbed on content: the recipient is operational
            // metadata, but the message body and its single-use token are not.
            _logger.LogError(
                "Outbound e-mail to {Recipient} failed at the SMTP relay ({ErrorType}).",
                mail.To,
                exception.GetType().Name);
            throw;
        }
    }

    private SmtpClient CreateClient()
    {
        var client = new SmtpClient(_options.Host, _options.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = _options.UseStartTls,
            Timeout = SendTimeoutMilliseconds,
            UseDefaultCredentials = false,
        };

        // MailPit (CI/local) accepts unauthenticated SMTP; Brevo needs credentials.
        if (!string.IsNullOrWhiteSpace(_options.User))
        {
            client.Credentials = new NetworkCredential(_options.User, _options.Password);
        }

        return client;
    }

    private MailMessage BuildMessage(OutboundMail mail)
    {
        var message = new MailMessage(_options.From, mail.To, mail.Subject, ComposeBody(mail));
        message.IsBodyHtml = false;
        // Bilingual bodies and subjects carry Turkish characters and a URL-escaped
        // token; encode UTF-8 so neither is mangled in transit.
        message.BodyEncoding = Encoding.UTF8;
        message.SubjectEncoding = Encoding.UTF8;
        return message;
    }

    /// <summary>
    /// The seam sends one bilingual message (TR then EN, `01` §10.5); this adapter
    /// renders both bodies stacked so no server-side locale guess is needed.
    /// </summary>
    private static string ComposeBody(OutboundMail mail)
        => string.Join(
            $"{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}",
            mail.BodyTr,
            mail.BodyEn);
}
