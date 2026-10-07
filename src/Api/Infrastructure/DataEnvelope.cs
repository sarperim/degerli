namespace Degerli.Api.Infrastructure;

/// <summary>
/// Honest-data envelope contract (`01` §10.7). Every data-bearing response carries
/// <c>asOf</c> and <c>stale</c>; historical series add <c>adjusted</c>; figures add
/// <c>restated</c>; missing data is represented explicitly (never blank-as-zero).
/// These are the only response-shape helpers domain modules may use.
/// </summary>
public static class DataEnvelope
{
    /// <summary>A single figure with its freshness and restatement markers.</summary>
    public static FigureEnvelope<T> Figure<T>(T value, DateOnly asOf, bool restated = false, bool stale = false)
        => new(value, asOf, stale, restated);

    /// <summary>A historical series with its freshness and adjustment markers.</summary>
    public static SeriesEnvelope<T> Series<T>(IReadOnlyList<T> points, DateOnly asOf, bool adjusted = false, bool stale = false)
        => new(points, asOf, stale, adjusted);

    /// <summary>An explicit missing-data response — state plus optional coverage note.</summary>
    public static MissingDataEnvelope Missing(DateOnly asOf, string state, DataCoverage? coverage = null, bool stale = false)
        => new(asOf, stale, state, coverage);
}

/// <summary>The documented honest-data state markers (`01` §10.7).</summary>
public static class DataStates
{
    /// <summary>No data exists for the requested period/instrument.</summary>
    public const string NoData = "no-data";

    /// <summary>Data is being prepared and is not yet available.</summary>
    public const string Preparing = "preparing";

    /// <summary>The data source is temporarily unavailable.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Every valid state marker.</summary>
    public static readonly IReadOnlyList<string> All = new[] { NoData, Preparing, Unavailable };
}

/// <summary>Coverage metadata accompanying a missing-data response.</summary>
public sealed record DataCoverage(string? AvailableFrom, string? BoundaryNote);

/// <summary>A figure: <c>value</c> plus <c>asOf</c>/<c>stale</c>/<c>restated</c>.</summary>
public sealed record FigureEnvelope<T>(T Value, DateOnly AsOf, bool Stale, bool Restated);

/// <summary>A historical series: <c>points</c> plus <c>asOf</c>/<c>stale</c>/<c>adjusted</c>.</summary>
public sealed record SeriesEnvelope<T>(IReadOnlyList<T> Points, DateOnly AsOf, bool Stale, bool Adjusted);

/// <summary>A missing-data response: <c>asOf</c>/<c>stale</c>/<c>state</c> (+ <c>coverage</c>).</summary>
public sealed record MissingDataEnvelope(DateOnly AsOf, bool Stale, string State, DataCoverage? Coverage);
