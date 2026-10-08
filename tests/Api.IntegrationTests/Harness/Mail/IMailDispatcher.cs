namespace Degerli.Api.IntegrationTests.Harness.Mail;

/// <summary>
/// One outbound transactional/alert e-mail. Bilingual by design — the platform sends
/// both TR and EN bodies in a single message so no server-side locale guess is needed
/// (`01` §10.5).
/// </summary>
public sealed record OutboundMail(string To, string Subject, string BodyTr, string BodyEn);

/// <summary>
/// Mail-dispatch seam. Production wires an SMTP/Brevo-backed implementation (C2
/// transactional e-mail, C3d alerting); L2 integration tests replace it with
/// <see cref="RecordingMailDispatcher"/> so dispatch content and trigger conditions
/// are asserted while real delivery never happens (test strategy §7).
/// </summary>
public interface IMailDispatcher
{
    Task SendAsync(OutboundMail mail, CancellationToken cancellationToken = default);
}
