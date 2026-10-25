using Degerli.SourceProbes.Contracts;
using Degerli.SourceProbes.Policy;
using Degerli.SourceProbes.Probing;
using Degerli.SourceProbes.Reporting;
using Degerli.SourceProbes.Shapes;

namespace Degerli.SourceProbes;

/// <summary>
/// The operator-facing entry point (TKT-foundation-068). Default mode is <b>offline</b>: an
/// argument is required to contact anything, and the live mode is additionally refused inside
/// a scheduled chain window unless forced (<see cref="ProbePolicy"/>).
/// </summary>
/// <remarks>
/// This executable is <b>never</b> invoked by CI. It has no CI entrypoint, no workflow file
/// references it, and the L1 suite proves that mechanically
/// (<c>AC68ii_ProbeHarness_IsNotWiredIntoCi</c>, acceptance (ii)).
/// </remarks>
public static class Program
{
    private const int ExitClean = 0;
    private const int ExitUsage = 1;
    private const int ExitDrift = 2;
    private const int ExitUnreachable = 3;

    /// <summary>Runs the CLI.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? ExitUsage : ExitClean;
        }

        try
        {
            return args[0] switch
            {
                "list" => RunList(),
                "show" => RunShow(args),
                "check" => RunCheck(args),
                "shape" => RunShape(args),
                "probe" => await RunProbeAsync(args).ConfigureAwait(false),
                _ => Unknown(args[0]),
            };
        }
        catch (LiveProbeRefusedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitUsage;
        }
        catch (ContractFormatException ex)
        {
            Console.Error.WriteLine($"contract error: {ex.Message}");
            return ExitUsage;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"i/o error: {ex.Message}");
            return ExitUsage;
        }
    }

    private static string Usage =>
        """
        Degerli source-contract probes — report-only shape-drift detection (TKT-foundation-068).

        Compares the STRUCTURE of a source response (paths, kinds, nullability, array nesting)
        against the committed double's shape. Values are never compared and never retained.
        This tool is never a CI gate and never edits a double: it writes a drift report.

        Usage:
          list                                  List the coverage set (offline).
          show   --source <key>                 Print a contract's expected shape (offline).
          check  --source <key> --body <file>   Compare a saved body against its double (offline).
          shape  --body <file>                  Print the shape of a body, contract-formatted (offline).
          probe  --source <key>|--all --live    One paced live request per source (explicit opt-in).

        Options:
          --contracts <dir>   Contract directory (default: tools/source-probes/contracts).
          --out <dir>         Report directory (default: <system temp>/degerli-source-probes).
          --force             Allow a live probe inside a scheduled chain window (emergency only).
          --symbol <ticker>   Override the {symbol} placeholder (default ASELS).
        """;

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command '{command}'.");
        Console.Error.WriteLine(Usage);
        return ExitUsage;
    }

    private static int RunList()
    {
        Console.WriteLine("source                          ref        method  pacing   double");
        foreach (var probe in SourceProbeRegistry.All)
        {
            Console.WriteLine(
                $"{probe.Key,-31} {probe.SourceRef,-10} {probe.Method,-7} {probe.EffectivePacingMs + " ms",-8} {probe.DoubleCatalogEntry}");
            if (!probe.HasEndpoint)
            {
                Console.WriteLine($"{"",-31} {probe.UnconfiguredReason}");
            }
        }

        return ExitClean;
    }

    private static int RunShow(string[] args)
    {
        var source = Option(args, "--source") ?? throw new ContractFormatException("--source is required.");
        var probe = SourceProbeRegistry.Find(source)
            ?? throw new ContractFormatException($"unknown source '{source}'.");
        var contract = LoadContract(probe, args);

        Console.WriteLine($"# {probe.Key} — {probe.DisplayName}");
        Console.WriteLine($"# endpoint: {contract.Endpoint}");
        Console.WriteLine($"# double:   {contract.Double.CatalogEntry} ({contract.Double.Provenance}, {contract.DerivedAt})");
        Console.WriteLine($"# expected status: {contract.ExpectedStatus}");
        foreach (var path in contract.Shape.Paths)
        {
            Console.WriteLine($"{path.Path}\t{ShapeKindsText.Render(path.Kinds)}\t{(path.Nullable ? "nullable" : string.Empty)}");
        }

        return ExitClean;
    }

    private static int RunShape(string[] args)
    {
        var bodyPath = Option(args, "--body") ?? throw new ContractFormatException("--body is required.");
        var body = File.ReadAllText(bodyPath);
        var spec = JsonShapeExtractor.Extract(body);
        foreach (var path in spec.Paths)
        {
            Console.WriteLine($"{path.Path}\t{ShapeKindsText.Render(path.Kinds)}\t{(path.Nullable ? "nullable" : string.Empty)}");
        }

        Console.WriteLine();
        Console.WriteLine("# Copy the entries above into the source's contract file by hand:");
        Console.WriteLine("# this tool never rewrites a contract (a double is a contract artifact, 01 §10.9.7).");
        return ExitClean;
    }

    private static int RunCheck(string[] args)
    {
        var source = Option(args, "--source") ?? throw new ContractFormatException("--source is required.");
        var bodyPath = Option(args, "--body") ?? throw new ContractFormatException("--body is required.");
        var probe = SourceProbeRegistry.Find(source)
            ?? throw new ContractFormatException($"unknown source '{source}'.");
        var contract = LoadContract(probe, args);

        var result = CompareBody(
            probe,
            contract,
            File.ReadAllText(bodyPath),
            ProbeMode.SavedBody,
            statusCode: null,
            detail: $"compared against {contract.Double.CatalogEntry} (saved body: {Path.GetFileName(bodyPath)})",
            DateTimeOffset.UtcNow);

        var report = new DriftReport("saved-body", DateTimeOffset.UtcNow, [result]);
        Console.Write(report.ToText());
        WriteReport(args, report, "saved-body");
        return ExitCodeFor(report);
    }

    private static async Task<int> RunProbeAsync(string[] args)
    {
        var live = args.Contains("--live", StringComparer.Ordinal);
        ProbePolicy.EnsureLiveRequested(live);

        var force = args.Contains("--force", StringComparer.Ordinal);
        ProbePolicy.EnsureNotInChainWindow(DateTimeOffset.UtcNow, force);

        var requested = args.Contains("--all", StringComparer.Ordinal)
            ? SourceProbeRegistry.All
            : [(SourceProbeRegistry.Find(Option(args, "--source") ?? throw new ContractFormatException("--source or --all is required."))
                ?? throw new ContractFormatException("unknown source."))];

        var executor = new LiveProbeExecutor(symbolOverride: Option(args, "--symbol"));
        var results = new List<ProbeResult>(requested.Count);
        foreach (var probe in requested)
        {
            var contract = LoadContract(probe, args);
            Console.WriteLine($"[probe] {probe.Key} (pacing ≥ {probe.EffectivePacingMs} ms) …");
            results.Add(await executor
                .ProbeAsync(probe, contract, DateTimeOffset.UtcNow)
                .ConfigureAwait(false));
            await Task.Delay(probe.EffectivePacingMs).ConfigureAwait(false);
        }

        var report = new DriftReport("live", DateTimeOffset.UtcNow, results);
        Console.Write(report.ToText());
        WriteReport(args, report, "live");
        return ExitCodeFor(report);
    }

    private static ProbeResult CompareBody(
        SourceProbe probe,
        ProbeContract contract,
        string body,
        ProbeMode mode,
        int? statusCode,
        string detail,
        DateTimeOffset checkedAt) =>
        ProbeRunner.Evaluate(probe.Key, contract.Endpoint, contract, body, mode, statusCode, detail, checkedAt);

    private static ProbeContract LoadContract(SourceProbe probe, string[] args)
    {
        var directory = Option(args, "--contracts") ?? DefaultContractsDirectory();
        var file = Path.Combine(directory, probe.ContractFileName);
        if (!File.Exists(file))
        {
            throw new ContractFormatException($"no contract at {file}");
        }

        return ProbeContractJson.Read(File.ReadAllText(file));
    }

    private static string DefaultContractsDirectory()
    {
        var root = RepoRoot();
        return root is null
            ? "contracts"
            : Path.Combine(root, "tools", "source-probes", "contracts");
    }

    /// <summary>The repository root, found by walking up for the solution file.</summary>
    internal static string? RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void WriteReport(string[] args, DriftReport report, string mode)
    {
        var directory = Option(args, "--out") ?? Path.Combine(Path.GetTempPath(), "degerli-source-probes");
        var full = Path.GetFullPath(directory);
        var root = RepoRoot();
        if (root is not null && full.StartsWith(root, StringComparison.Ordinal))
        {
            // A report carries hostnames and structural excerpts; it is an operator artifact,
            // never a repository file (acceptance iii).
            throw new IOException(
                $"refusing to write a drift report inside the repository ({full}); pass --out outside it.");
        }

        Directory.CreateDirectory(full);
        var stamp = report.GeneratedAt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var path = Path.Combine(full, $"{stamp}-{mode}.json");
        File.WriteAllText(path, report.ToJson());
        Console.WriteLine($"report: {path}");
    }

    private static int ExitCodeFor(DriftReport report)
    {
        if (report.DriftCount > 0)
        {
            return ExitDrift;
        }

        return report.UnreachableCount > 0 ? ExitUnreachable : ExitClean;
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
