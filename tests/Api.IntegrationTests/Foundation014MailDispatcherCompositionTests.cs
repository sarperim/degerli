using Degerli.Api.Identity;
using Degerli.Api.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-014 — the dispatcher-selection logic in
/// <see cref="IdentityServiceCollectionExtensions.AddDegerliIdentity"/> exercised
/// directly on a bare <see cref="ServiceCollection"/> (no host, no database), so the
/// gate itself is proven deterministically and independently of the boot harness:
/// <c>Smtp:Host</c> unset → <see cref="LoggingMailDispatcher"/>; set → <see cref="SmtpMailDispatcher"/>.
/// The boot-level companion lives in <see cref="Foundation014MailDispatcherGatingTests"/>.
/// </summary>
public sealed class Foundation014MailDispatcherCompositionTests
{
    [Fact]
    public void Unconfigured_smtp_host_registers_the_logging_dispatcher()
    {
        var dispatcher = ResolveDispatcher(new Dictionary<string, string?>());

        Assert.IsType<LoggingMailDispatcher>(dispatcher);
    }

    [Fact]
    public void Configured_smtp_host_registers_the_smtp_dispatcher()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "mailpit",
            ["Smtp:Port"] = "1025",
            ["Smtp:UseStartTls"] = "false",
        };

        var dispatcher = ResolveDispatcher(settings);

        Assert.IsType<SmtpMailDispatcher>(dispatcher);
    }

    [Fact]
    public void Blank_smtp_host_keeps_the_logging_dispatcher()
    {
        var dispatcher = ResolveDispatcher(new Dictionary<string, string?> { ["Smtp:Host"] = "  " });

        Assert.IsType<LoggingMailDispatcher>(dispatcher);
    }

    private static IMailDispatcher ResolveDispatcher(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDegerliIdentity(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMailDispatcher>();
    }
}
