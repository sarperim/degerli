using System.Text.Json;
using Degerli.SourceProbes.Shapes;

namespace Degerli.SourceProbes.Contracts;

/// <summary>
/// Reads and writes the checked-in contract files (<c>tools/source-probes/contracts/*.json</c>).
/// The format is deliberately flat and sorted so a re-capture shows up as a small, reviewable
/// diff rather than a wall of nested JSON. Values never appear — only paths and kinds.
/// </summary>
public static class ProbeContractJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Serializes <paramref name="contract"/> to the checked-in file form.</summary>
    public static string Write(ProbeContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var document = new ContractDocument
        {
            Source = contract.Source,
            Endpoint = contract.Endpoint,
            Double = new DoubleDocument
            {
                CatalogEntry = contract.Double.CatalogEntry,
                File = contract.Double.File,
                Provenance = contract.Double.Provenance.ToString().ToLowerInvariant(),
                Note = contract.Double.Note,
            },
            DerivedAt = contract.DerivedAt,
            ExpectedStatus = contract.ExpectedStatus,
            AllowExtraUnder = contract.AllowExtraUnder.ToList(),
            Paths = contract.Shape.Paths
                .Select(p => new PathDocument
                {
                    Path = p.Path,
                    Kinds = ShapeKindsText.Render(p.Kinds),
                    Nullable = p.Nullable,
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(document, WriteOptions) + "\n";
    }

    /// <summary>Parses a checked-in contract file.</summary>
    /// <exception cref="ContractFormatException">The file is not a well-formed contract.</exception>
    public static ProbeContract Read(string json)
    {
        ContractDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ContractDocument>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new ContractFormatException($"The contract is not JSON ({ex.Message}).", ex);
        }

        if (document is null || document.Double is null || document.Paths is null || document.Paths.Count == 0)
        {
            throw new ContractFormatException("The contract is missing its source, double or paths.");
        }

        try
        {
            return new ProbeContract
            {
                Source = document.Source ?? throw new ContractFormatException("The contract has no source."),
                Endpoint = document.Endpoint ?? string.Empty,
                Double = new DoubleReference(
                    document.Double.CatalogEntry ?? string.Empty,
                    document.Double.File ?? string.Empty,
                    string.Equals(document.Double.Provenance, "captured", StringComparison.OrdinalIgnoreCase)
                        ? DoubleProvenance.Captured
                        : DoubleProvenance.Constructed,
                    document.Double.Note ?? string.Empty),
                DerivedAt = document.DerivedAt ?? string.Empty,
                ExpectedStatus = document.ExpectedStatus,
                AllowExtraUnder = document.AllowExtraUnder ?? [],
                Shape = new ShapeSpec(document.Paths.Select(p => new ShapePath(
                    p.Path ?? throw new ContractFormatException("A path entry has no path."),
                    ShapeKindsText.Parse(p.Kinds ?? "none"),
                    p.Nullable))),
            };
        }
        catch (FormatException ex)
        {
            throw new ContractFormatException($"The contract carries an unreadable shape ({ex.Message}).", ex);
        }
    }

    private sealed class ContractDocument
    {
        public string? Source { get; set; }

        public string? Endpoint { get; set; }

        public DoubleDocument? Double { get; set; }

        public string? DerivedAt { get; set; }

        public int ExpectedStatus { get; set; }

        public List<string>? AllowExtraUnder { get; set; }

        public List<PathDocument>? Paths { get; set; }
    }

    private sealed class DoubleDocument
    {
        public string? CatalogEntry { get; set; }

        public string? File { get; set; }

        public string? Provenance { get; set; }

        public string? Note { get; set; }
    }

    private sealed class PathDocument
    {
        public string? Path { get; set; }

        public string? Kinds { get; set; }

        public bool Nullable { get; set; }
    }
}

/// <summary>A contract file could not be read.</summary>
public sealed class ContractFormatException : Exception
{
    /// <summary>Creates the exception with a human-readable <paramref name="message"/>.</summary>
    public ContractFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception wrapping a parser failure.</summary>
    public ContractFormatException(string message, Exception inner) : base(message, inner)
    {
    }
}
