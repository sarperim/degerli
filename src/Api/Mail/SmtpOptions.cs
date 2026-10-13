namespace Degerli.Api.Mail;

/// <summary>
/// SMTP-relay configuration for the transactional-mail adapter (`01` §10.4; the
/// <c>Smtp__*</c> environment variables documented in `.env.example` and set by
/// <c>compose.ci.yml</c>/<c>compose.dev.yml</c>). Bound from the <c>Smtp</c>
/// section. Its presence is what gates the dispatcher choice: no
/// <see cref="Host"/> → the honest <see cref="LoggingMailDispatcher"/> stays
/// (FLG-04 production posture, zero required secrets).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>SMTP relay host. Empty/unset keeps the logging sink (FLG-04).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP relay port (MailPit 1025; Brevo 587).</summary>
    public int Port { get; set; } = 25;

    /// <summary>Relay user; empty means no authentication (MailPit in CI/local).</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Relay password. Never logged.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Envelope/header sender for every transactional message.</summary>
    public string From { get; set; } = "no-reply@degerli.local";

    /// <summary>
    /// Upgrade the connection with STARTTLS. Brevo requires it in production;
    /// MailPit (CI/local) does not, and the CI stack forces it off.
    /// </summary>
    public bool UseStartTls { get; set; }
}
