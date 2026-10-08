using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;

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

    public async Task<Instrument> CreateInstrumentAsync(string symbol, long? sectorId = null)
    {
        var instrument = new Instrument
        {
            Symbol = symbol,
            Name = $"Test {symbol}",
            SectorId = sectorId,
            SourceRef = $"fixture:{symbol}",
            RecordedAt = Epoch,
        };
        _db.Instruments.Add(instrument);
        await _db.SaveChangesAsync();
        return instrument;
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
