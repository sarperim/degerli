using System.Text.Json;
using Degerli.Fixtures;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-007 acceptance: the checked-in fixture-universe module seeds the
/// FU tables exactly (L2 absolute-date variant), is idempotent/re-runnable, and every
/// section of the contract is spot-asserted. The now-anchored L4 variant is covered
/// by <see cref="FixtureUniverseNowAnchoredTests"/>.
/// </summary>
public sealed class FixtureUniverseSeedTests : IClassFixture<PostgresFixture>
{
    private static readonly DateOnly AlfaFy2025End = new(2025, 12, 31);

    private readonly PostgresFixture _fixture;

    public FixtureUniverseSeedTests(PostgresFixture fixture) => _fixture = fixture;

    private static FixtureSet L2 => FixtureUniverse.Build(FixtureAnchor.L2);

    private async Task<DegerliDbContext> SeedL2Async()
    {
        var db = _fixture.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        return db;
    }

    [Fact]
    public async Task Seed_is_idempotent_and_rerunnable()
    {
        await using var db = await SeedL2Async();

        var first = await SnapshotAsync(db);
        await FixtureSeeder.ApplyAsync(db, L2);
        await FixtureSeeder.ApplyAsync(db, L2);
        var second = await SnapshotAsync(db);

        Assert.Equal(first, second);
        Assert.Equal(16L, first["instruments"]);
        Assert.Equal(3L, first["sectors"]);
        Assert.Equal(163L, first["daily_prices"]);
        Assert.Equal(107L, first["financial_statements"]);
    }

    [Fact]
    public async Task L2_reproduces_sectors_instruments_and_membership()
    {
        await using var db = await SeedL2Async();

        Assert.Equal(3, await db.Sectors.CountAsync());

        var instruments = await db.Instruments.OrderBy(i => i.Symbol).ToListAsync();
        Assert.Equal(16, instruments.Count);

        var unsec = Assert.Single(instruments, i => i.Symbol == "UNSEC");
        Assert.Null(unsec.SectorId);
        Assert.Equal("active", unsec.Status);

        var alfa = Assert.Single(instruments, i => i.Symbol == "ALFA");
        var techSector = await db.Sectors.SingleAsync(s => s.Code == "A");
        Assert.Equal(techSector.Id, alfa.SectorId);
        Assert.Equal(new DateOnly(2010, 1, 4), alfa.ListingDate);

        var xu100 = await db.Indices.SingleAsync(i => i.Code == "XU100");
        var xu30 = await db.Indices.SingleAsync(i => i.Code == "XU30");
        Assert.Equal(16, await db.IndexConstituents.CountAsync(c => c.IndexId == xu100.Id && c.EffectiveTo == null));
        Assert.Equal(5, await db.IndexConstituents.CountAsync(c => c.IndexId == xu30.Id && c.EffectiveTo == null));
    }

    [Fact]
    public async Task L2_reproduces_daily_prices_and_index_levels()
    {
        await using var db = await SeedL2Async();
        var alfa = await InstrumentIdAsync(db, "ALFA");

        var tMinus1 = await db.DailyPrices.SingleAsync(p => p.InstrumentId == alfa && p.PriceDate == new DateOnly(2026, 10, 5));
        var t = await db.DailyPrices.SingleAsync(p => p.InstrumentId == alfa && p.PriceDate == new DateOnly(2026, 10, 6));

        Assert.Equal(19.80m, tMinus1.CloseRaw);
        Assert.Equal(20.00m, t.CloseRaw);
        Assert.Equal(12_000_000L, t.Volume);
        Assert.Equal(t.CloseRaw, t.CloseAdjusted);

        // T−9 dotted and T−1/T present per instrument: 10 trading days each.
        Assert.Equal(10, await db.DailyPrices.CountAsync(p => p.InstrumentId == alfa));

        var xu100Final = await db.IndexLevels.SingleAsync(l => l.IndexId == (from i in db.Indices where i.Code == "XU100" select i.Id).Single() && l.LevelDate == new DateOnly(2026, 10, 6));
        var xu30Final = await db.IndexLevels.SingleAsync(l => l.IndexId == (from i in db.Indices where i.Code == "XU30" select i.Id).Single() && l.LevelDate == new DateOnly(2026, 10, 6));
        Assert.Equal(10200.00m, xu100Final.Close);
        Assert.Equal(30600.00m, xu30Final.Close);
    }

    [Fact]
    public async Task L2_reproduces_EPSL_adjusted_prices_around_actions()
    {
        await using var db = await SeedL2Async();
        var epsl = await InstrumentIdAsync(db, "EPSL");

        var before = await db.DailyPrices.SingleAsync(p => p.InstrumentId == epsl && p.PriceDate == new DateOnly(2025, 5, 30));
        Assert.Equal(100.00m, before.CloseRaw);
        Assert.Equal(18.1818m, before.CloseAdjusted);

        var between = await db.DailyPrices.SingleAsync(p => p.InstrumentId == epsl && p.PriceDate == new DateOnly(2026, 2, 27));
        Assert.Equal(22.00m, between.CloseRaw);
        Assert.Equal(20.0000m, between.CloseAdjusted);

        var after = await db.DailyPrices.SingleAsync(p => p.InstrumentId == epsl && p.PriceDate == new DateOnly(2026, 6, 2));
        Assert.Equal(20.20m, after.CloseRaw);
        Assert.Equal(20.2000m, after.CloseAdjusted);

        var actions = await db.CorporateActions.Where(a => a.InstrumentId == epsl).OrderBy(a => a.ActionDate).ToListAsync();
        Assert.Collection(
            actions,
            a => { Assert.Equal("split", a.ActionType); Assert.Equal(new DateOnly(2025, 6, 2), a.ActionDate); },
            a => { Assert.Equal("bonus_issue", a.ActionType); Assert.Equal(new DateOnly(2026, 3, 2), a.ActionDate); });
    }

    [Fact]
    public async Task L2_reproduces_ALFA_FY2025_row_and_quarterly_sum()
    {
        await using var db = await SeedL2Async();
        var alfa = await InstrumentIdAsync(db, "ALFA");

        var fy = await LinesAsync(db, alfa, AlfaFy2025End, "FY", "as_reported");

        Assert.Equal(1000m, fy[FinItemCodes.Rev]);
        Assert.Equal(600m, fy[FinItemCodes.Cogs]);
        Assert.Equal(400m, fy[FinItemCodes.GrossProfit]);
        Assert.Equal(300m, fy[FinItemCodes.Ebit]);
        Assert.Equal(100m, fy[FinItemCodes.DeprAmort]);
        Assert.Equal(400m, fy[FinItemCodes.Ebitda]);
        Assert.Equal(50m, fy[FinItemCodes.FinExp]);
        Assert.Equal(250m, fy[FinItemCodes.PretaxInc]);
        Assert.Equal(50m, fy[FinItemCodes.TaxExp]);
        Assert.Equal(200m, fy[FinItemCodes.Ni]);
        Assert.Equal(150m, fy[FinItemCodes.Capex]);
        Assert.Equal(50m, fy[FinItemCodes.Dwc]);
        Assert.Equal(200m, fy[FinItemCodes.Cash]);
        Assert.Equal(100m, fy[FinItemCodes.StDebt]);
        Assert.Equal(300m, fy[FinItemCodes.LtDebt]);
        Assert.Equal(1000m, fy[FinItemCodes.TotalEquity]);
        Assert.Equal(600m, fy[FinItemCodes.CurAssets]);
        Assert.Equal(300m, fy[FinItemCodes.CurLiab]);
        Assert.Equal(100m, fy[FinItemCodes.SharesDiluted]);

        // The four quarters sum exactly to the FY row (FU §5, TTM consistency fixture).
        var quarters = new[] { new DateOnly(2025, 3, 31), new DateOnly(2025, 6, 30), new DateOnly(2025, 9, 30), new DateOnly(2025, 12, 31) };
        var sums = new Dictionary<string, decimal>();
        foreach (var quarter in quarters)
        {
            foreach (var (code, value) in await LinesAsync(db, alfa, quarter, "Q", "as_reported"))
            {
                sums[code] = sums.GetValueOrDefault(code) + value;
            }
        }

        Assert.Equal(fy[FinItemCodes.Rev], sums[FinItemCodes.Rev]);
        Assert.Equal(fy[FinItemCodes.Ni], sums[FinItemCodes.Ni]);
        Assert.Equal(fy[FinItemCodes.Ebit], sums[FinItemCodes.Ebit]);
        Assert.Equal(fy[FinItemCodes.Capex], sums[FinItemCodes.Capex]);
        Assert.Equal(11, await db.FinancialStatements
            .Where(s => s.InstrumentId == alfa && s.PeriodType == "FY")
            .Select(s => s.PeriodEndDate)
            .Distinct()
            .CountAsync());
    }

    [Fact]
    public async Task L2_reproduces_REST_dual_versions_and_PART_no_cf()
    {
        await using var db = await SeedL2Async();

        var rest = await InstrumentIdAsync(db, "REST");
        var asReported = await LinesAsync(db, rest, AlfaFy2025End, "FY", "as_reported");
        var restated = await LinesAsync(db, rest, AlfaFy2025End, "FY", "restated");
        Assert.Equal(60m, asReported[FinItemCodes.Ni]);
        Assert.Equal(75m, restated[FinItemCodes.Ni]);

        var restatedStatement = await db.FinancialStatements.SingleAsync(s =>
            s.InstrumentId == rest && s.Version == "restated" && s.StatementType == "IS");
        Assert.Equal(new DateOnly(2026, 6, 20), restatedStatement.RestatementDate);
        Assert.Equal(2, await db.FinancialStatements.CountAsync(s => s.InstrumentId == rest && s.StatementType == "IS"));

        // PART has no CF statement → FCF is not derivable.
        var part = await InstrumentIdAsync(db, "PART");
        Assert.False(await db.FinancialStatements.AnyAsync(s => s.InstrumentId == part && s.StatementType == "CF"));
        Assert.True(await db.FinancialStatements.AnyAsync(s => s.InstrumentId == part && s.StatementType == "IS"));
    }

    [Fact]
    public async Task L2_reproduces_ZETA_NEWP_and_KAPPA_series()
    {
        await using var db = await SeedL2Async();

        // ZETA: exactly 2 FYs.
        var zeta = await InstrumentIdAsync(db, "ZETA");
        Assert.Equal(2, await db.FinancialStatements.CountAsync(s => s.InstrumentId == zeta && s.PeriodType == "FY" && s.StatementType == "IS"));

        // NEWP: 1 FY only.
        var newp = await InstrumentIdAsync(db, "NEWP");
        Assert.Equal(1, await db.FinancialStatements.CountAsync(s => s.InstrumentId == newp && s.PeriodType == "FY" && s.StatementType == "IS"));

        // KAPPA FCF series: +20, +25, +10, −5 (FY2021..FY2024), FY2025 tabulated.
        var kappa = await InstrumentIdAsync(db, "KAPPA");
        var expected = new (int Year, decimal Fcf)[]
        {
            (2021, 20m),
            (2022, 25m),
            (2023, 10m),
            (2024, -5m),
        };
        foreach (var (year, fcf) in expected)
        {
            var lines = await LinesAsync(db, kappa, new DateOnly(year, 12, 31), "FY", "as_reported");
            var derived = lines[FinItemCodes.Ni] + lines[FinItemCodes.DeprAmort] - lines[FinItemCodes.Dwc] - lines[FinItemCodes.Capex];
            Assert.Equal(fcf, derived);
        }
    }

    [Fact]
    public async Task L2_reproduces_dividends()
    {
        await using var db = await SeedL2Async();

        var alfa = await InstrumentIdAsync(db, "ALFA");
        Assert.Equal(4, await db.Dividends.CountAsync(d => d.InstrumentId == alfa && d.AmountPerShare == 0.25m));
        Assert.True(await db.Dividends.AnyAsync(d => d.InstrumentId == alfa && d.ExDate == new DateOnly(2025, 7, 15) && d.AmountPerShare == 1.00m));

        var lamda = await InstrumentIdAsync(db, "LAMDA");
        Assert.False(await db.Dividends.AnyAsync(d => d.InstrumentId == lamda));

        // FU §6 DIV_TTM totals derived per share × shares(M) sum to 390 TRY M.
        var seeds = await db.Dividends.ToListAsync();
        var instruments = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var shares = FixtureUniverse.Build(FixtureAnchor.L2).Instruments.ToDictionary(i => i.Symbol, i => i.SharesDiluted);
        var divTtm = seeds
            .Where(d => d.ExDate >= new DateOnly(2025, 10, 6))
            .GroupBy(d => instruments[d.InstrumentId])
            .Sum(g => g.Sum(d => d.AmountPerShare) * shares[g.Key]);
        Assert.Equal(390m, divTtm);
    }

    [Fact]
    public async Task L2_reproduces_macro_revision_pair_and_stale_gold()
    {
        await using var db = await SeedL2Async();

        var revisionRows = await db.MacroValues
            .Where(v => v.SeriesCode == "TUIK_CPI" && v.ValueDate == new DateOnly(2026, 8, 1))
            .OrderBy(v => v.RecordedAt)
            .ToListAsync();
        Assert.Equal(2, revisionRows.Count);
        Assert.Equal(44.80m, revisionRows[0].Value);
        var canonical = revisionRows[^1];
        Assert.Equal(45.00m, canonical.Value);

        var gold = await db.MacroValues.SingleAsync(v => v.SeriesCode == "GOLD");
        Assert.Equal(new DateOnly(2026, 9, 26), gold.ValueDate);
        Assert.Equal(5200.00m, gold.Value);
        Assert.Equal(new DateOnly(2026, 9, 26), DateOnly.FromDateTime(gold.RecordedAt.UtcDateTime));

        var freshness = L2.MacroFreshness.Single(f => f.SeriesCode == "GOLD");
        Assert.True(freshness.IsStale);
        Assert.Equal(10, freshness.AgeDays);
        Assert.False(L2.MacroFreshness.Single(f => f.SeriesCode == "USD_TRY").IsStale);
    }

    [Fact]
    public async Task L2_reproduces_funds_and_TEF0002_holdings_gap()
    {
        await using var db = await SeedL2Async();

        Assert.Equal(3, await db.Funds.CountAsync());

        var tef1 = await db.FundHoldings.Where(h => h.FundId == "TEF0001").OrderBy(h => h.LineNo).ToListAsync();
        Assert.Equal(3, tef1.Count);
        var alfa = await InstrumentIdAsync(db, "ALFA");
        Assert.Equal(alfa, tef1[0].InstrumentId);
        Assert.Equal(0.05m, tef1[0].Weight);
        Assert.Equal(250_000m, tef1[0].Units);
        Assert.Equal("Yabancı Hisse X", tef1[2].NameRaw);
        Assert.Null(tef1[2].InstrumentId);

        // TEF0002: holdings gap recorded.
        Assert.False(await db.FundHoldings.AnyAsync(h => h.FundId == "TEF0002"));
        Assert.True(await db.CoverageMetadata.AnyAsync(c => c.Scope == "fund" && c.FundId == "TEF0002" && c.DataType == "holdings"));

        // TEF0003: one partial line with NULL weight/units.
        var tef3 = Assert.Single(await db.FundHoldings.Where(h => h.FundId == "TEF0003").ToListAsync());
        Assert.Equal("Hisse Y", tef3.NameRaw);
        Assert.Null(tef3.Weight);
        Assert.Null(tef3.Units);
    }

    [Fact]
    public async Task L2_reproduces_accounts_and_preseeded_content()
    {
        await using var db = await SeedL2Async();

        var accounts = await db.Users.Where(u => u.Email!.EndsWith("@degerli.test")).ToListAsync();
        Assert.Equal(4, accounts.Count);

        var builder = accounts.Single(u => u.Email == "builder@degerli.test");
        Assert.True(builder.EmailConfirmed);
        Assert.True(await db.UserRoles.AnyAsync(ur => ur.UserId == builder.Id));

        var userA = accounts.Single(u => u.Email == "user-a@degerli.test");
        var userB = accounts.Single(u => u.Email == "user-b@degerli.test");
        var userC = accounts.Single(u => u.Email == "user-c@degerli.test");
        Assert.True(userA.EmailConfirmed);
        Assert.False(userB.EmailConfirmed);
        Assert.Equal("tr", userA.LanguagePref);
        Assert.Equal("en", userC.LanguagePref);

        var screen = await db.SavedScreens.SingleAsync(s => s.UserId == userA.Id && s.Name == "Ekranım");
        using (var criteria = JsonDocument.Parse(screen.CriteriaJson))
        {
            Assert.Equal(2, criteria.RootElement.GetArrayLength());
            Assert.Equal("pe", criteria.RootElement[0].GetProperty("metricCode").GetString());
        }

        var alfa = await InstrumentIdAsync(db, "ALFA");
        var scenario = await db.DcfScenarios.SingleAsync(s => s.UserId == userA.Id && s.Name == "Temel" && s.InstrumentId == alfa);
        using (var parameters = JsonDocument.Parse(scenario.ParamsJson))
        {
            Assert.Equal(0.15m, parameters.RootElement.GetProperty("discount_rate").GetDecimal());
        }
    }

    [Fact]
    public async Task L2_reproduces_business_descriptions_and_kap_disclosures()
    {
        await using var db = await SeedL2Async();

        var alfa = await InstrumentIdAsync(db, "ALFA");
        var rest = await InstrumentIdAsync(db, "REST");
        var zeta = await InstrumentIdAsync(db, "ZETA");

        var alfaDescription = await db.BusinessDescriptions.SingleAsync(d => d.InstrumentId == alfa);
        Assert.Equal("published", alfaDescription.Status);
        Assert.Equal(2, alfaDescription.Version);
        Assert.False(string.IsNullOrEmpty(alfaDescription.TextTr));
        Assert.False(string.IsNullOrEmpty(alfaDescription.TextEn));
        Assert.NotNull(alfaDescription.PublishedAt);
        using (var refs = JsonDocument.Parse(alfaDescription.SourceRefsJson!))
        {
            Assert.Equal(2, refs.RootElement.GetArrayLength());
            Assert.Equal(8841, refs.RootElement[0].GetInt64());
            Assert.Equal(9999, refs.RootElement[1].GetInt64());
        }

        Assert.Equal("draft", (await db.BusinessDescriptions.SingleAsync(d => d.InstrumentId == rest)).Status);
        var zetaDescription = await db.BusinessDescriptions.SingleAsync(d => d.InstrumentId == zeta);
        Assert.Equal("draft", zetaDescription.Status);
        Assert.Equal(string.Empty, zetaDescription.TextEn);

        Assert.Equal(3, await db.KapDisclosures.CountAsync());
        Assert.True(await db.KapDisclosures.AnyAsync(k => k.Id == 8841 && k.DisclosureType == "annual_report" && k.PublishDate == new DateOnly(2026, 3, 14)));
        Assert.False(await db.KapDisclosures.AnyAsync(k => k.Id == 9999));
    }

    private static async Task<long> InstrumentIdAsync(DegerliDbContext db, string symbol) =>
        await db.Instruments.Where(i => i.Symbol == symbol).Select(i => i.Id).SingleAsync();

    private static async Task<Dictionary<string, decimal>> LinesAsync(
        DegerliDbContext db,
        long instrumentId,
        DateOnly periodEnd,
        string periodType,
        string version)
    {
        var statementIds = await db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId && s.PeriodEndDate == periodEnd && s.PeriodType == periodType && s.Version == version)
            .Select(s => s.Id)
            .ToListAsync();
        var lines = await db.FinLineItems.Where(l => statementIds.Contains(l.StatementId)).ToListAsync();
        return lines.ToDictionary(l => l.ItemCode, l => l.Value);
    }

    private static async Task<Dictionary<string, long>> SnapshotAsync(DegerliDbContext db) => new(StringComparer.Ordinal)
    {
        ["sectors"] = await db.Sectors.LongCountAsync(),
        ["indices"] = await db.Indices.LongCountAsync(),
        ["instruments"] = await db.Instruments.LongCountAsync(),
        ["memberships"] = await db.IndexConstituents.LongCountAsync(),
        ["daily_prices"] = await db.DailyPrices.LongCountAsync(),
        ["index_levels"] = await db.IndexLevels.LongCountAsync(),
        ["financial_statements"] = await db.FinancialStatements.LongCountAsync(),
        ["fin_line_items"] = await db.FinLineItems.LongCountAsync(),
        ["dividends"] = await db.Dividends.LongCountAsync(),
        ["corporate_actions"] = await db.CorporateActions.LongCountAsync(),
        ["kap_disclosures"] = await db.KapDisclosures.LongCountAsync(),
        ["macro_values"] = await db.MacroValues.LongCountAsync(),
        ["funds"] = await db.Funds.LongCountAsync(),
        ["fund_navs"] = await db.FundNavs.LongCountAsync(),
        ["fund_performances"] = await db.FundPerformances.LongCountAsync(),
        ["fund_holdings"] = await db.FundHoldings.LongCountAsync(),
        ["accounts"] = await db.Users.LongCountAsync(),
        ["user_roles"] = await db.UserRoles.LongCountAsync(),
        ["saved_screens"] = await db.SavedScreens.LongCountAsync(),
        ["dcf_scenarios"] = await db.DcfScenarios.LongCountAsync(),
        ["business_descriptions"] = await db.BusinessDescriptions.LongCountAsync(),
        ["coverage_metadata"] = await db.CoverageMetadata.LongCountAsync(),
    };
}
