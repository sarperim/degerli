namespace Degerli.Api.IntegrationTests.Harness.Mail;

/// <summary>
/// The mail seam now lives in the application (<c>Degerli.Api.Mail</c>, established
/// by TKT-acc-002); the harness aliases the application types so the foundation
/// sample tests keep compiling while tests and production resolve one interface.
/// </summary>
public record OutboundMail(string To, string Subject, string BodyTr, string BodyEn)
    : Degerli.Api.Mail.OutboundMail(To, Subject, BodyTr, BodyEn);

public interface IMailDispatcher : Degerli.Api.Mail.IMailDispatcher
{
}
