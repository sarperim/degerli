using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// Binds the <c>statements</c> job's fetch bound to a <b>test-local</b> index carrying exactly
/// the requested current constituents (01 v1.5 §10.9.2, TKT-foundation-024). The production
/// bound is a real BIST index (default <c>XU030</c>); the L2 harness cannot use it (the FU
/// universe publishes <c>XU30</c>, not <c>XU030</c>, and the company-scoped fetch is exercised
/// one symbol at a time), so each statements-running test binds its own index. This is the only
/// permitted harness delta for the company-scoped seam — every golden value stays intact.
/// </summary>
public static class StatementBound
{
    /// <summary>The test-local index code the statements-job tests bind their fetch bound to.</summary>
    public const string IndexCode = "STMT";

    // A fixed, pre-anchor effective date and ingest timestamp: the resolver only filters on
    // `effective_to IS NULL`, so the exact dates carry no semantics here.
    private static readonly DateOnly EffectiveFrom = new(2010, 1, 4);
    private static readonly DateTimeOffset RecordedAt = new(2010, 1, 4, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Replaces the test-local bound's membership with exactly <paramref name="symbols"/>: one
    /// open <c>index_constituents</c> row per symbol (<c>effective_to</c> NULL). Any prior
    /// <c>STMT</c> membership is cleared so each test's bound is self-contained. Each symbol must
    /// already have an instrument row (the resolver joins <c>index_constituents</c> to
    /// <c>instruments</c>).
    /// </summary>
    public static async Task ReplaceAsync(DegerliDbContext db, params string[] symbols)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(symbols);

        var index = await db.Indices.FirstOrDefaultAsync(i => i.Code == IndexCode).ConfigureAwait(false);
        if (index is null)
        {
            index = new MarketIndex
            {
                Code = IndexCode,
                NameTr = "Test statements bound",
                NameEn = "Test statements bound",
            };
            db.Indices.Add(index);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        await db.IndexConstituents
            .Where(c => c.IndexId == index.Id)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        var instrumentIds = await db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i.Id)
            .ConfigureAwait(false);

        foreach (var symbol in symbols)
        {
            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = index.Id,
                InstrumentId = instrumentIds[symbol],
                EffectiveFrom = EffectiveFrom,
                EffectiveTo = null,
                SourceRef = "fixture:statement-bound",
                RecordedAt = RecordedAt,
            });
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
