using Degerli.ContentPipeline.Drafting;
using Degerli.Persistence;

namespace Degerli.ContentPipeline;

/// <summary>
/// The content pipeline CLI entrypoint (C4, `03` §9). Only the <c>draft</c> command is
/// defined here (FR-RES-019); baseline regeneration is a separate concern. Runs offline,
/// builder-triggered, through <c>docker compose run --rm contentpipeline draft [--symbol=]</c>.
/// </summary>
public static class ContentPipelineCli
{
    /// <summary>Usage/configuration error exit code (mirrors the fixtures seeder).</summary>
    public const int UsageExitCode = 2;

    /// <summary>
    /// Parses <paramref name="args"/> and runs the requested command. <paramref name="settings"/>
    /// defaults to the process environment; tests pass an explicit settings instance so the
    /// evren API is doubled at the HTTP boundary.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        ContentPipelineSettings? settings = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        output ??= Console.Out;
        error ??= Console.Error;
        settings ??= ContentPipelineSettings.FromEnvironment();

        if (args.Length == 0 || !string.Equals(args[0], "draft", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync("usage: draft [--symbol=<SYMBOL>]").ConfigureAwait(false);
            return UsageExitCode;
        }

        var symbol = ParseSymbol(args);
        if (symbol is { Length: 0 })
        {
            await error.WriteLineAsync("draft: --symbol requires a non-empty value.").ConfigureAwait(false);
            return UsageExitCode;
        }

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            await error.WriteLineAsync(
                $"draft: no connection string (pass {ContentPipelineSettings.ConnectionStringEnvironmentKey}).").ConfigureAwait(false);
            return UsageExitCode;
        }

        if (string.IsNullOrWhiteSpace(settings.EvrenBaseUrl))
        {
            await error.WriteLineAsync(
                $"draft: no evren base URL (pass {ContentPipelineSettings.EvrenBaseUrlEnvironmentKey}).").ConfigureAwait(false);
            return UsageExitCode;
        }

        using var http = new HttpClient { BaseAddress = new Uri(settings.EvrenBaseUrl, UriKind.Absolute) };
        var client = new EvrenDraftClient(http, settings.EvrenApiKey, settings.EvrenDraftPath);

        await using var db = new DegerliDbContext(DegerliDbContextOptions.Build(settings.ConnectionString));
        var drafter = new DescriptionDrafter(db, client, output);

        try
        {
            var written = await drafter.DraftAsync(symbol, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"draft: wrote {written} row(s).").ConfigureAwait(false);
            return 0;
        }
        catch (UnknownSymbolException ex)
        {
            await error.WriteLineAsync($"draft: {ex.Message}").ConfigureAwait(false);
            return UsageExitCode;
        }
    }

    /// <summary>Reads <c>--symbol=THETA</c> (or <c>--symbol THETA</c>); null when omitted.</summary>
    private static string? ParseSymbol(IReadOnlyList<string> args)
    {
        for (var i = 1; i < args.Count; i++)
        {
            const string flag = "--symbol";
            var arg = args[i];

            if (arg.StartsWith($"{flag}=", StringComparison.Ordinal))
            {
                return arg[(flag.Length + 1)..];
            }

            if (string.Equals(arg, flag, StringComparison.Ordinal))
            {
                return i + 1 < args.Count ? args[i + 1] : string.Empty;
            }
        }

        return null;
    }
}
