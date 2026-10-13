using Degerli.Api.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-014 — the mail-dispatch seam is configuration-gated (FLG-04):
/// with no <c>Smtp:Host</c> the app resolves the honest <see cref="LoggingMailDispatcher"/>
/// (unchanged production posture, no secrets required); a configured host selects
/// the real <see cref="SmtpMailDispatcher"/> behind the same interface, so an
/// API-initiated transactional mail actually reaches the SMTP relay (MailPit in CI).
///
/// These are boot-level checks against <see cref="ApiFactory"/> (no live database is
/// touched — the health check is stubbed), so they run without Testcontainers.
/// </summary>
public sealed class Foundation014MailDispatcherGatingTests
{
    [Fact]
    public async Task Unconfigured_smtp_host_keeps_the_logging_dispatcher()
    {
        await using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IMailDispatcher>();

        Assert.IsType<LoggingMailDispatcher>(dispatcher);
    }

    [Fact]
    public async Task Configured_smtp_host_selects_the_smtp_dispatcher()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "mailpit",
            ["Smtp:Port"] = "1025",
            ["Smtp:UseStartTls"] = "false",
        };

        await using var factory = new ApiFactory(overrides, HealthStatus.Healthy);
        using var scope = factory.Services.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IMailDispatcher>();

        Assert.IsType<SmtpMailDispatcher>(dispatcher);
    }
}
