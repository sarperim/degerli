using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Degerli.SourceProbes.Probing;
using Degerli.SourceProbes.Shapes;

namespace Degerli.SourceProbes.Reporting;

/// <summary>
/// The machine-readable drift report (TKT-foundation-068 acceptance i/iii): one entry per
/// probed source, every drifted path named, and a structural excerpt that carries paths and
/// kinds only. It is written <b>outside</b> the repository by default and is never committed;
/// no raw body and no credential ever reaches it.
/// </summary>
public sealed class DriftReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Creates a report over <paramref name="results"/>.</summary>
    public DriftReport(string mode, DateTimeOffset generatedAt, IReadOnlyList<ProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Mode = mode;
        GeneratedAt = generatedAt;
        Results = results;
    }

    /// <summary>The run mode (<c>live</c>, <c>saved-body</c>).</summary>
    public string Mode { get; }

    /// <summary>When the report was generated (UTC).</summary>
    public DateTimeOffset GeneratedAt { get; }

    /// <summary>One entry per probed source, in registry order.</summary>
    public IReadOnlyList<ProbeResult> Results { get; }

    /// <summary>How many sources matched the double's shape.</summary>
    public int CleanCount => Results.Count(r => r.Outcome == ProbeOutcomeKind.Clean);

    /// <summary>How many sources drifted.</summary>
    public int DriftCount => Results.Count(r => r.Outcome == ProbeOutcomeKind.Drift);

    /// <summary>How many sources could not be read at all.</summary>
    public int UnreachableCount => Results.Count(r => r.Outcome == ProbeOutcomeKind.Unreachable);

    /// <summary>True when at least one source drifted or could not be read.</summary>
    public bool NeedsAttention => DriftCount > 0 || UnreachableCount > 0;

    /// <summary>Serializes the report (the operator artifact; never a CI input).</summary>
    public string ToJson()
    {
        var document = new ReportDocument
        {
            Mode = Mode,
            GeneratedAt = GeneratedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Summary = new SummaryDocument
            {
                Clean = CleanCount,
                Drift = DriftCount,
                Unreachable = UnreachableCount,
            },
            Results = Results.Select(r => new ResultDocument
            {
                Source = r.Source,
                Endpoint = r.Endpoint,
                Mode = r.Mode.ToString().ToLowerInvariant(),
                Outcome = r.Outcome switch
                {
                    ProbeOutcomeKind.Clean => "clean",
                    ProbeOutcomeKind.Drift => "drift",
                    _ => "unreachable",
                },
                StatusCode = r.StatusCode,
                CheckedAt = r.CheckedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Detail = r.Detail,
                Findings = r.Findings.Select(f => new FindingDocument
                {
                    Path = f.Path,
                    Kind = f.Kind switch
                    {
                        DriftKind.MissingKey => "missing-key",
                        DriftKind.ExtraKey => "extra-key",
                        DriftKind.TypeChanged => "type-changed",
                        DriftKind.NullabilityChanged => "nullability-changed",
                        DriftKind.ElementShapeMissing => "element-shape-missing",
                        _ => "status-changed",
                    },
                    Expected = f.Expected,
                    Observed = f.Observed,
                    Severity = f.Severity == DriftSeverity.Breaking ? "breaking" : "warning",
                }).ToList(),
                ObservedShape = r.ObservedShape.Select(p => new ShapePathDocument
                {
                    Path = p.Path,
                    Kinds = ShapeKindsText.Render(p.Kinds),
                    Nullable = p.Nullable,
                }).ToList(),
            }).ToList(),
        };

        return JsonSerializer.Serialize(document, JsonOptions) + "\n";
    }

    /// <summary>Renders the operator-facing summary (also the CLI's stdout).</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        builder.Append("source-contract drift report — mode ").Append(Mode)
            .Append(" — generated ").Append(GeneratedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .AppendLine()
            .Append("clean ").Append(CleanCount)
            .Append(" · drift ").Append(DriftCount)
            .Append(" · unreachable ").Append(UnreachableCount)
            .AppendLine()
            .AppendLine();

        foreach (var result in Results)
        {
            builder.Append('[').Append(result.Outcome.ToString().ToLowerInvariant().PadRight(11)).Append("] ")
                .Append(result.Source.PadRight(26))
                .Append(result.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append("  ").Append(result.Detail)
                .AppendLine();

            foreach (var finding in result.Findings)
            {
                builder.Append("    - ").Append(finding.Path)
                    .Append("  ").Append(KindText(finding.Kind))
                    .Append("  expected ").Append(finding.Expected)
                    .Append(" · observed ").Append(finding.Observed)
                    .Append("  [").Append(finding.Severity == DriftSeverity.Breaking ? "breaking" : "warning").Append(']')
                    .AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string KindText(DriftKind kind) => kind switch
    {
        DriftKind.MissingKey => "missing-key",
        DriftKind.ExtraKey => "extra-key",
        DriftKind.TypeChanged => "type-changed",
        DriftKind.NullabilityChanged => "nullability-changed",
        DriftKind.ElementShapeMissing => "element-shape-missing",
        _ => "status-changed",
    };

    private sealed class ReportDocument
    {
        public string? Mode { get; set; }

        public string? GeneratedAt { get; set; }

        public SummaryDocument? Summary { get; set; }

        public List<ResultDocument>? Results { get; set; }
    }

    private sealed class SummaryDocument
    {
        public int Clean { get; set; }

        public int Drift { get; set; }

        public int Unreachable { get; set; }
    }

    private sealed class ResultDocument
    {
        public string? Source { get; set; }

        public string? Endpoint { get; set; }

        public string? Mode { get; set; }

        public string? Outcome { get; set; }

        public int? StatusCode { get; set; }

        public string? CheckedAt { get; set; }

        public string? Detail { get; set; }

        public List<FindingDocument>? Findings { get; set; }

        public List<ShapePathDocument>? ObservedShape { get; set; }
    }

    private sealed class FindingDocument
    {
        public string? Path { get; set; }

        public string? Kind { get; set; }

        public string? Expected { get; set; }

        public string? Observed { get; set; }

        public string? Severity { get; set; }
    }

    private sealed class ShapePathDocument
    {
        public string? Path { get; set; }

        public string? Kinds { get; set; }

        public bool Nullable { get; set; }
    }
}
