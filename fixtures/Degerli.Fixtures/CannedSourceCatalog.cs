using System.Text.Json;

namespace Degerli.Fixtures;

/// <summary>
/// One named canned-source payload (FU §10). <see cref="DefaultPath"/> is the source-double
/// route the payload is stubbed at by default; callers may override it with the route their
/// adapter actually calls. <see cref="DelayMs"/> drives the slow-source fixture.
/// </summary>
public sealed record CannedSourcePayload(
    string Name,
    string Source,
    string Method,
    string DefaultPath,
    int StatusCode,
    int DelayMs,
    string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// The named canned-source payload catalog (FU §10; test strategy §7). Payload bodies are
/// generated from the single <see cref="FixtureUniverse"/> definition at build time, so the
/// canned responses always agree with the seeded facts (same instruments, dates, values) —
/// there is no second, hand-maintained copy of the data. Every validation/quarantine/error
/// class the ingestion adapters must handle (TKT-mdf-002 and friends) has exactly one named
/// payload here.
/// </summary>
public static class CannedSourceCatalog
{
    // FU §10 — the named payload catalog.
    public const string PricesOk = "prices-ok";
    public const string PricesOkTminus1 = "prices-ok-tminus1";
    public const string PricesInvalidNegativeClose = "prices-invalid-negative-close";
    public const string PricesMissingProvenance = "prices-missing-provenance";
    public const string PricesUnparseable = "prices-unparseable";
    public const string PricesConflictingValue = "prices-conflicting-value";
    public const string PricesSourceDown = "prices-source-down";
    public const string PricesSourceSlow = "prices-source-slow";
    public const string PricesBackfillSourceLimited = "prices-backfill-source-limited";
    public const string PricesVariantThreshold = "prices-variant-threshold";
    public const string StatementsOkAlfaFy2025 = "statements-ok-alfa-fy2025";
    public const string StatementsRestatedRest = "statements-restated-rest";
    public const string StatementsNoCfPart = "statements-no-cf-part";
    public const string DividendsOk = "dividends-ok";
    public const string CorporateActionsOk = "corporate-actions-ok";
    public const string DisclosuresOk = "disclosures-ok";
    public const string UniverseAddRemove = "universe-add-remove";
    public const string UniverseMissingSector = "universe-missing-sector";
    public const string IndexLevelsOk = "index-levels-ok";
    public const string MacroOk = "macro-ok";
    public const string MacroIndepCpiAbsent = "macro-indep-cpi-absent";
    public const string TefasTef0001 = "tefas-tef0001";
    public const string TefasTef0002 = "tefas-tef0002";
    public const string TefasTef0003 = "tefas-tef0003";
    public const string EvrenDraftOk = "evren-draft-ok";

    /// <summary>
    /// The price source's real history limit for the backfill fixture (TKT-mdf-007): the
    /// canned history begins here, well short of a 10Y target, so the achieved-depth
    /// recording is exercised (BR-MDF-006, TC-MDF-016).
    /// </summary>
    public static readonly DateOnly BackfillSourceLimit = new(2021, 1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Every catalog name, in catalog order (FU §10).</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        PricesOk,
        PricesOkTminus1,
        PricesInvalidNegativeClose,
        PricesMissingProvenance,
        PricesUnparseable,
        PricesConflictingValue,
        PricesSourceDown,
        PricesSourceSlow,
        PricesBackfillSourceLimited,
        PricesVariantThreshold,
        StatementsOkAlfaFy2025,
        StatementsRestatedRest,
        StatementsNoCfPart,
        DividendsOk,
        CorporateActionsOk,
        DisclosuresOk,
        UniverseAddRemove,
        UniverseMissingSector,
        IndexLevelsOk,
        MacroOk,
        MacroIndepCpiAbsent,
        TefasTef0001,
        TefasTef0002,
        TefasTef0003,
        EvrenDraftOk,
    ];

    /// <summary>Builds every named payload for the given fixture set.</summary>
    public static IReadOnlyList<CannedSourcePayload> Build(FixtureSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var byName = new Dictionary<string, CannedSourcePayload>(StringComparer.Ordinal)
        {
            [PricesOk] = Payload(PricesOk, "isbank", BuildPricesBody(set, set.Anchor.T)),
            [PricesOkTminus1] = Payload(PricesOkTminus1, "isbank", BuildPricesBody(set, set.Anchor.TradingDay(-1))),
            [PricesInvalidNegativeClose] = Payload(
                PricesInvalidNegativeClose,
                "isbank",
                BuildPricesBody(set, set.Anchor.T, closeOverrides: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["ALFA"] = -5m })),
            [PricesMissingProvenance] = Payload(PricesMissingProvenance, "isbank", BuildPricesBody(set, set.Anchor.T, includeProvenance: false)),
            [PricesConflictingValue] = Payload(
                PricesConflictingValue,
                "isbank",
                BuildPricesBody(set, set.Anchor.T, closeOverrides: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["ALFA"] = 21.00m })),
            [PricesSourceDown] = Payload(PricesSourceDown, "isbank", Serialize(new { error = "source unavailable" }), statusCode: 500),
            [PricesSourceSlow] = Payload(PricesSourceSlow, "isbank", BuildPricesBody(set, set.Anchor.T), delayMs: 5_000),
            [PricesBackfillSourceLimited] = Payload(PricesBackfillSourceLimited, "isbank", BuildBackfillHistoryBody(set)),
            [StatementsOkAlfaFy2025] = Payload(StatementsOkAlfaFy2025, "kap", BuildStatementsBody(set, "ALFA")),
            [StatementsRestatedRest] = Payload(StatementsRestatedRest, "kap", BuildStatementsBody(set, "REST")),
            [StatementsNoCfPart] = Payload(StatementsNoCfPart, "kap", BuildStatementsBody(set, "PART")),
            [DividendsOk] = Payload(DividendsOk, "kap", BuildDividendsBody(set)),
            [CorporateActionsOk] = Payload(CorporateActionsOk, "kap", BuildCorporateActionsBody(set)),
            [DisclosuresOk] = Payload(DisclosuresOk, "kap", BuildDisclosuresBody(set)),
            [UniverseAddRemove] = Payload(UniverseAddRemove, "kap", BuildUniverseChangeSetBody(set)),
            [UniverseMissingSector] = Payload(UniverseMissingSector, "kap", BuildUniverseBody(set)),
            [IndexLevelsOk] = Payload(IndexLevelsOk, "kap", BuildIndexLevelsBody(set)),
            [MacroOk] = Payload(MacroOk, "macro", BuildMacroBody(set, includeIndependentCpi: true)),
            [MacroIndepCpiAbsent] = Payload(MacroIndepCpiAbsent, "macro", BuildMacroBody(set, includeIndependentCpi: false)),
            [TefasTef0001] = Payload(TefasTef0001, "tefas", BuildFundBody(set, "TEF0001")),
            [TefasTef0002] = Payload(TefasTef0002, "tefas", BuildFundBody(set, "TEF0002")),
            [TefasTef0003] = Payload(TefasTef0003, "tefas", BuildFundBody(set, "TEF0003")),
            [EvrenDraftOk] = Payload(EvrenDraftOk, "evren", BuildDescriptionsBody(set)),
        };

        // prices-unparseable is deliberately not valid JSON: a truncated copy of the ok body.
        var okBody = byName[PricesOk].Body;
        byName[PricesUnparseable] = Payload(
            PricesUnparseable,
            "isbank",
            okBody[..Math.Min(okBody.Length, 200)]);

        // prices-variant-threshold is derived from the universe with overridden volumes: it
        // presents 12 eligible gainers, exactly one sitting on the 1,000,000 boundary, with
        // LAMDA retained as the illiquid big gainer that the threshold must exclude.
        byName[PricesVariantThreshold] = Payload(PricesVariantThreshold, "isbank", BuildVariantThresholdBody(set));

        return Names.Select(name => byName[name]).ToList();
    }

    /// <summary>Loads one payload by name; throws <see cref="KeyNotFoundException"/> when unknown.</summary>
    public static CannedSourcePayload Get(FixtureSet set, string name)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var payload = Build(set).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        return payload ?? throw new KeyNotFoundException($"No canned-source payload named '{name}' (FU §10).");
    }

    /// <summary>Attempts to load one payload by name.</summary>
    public static bool TryGet(FixtureSet set, string name, out CannedSourcePayload payload)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        payload = Build(set).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))!;
        return payload is not null;
    }

    private static CannedSourcePayload Payload(
        string name,
        string source,
        string body,
        int statusCode = 200,
        int delayMs = 0) =>
        new(name, source, "GET", $"/canned-sources/{name}", statusCode, delayMs, body);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string BuildPricesBody(
        FixtureSet set,
        DateOnly date,
        IReadOnlyDictionary<string, decimal>? closeOverrides = null,
        bool includeProvenance = true)
    {
        var items = set.Prices
            .Where(p => p.Date == date)
            .OrderBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(p =>
            {
                var close = closeOverrides is not null && closeOverrides.TryGetValue(p.Symbol, out var overridden)
                    ? overridden
                    : p.CloseRaw;
                var previousClose = set.Prices
                    .Where(q => q.Symbol == p.Symbol && q.Date < date)
                    .OrderByDescending(q => q.Date)
                    .First()
                    .CloseRaw;
                return new PriceItem(p.Symbol, previousClose, p.Open, p.High, p.Low, close, p.Volume);
            })
            .ToList();

        return includeProvenance
            ? Serialize(new { sourceRef = $"isbank://eod/{date:yyyy-MM-dd}", date, items })
            : Serialize(new { date, items });
    }

    /// <summary>
    /// The backfill history payload (TKT-mdf-007): a sparse, dated range per instrument
    /// that begins at the source's real limit (<see cref="BackfillSourceLimit"/>) and runs
    /// to the anchor day. The source holds no earlier history, so a 10Y target cannot be
    /// reached — the fixture for TC-MDF-016.
    /// </summary>
    private static string BuildBackfillHistoryBody(FixtureSet set)
    {
        var latest = set.Prices
            .Where(p => p.Date == set.Anchor.T)
            .OrderBy(p => p.Symbol, StringComparer.Ordinal)
            .ToList();

        var rows = new List<HistoryItem>();
        foreach (var price in latest)
        {
            for (var year = BackfillSourceLimit.Year; year <= set.Anchor.T.Year; year++)
            {
                rows.Add(new HistoryItem(
                    price.Symbol,
                    new DateOnly(year, 1, 1),
                    price.Open,
                    price.High,
                    price.Low,
                    price.CloseRaw,
                    price.Volume));
            }

            // The anchor day closes the range so the recorded depth ends at T.
            rows.Add(new HistoryItem(price.Symbol, set.Anchor.T, price.Open, price.High, price.Low, price.CloseRaw, price.Volume));
        }

        return Serialize(new { sourceRef = "isbank://eod/history", rows });
    }

    private static string BuildVariantThresholdBody(FixtureSet set)
    {
        // Start from the T universe, then force BETA/GAMA/IOTA into gainers so the payload
        // carries a broader mover set; ALFA's volume is pinned to the exact 1,000,000
        // threshold while LAMDA keeps its illiquid 500,000 volume (must be excluded).
        var forcedGainers = new HashSet<string>(StringComparer.Ordinal) { "BETA", "GAMA", "IOTA" };
        var items = set.Prices
            .Where(p => p.Date == set.Anchor.T)
            .OrderBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(p =>
            {
                var previousClose = set.Prices
                    .Where(q => q.Symbol == p.Symbol && q.Date < set.Anchor.T)
                    .OrderByDescending(q => q.Date)
                    .First()
                    .CloseRaw;
                var close = forcedGainers.Contains(p.Symbol) ? p.CloseRaw + 0.50m : p.CloseRaw;
                var volume = p.Symbol == "ALFA" ? 1_000_000L : p.Volume;
                return new PriceItem(p.Symbol, previousClose, p.Open, p.High, p.Low, close, volume);
            })
            .ToList();

        return Serialize(new { sourceRef = $"isbank://eod/{set.Anchor.T:yyyy-MM-dd}/variant-threshold", date = set.Anchor.T, items });
    }

    private static string BuildStatementsBody(FixtureSet set, string symbol)
    {
        // The ingest payload carries the latest fiscal year (FY + its quarters); the
        // deep-history rows are a separate backfill concern (FU §5/§5.1).
        var instrumentStatements = set.Statements.Where(s => s.Symbol == symbol).ToList();
        var latestFiscalYear = instrumentStatements.Max(s => s.FiscalYear);
        var statements = instrumentStatements
            .Where(s => s.FiscalYear == latestFiscalYear)
            .Select(s => new StatementItem(
                s.PeriodType,
                s.PeriodEndDate,
                s.FiscalYear,
                s.StatementType,
                s.Version,
                s.RestatementDate,
                s.PublishedAt,
                new SortedDictionary<string, decimal>(
                    s.Lines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal)))
            .ToList();

        return Serialize(new { sourceRef = $"kap://statements/{symbol}", symbol, statements });
    }

    private static string BuildDividendsBody(FixtureSet set) =>
        Serialize(new
        {
            sourceRef = "kap://dividends",
            dividends = set.Dividends
                .Select(d => new DividendItem(d.Symbol, d.ExDate, d.PayDate, d.AmountPerShare, d.Currency))
                .ToList(),
        });

    private static string BuildCorporateActionsBody(FixtureSet set) =>
        Serialize(new
        {
            sourceRef = "kap://corporate-actions",
            actions = set.CorporateActions
                .Select(a => new CorporateActionItem(a.Symbol, a.ActionType, a.ActionDate, ParseJson(a.TermsJson)))
                .ToList(),
        });

    private static string BuildDisclosuresBody(FixtureSet set) =>
        Serialize(new
        {
            sourceRef = "kap://disclosures",
            disclosures = set.KapDisclosures
                .Select(k => new DisclosureItem(k.Id, k.Symbol, k.DisclosureType, k.PublishDate, k.Title, k.SourceUrl))
                .ToList(),
        });

    private static string BuildUniverseBody(FixtureSet set) =>
        Serialize(new
        {
            sourceRef = "kap://universe",
            // Reference data the universe-sync job upserts before resolving links
            // (FR-MDF-006): sectors (bilingual labels, flat in FU §3) and indices.
            sectors = set.Sectors
                .Select(s => new UniverseSectorItem(s.Code, s.NameTr, s.NameEn, null))
                .ToList(),
            indices = set.Indices
                .Select(i => new UniverseIndexItem(i.Code, i.NameTr, i.NameEn))
                .ToList(),
            instruments = set.Instruments
                .Select(i => new UniverseInstrumentItem(i.Symbol, i.Name, i.SectorCode, i.ListingDate))
                .ToList(),
        });

    private static string BuildUniverseChangeSetBody(FixtureSet set)
    {
        var effectiveDate = set.Anchor.T.AddDays(1);
        var sigma = new UniverseInstrumentItem("SIGMA", "Sigma Enerji A.Ş.", "B", effectiveDate);
        return Serialize(new
        {
            sourceRef = "kap://universe/changes",
            effectiveDate,
            // The change set may also carry reference data so a cold database can
            // resolve its membership changes (idempotent on a warm one).
            sectors = set.Sectors
                .Select(s => new UniverseSectorItem(s.Code, s.NameTr, s.NameEn, null))
                .ToList(),
            indices = set.Indices
                .Select(i => new UniverseIndexItem(i.Code, i.NameTr, i.NameEn))
                .ToList(),
            instruments = new[] { sigma },
            add = new[] { new MembershipChangeItem("XU100", "SIGMA") },
            remove = new[] { new MembershipChangeItem("XU100", "NEWP") },
        });
    }

    private static string BuildIndexLevelsBody(FixtureSet set) =>
        Serialize(new
        {
            sourceRef = "kap://index-levels",
            levels = set.IndexLevels
                .Where(l => l.Date == set.Anchor.T)
                .OrderBy(l => l.IndexCode, StringComparer.Ordinal)
                .Select(l => new IndexLevelItem(l.IndexCode, l.Date, l.Close))
                .ToList(),
        });

    private static readonly Dictionary<string, string> MacroUnits = new(StringComparer.Ordinal)
    {
        ["TUIK_CPI"] = "% YoY",
        ["INDEP_CPI"] = "% YoY",
        ["CBRT_REPO"] = "%",
        ["USD_TRY"] = "TRY",
        ["EUR_TRY"] = "TRY",
        ["GOLD"] = "USD/oz",
    };

    private static string BuildMacroBody(FixtureSet set, bool includeIndependentCpi)
    {
        var series = set.MacroValues
            .Where(v => includeIndependentCpi || v.SeriesCode != "INDEP_CPI")
            .Select(v => new MacroItem(v.SeriesCode, v.ValueDate, v.Value, MacroUnits[v.SeriesCode], v.RecordedAt))
            .ToList();

        return Serialize(new { sourceRef = "macro://series", series });
    }

    private static string BuildFundBody(FixtureSet set, string fundCode)
    {
        var fund = set.Funds.Single(f => f.Code == fundCode);
        return Serialize(new
        {
            sourceRef = $"tefas://funds/{fundCode}",
            fund = new FundItem(fund.Code, fund.Name, fund.FundType),
            navs = set.FundNavs
                .Where(n => n.FundCode == fundCode)
                .OrderBy(n => n.NavDate)
                .Select(n => new FundNavItem(n.NavDate, n.NavValue))
                .ToList(),
            performance = set.FundPerformances
                .Where(p => p.FundCode == fundCode)
                .Select(p => new FundPerformanceItem(p.Period, p.AsOfDate, p.ReturnValue))
                .ToList(),
            holdings = set.FundHoldings
                .Where(h => h.FundCode == fundCode)
                .OrderBy(h => h.LineNo)
                .Select(h => new FundHoldingItem(h.LineNo, h.Symbol, h.NameRaw, h.Weight, h.Units))
                .ToList(),
        });
    }

    private static string BuildDescriptionsBody(FixtureSet set) =>
        Serialize(new
        {
            sourceRef = "evren://drafts",
            descriptions = set.BusinessDescriptions
                .Select(d => new DescriptionItem(d.Symbol, d.Version, d.Status, d.TextTr, d.TextEn, d.SourceRefs))
                .ToList(),
        });

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed record PriceItem(
        string Symbol,
        decimal PreviousClose,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume);

    private sealed record HistoryItem(
        string Symbol,
        DateOnly Date,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume);

    private sealed record StatementItem(
        string PeriodType,
        DateOnly PeriodEndDate,
        int FiscalYear,
        string StatementType,
        string Version,
        DateOnly? RestatementDate,
        DateOnly? PublishedAt,
        IReadOnlyDictionary<string, decimal> Lines);

    private sealed record DividendItem(
        string Symbol,
        DateOnly ExDate,
        DateOnly? PayDate,
        decimal AmountPerShare,
        string Currency);

    private sealed record CorporateActionItem(
        string Symbol,
        string ActionType,
        DateOnly ActionDate,
        JsonElement Terms);

    private sealed record DisclosureItem(
        long Id,
        string Symbol,
        string DisclosureType,
        DateOnly PublishDate,
        string Title,
        string SourceUrl);

    private sealed record UniverseSectorItem(
        string Code,
        string NameTr,
        string NameEn,
        string? ParentCode);

    private sealed record UniverseIndexItem(string Code, string NameTr, string NameEn);

    private sealed record UniverseInstrumentItem(
        string Symbol,
        string Name,
        string? SectorCode,
        DateOnly ListingDate);

    private sealed record MembershipChangeItem(string IndexCode, string Symbol);

    private sealed record IndexLevelItem(string IndexCode, DateOnly Date, decimal Close);

    private sealed record MacroItem(
        string Code,
        DateOnly ValueDate,
        decimal Value,
        string Unit,
        DateTimeOffset RecordedAt);

    private sealed record FundItem(string Code, string Name, string? Type);

    private sealed record FundNavItem(DateOnly Date, decimal Value);

    private sealed record FundPerformanceItem(string Period, DateOnly AsOfDate, decimal? ReturnValue);

    private sealed record FundHoldingItem(
        int LineNo,
        string? Symbol,
        string? NameRaw,
        decimal? Weight,
        decimal? Units);

    private sealed record DescriptionItem(
        string Symbol,
        int Version,
        string Status,
        string? TextTr,
        string? TextEn,
        IReadOnlyList<long> SourceRefs);
}
