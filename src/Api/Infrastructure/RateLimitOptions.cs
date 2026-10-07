namespace Degerli.Api.Infrastructure;

/// <summary>
/// Rate-limit budgets (`03` §12). Bound from the <c>RateLimits</c> configuration
/// section; documented defaults are 5/min on <c>/auth/*</c>, 600/min global and
/// 60/min on <c>/admin</c>. Tests override the section to exercise the edges cheaply.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public int AuthPermitLimit { get; set; } = 5;

    public int GlobalPermitLimit { get; set; } = 600;

    public int AdminPermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}
