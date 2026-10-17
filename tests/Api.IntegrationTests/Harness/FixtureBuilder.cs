using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Degerli.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// Fixture-builder helpers (test strategy §5.2 isolation): each test creates its own
/// users/screens/scenarios so tests are order-independent and never read another
/// test's writes. Backed by the real <see cref="DegerliDbContext"/> against the
/// Testcontainers database. The API endpoints that will eventually expose these
/// operations are built by the ACC/SCR/VAL tickets; until then the builder is the
/// single, shared mechanism tests use to make mutable state.
/// </summary>
public sealed class FixtureBuilder
{
    /// <summary>Deterministic default fixture timestamp (the L2 fixture epoch, FU R = 2026-10-06).</summary>
    public static readonly DateTimeOffset Epoch = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    private readonly DegerliDbContext _db;
    private static readonly PasswordHasher<ApplicationUser> PasswordHasher = new();

    public FixtureBuilder(DegerliDbContext db) => _db = db;

    public async Task<ApplicationUser> CreateUserAsync(
        string email,
        string password = "Degerli-Test-2026",
        bool emailConfirmed = true,
        string language = "tr")
    {
        var normalized = email.ToUpperInvariant();
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            EmailConfirmed = emailConfirmed,
            LanguagePref = language,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        user.PasswordHash = PasswordHasher.HashPassword(user, password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<Sector> CreateSectorAsync(string code, long? parentSectorId = null)
    {
        var sector = new Sector
        {
            Code = code,
            NameTr = $"Sektör {code}",
            NameEn = $"Sector {code}",
            ParentSectorId = parentSectorId,
        };
        _db.Sectors.Add(sector);
        await _db.SaveChangesAsync();
        return sector;
    }

    public async Task<Instrument> CreateInstrumentAsync(
        string symbol,
        long? sectorId = null,
        string accountingProfile = AccountingProfiles.Industrial,
        DateOnly? listingDate = null)
    {
        var instrument = new Instrument
        {
            Symbol = symbol,
            Name = $"Test {symbol}",
            SectorId = sectorId,
            AccountingProfile = accountingProfile,
            ListingDate = listingDate,
            SourceRef = $"fixture:{symbol}",
            RecordedAt = Epoch,
        };
        _db.Instruments.Add(instrument);
        await _db.SaveChangesAsync();
        return instrument;
    }

    /// <summary>
    /// A daily EOD price row for one instrument (FR-MDF-001) — the covered-universe
    /// precondition the Metrics Engine keys on.
    /// </summary>
    public async Task<DailyPrice> CreatePriceAsync(
        long instrumentId,
        DateOnly date,
        decimal close,
        string sourceRef = "fixture:price")
    {
        var price = new DailyPrice
        {
            InstrumentId = instrumentId,
            PriceDate = date,
            CloseRaw = close,
            SourceRef = sourceRef,
            RecordedAt = Epoch,
        };
        _db.DailyPrices.Add(price);
        await _db.SaveChangesAsync();
        return price;
    }

    /// <summary>
    /// Adds a current (open-ended) index membership — the covered-universe view the
    /// stock sections resolve symbols through (02 §3.1, <c>v_current_universe</c>).
    /// </summary>
    public async Task<IndexConstituent> CreateCurrentConstituentAsync(
        string indexCode,
        long instrumentId,
        DateOnly effectiveFrom)
    {
        var indexId = await _db.Indices
            .Where(index => index.Code == indexCode)
            .Select(index => index.Id)
            .SingleAsync();

        var membership = new IndexConstituent
        {
            IndexId = indexId,
            InstrumentId = instrumentId,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            SourceRef = $"fixture:{indexCode}",
            RecordedAt = Epoch,
        };
        _db.IndexConstituents.Add(membership);
        await _db.SaveChangesAsync();
        return membership;
    }

    /// <summary>
    /// Upserts canonical line items onto one instrument's FY statement of the given type,
    /// creating the statement when it is absent. A test-local write for facts the canned
    /// payloads do not carry (TKT-mdf-020 DR-1: a complete stray industrial statement
    /// behind a financial profile) — the shared fixture catalog stays untouched.
    /// </summary>
    public async Task<FinancialStatement> UpsertStatementLinesAsync(
        long instrumentId,
        string statementType,
        DateOnly periodEndDate,
        IReadOnlyDictionary<string, decimal> lines)
    {
        var statement = await _db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId
                && s.PeriodType == "FY"
                && s.StatementType == statementType
                && s.PeriodEndDate == periodEndDate)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync();

        if (statement is null)
        {
            statement = new FinancialStatement
            {
                InstrumentId = instrumentId,
                PeriodType = "FY",
                PeriodEndDate = periodEndDate,
                FiscalYear = periodEndDate.Year,
                StatementType = statementType,
                Version = 0,
                PublishedAt = periodEndDate,
                SourceUnitScale = "units",
                SourceCurrency = "TRY",
                SourceRef = $"fixture:{instrumentId}:{statementType}:{periodEndDate:yyyyMMdd}",
                RecordedAt = Epoch,
            };
            _db.FinancialStatements.Add(statement);
            await _db.SaveChangesAsync();
        }

        var existing = await _db.FinLineItems
            .Where(l => l.StatementId == statement.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l, StringComparer.Ordinal);

        foreach (var (code, value) in lines)
        {
            if (existing.TryGetValue(code, out var line))
            {
                line.Value = value;
            }
            else
            {
                _db.FinLineItems.Add(new FinLineItem
                {
                    StatementId = statement.Id,
                    ItemCode = code,
                    Value = value,
                });
            }
        }

        await _db.SaveChangesAsync();
        return statement;
    }

    public async Task<SavedScreen> CreateScreenAsync(
        long userId,
        string name,
        string criteriaJson = "[]",
        DateTimeOffset? at = null)
    {
        var timestamp = at ?? Epoch;
        var screen = new SavedScreen
        {
            UserId = userId,
            Name = name,
            CriteriaJson = criteriaJson,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        _db.SavedScreens.Add(screen);
        await _db.SaveChangesAsync();
        return screen;
    }

    public async Task<DcfScenario> CreateScenarioAsync(
        long userId,
        long instrumentId,
        string name,
        string paramsJson = "{}",
        DateTimeOffset? at = null)
    {
        var timestamp = at ?? Epoch;
        var scenario = new DcfScenario
        {
            UserId = userId,
            InstrumentId = instrumentId,
            Name = name,
            ParamsJson = paramsJson,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        _db.DcfScenarios.Add(scenario);
        await _db.SaveChangesAsync();
        return scenario;
    }
}
