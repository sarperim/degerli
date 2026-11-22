using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-011 — credential hardening and rate-limit configuration assertions
/// (`03` §12, `01` §10.1, NFR-ACC-002): the documented default budgets (5/600/60 per
/// 60 s window) are the configured values, the password hasher is PBKDF2 with at
/// least 100 000 iterations, and a fixed window releases its budget once it elapses
/// (window reset — driven here by a short controllable window rather than wall-clock
/// minutes). Traces: `03` §12, `01` §10.1.
/// </summary>
public sealed class Xc011CredentialHardeningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc011CredentialHardeningTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void Documented_rate_limit_defaults_are_the_configured_values()
    {
        var limits = _factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        Assert.Equal(5, limits.AuthPermitLimit);
        Assert.Equal(600, limits.GlobalPermitLimit);
        Assert.Equal(60, limits.AdminPermitLimit);
        Assert.Equal(60, limits.WindowSeconds);
    }

    [Fact]
    public void Password_hasher_is_configured_for_at_least_100k_iterations()
    {
        var options = _factory.Services.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;

        Assert.True(
            options.IterationCount >= 100_000,
            $"NFR-ACC-002 requires PBKDF2 >= 100000 iterations, configured was {options.IterationCount}");
        Assert.Equal(PasswordHasherCompatibilityMode.IdentityV3, options.CompatibilityMode);
    }

    [Fact]
    public void Issued_password_hash_is_pbkdf2_with_at_least_100k_iterations()
    {
        // Assert the *effective* format, not just the option: parse the Identity V3
        // hash the app's configured hasher actually produces.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var hash = hasher.HashPassword(new ApplicationUser(), "Degerli-Test-2026");

        var bytes = Convert.FromBase64String(hash);

        // Identity V3: [0]=0x01 format marker, [1..5] PRF (1=HMACSHA256, 2=HMACSHA512),
        // [5..9] iteration count (big-endian), then salt length/salt/subkey.
        Assert.Equal(0x01, bytes[0]);

        var prf = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4));
        Assert.True(prf is 1 or 2, $"expected an HMAC-SHA PRF for PBKDF2, got {prf}");

        var iterations = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4));
        Assert.True(iterations >= 100_000, $"expected >= 100000 iterations, got {iterations}");
    }

    [Fact]
    public async Task Auth_limit_window_resets_once_the_window_elapses()
    {
        var overrides = new Dictionary<string, string?> { ["RateLimits:WindowSeconds"] = "1" };
        await using var factory = new ApiFactory(overrides, HealthStatus.Healthy);
        using var client = factory.CreateClient();
        var csrfToken = await FetchCsrfTokenAsync(client);
        const string ip = "203.0.113.71";

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await PostAuthProbeAsync(client, ip, csrfToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await PostAuthProbeAsync(client, ip, csrfToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // One 1-second window elapses: the fixed window replenishes and the budget resets.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        using var afterReset = await PostAuthProbeAsync(client, ip, csrfToken);
        Assert.Equal(HttpStatusCode.OK, afterReset.StatusCode);
    }

    [Fact]
    public async Task Data_protection_keys_are_persisted_when_keys_path_is_configured()
    {
        var keysDir = Path.Combine(Path.GetTempPath(), $"degerli-dp-keys-{Guid.NewGuid():N}");
        try
        {
            var overrides = new Dictionary<string, string?> { ["DataProtection:KeysPath"] = keysDir };
            await using var factory = new ApiFactory(overrides, HealthStatus.Healthy);
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/api/v1/auth/csrf-token");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.True(Directory.Exists(keysDir));
            var keyFiles = Directory.GetFiles(keysDir, "key-*.xml");
            Assert.NotEmpty(keyFiles);
        }
        finally
        {
            if (Directory.Exists(keysDir))
            {
                Directory.Delete(keysDir, recursive: true);
            }
        }
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf-token");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> PostAuthProbeAsync(HttpClient client, string ip, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Forwarded-For", ip);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        return client.SendAsync(request);
    }
}
