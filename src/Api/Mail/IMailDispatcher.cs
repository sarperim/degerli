namespace Degerli.Api.Mail;

/// <summary>
/// One outbound transactional/alert e-mail. Bilingual by design — the platform sends
/// both TR and EN bodies in a single message so no server-side locale guess is needed
/// (`01` §10.5, `03` §2 "transactional e-mails are bilingual").
/// </summary>
public record OutboundMail(string To, string Subject, string BodyTr, string BodyEn);

/// <summary>
/// Mail-dispatch seam. The application resolves this interface; production wires the
/// real SMTP/Brevo adapter via configuration (FLG-04), while L2 integration tests
/// replace it with the recording double (test strategy §7). Established here because
/// the Identity module is the first e-mail producer (FR-ACC-008).
/// </summary>
public interface IMailDispatcher
{
    Task SendAsync(OutboundMail mail, CancellationToken cancellationToken = default);
}
