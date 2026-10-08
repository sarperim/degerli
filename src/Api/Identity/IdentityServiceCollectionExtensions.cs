using Degerli.Api.Mail;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Degerli.Api.Identity;

/// <summary>
/// Identity-module composition (`01` §10.1): ASP.NET Core Identity with the platform
/// <see cref="DegerliDbContext"/>, cookie authentication (HttpOnly; Secure;
/// SameSite=Lax), the password policy (min 10 chars, no composition rules) and the
/// bilingual mail seam. Later ACC tickets extend the route group; this owns the
/// module's service registration only.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddDegerliIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName));
        services.PostConfigure<AuthOptions>(options =>
        {
            var appUrl = configuration["APP_URL"];
            if (!string.IsNullOrWhiteSpace(appUrl))
            {
                options.AppUrl = appUrl!;
            }
        });

        // Deterministic-time seam: tests register the fake clock afterwards, which
        // wins on resolution (test strategy §7).
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMailDispatcher, LoggingMailDispatcher>();

        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddAuthorization();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // 01 §10.5: min 10 chars, no composition rules (NIST-aligned).
                options.Password.RequiredLength = 10;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // 01 §10.1: lockout 10 fails / 15 min, per account.
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<DegerliDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // NFR-ACC-002: PBKDF2 >= 100k iterations.
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 100_000);

        // 01 §10.1: verification token 48h single-use (reset overrides per-provider in TKT-acc-005).
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(48));

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "degerli.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);

            // API contract: no redirects to an HTML login page — 401/403 as codes
            // (`03` §7).
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        return services;
    }
}
