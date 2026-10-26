using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-114 / TC-MDF-115 — the price-lane share-count derivation (TKT-foundation-086,
/// USER DECISION D5, 01 v1.5.60 §10.9.11) and the two sides of the 065 contract.
///
/// The double is the <b>committed</b> İş Yatırım real-source capture set
/// (<see cref="RealSourceCatalog.PricesAsels"/> and its siblings, the 2026-10-16 capture
/// window 2026-10-06..08) — a capture that is missing is a blocker report, never a fetch,
/// and <b>no test issues a live request to kap.org.tr or isYatirim</b>. Every negative case
/// is derived by mutating the decoded capture in memory.
///
/// The pinned arithmetic (D5): <c>count = PD ÷ close</c>, both operands from the same row.
/// ASELS <c>1,714,560,000,000 ÷ 376 = 4,560,000,000</c>; THYAO <c>400,890,000,000 ÷ 290.5 =
/// 1,380,000,000</c>; GARAN's 2026-10-08 row <c>539,700,000,000 ÷ 128.5 = 4,200,000,000</c>
/// while its 06-10/07-10 rows are <b>non-integral</b> (4,199,999,902.306534 /
/// 4,199,999,802.657694 — a ~100-share disagreement on a 4.2B count), FROTO is non-integral
/// on all three dates and EBEBK is non-integral through the decoder's own <c>HGDG_KAPANIS</c>.
/// The captures therefore exercise both outcomes without inventing a body.
/// </summary>
public sealed class PriceDerivedShareCountsTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The capture window's first date — the as-of the derivation is exercised over.</summary>
    private static readonly DateOnly CaptureDate = new(2026, 10, 6);
    private static readonly DateOnly CaptureLastDate = new(2026, 10, 8);

    /// <summary>The ASELS count the captures derive exactly (D5's exact-capture evidence).</summary>
    private const long AselsCount = 4_560_000_000L;

    private const decimal AselsMarketCap = 1_714_560_000_000m;
    private const decimal AselsClose = 376m;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public PriceDerivedShareCountsTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-114 (i) — the decoder reads PD beside the close
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-114 (i) — the market cap <c>PD</c> rides the same decoded fact as the close it
    /// is divided by: ASELS 2026-10-06 carries <c>1,714,560,000,000</c> next to the pinned
    /// <c>376</c>. The <c>HG_*</c>-fallback variant carries <b>no</b> <c>PD</c> — the additive
    /// field is nullable and an envelope without it is an honest absence, not a zero.
    /// </summary>
    [Fact]
    public void TC_MDF_114_decoded_fact_carries_the_market_cap_beside_the_close()
    {
        using var capture = Parse(RealSourceCatalog.PricesAsels);
        var rows = IsYatirimPriceDecoder.DecodeRows(capture);

        var row = rows.Single(r => r.Date == CaptureDate);
        Assert.Equal("ASELS", row.Symbol);
        Assert.Equal(AselsMarketCap, row.MarketCap);
        Assert.Equal(AselsClose, row.Close);

        // Every captured ASELS row carries its own PD; the close differs, the count does not.
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.MarketCap));

        // The constructed HG_* fallback envelope has no PD at all: honest absence.
        using var fallbackCapture = Parse(RealSourceCatalog.PricesHgFallback);
        var fallback = IsYatirimPriceDecoder.DecodeRows(fallbackCapture);
        var fallbackRow = Assert.Single(fallback);
        Assert.Null(fallbackRow.MarketCap);
        Assert.Equal(348.75m, fallbackRow.Close);
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-114 (ii)/(iii) — the derived row, its operands, its as-of, input-awareness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-114 (ii) — the derivation writes exactly one row per instrument with the
    /// integral count, <b>both operands</b>, the price date as the as-of and the source
    /// provenance: the arithmetic is auditable from the row itself (FR-MDF-008).
    /// </summary>
    [Fact]
    public async Task TC_MDF_114_derivation_writes_the_integral_count_with_both_operands_and_its_as_of()
    {
        await ResetAsync();
        var recordedAt = new DateTimeOffset(CaptureDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, recordedAt, AselsFact());

            Assert.Equal(1, result.Inserted);
            Assert.Equal(0, result.Updated);
            Assert.Equal(0, result.Unchanged);
            Assert.Equal(0, result.Stale);
            Assert.Equal(0, result.WithheldTotal);
            Assert.Equal(1, result.Written);
        }

        await using (var db = _postgres.CreateContext())
        {
            var row = await db.PriceDerivedShareCounts.SingleAsync();

            Assert.Equal(AselsCount, row.SharesOutstanding);
            Assert.Equal(AselsMarketCap, row.MarketCapRaw);
            Assert.Equal(AselsClose, row.CloseRaw);
            Assert.Equal(CaptureDate, row.PriceDate);
            Assert.Equal("isyatirim", row.SourceRef);
            Assert.Equal(recordedAt, row.RecordedAt);

            // The stored operands reproduce the stored count exactly — no rounding anywhere.
            Assert.Equal(row.SharesOutstanding, (long)(row.MarketCapRaw / row.CloseRaw));
        }
    }

    /// <summary>
    /// TC-MDF-114 (iii) — the store is input-aware: an identical re-run writes nothing and
    /// leaves <c>recorded_at</c> (the served as-of of the write) unmoved, while a changed
    /// market cap repairs the row in place and is counted — never a second row, never a
    /// silently stale value.
    /// </summary>
    [Fact]
    public async Task TC_MDF_114_rerun_is_input_aware_and_a_changed_fact_repairs_in_place()
    {
        await ResetAsync();
        var firstWrite = new DateTimeOffset(CaptureDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);
        var laterRun = new DateTimeOffset(CaptureLastDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        await using (var db = _postgres.CreateContext())
        {
            await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, firstWrite, AselsFact());
        }

        // (a) Identical operands → unchanged, and the write instant does not move.
        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, laterRun, AselsFact());

            Assert.Equal(0, result.Written);
            Assert.Equal(1, result.Unchanged);
            Assert.Equal(1, await db.PriceDerivedShareCounts.CountAsync());
            Assert.Equal(firstWrite, (await db.PriceDerivedShareCounts.SingleAsync()).RecordedAt);
        }

        // (b) A changed market cap (a 4.5B count) repairs the same row in place.
        var changed = AselsFact().Select(fact => fact with { MarketCap = 1_692_000_000_000m }).ToList();
        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, laterRun, changed);

            Assert.Equal(1, result.Updated);
            Assert.Equal(0, result.Inserted);
        }

        await using (var db = _postgres.CreateContext())
        {
            var row = await db.PriceDerivedShareCounts.SingleAsync();
            Assert.Equal(4_500_000_000L, row.SharesOutstanding);
            Assert.Equal(1_692_000_000_000m, row.MarketCapRaw);
            Assert.Equal(laterRun, row.RecordedAt);
        }
    }

    /// <summary>
    /// TC-MDF-114 (iv) — monotonic as-of: an <b>older</b> dated fact never regresses the
    /// stored row. The count and the as-of keep the newer fact, and the refusal is counted.
    /// </summary>
    [Fact]
    public async Task TC_MDF_114_an_older_fact_never_regresses_the_stored_row()
    {
        await ResetAsync();
        var recordedAt = new DateTimeOffset(CaptureLastDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureLastDate, recordedAt, AselsFact(CaptureLastDate));

            Assert.Equal(1, result.Inserted);
        }

        // Offer an older dated fact carrying a different count: it must not regress the row.
        await using (var db = _postgres.CreateContext())
        {
            var olderFact = AselsFact(CaptureDate)
                .Select(fact => fact with { MarketCap = 1_692_000_000_000m })
                .ToList();
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, recordedAt.AddDays(-2), olderFact);

            Assert.Equal(0, result.Written);
            Assert.Equal(1, result.Stale);
            Assert.Equal(0, result.WithheldTotal);
        }

        await using (var db = _postgres.CreateContext())
        {
            var row = await db.PriceDerivedShareCounts.SingleAsync();
            Assert.Equal(CaptureLastDate, row.PriceDate);
            Assert.Equal(AselsCount, row.SharesOutstanding);
            Assert.Equal(1_590_300_000_000m, row.MarketCapRaw);
            Assert.Equal(recordedAt, row.RecordedAt);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-114 (v) — every refusal is named, counted, and writes nothing
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-114 (v) — a missing <c>PD</c>, a close that is not positive and a
    /// <b>non-integral quotient</b> each take their own named, counted skip and write
    /// <b>no</b> row: the quotient is never rounded into a guess, and no zero row is ever
    /// produced (the 031/052 doctrine). The non-integral cases are the committed captures
    /// themselves (GARAN 06-10/07-10, FROTO, EBEBK).
    /// </summary>
    [Fact]
    public async Task TC_MDF_114_missing_pd_invalid_close_and_non_integral_quotients_are_named_counted_skips()
    {
        await ResetAsync();
        var recordedAt = new DateTimeOffset(CaptureDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        // (a) No PD at all (the HG_* fallback shape) — a counted, named skip, no row.
        var noMarketCap = AselsFact().Select(fact => fact with { MarketCap = null }).ToList();
        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, recordedAt, noMarketCap);

            Assert.Equal(0, result.Written);
            Assert.Equal(1, Withheld(result, PriceDerivedShareCountStore.MarketCapMissing));
            Assert.Equal(0, await db.PriceDerivedShareCounts.CountAsync());
        }

        // (a') A close that is not positive — the same honest refusal, its own reason.
        var zeroClose = AselsFact().Select(fact => fact with { Close = 0m }).ToList();
        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, recordedAt, zeroClose);

            Assert.Equal(0, result.Written);
            Assert.Equal(1, Withheld(result, PriceDerivedShareCountStore.CloseInvalid));
            Assert.Equal(0, await db.PriceDerivedShareCounts.CountAsync());
        }

        // (b) The committed non-integral captures on the same date: GARAN, FROTO and EBEBK
        // are each withheld and counted — never rounded to 4,199,999,902, never 3,509,099,928.
        var nonIntegral = new List<PriceFact>();
        nonIntegral.AddRange(PriceFactsOf(RealSourceCatalog.PricesGaran, "GARAN", CaptureDate));
        nonIntegral.AddRange(PriceFactsOf(RealSourceCatalog.PricesFroto, "FROTO", CaptureDate));
        nonIntegral.AddRange(PriceFactsOf(RealSourceCatalog.PricesEbebk, "EBEBK", CaptureDate));
        Assert.Equal(3, nonIntegral.Count);

        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db)
                .UpsertAsync("isyatirim", CaptureDate, recordedAt, nonIntegral);

            Assert.Equal(0, result.Written);
            Assert.Equal(3, Withheld(result, PriceDerivedShareCountStore.NonIntegralQuotient));
            Assert.Equal(0, result.Stale);
        }

        await using (var db = _postgres.CreateContext())
        {
            var rows = await db.PriceDerivedShareCounts.ToListAsync();
            Assert.Empty(rows);
        }

        // The same captures' integral rows do derive (GARAN 08-10), so the refusal is about
        // the operands disagreeing on that date, not about the instrument.
        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db).UpsertAsync(
                "isyatirim",
                CaptureLastDate,
                recordedAt,
                PriceFactsOf(RealSourceCatalog.PricesGaran, "GARAN", CaptureLastDate));

            Assert.Equal(1, result.Inserted);
        }

        await using (var db = _postgres.CreateContext())
        {
            var row = await db.PriceDerivedShareCounts.SingleAsync();
            Assert.Equal(4_200_000_000L, row.SharesOutstanding);
            Assert.NotEqual(0L, row.SharesOutstanding);
        }
    }

    /// <summary>
    /// TC-MDF-114 (v) — a withheld fact never deletes or rewrites a previously stored row:
    /// the row keeps its count and its as-of (which is what makes a stale count honest),
    /// while the disagreement is counted under its own reason.
    /// </summary>
    [Fact]
    public async Task TC_MDF_114_a_withheld_fact_keeps_the_previous_row_and_its_as_of()
    {
        await ResetAsync();
        var recordedAt = new DateTimeOffset(CaptureLastDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db).UpsertAsync(
                "isyatirim",
                CaptureLastDate,
                recordedAt,
                PriceFactsOf(RealSourceCatalog.PricesGaran, "GARAN", CaptureLastDate));

            Assert.Equal(1, result.Inserted);
        }

        // The earlier GARAN row is non-integral: withheld, counted, nothing removed.
        await using (var db = _postgres.CreateContext())
        {
            var result = await new PriceDerivedShareCountStore(db).UpsertAsync(
                "isyatirim",
                CaptureDate,
                recordedAt.AddDays(-2),
                PriceFactsOf(RealSourceCatalog.PricesGaran, "GARAN", CaptureDate));

            Assert.Equal(0, result.Written);
            Assert.Equal(0, result.Stale);
            Assert.Equal(1, Withheld(result, PriceDerivedShareCountStore.NonIntegralQuotient));
        }

        await using (var db = _postgres.CreateContext())
        {
            var row = await db.PriceDerivedShareCounts.SingleAsync();
            Assert.Equal(4_200_000_000L, row.SharesOutstanding);
            Assert.Equal(CaptureLastDate, row.PriceDate);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-114 (vi) — the prices job derives end-to-end (no new fetch, no new source)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-114 (vi) — the derivation runs <b>inside</b> the prices lane: one production
    /// <c>prices</c> run over a single-member bound serving the committed ASELS capture
    /// writes the row through the real decoder/adapter/job path, identical to the
    /// store-level row, at zero additional requests (one fetch per constituent is the lane's
    /// own; the derivation adds none).
    /// </summary>
    [Fact]
    public async Task TC_MDF_114_prices_job_derives_the_count_from_the_payload_it_already_fetches()
    {
        await ResetAsync();
        var capture = _wireMock.StubRealSource(RealSourceCatalog.PricesAsels);
        await RestrictBoundToAselsAsync();

        await using var host = BuildIngestion(capture.DefaultPath, CaptureDate);
        var result = await RunAsync(host, CaptureDate);

        Assert.Equal(IngestResult.Succeeded, result.Status);

        await using (var db = _postgres.CreateContext())
        {
            var row = await db.PriceDerivedShareCounts.SingleAsync();
            Assert.Equal(AselsCount, row.SharesOutstanding);
            Assert.Equal(AselsMarketCap, row.MarketCapRaw);
            Assert.Equal(AselsClose, row.CloseRaw);
            Assert.Equal(CaptureDate, row.PriceDate);
            Assert.Equal("isyatirim", row.SourceRef);
            Assert.NotEqual(default, row.RecordedAt);
        }

        // One fetch per constituent on the first pass — the derivation issues no request of
        // its own (the politeness posture is untouched).
        var priceRequests = (_wireMock.Server.LogEntries ?? [])
            .Count(entry => entry.RequestMessage?.Path == capture.DefaultPath);
        Assert.Equal(1, priceRequests);
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-115 (i)/(ii) — the served contract: two named facts, two as-ofs
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-115 (i)/(ii) — the balance-sheet <c>shareCount</c> block serves
    /// <b>paidInCapital</b> (TRY, the renamed store's figure) with its own as-of and the
    /// derived <b>sharesOutstanding</b> with its own as-of; both are honestly null (with null
    /// as-ofs) for an instrument carrying neither. The statement-reported lines and BVPS are
    /// untouched — no money under a count name, no count named as capital.
    /// </summary>
    [Fact]
    public async Task TC_MDF_115_balance_sheet_serves_paid_in_capital_and_the_derived_count_with_their_own_as_ofs()
    {
        await SeedAsync();

        var capitalAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ALFA");
            db.ShareCounts.Add(new ShareCount
            {
                InstrumentId = alfa.Id,
                PaidInCapital = 4_560_000_000m,
                RecordedAt = capitalAt,
                SourceRef = "kap",
            });
            db.PriceDerivedShareCounts.Add(new PriceDerivedShareCount
            {
                InstrumentId = alfa.Id,
                SharesOutstanding = AselsCount,
                MarketCapRaw = AselsMarketCap,
                CloseRaw = AselsClose,
                PriceDate = CaptureDate,
                RecordedAt = new DateTimeOffset(CaptureDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero),
                SourceRef = "isyatirim",
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // (1) Both facts serve, each with its own as-of, under its own honest name.
        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/balance-sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.Public);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var shareCount = root.GetProperty("shareCount");
            Assert.Equal(4_560_000_000m, shareCount.GetProperty("paidInCapital").GetDecimal());
            Assert.Equal(T.ToString("yyyy-MM-dd"), shareCount.GetProperty("paidInCapitalAsOf").GetString());
            Assert.Equal(AselsCount, shareCount.GetProperty("sharesOutstanding").GetInt64());
            Assert.Equal(CaptureDate.ToString("yyyy-MM-dd"), shareCount.GetProperty("sharesOutstandingAsOf").GetString());

            // The old, ambiguous single as-of is gone from the block: each fact carries its own.
            Assert.False(shareCount.TryGetProperty("asOf", out _));

            // The statement's own figures are untouched (golden stability): the derived count
            // is a distinct fact, not a replacement for the BS line, and the paid-in-capital
            // value never reappears as a count.
            AssertFigure(root.GetProperty("lines"), "sharesOutstanding", 100m, restated: false);
            AssertFigure(root.GetProperty("lines"), "sharesDiluted", 100m, restated: false);
            Assert.Equal(10.0m, root.GetProperty("bookValuePerShare").GetProperty("value").GetDecimal());
        }

        // (2) An instrument with neither fact is honestly absent: nulls, never zeros.
        using (var response = await client.GetAsync("/api/v1/stocks/LAMDA/balance-sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var shareCount = document.RootElement.GetProperty("shareCount");

            Assert.Equal(JsonValueKind.Null, shareCount.GetProperty("paidInCapital").ValueKind);
            Assert.Equal(JsonValueKind.Null, shareCount.GetProperty("paidInCapitalAsOf").ValueKind);
            Assert.Equal(JsonValueKind.Null, shareCount.GetProperty("sharesOutstanding").ValueKind);
            Assert.Equal(JsonValueKind.Null, shareCount.GetProperty("sharesOutstandingAsOf").ValueKind);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Decodes one committed capture — the whole decode input is the checked-in body.</summary>
    private static JsonDocument Parse(string payloadName) =>
        JsonDocument.Parse(RealSourceCatalog.Get(payloadName).Body);

    /// <summary>
    /// The decoded capture as canonical price facts — the exact mapping the production
    /// adapter applies (open stays NULL, the market cap rides the fact).
    /// </summary>
    private static IReadOnlyList<PriceFact> PriceFactsOf(
        string payloadName,
        string? symbolFilter = null,
        DateOnly? date = null)
    {
        using var document = Parse(payloadName);
        return IsYatirimPriceDecoder.DecodeRows(document)
            .Where(row => symbolFilter is null || string.Equals(row.Symbol, symbolFilter, StringComparison.Ordinal))
            .Where(row => date is null || row.Date == date)
            .Select(row => new PriceFact(
                row.Symbol,
                null,
                row.Open,
                row.High,
                row.Low,
                row.Close,
                row.Volume,
                row.MarketCap))
            .ToList();
    }

    /// <summary>One ASELS capture row as a canonical fact (default: the 2026-10-06 row).</summary>
    private static IReadOnlyList<PriceFact> AselsFact(DateOnly? date = null) =>
        PriceFactsOf(RealSourceCatalog.PricesAsels, "ASELS", date ?? CaptureDate);

    private static int Withheld(PriceDerivedShareCountWriteResult result, string reason) =>
        result.Withheld.TryGetValue(reason, out var count) ? count : 0;

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM price_derived_share_counts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM share_counts");
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM price_derived_share_counts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices WHERE price_date = {0}", CaptureDate);
        await EnsureCaptureInstrumentsAsync(db);
    }

    /// <summary>
    /// The fixture universe carries none of the captures' tickers (they are the price captures'
    /// own): add the instruments the cases derive onto, so the committed rows have an instrument
    /// to land on. The fixture's own rows and memberships are untouched.
    /// </summary>
    private static async Task EnsureCaptureInstrumentsAsync(DegerliDbContext db)
    {
        var alfa = await db.Instruments.AsNoTracking().SingleAsync(instrument => instrument.Symbol == "ALFA");

        foreach (var (symbol, name) in new[]
        {
            ("ASELS", "Aselsan Elektronik Sanayi ve Ticaret A.Ş."),
            ("THYAO", "Türk Hava Yolları A.O."),
            ("GARAN", "Türkiye Garanti Bankası A.Ş."),
            ("FROTO", "Ford Otomotiv Sanayi A.Ş."),
            ("EBEBK", "Ebru Bebek Gıda A.Ş."),
        })
        {
            if (await db.Instruments.AnyAsync(instrument => instrument.Symbol == symbol))
            {
                continue;
            }

            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Isin = null,
                Name = name,
                SectorId = alfa.SectorId,
                AccountingProfile = alfa.AccountingProfile,
                ListingDate = new DateOnly(2010, 1, 4),
                Status = "active",
                SourceRef = "fixture:universe",
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Binds the prices fetch to exactly one current constituent (ASELS) — the same
    /// single-member harness the per-ticker loop's other cases use, so the asserted outcome
    /// is one symbol's.
    /// </summary>
    private async Task RestrictBoundToAselsAsync()
    {
        await using var db = _postgres.CreateContext();
        var aselsId = await db.Instruments
            .Where(instrument => instrument.Symbol == "ASELS")
            .Select(instrument => instrument.Id)
            .SingleAsync();
        var indexId = await db.Indices
            .Where(index => index.Code == "XU100")
            .Select(index => index.Id)
            .SingleAsync();

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM index_constituents WHERE index_id = {0} AND instrument_id <> {1}",
            indexId,
            aselsId);

        if (!await db.IndexConstituents.AnyAsync(row => row.IndexId == indexId && row.InstrumentId == aselsId))
        {
            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = indexId,
                InstrumentId = aselsId,
                EffectiveFrom = new DateOnly(2010, 1, 4),
                EffectiveTo = null,
                SourceRef = "fixture:universe:membership",
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The production ingestion composition, retargeted at the Testcontainers database and
    /// the WireMock source double (the `prices` lane's own harness shape).
    /// </summary>
    private IngestionHost BuildIngestion(string pricesPath, DateOnly captureDate)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(
            new DateTimeOffset(captureDate.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Prices:Path"] = pricesPath,
                ["Ingestion:Prices:UniverseIndex"] = "XU100",
                ["Ingestion:Prices:RequestDelayMs"] = "0",
            })
            .Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider());
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, DateOnly date)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(PricesJob.Code, new IngestionRequest(date));
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static void AssertFigure(JsonElement parent, string name, decimal expected, bool restated)
    {
        var figure = parent.GetProperty(name);
        Assert.Equal(expected, figure.GetProperty("value").GetDecimal());
        Assert.Equal(restated, figure.GetProperty("restated").GetBoolean());
    }

    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider) => Provider = provider;

        public ServiceProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class HarnessMailAlerter : IIngestionAlerter
    {
        private readonly RecordingMailDispatcher _mail;

        public HarnessMailAlerter(RecordingMailDispatcher mail) => _mail = mail;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            _mail.SendAsync(
                new OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
