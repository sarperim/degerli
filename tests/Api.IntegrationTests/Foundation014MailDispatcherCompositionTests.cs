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

    /// <summary>
    /// Reproduces the WebApplicationFactory ordering that broke the boot-level test:
    /// the SMTP host is supplied <em>after</em> <see cref="AddDegerliIdentity"/> has
    /// registered services (as the test host's configuration overrides are). The gate
    /// must still select the SMTP adapter, because it is evaluated at resolution time
    /// against the DI-resolved configuration.
    /// </summary>
    [Fact]
    public void Smtp_host_supplied_after_registration_still_selects_the_smtp_dispatcher()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDegerliIdentity(configuration);

        // The override arrives only now (post-registration), like a test host's.
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "mailpit",
            ["Smtp:Port"] = "1025",
        });

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IMailDispatcher>();

        Assert.IsType<SmtpMailDispatcher>(dispatcher);
    }

    private static IMailDispatcher ResolveDispatcher(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        // Mirror the host: IConfiguration is resolvable, and the dispatcher gate
        // binds its options from DI (see PersistenceServiceCollectionExtensions on
        // why an eager snapshot read misses WebApplicationFactory overrides).
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDegerliIdentity(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMailDispatcher>();
    }
}
