using System.Text.Json;
using Degerli.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-008 acceptance: the FU §10 named canned-source payload catalog is
/// loadable through a helper, its contents derive from the single fixture universe (the
/// <c>prices-ok</c> consistency test cross-checks it against the seeded <c>daily_prices</c>),
/// and every validation/quarantine/error class the adapters must handle has a payload.
/// </summary>
public sealed class CannedSourceCatalogTests : IClassFixture<PostgresFixture>
{
    private static readonly DateOnly AnchorT = new(2026, 10, 6);
    private static readonly DateOnly AnchorTMinus1 = new(2026, 10, 5);

    private readonly PostgresFixture _postgres;

    public CannedSourceCatalogTests(PostgresFixture postgres) => _postgres = postgres;

    private static FixtureSet L2 => FixtureUniverse.Build(FixtureAnchor.L2);

    [Fact]
    public void Every_named_payload_is_loadable_through_the_catalog()
    {
        var set = L2;

        var all = CannedSourceCatalog.Build(set);

        Assert.Equal(CannedSourceCatalog.Names.Count, all.Count);
        Assert.Equal(CannedSourceCatalog.Names, all.Select(p => p.Name).ToList());

        foreach (var name in CannedSourceCatalog.Names)
        {
            Assert.True(CannedSourceCatalog.TryGet(set, name, out var payload), name);
            Assert.Equal(name, payload.Name);
            Assert.False(string.IsNullOrWhiteSpace(payload.Body), $"{name} has an empty body");
            Assert.StartsWith("/", payload.DefaultPath, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(payload.Source), $"{name} has no source");
        }
    }

    [Fact]
    public void Unknown_payload_name_is_rejected()
    {
        var set = L2;

        Assert.False(CannedSourceCatalog.TryGet(set, "does-not-exist", out _));
        Assert.Throws<KeyNotFoundException>(() => CannedSourceCatalog.Get(set, "does-not-exist"));
    }

    [Fact]
    public async Task Prices_ok_matches_the_seeded_daily_prices()
    {
        var set = L2;
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, set);

        var payload = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesOk);
        using var doc = JsonDocument.Parse(payload.Body);

        Assert.Equal(AnchorT, DateOnly.Parse(doc.RootElement.GetProperty("date").GetString()!));
        var items = ParsePrices(payload.Body);
        Assert.Equal(16, items.Length);

        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id);
        foreach (var item in items)
        {
            var instrumentId = instrumentIds[item.Symbol];
            var stored = await db.DailyPrices.SingleAsync(p => p.InstrumentId == instrumentId && p.PriceDate == AnchorT);
            var prior = await db.DailyPrices.SingleAsync(p => p.InstrumentId == instrumentId && p.PriceDate == AnchorTMinus1);

            Assert.Equal(stored.CloseRaw, item.Close);
            Assert.Equal(stored.Volume, item.Volume);
            Assert.Equal(prior.CloseRaw, item.PreviousClose);
        }
    }

    [Fact]
    public void Prices_ok_tminus1_is_the_prior_trading_day()
    {
        var set = L2;

        var body = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesOkTminus1).Body;
        using var doc = JsonDocument.Parse(body);

        Assert.Equal(AnchorTMinus1, DateOnly.Parse(doc.RootElement.GetProperty("date").GetString()!));
        Assert.Equal(16, ParsePrices(body).Length);
    }

    [Fact]
    public void Price_validation_payloads_carry_their_named_anomaly()
    {
        var set = L2;
        var ok = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesOk);

        // Invalid negative close: ALFA closes at −5.
        var negative = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesInvalidNegativeClose);
        Assert.Equal(-5m, Assert.Single(ParsePrices(negative.Body), p => p.Symbol == "ALFA").Close);
        Assert.Equal(16, ParsePrices(negative.Body).Length);

        // Conflicting value: ALFA closes at 21.00 against the already-stored 20.00; the
        // other 15 instruments are byte-identical to the ok payload.
        var conflicting = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesConflictingValue);
        Assert.Equal(21.00m, Assert.Single(ParsePrices(conflicting.Body), p => p.Symbol == "ALFA").Close);
        Assert.Equal(
            ParsePrices(ok.Body).Where(p => p.Symbol != "ALFA").ToList(),
            ParsePrices(conflicting.Body).Where(p => p.Symbol != "ALFA").ToList());

        // Missing provenance: the envelope drops source_ref.
        var missing = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesMissingProvenance);
        using (var doc = JsonDocument.Parse(missing.Body))
        {
            Assert.False(doc.RootElement.TryGetProperty("sourceRef", out _));
        }

        // Unparseable: a truncated body that is not valid JSON.
        var unparseable = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesUnparseable);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(unparseable.Body));

        // Source down: HTTP 500 forever.
        var down = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesSourceDown);
        Assert.Equal(500, down.StatusCode);
        Assert.False(down.IsSuccess);

        // Source slow: the ok payload behind a 5 s delay.
        var slow = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesSourceSlow);
        Assert.Equal(5_000, slow.DelayMs);
        Assert.Equal(ok.Body, slow.Body);
    }

    [Fact]
    public void Prices_variant_threshold_has_twelve_eligible_gainers_including_the_boundary()
    {
        var set = L2;
        var body = CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesVariantThreshold).Body;
        var items = ParsePrices(body);

        var eligible = items.Where(p => p.Close > p.PreviousClose && p.Volume >= 1_000_000).ToList();
        Assert.Equal(12, eligible.Count);
        Assert.Single(eligible, p => p.Volume == 1_000_000);

        // LAMDA is the illiquid big gainer: present, but excluded by the 1,000,000 threshold.
        var lamda = Assert.Single(items, p => p.Symbol == "LAMDA");
        Assert.True(lamda.Close > lamda.PreviousClose);
        Assert.True(lamda.Volume < 1_000_000);
    }

    [Fact]
    public void Statement_payloads_carry_the_seeded_periods()
    {
        var set = L2;

        // ALFA FY2025 + four quarters, each with IS/BS/CF → 3 + 12 = 15 statements.
        var alfa = CannedSourceCatalog.Get(set, CannedSourceCatalog.StatementsOkAlfaFy2025);
        using (var doc = JsonDocument.Parse(alfa.Body))
        {
            Assert.Equal("ALFA", doc.RootElement.GetProperty("symbol").GetString());
            var statements = doc.RootElement.GetProperty("statements");
            Assert.Equal(15, statements.GetArrayLength());
            Assert.Equal(4, statements.EnumerateArray().Count(s =>
                s.GetProperty("periodType").GetString() == "Q" && s.GetProperty("statementType").GetString() == "IS"));
            var fy = statements.EnumerateArray().Single(s =>
                s.GetProperty("periodType").GetString() == "FY" && s.GetProperty("statementType").GetString() == "IS");
            Assert.Equal(1000m, fy.GetProperty("lines").GetProperty("REV").GetDecimal());
        }

        // REST: as-reported (NI 60) and restated (NI 75) both present.
        var rest = CannedSourceCatalog.Get(set, CannedSourceCatalog.StatementsRestatedRest);
        using (var doc = JsonDocument.Parse(rest.Body))
        {
            Assert.Equal("REST", doc.RootElement.GetProperty("symbol").GetString());
            var income = doc.RootElement.GetProperty("statements").EnumerateArray()
                .Where(s => s.GetProperty("statementType").GetString() == "IS")
                .ToList();
            Assert.Equal(2, income.Count);
            Assert.Contains(income, s => s.GetProperty("version").GetString() == "as_reported" && s.GetProperty("lines").GetProperty("NI").GetDecimal() == 60m);
            Assert.Contains(income, s => s.GetProperty("version").GetString() == "restated" && s.GetProperty("lines").GetProperty("NI").GetDecimal() == 75m);
        }

        // PART: no cash-flow statement (FCF not computable).
        var part = CannedSourceCatalog.Get(set, CannedSourceCatalog.StatementsNoCfPart);
        using (var doc = JsonDocument.Parse(part.Body))
        {
            Assert.Equal("PART", doc.RootElement.GetProperty("symbol").GetString());
            Assert.DoesNotContain(
                doc.RootElement.GetProperty("statements").EnumerateArray(),
                s => s.GetProperty("statementType").GetString() == "CF");
        }
    }

    [Fact]
    public void Reference_payloads_derive_from_the_seed()
    {
        var set = L2;

        // Dividends: ALFA's four quarterly 0.25 rows; LAMDA has none.
        var dividends = CannedSourceCatalog.Get(set, CannedSourceCatalog.DividendsOk);
        using (var doc = JsonDocument.Parse(dividends.Body))
        {
            var rows = doc.RootElement.GetProperty("dividends").EnumerateArray().ToList();
            Assert.Equal(set.Dividends.Count, rows.Count);
            Assert.Equal(4, rows.Count(d => d.GetProperty("symbol").GetString() == "ALFA" && d.GetProperty("amountPerShare").GetDecimal() == 0.25m));
            Assert.DoesNotContain(rows, d => d.GetProperty("symbol").GetString() == "LAMDA");
        }

        // Corporate actions: EPSL split + bonus.
        var actions = CannedSourceCatalog.Get(set, CannedSourceCatalog.CorporateActionsOk);
        using (var doc = JsonDocument.Parse(actions.Body))
        {
            var rows = doc.RootElement.GetProperty("actions").EnumerateArray().ToList();
            Assert.Contains(rows, a => a.GetProperty("symbol").GetString() == "EPSL"
                && a.GetProperty("actionType").GetString() == "split"
                && a.GetProperty("actionDate").GetString() == "2025-06-02");
            Assert.Contains(rows, a => a.GetProperty("symbol").GetString() == "EPSL"
                && a.GetProperty("actionType").GetString() == "bonus_issue"
                && a.GetProperty("actionDate").GetString() == "2026-03-02");
        }

        // Disclosures: the three seeded KAP rows.
        var disclosures = CannedSourceCatalog.Get(set, CannedSourceCatalog.DisclosuresOk);
        using (var doc = JsonDocument.Parse(disclosures.Body))
        {
            var rows = doc.RootElement.GetProperty("disclosures").EnumerateArray().ToList();
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, d => d.GetProperty("id").GetInt64() == 8841 && d.GetProperty("disclosureType").GetString() == "annual_report");
        }

        // Index levels: XU100 10200 / XU30 30600 at T.
        var levels = CannedSourceCatalog.Get(set, CannedSourceCatalog.IndexLevelsOk);
        using (var doc = JsonDocument.Parse(levels.Body))
        {
            var rows = doc.RootElement.GetProperty("levels").EnumerateArray().ToList();
            Assert.Contains(rows, l => l.GetProperty("indexCode").GetString() == "XU100"
                && l.GetProperty("date").GetString() == "2026-10-06"
                && l.GetProperty("close").GetDecimal() == 10200.00m);
            Assert.Contains(rows, l => l.GetProperty("indexCode").GetString() == "XU30"
                && l.GetProperty("close").GetDecimal() == 30600.00m);
        }

        // Universe without sector classification: UNSEC's sectorCode is null.
        var universe = CannedSourceCatalog.Get(set, CannedSourceCatalog.UniverseMissingSector);
        using (var doc = JsonDocument.Parse(universe.Body))
        {
            var rows = doc.RootElement.GetProperty("instruments").EnumerateArray().ToList();
            Assert.Equal(16, rows.Count);
            var unsec = Assert.Single(rows, i => i.GetProperty("symbol").GetString() == "UNSEC");
            Assert.Equal(JsonValueKind.Null, unsec.GetProperty("sectorCode").ValueKind);
        }

        // Membership change set: NEWP removed and SIGMA added effective T+1.
        var changeSet = CannedSourceCatalog.Get(set, CannedSourceCatalog.UniverseAddRemove);
        using (var doc = JsonDocument.Parse(changeSet.Body))
        {
            Assert.Equal("2026-10-07", doc.RootElement.GetProperty("effectiveDate").GetString());
            Assert.Contains(
                doc.RootElement.GetProperty("remove").EnumerateArray(),
                m => m.GetProperty("symbol").GetString() == "NEWP" && m.GetProperty("indexCode").GetString() == "XU100");
            Assert.Contains(
                doc.RootElement.GetProperty("add").EnumerateArray(),
                m => m.GetProperty("symbol").GetString() == "SIGMA" && m.GetProperty("indexCode").GetString() == "XU100");
        }
    }

    [Fact]
    public void Macro_payloads_cover_the_full_and_degraded_profiles()
    {
        var set = L2;

        var full = CannedSourceCatalog.Get(set, CannedSourceCatalog.MacroOk);
        using (var doc = JsonDocument.Parse(full.Body))
        {
            var series = doc.RootElement.GetProperty("series").EnumerateArray().ToList();
            var codes = series.Select(s => s.GetProperty("code").GetString()).Distinct(StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "TUIK_CPI", "INDEP_CPI", "CBRT_REPO", "USD_TRY", "EUR_TRY", "GOLD" }, codes);

            // TUIK_CPI's revision case is carried (two rows for the same prior-month value).
            var priorMonth = new DateOnly(2026, 8, 1);
            var revisions = series
                .Where(s => s.GetProperty("code").GetString() == "TUIK_CPI" && s.GetProperty("valueDate").GetString() == priorMonth.ToString("yyyy-MM-dd"))
                .Select(s => s.GetProperty("value").GetDecimal())
                .ToList();
            Assert.Equal(new[] { 44.80m, 45.00m }, revisions);

            var gold = series.Single(s => s.GetProperty("code").GetString() == "GOLD");
            Assert.Equal("USD/oz", gold.GetProperty("unit").GetString());
            Assert.Equal(5200.00m, gold.GetProperty("value").GetDecimal());
        }

        var degraded = CannedSourceCatalog.Get(set, CannedSourceCatalog.MacroIndepCpiAbsent);
        using (var doc = JsonDocument.Parse(degraded.Body))
        {
            var codes = doc.RootElement.GetProperty("series").EnumerateArray()
                .Select(s => s.GetProperty("code").GetString())
                .ToList();
            Assert.DoesNotContain("INDEP_CPI", codes);
            Assert.Contains("TUIK_CPI", codes);
        }
    }

    [Fact]
    public void Fund_payloads_reproduce_the_three_seeded_funds()
    {
        var set = L2;

        var tef1 = CannedSourceCatalog.Get(set, CannedSourceCatalog.TefasTef0001);
        using (var doc = JsonDocument.Parse(tef1.Body))
        {
            Assert.Equal("TEF0001", doc.RootElement.GetProperty("fund").GetProperty("code").GetString());
            Assert.Equal(5, doc.RootElement.GetProperty("navs").GetArrayLength());
            Assert.Equal(3, doc.RootElement.GetProperty("holdings").GetArrayLength());
            Assert.Contains(
                doc.RootElement.GetProperty("holdings").EnumerateArray(),
                h => h.GetProperty("nameRaw").ValueKind != JsonValueKind.Null && h.GetProperty("symbol").ValueKind == JsonValueKind.Null);
        }

        var tef2 = CannedSourceCatalog.Get(set, CannedSourceCatalog.TefasTef0002);
        using (var doc = JsonDocument.Parse(tef2.Body))
        {
            Assert.Equal("TEF0002", doc.RootElement.GetProperty("fund").GetProperty("code").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("holdings").GetArrayLength());
        }

        var tef3 = CannedSourceCatalog.Get(set, CannedSourceCatalog.TefasTef0003);
        using (var doc = JsonDocument.Parse(tef3.Body))
        {
            var holding = Assert.Single(doc.RootElement.GetProperty("holdings").EnumerateArray());
            Assert.Equal("Hisse Y", holding.GetProperty("nameRaw").GetString());
            Assert.Equal(JsonValueKind.Null, holding.GetProperty("weight").ValueKind);
            Assert.Equal(JsonValueKind.Null, holding.GetProperty("units").ValueKind);
        }
    }

    [Fact]
    public void Evren_draft_payload_carries_the_seeded_descriptions()
    {
        var set = L2;

        var payload = CannedSourceCatalog.Get(set, CannedSourceCatalog.EvrenDraftOk);
        using var doc = JsonDocument.Parse(payload.Body);

        var descriptions = doc.RootElement.GetProperty("descriptions").EnumerateArray().ToList();
        Assert.Equal(set.BusinessDescriptions.Count, descriptions.Count);
        Assert.Contains(descriptions, d => d.GetProperty("symbol").GetString() == "REST" && d.GetProperty("status").GetString() == "draft");
    }

    [Fact]
    public void Catalog_payloads_follow_the_now_anchored_anchor()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2027, 3, 15, 6, 0, 0, TimeSpan.Zero));
        var anchor = FixtureAnchor.NowAnchored(clock);
        var set = FixtureUniverse.Build(anchor);

        using var ok = JsonDocument.Parse(CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesOk).Body);
        using var prior = JsonDocument.Parse(CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesOkTminus1).Body);

        Assert.Equal(anchor.T, DateOnly.Parse(ok.RootElement.GetProperty("date").GetString()!));
        Assert.Equal(anchor.TradingDay(-1), DateOnly.Parse(prior.RootElement.GetProperty("date").GetString()!));
        Assert.Equal(16, ParsePrices(CannedSourceCatalog.Get(set, CannedSourceCatalog.PricesOk).Body).Length);
    }

    private static (string Symbol, decimal PreviousClose, decimal Close, long Volume)[] ParsePrices(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => (
                e.GetProperty("symbol").GetString()!,
                e.GetProperty("previousClose").GetDecimal(),
                e.GetProperty("close").GetDecimal(),
                e.GetProperty("volume").GetInt64()))
            .ToArray();
    }
}
