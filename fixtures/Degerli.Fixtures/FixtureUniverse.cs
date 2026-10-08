namespace Degerli.Fixtures;

/// <summary>
/// The single definition of the fixture universe (FU `.pipeline/testing/01-fixture-universe.md`)
/// resolved against a <see cref="FixtureAnchor"/>. Both the L2 absolute-date variant
/// and the L4 now-anchored variant are produced here — there is no second data path.
/// </summary>
public static class FixtureUniverse
{
    // FU §3 — sectors (bilingual labels per 02 §3.1).
    private static readonly FixtureSector[] SectorRows =
    [
        new("A", "Teknoloji", "Technology"),
        new("B", "Sanayi", "Industry"),
        new("C", "Gıda", "Food"),
    ];

    private static readonly FixtureIndex[] IndexRows =
    [
        new("XU100", "BIST 100", "BIST 100"),
        new("XU30", "BIST 30", "BIST 30"),
    ];

    // FU §3 — 16 instruments with their archetypes (listing dates are historical facts).
    private static readonly FixtureInstrument[] InstrumentRows =
    [
        // shares_diluted is expressed in millions (FU §3 header), matching the TRY-M
        // monetary convention of §5 so EPS = NI / shares is per-share and mcap = price × shares is TRY M.
        new("ALFA", "Alfa Teknoloji A.Ş.", "A", new DateOnly(2010, 1, 4), 100, "STD"),
        new("BETA", "Beta Yazılım A.Ş.", "A", new DateOnly(2012, 5, 10), 80, "LOSS-A"),
        new("GAMA", "Gama Bilişim A.Ş.", "A", new DateOnly(2015, 9, 1), 60, "LOSS-B"),
        new("DELTA", "Delta Elektronik A.Ş.", "A", new DateOnly(2011, 3, 15), 100, "NEGEQ"),
        new("REST", "Rest Telekom A.Ş.", "A", new DateOnly(2008, 11, 20), 50, "REST"),
        new("EPSL", "Epsl Sanayi A.Ş.", "B", new DateOnly(2009, 4, 1), 400, "ACT"),
        new("ZETA", "Zeta İmalat A.Ş.", "B", new DateOnly(2024, 3, 15), 50, "IPO"),
        new("ETA", "Eta Enerji A.Ş.", "B", new DateOnly(2013, 6, 3), 90, "MOVER-G"),
        new("THETA", "Theta İnşaat A.Ş.", "B", new DateOnly(2010, 2, 8), 70, "MOVER-L"),
        new("PART", "Part Holding A.Ş.", "B", new DateOnly(2005, 8, 15), 120, "PART"),
        new("NEWP", "Yeni Gelişim A.Ş.", "B", new DateOnly(2025, 8, 1), 30, "1FY"),
        new("IOTA", "Iota Gıda A.Ş.", "C", new DateOnly(2014, 10, 9), 200, "NEGEBITDA"),
        new("KAPPA", "Kappa İçecek A.Ş.", "C", new DateOnly(2007, 12, 3), 150, "NEGFCF"),
        new("LAMDA", "Lamda Tarım A.Ş.", "C", new DateOnly(2016, 4, 12), 110, "NODIV"),
        new("NU", "Nu Perakende A.Ş.", "C", new DateOnly(2003, 1, 6), 90, "HIGHPAY"),
        new("UNSEC", "Unsur Ticaret A.Ş.", null, new DateOnly(2011, 7, 19), 70, "UNSEC"),
    ];

    // XU30 subset (FU §3).
    private static readonly HashSet<string> Xu30Members =
        new(StringComparer.Ordinal) { "ALFA", "EPSL", "KAPPA", "REST", "ETA" };

    // FU §4 — close T−1 / close T / volume T (per instrument).
    private static readonly Dictionary<string, (decimal CloseT1, decimal CloseT, long VolumeT)> PriceRows =
        new(StringComparer.Ordinal)
        {
            ["ALFA"] = (19.80m, 20.00m, 12_000_000),
            ["BETA"] = (10.20m, 10.00m, 5_000_000),
            ["GAMA"] = (8.10m, 8.00m, 4_000_000),
            ["DELTA"] = (9.95m, 10.00m, 6_000_000),
            ["REST"] = (14.82m, 15.00m, 3_000_000),
            ["EPSL"] = (19.61m, 20.00m, 8_000_000),
            ["ZETA"] = (40.00m, 41.20m, 7_000_000),
            ["ETA"] = (20.00m, 22.00m, 50_000_000),
            ["THETA"] = (20.00m, 18.00m, 40_000_000),
            ["PART"] = (24.63m, 25.00m, 5_000_000),
            ["NEWP"] = (50.00m, 52.00m, 9_000_000),
            ["IOTA"] = (5.15m, 5.00m, 10_000_000),
            ["KAPPA"] = (40.00m, 41.00m, 6_000_000),
            ["LAMDA"] = (10.00m, 12.50m, 500_000),
            ["NU"] = (30.00m, 30.00m, 7_000_000),
            ["UNSEC"] = (9.04m, 9.00m, 2_000_000),
        };

    /// <summary>Builds the complete fixture set for <paramref name="anchor"/>.</summary>
    public static FixtureSet Build(FixtureAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        // FU §5/§5.1 — the statement set is an absolute structural fact pinned to
        // FY2025 (the tabulated series and the ALFA deep-history years); it is NOT
        // shifted by the anchor. Only "fresh" rows — prices, index levels, macro,
        // funds and saved-content timestamps — are now-anchored (FU §1.4/§8.3).
        const int fyLatest = 2025;

        var prices = BuildPrices(anchor);
        var statements = BuildStatements(fyLatest);
        var dividends = BuildDividends(anchor);
        var macroValues = BuildMacroValues(anchor);

        var sectors = SectorRows.ToList();
        var indices = IndexRows.ToList();
        var instruments = InstrumentRows.ToList();

        var memberships = new List<FixtureMembership>();
        foreach (var instrument in instruments)
        {
            memberships.Add(new FixtureMembership("XU100", instrument.Symbol, instrument.ListingDate, null));
        }

        foreach (var instrument in instruments.Where(i => Xu30Members.Contains(i.Symbol)))
        {
            memberships.Add(new FixtureMembership("XU30", instrument.Symbol, instrument.ListingDate, null));
        }

        var indexLevels = BuildIndexLevels(anchor);

        var funds = new List<FixtureFund>
        {
            new("TEF0001", "Fon Alfa", "equity"),
            new("TEF0002", "Fon Beta", "equity-heavy mixed"),
            new("TEF0003", "Fon Gamma", "equity"),
        };

        var navDates = new[]
        {
            anchor.TradingDay(-6),
            anchor.TradingDay(-4),
            anchor.TradingDay(-3),
            anchor.TradingDay(-2),
            anchor.T,
        };
        var navValues = new[] { 10.00m, 10.10m, 10.05m, 10.20m, 10.25m };
        var fundNavs = new List<FixtureFundNav>();
        foreach (var fund in funds)
        {
            for (var i = 0; i < navDates.Length; i++)
            {
                fundNavs.Add(new FixtureFundNav(fund.Code, navDates[i], navValues[i]));
            }
        }

        var fundPerformances = new List<FixtureFundPerformance>
        {
            new("TEF0001", "1M", anchor.T, 0.025m),
            new("TEF0001", "3M", anchor.T, 0.060m),
            new("TEF0001", "1Y", anchor.T, 0.180m),
            new("TEF0002", "1M", anchor.T, 0.012m),
            new("TEF0002", "1Y", anchor.T, 0.090m),
        };

        var fundHoldings = new List<FixtureFundHolding>
        {
            new("TEF0001", anchor.T, 1, "ALFA", null, 0.05m, 250_000m),
            new("TEF0001", anchor.T, 2, "EPSL", null, 0.03m, 120_000m),
            new("TEF0001", anchor.T, 3, null, "Yabancı Hisse X", 0.02m, null),
            new("TEF0003", anchor.T, 1, null, "Hisse Y", null, null),
        };

        var accounts = new List<FixtureAccount>
        {
            new("builder@degerli.test", "FixturePass1!", true, "builder", "tr"),
            new("user-a@degerli.test", "FixturePass1!", true, "user", "tr"),
            new("user-b@degerli.test", "FixturePass1!", false, "user", "tr"),
            new("user-c@degerli.test", "FixturePass1!", true, "user", "en"),
        };

        var savedScreens = new List<FixtureSavedScreen>
        {
            new(
                "user-a@degerli.test",
                "Ekranım",
                """[{"metricCode":"pe","bound":"max","maxValue":15},{"metricCode":"roe","bound":"min","minValue":0.15}]"""),
        };

        var scenarios = new List<FixtureScenario>
        {
            new(
                "user-a@degerli.test",
                "ALFA",
                "Temel",
                """{"base_fcf":100,"growth_rate":0.10,"horizon_years":5,"terminal_growth":0.03,"discount_rate":0.15,"debt":400,"cash":200,"share_count":100}"""),
        };

        var reviewedAt = At(anchor.Calendar(-26), 9);
        var businessDescriptions = new List<FixtureBusinessDescription>
        {
            new(
                "ALFA",
                2,
                "published",
                "Alfa Teknoloji, haberleşme ve savunma elektronikleri geliştirir ve üretir.",
                "Alfa Teknoloji develops and manufactures communications and defence electronics.",
                reviewedAt,
                reviewedAt,
                [8841, 9999]),
            new(
                "REST",
                1,
                "draft",
                "Rest Telekom, sabit ve mobil telekomünikasyon hizmetleri sunar.",
                "Rest Telekom provides fixed and mobile telecommunications services.",
                null,
                null,
                [8842]),
            new(
                "ZETA",
                1,
                "draft",
                "Zeta İmalat, endüstriyel imalat yapar.",
                string.Empty,
                null,
                null,
                [8843]),
        };

        var kapDisclosures = new List<FixtureKapDisclosure>
        {
            new(
                8841,
                "ALFA",
                "annual_report",
                anchor.Calendar(-206),
                "2025 Faaliyet Raporu",
                "https://www.kap.org.tr/tr/Bildirim/8841"),
            new(8842, "REST", "financial_report", anchor.Calendar(-300), "Rest Telekom Finansal Rapor", "https://www.kap.org.tr/tr/Bildirim/8842"),
            new(8843, "ZETA", "financial_report", anchor.Calendar(-120), "Zeta İmalat Finansal Rapor", "https://www.kap.org.tr/tr/Bildirim/8843"),
        };

        var coverage = new List<FixtureCoverage>
        {
            // FU §8 — TEF0002 is the only fund whose holdings coverage gap is recorded.
            new("fund", null, "TEF0002", "holdings", null, null, "Holdings coverage gap recorded (BR-FDF-004)."),
        };

        return new FixtureSet
        {
            Anchor = anchor,
            Sectors = sectors,
            Indices = indices,
            Instruments = instruments,
            Memberships = memberships,
            Prices = prices,
            IndexLevels = indexLevels,
            Statements = statements,
            Dividends = dividends,
            CorporateActions =
            [
                new("EPSL", "split", new DateOnly(2025, 6, 2), """{"n":5}"""),
                new("EPSL", "bonus_issue", new DateOnly(2026, 3, 2), """{"b":0.1}"""),
            ],
            KapDisclosures = kapDisclosures,
            MacroValues = macroValues,
            Funds = funds,
            FundNavs = fundNavs,
            FundPerformances = fundPerformances,
            FundHoldings = fundHoldings,
            Accounts = accounts,
            SavedScreens = savedScreens,
            Scenarios = scenarios,
            BusinessDescriptions = businessDescriptions,
            Coverage = coverage,
            MacroFreshness = BuildMacroFreshness(anchor),
        };
    }

    private static List<FixturePrice> BuildPrices(FixtureAnchor anchor)
    {
        var prices = new List<FixturePrice>();
        var tradingDays = anchor.TradingDays(10); // T−9 … T
        foreach (var instrument in InstrumentRows)
        {
            var row = PriceRows[instrument.Symbol];
            for (var i = 0; i < tradingDays.Count; i++)
            {
                var isLatest = i == tradingDays.Count - 1;
                var close = isLatest ? row.CloseT : row.CloseT1;
                prices.Add(new FixturePrice(
                    instrument.Symbol,
                    tradingDays[i],
                    close,
                    Round(close * 1.01m),
                    Round(close * 0.99m),
                    close,
                    null,
                    row.VolumeT));
            }
        }

        // FU §4 — EPSL adjusted historical rows around the split/bonus actions.
        prices.Add(new FixturePrice("EPSL", new DateOnly(2025, 5, 30), 100.00m, 101.00m, 99.00m, 100.00m, 18.1818m, 1_000_000));
        prices.Add(new FixturePrice("EPSL", new DateOnly(2026, 2, 27), 22.00m, 22.22m, 21.78m, 22.00m, 20.0000m, 1_000_000));
        prices.Add(new FixturePrice("EPSL", new DateOnly(2026, 6, 2), 20.20m, 20.40m, 20.00m, 20.20m, 20.2000m, 1_000_000));

        return prices;
    }

    private static List<FixtureIndexLevel> BuildIndexLevels(FixtureAnchor anchor)
    {
        var levels = new List<FixtureIndexLevel>();
        var tradingDays = anchor.TradingDays(10);
        for (var i = 0; i < tradingDays.Count; i++)
        {
            var isLatest = i == tradingDays.Count - 1;
            levels.Add(new FixtureIndexLevel("XU100", tradingDays[i], isLatest ? 10200.00m : 10000.00m));
            levels.Add(new FixtureIndexLevel("XU30", tradingDays[i], isLatest ? 30600.00m : 30000.00m));
        }

        return levels;
    }

    private sealed record FyRow(
        decimal Rev,
        decimal Cogs,
        decimal Gp,
        decimal Ebit,
        decimal Depr,
        decimal Ebitda,
        decimal FinExp,
        decimal Pretax,
        decimal Tax,
        decimal Ni,
        decimal? Capex,
        decimal? Dwc,
        decimal Cash,
        decimal StDebt,
        decimal LtDebt,
        decimal Equity,
        decimal CurAssets,
        decimal CurLiab);

    private static List<FixtureStatement> BuildStatements(int fyLatest)
    {
        var statements = new List<FixtureStatement>();
        var fy = fyLatest;
        var published = new DateOnly(fy + 1, 3, 10);

        void AddFull(
            string symbol,
            int year,
            long shares,
            FyRow row,
            string version = "as_reported",
            DateOnly? restatement = null,
            DateOnly? publishedAt = null)
        {
            var end = new DateOnly(year, 12, 31);
            statements.Add(new FixtureStatement(
                symbol, "FY", end, year, "IS", version, restatement, publishedAt,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.Rev] = row.Rev,
                    [FinItemCodes.Cogs] = row.Cogs,
                    [FinItemCodes.GrossProfit] = row.Gp,
                    [FinItemCodes.Ebit] = row.Ebit,
                    [FinItemCodes.DeprAmort] = row.Depr,
                    [FinItemCodes.Ebitda] = row.Ebitda,
                    [FinItemCodes.FinExp] = row.FinExp,
                    [FinItemCodes.PretaxInc] = row.Pretax,
                    [FinItemCodes.TaxExp] = row.Tax,
                    [FinItemCodes.Ni] = row.Ni,
                }));
            statements.Add(new FixtureStatement(
                symbol, "FY", end, year, "BS", version, restatement, publishedAt,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.Cash] = row.Cash,
                    [FinItemCodes.StDebt] = row.StDebt,
                    [FinItemCodes.LtDebt] = row.LtDebt,
                    [FinItemCodes.TotalEquity] = row.Equity,
                    [FinItemCodes.CurAssets] = row.CurAssets,
                    [FinItemCodes.CurLiab] = row.CurLiab,
                    [FinItemCodes.SharesDiluted] = shares,
                    [FinItemCodes.SharesOut] = shares,
                }));
            if (row.Capex is not null && row.Dwc is not null)
            {
                statements.Add(new FixtureStatement(
                    symbol, "FY", end, year, "CF", version, restatement, publishedAt,
                    new Dictionary<string, decimal>
                    {
                        [FinItemCodes.Capex] = row.Capex.Value,
                        [FinItemCodes.Dwc] = row.Dwc.Value,
                    }));
            }
        }

        void AddDeep(
            string symbol,
            int year,
            long shares,
            decimal? rev,
            decimal ni,
            decimal depr,
            decimal dwc,
            decimal fcf)
        {
            var end = new DateOnly(year, 12, 31);
            var incomeLines = new Dictionary<string, decimal>
            {
                [FinItemCodes.DeprAmort] = depr,
                [FinItemCodes.Ni] = ni,
            };
            // FU §5.2 does not specify REV for the KAPPA deep-history rows: leave the
            // line absent (honest missing) rather than seeding a false zero revenue.
            if (rev is not null)
            {
                incomeLines[FinItemCodes.Rev] = rev.Value;
            }

            statements.Add(new FixtureStatement(
                symbol, "FY", end, year, "IS", "as_reported", null, null,
                incomeLines));
            statements.Add(new FixtureStatement(
                symbol, "FY", end, year, "BS", "as_reported", null, null,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.SharesDiluted] = shares,
                    [FinItemCodes.SharesOut] = shares,
                }));
            statements.Add(new FixtureStatement(
                symbol, "FY", end, year, "CF", "as_reported", null, null,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.Capex] = ni + depr - dwc - fcf,
                    [FinItemCodes.Dwc] = dwc,
                }));
        }

        // FU §5 — FY2025 full rows (the tabulated statement row per instrument).
        // Shares are in millions; monetary values are TRY M (FU §2 units).
        AddFull("ALFA", fy, 100, new FyRow(1000, 600, 400, 300, 100, 400, 50, 250, 50, 200, 150, 50, 200, 100, 300, 1000, 600, 300), publishedAt: published);
        AddFull("BETA", fy, 80, new FyRow(300, 240, 60, -20, 30, 10, 10, -30, 0, -50, 20, 10, 50, 80, 120, 150, 200, 180), publishedAt: published);
        AddFull("GAMA", fy, 60, new FyRow(250, 200, 50, -30, 25, -5, 5, -35, 0, -40, 15, 5, 30, 60, 90, 120, 150, 140), publishedAt: published);
        AddFull("DELTA", fy, 100, new FyRow(500, 350, 150, 60, 20, 80, 10, 50, 10, 50, 30, 10, 50, 60, 40, -200, 180, 150), publishedAt: published);
        AddFull("EPSL", fy, 400, new FyRow(1200, 800, 400, 360, 90, 450, 60, 300, 60, 300, 180, 40, 300, 200, 500, 1600, 900, 500), publishedAt: published);
        AddFull("ZETA", fy, 50, new FyRow(100, 70, 30, 25, 8, 33, 4, 21, 5, 60, 15, 3, 40, 30, 50, 200, 120, 90), publishedAt: published);
        AddFull("ETA", fy, 90, new FyRow(450, 315, 135, 90, 30, 120, 15, 75, 15, 90, 45, 10, 100, 80, 120, 600, 350, 200), publishedAt: published);
        AddFull("THETA", fy, 70, new FyRow(380, 270, 110, 70, 20, 90, 12, 58, 12, 70, 35, 8, 60, 90, 150, 500, 280, 160), publishedAt: published);
        AddFull("PART", fy, 120, new FyRow(600, 420, 180, 90, 30, 120, 20, 70, 15, 120, null, null, 150, 100, 250, 800, 400, 240), publishedAt: published);
        AddFull("NEWP", fy, 30, new FyRow(50, 35, 15, 12, 4, 16, 2, 10, 2, 30, 8, 2, 20, 15, 25, 90, 55, 35), publishedAt: published);
        AddFull("IOTA", fy, 200, new FyRow(280, 210, 70, -15, 25, -20, 8, -23, 0, 20, 20, 5, 40, 70, 110, 180, 160, 130), publishedAt: published);
        AddFull("KAPPA", fy, 150, new FyRow(520, 370, 150, 80, 25, 105, 14, 66, 14, 60, 70, 20, 90, 120, 190, 550, 300, 190), publishedAt: published);
        AddFull("LAMDA", fy, 110, new FyRow(200, 140, 60, 35, 12, 47, 6, 29, 6, 30, 18, 4, 35, 45, 75, 220, 130, 85), publishedAt: published);
        AddFull("NU", fy, 90, new FyRow(320, 230, 90, 45, 15, 60, 9, 36, 7, 40, 20, 5, 45, 60, 95, 260, 150, 100), publishedAt: published);
        AddFull("UNSEC", fy, 70, new FyRow(260, 185, 75, 40, 12, 52, 7, 33, 7, 45, 22, 5, 40, 55, 90, 210, 120, 80), publishedAt: published);

        // REST — the as-reported version (NI 60) is retained alongside the restated
        // version (NI 75, the tabulated row) — FU §5 footnote / 02 §5.7.
        AddFull(
            "REST", fy, 50,
            new FyRow(400, 280, 120, 110, 30, 140, 15, 95, 20, 60, 40, 5, 80, 50, 70, 500, 250, 160),
            publishedAt: published);
        AddFull(
            "REST", fy, 50,
            new FyRow(400, 280, 120, 110, 30, 140, 15, 95, 20, 75, 40, 5, 80, 50, 70, 500, 250, 160),
            version: "restated",
            restatement: new DateOnly(fy + 1, 6, 20),
            publishedAt: published);

        // FU §5.1 — ALFA deep history (FY2015…FY2024). FCF is reconstructed exactly:
        // depr 40, ΔWC 20, CAPEX = NI + D&A − ΔWC − FCF.
        var deep = new (int Year, decimal Rev, decimal Eps, decimal Fcf)[]
        {
            (2015, 200, 1.00m, 80),
            (2016, 220, 1.05m, 90),
            (2017, 250, 1.10m, 95),
            (2018, 270, 1.15m, 100),
            (2019, 290, 1.20m, 110),
            (2020, 400, 1.50m, 160),
            (2021, 430, 1.55m, 170),
            (2022, 640, 1.80m, 200),
            (2023, 700, 1.85m, 190),
            (2024, 850, 1.90m, 180),
        };
        foreach (var (year, rev, eps, fcf) in deep)
        {
            AddDeep("ALFA", year, 100, rev, eps * 100, 40, 20, fcf);
        }

        // FU §5.1 — ALFA FY2025 quarterly statements summing to the FY row.
        AddQuarter("ALFA", fy, new DateOnly(fy, 3, 31), 220, 130, 60, 20, 10, 10, 30, 10, 100);
        AddQuarter("ALFA", fy, new DateOnly(fy, 6, 30), 240, 145, 70, 25, 12, 11, 35, 12, 100);
        AddQuarter("ALFA", fy, new DateOnly(fy, 9, 30), 260, 155, 75, 25, 13, 13, 40, 13, 100);
        AddQuarter("ALFA", fy, new DateOnly(fy, 12, 31), 280, 170, 95, 30, 15, 16, 45, 15, 100);

        // FU §5.2 — ZETA exactly 2 FYs (FY2024 REV 80 / EPS 1.00 / FCF 10).
        AddDeep("ZETA", fy - 1, 50, 80, 50, 8, 3, 10);

        // FU §5.2 — KAPPA FCF series with a FY sign change (+20, +25, +10, −5, −30).
        // REV is not specified by FU §5.2 for these years, so it is left absent.
        AddDeep("KAPPA", fy - 4, 150, null, 30, 20, 10, 20);
        AddDeep("KAPPA", fy - 3, 150, null, 30, 20, 10, 25);
        AddDeep("KAPPA", fy - 2, 150, null, 30, 20, 10, 10);
        AddDeep("KAPPA", fy - 1, 150, null, 30, 20, 10, -5);

        return statements;

        void AddQuarter(
            string symbol,
            int year,
            DateOnly end,
            decimal rev,
            decimal cogs,
            decimal ebit,
            decimal depr,
            decimal finExp,
            decimal tax,
            decimal capex,
            decimal dwc,
            long shares)
        {
            var pretax = ebit - finExp;
            var ni = pretax - tax;
            statements.Add(new FixtureStatement(
                symbol, "Q", end, year, "IS", "as_reported", null, null,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.Rev] = rev,
                    [FinItemCodes.Cogs] = cogs,
                    [FinItemCodes.Ebit] = ebit,
                    [FinItemCodes.DeprAmort] = depr,
                    [FinItemCodes.FinExp] = finExp,
                    [FinItemCodes.PretaxInc] = pretax,
                    [FinItemCodes.TaxExp] = tax,
                    [FinItemCodes.Ni] = ni,
                }));
            statements.Add(new FixtureStatement(
                symbol, "Q", end, year, "CF", "as_reported", null, null,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.Capex] = capex,
                    [FinItemCodes.Dwc] = dwc,
                }));
            statements.Add(new FixtureStatement(
                symbol, "Q", end, year, "BS", "as_reported", null, null,
                new Dictionary<string, decimal>
                {
                    [FinItemCodes.SharesDiluted] = shares,
                    [FinItemCodes.SharesOut] = shares,
                }));
        }
    }

    private static List<FixtureDividend> BuildDividends(FixtureAnchor anchor)
    {
        var dividends = new List<FixtureDividend>();

        // FU §5.1 / §6 — ALFA annual DPS series (ex-date Jul 15 of each FY).
        var dpsByYear = new Dictionary<int, decimal>
        {
            [2015] = 0.60m,
            [2016] = 0.62m,
            [2017] = 0.65m,
            [2018] = 0.68m,
            [2019] = 0.70m,
            [2020] = 0.80m,
            [2021] = 0.82m,
            [2022] = 0.90m,
            [2023] = 0.92m,
            [2024] = 0.95m,
            [2025] = 1.00m,
        };
        foreach (var (year, amount) in dpsByYear)
        {
            var ex = new DateOnly(year, 7, 15);
            dividends.Add(new FixtureDividend("ALFA", ex, ex.AddDays(20), amount, "TRY"));
        }

        // FU §6 — ALFA quarterly dividends making TTM DPS 1.00 (four 0.25 rows).
        foreach (var offset in new[] { -264, -174, -83, -5 })
        {
            var ex = anchor.Calendar(offset);
            dividends.Add(new FixtureDividend("ALFA", ex, ex.AddDays(20), 0.25m, "TRY"));
        }

        // FU §6 — DIV_TTM per instrument (TRY M), expressed per share; sum = 390.
        var ttmEx = anchor.Calendar(-120);
        var perShare = new (string Symbol, decimal Amount)[]
        {
            ("DELTA", 0.20m),
            ("REST", 0.60m),
            ("EPSL", 0.30m),
            ("THETA", 0.4286m),
            ("IOTA", 0.05m),
            ("KAPPA", 0.1333m),
            ("NU", 0.6667m),
        };
        foreach (var (symbol, amount) in perShare)
        {
            dividends.Add(new FixtureDividend(symbol, ttmEx, ttmEx.AddDays(20), amount, "TRY"));
        }

        // LAMDA deliberately has no dividend rows (FU §6).
        return dividends;
    }

    private static List<FixtureMacroValue> BuildMacroValues(FixtureAnchor anchor)
    {
        var dailyRecorded = new DateTimeOffset(anchor.T.ToDateTime(new TimeOnly(21, 30)), TimeSpan.Zero);
        var goldDate = anchor.Calendar(-10);
        return
        [
            new("TUIK_CPI", anchor.MonthStart(-1), 45.20m, new DateTimeOffset(anchor.Calendar(-3).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)),
            new("TUIK_CPI", anchor.MonthStart(-2), 44.80m, new DateTimeOffset(anchor.Calendar(-31).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)),
            new("TUIK_CPI", anchor.MonthStart(-2), 45.00m, new DateTimeOffset(anchor.Calendar(-16).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)),
            new("INDEP_CPI", anchor.MonthStart(-1), 58.30m, new DateTimeOffset(anchor.Calendar(-3).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)),
            new("CBRT_REPO", anchor.Calendar(-21), 42.50m, new DateTimeOffset(anchor.Calendar(-21).ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero)),
            new("USD_TRY", anchor.T, 47.10m, dailyRecorded),
            new("EUR_TRY", anchor.T, 51.40m, dailyRecorded),
            new("GOLD", goldDate, 5200.00m, new DateTimeOffset(goldDate.ToDateTime(new TimeOnly(21, 30)), TimeSpan.Zero)),
        ];
    }

    // FU §7 — staleness is a missed ingest, not the vintage of the published value.
    // Each series is stale only when its latest ingest (recorded_at) has overrun the
    // tolerable window for its cadence (02 §5.6 macro_series.cadence). GOLD is the
    // only stale fixture: a daily series whose last successful ingest was T−10.
    // per_release has no fixed window, so it is stale only on an explicit failure
    // (which the fixture set does not model) — hence null.
    private static readonly Dictionary<string, int?> MacroCadenceToleranceDays = new(StringComparer.Ordinal)
    {
        ["TUIK_CPI"] = 31,
        ["INDEP_CPI"] = 31,
        ["CBRT_REPO"] = null,
        ["USD_TRY"] = 1,
        ["EUR_TRY"] = 1,
        ["GOLD"] = 1,
    };

    private static List<FixtureMacroFreshness> BuildMacroFreshness(FixtureAnchor anchor)
    {
        var macroValues = BuildMacroValues(anchor);
        return macroValues
            .GroupBy(v => v.SeriesCode, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                // FU §7 / FR-MOV-014 — canonical ingest is the most recently recorded
                // row; age is measured against that ingest, never the value_date.
                var latestIngest = g.MaxBy(v => v.RecordedAt)!;
                var ingestedOn = DateOnly.FromDateTime(latestIngest.RecordedAt.UtcDateTime);
                var ageDays = anchor.T.DayNumber - ingestedOn.DayNumber;
                var tolerance = MacroCadenceToleranceDays[g.Key];
                return new FixtureMacroFreshness(g.Key, ageDays, tolerance is int maxAge && ageDays > maxAge);
            })
            .ToList();
    }

    private static DateTimeOffset At(DateOnly date, int hour) =>
        new(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private static decimal Round(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
