namespace Degerli.ContentPipeline;

/// <summary>
/// Runtime configuration for the offline content pipeline CLI (C4). Values come from
/// the container environment using the standard .NET double-underscore convention
/// (<c>ConnectionStrings__Default</c>, <c>Evren__*</c>); tests supply them directly so
/// the evren API is always doubled at the HTTP boundary and never called live.
/// </summary>
public sealed record ContentPipelineSettings
{
    /// <summary>Database connection string env key (foundation-001 / architecture §10.4).</summary>
    public const string ConnectionStringEnvironmentKey = "ConnectionStrings__Default";

    /// <summary>evren API base URL env key (the secret itself is <c>Evren__ApiKey</c>).</summary>
    public const string EvrenBaseUrlEnvironmentKey = "Evren__BaseUrl";

    /// <summary>evren API key env key (offline AI drafting; never logged, never imaged).</summary>
    public const string EvrenApiKeyEnvironmentKey = "Evren__ApiKey";

    /// <summary>evren drafting route env key; defaults to <see cref="DefaultEvrenDraftPath"/>.</summary>
    public const string EvrenDraftPathEnvironmentKey = "Evren__DraftPath";

    /// <summary>Public evren API host used when no override is configured.</summary>
    public const string DefaultEvrenBaseUrl = "https://api.evren.ai";

    /// <summary>evren drafting endpoint path.</summary>
    public const string DefaultEvrenDraftPath = "/v1/drafts";

    /// <summary>The platform database (MDF facts + RES descriptions).</summary>
    public string? ConnectionString { get; init; }

    /// <summary>evren API base URL (scheme + host, no trailing draft path).</summary>
    public string EvrenBaseUrl { get; init; } = DefaultEvrenBaseUrl;

    /// <summary>Bearer credential for the evren API; optional in the doubled test boundary.</summary>
    public string? EvrenApiKey { get; init; }

    /// <summary>evren drafting endpoint path, relative to <see cref="EvrenBaseUrl"/>.</summary>
    public string EvrenDraftPath { get; init; } = DefaultEvrenDraftPath;

    /// <summary>Builds settings from the process environment (the container path).</summary>
    public static ContentPipelineSettings FromEnvironment() => new()
    {
        ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentKey),
        EvrenBaseUrl = NonEmpty(Environment.GetEnvironmentVariable(EvrenBaseUrlEnvironmentKey)) ?? DefaultEvrenBaseUrl,
        EvrenApiKey = Environment.GetEnvironmentVariable(EvrenApiKeyEnvironmentKey),
        EvrenDraftPath = NonEmpty(Environment.GetEnvironmentVariable(EvrenDraftPathEnvironmentKey)) ?? DefaultEvrenDraftPath,
    };

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
