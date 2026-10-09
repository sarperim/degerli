using System.Text.Json;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Funds;

/// <summary>In-scope funds to process plus the count of out-of-scope funds skipped by the
/// universe bound (BR-FDF-006, I-FDF-1).</summary>
public sealed record FundUniverseOutcome(IReadOnlyList<TefasFund> InScope, int Skipped);

/// <summary>A valid fund fact that conflicts with an already-stored fact for the same key;
/// the stored fact is kept and the new value is quarantined (Q1 policy, TC-FDF-009).</summary>
public sealed record FundConflict(string FundId, string PayloadJson);

/// <summary>Result of one fund fact batch: written, idempotent no-ops, conflicting values.</summary>
public sealed record FundFactWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<FundConflict> Conflicts)
{
    public static readonly FundFactWriteResult Empty = new(0, 0, []);
}

/// <summary>
/// Fact storage for the fund tables (FR-FDF-001..003; 02 §3.7, NFR-FDF-002). One row per
/// fact key: a re-send of an identical fact is an idempotent no-op, a valid value that
/// conflicts with an already-stored fact is refused and never overwrites the stored row
/// (same Q1 policy as the equity fact tables). Writes are append-only — existing rows are
/// never mutated or deleted.
/// </summary>
public sealed class TefasFundStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DegerliDbContext _db;

    public TefasFundStore(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Applies the fund-universe bound: creates a <c>funds</c> row for every in-scope fund
    /// that does not exist yet (append-only; an existing fund row is never modified), skips
    /// the out-of-scope types and counts them. Returns the in-scope funds to process.
    /// </summary>
    public async Task<FundUniverseOutcome> SyncUniverseAsync(
        IReadOnlyList<TefasFund> funds,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(funds);

        var existing = (await _db.Funds.Select(f => f.Code).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        var inScope = new List<TefasFund>();
        var skipped = 0;
        var added = false;

        foreach (var fund in funds)
        {
            var info = fund.Fund;
            if (info is null || !TefasFundTypes.IsInScope(info.Type))
            {
                // Scope bound, not a data-quality failure: skipped-and-counted, never quarantined.
                skipped++;
                continue;
            }

            inScope.Add(fund);

            if (existing.Add(info.Code))
            {
                _db.Funds.Add(new Fund
                {
                    Code = info.Code,
                    Name = string.IsNullOrWhiteSpace(info.Name) ? info.Code : info.Name,
                    FundType = info.Type,
                    Status = "active",
                    SourceRef = fund.SourceRef ?? $"tefas://funds/{info.Code}",
                    RecordedAt = recordedAt,
                });
                added = true;
            }
        }

        if (added)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FundUniverseOutcome(inScope, skipped);
    }

    /// <summary>Upserts a fund's NAV rows keyed <c>(fund_id, nav_date)</c>.</summary>
    public async Task<FundFactWriteResult> UpsertNavsAsync(
        string fundId,
        string sourceRef,
        DateTimeOffset recordedAt,
        IReadOnlyList<TefasNavItem> navs,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundId);
        ArgumentNullException.ThrowIfNull(navs);

        var existing = await _db.FundNavs
            .Where(n => n.FundId == fundId)
            .ToDictionaryAsync(n => n.NavDate, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var unchanged = 0;
        var conflicts = new List<FundConflict>();

        foreach (var nav in navs)
        {
            if (existing.TryGetValue(nav.Date, out var stored))
            {
                if (stored.NavValue == nav.Value)
                {
                    unchanged++;
                }
                else
                {
                    conflicts.Add(new FundConflict(fundId, Serialize(new
                    {
                        fundId,
                        navDate = nav.Date,
                        publishedValue = nav.Value,
                        storedValue = stored.NavValue,
                    })));
                }

                continue;
            }

            _db.FundNavs.Add(new FundNav
            {
                FundId = fundId,
                NavDate = nav.Date,
                NavValue = nav.Value,
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FundFactWriteResult(inserted, unchanged, conflicts);
    }

    /// <summary>Upserts a fund's performance rows keyed <c>(fund_id, period, as_of_date)</c>.</summary>
    public async Task<FundFactWriteResult> UpsertPerformancesAsync(
        string fundId,
        string sourceRef,
        DateTimeOffset recordedAt,
        IReadOnlyList<TefasPerformanceItem> performances,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundId);
        ArgumentNullException.ThrowIfNull(performances);

        var existing = await _db.FundPerformances
            .Where(p => p.FundId == fundId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byKey = existing.ToDictionary(p => (p.Period, p.AsOfDate));

        var inserted = 0;
        var unchanged = 0;
        var conflicts = new List<FundConflict>();

        foreach (var performance in performances)
        {
            if (byKey.TryGetValue((performance.Period, performance.AsOfDate), out var stored))
            {
                if (stored.ReturnValue == performance.ReturnValue)
                {
                    unchanged++;
                }
                else
                {
                    conflicts.Add(new FundConflict(fundId, Serialize(new
                    {
                        fundId,
                        performance.Period,
                        performance.AsOfDate,
                        publishedValue = performance.ReturnValue,
                        storedValue = stored.ReturnValue,
                    })));
                }

                continue;
            }

            _db.FundPerformances.Add(new FundPerformance
            {
                FundId = fundId,
                Period = performance.Period,
                AsOfDate = performance.AsOfDate,
                ReturnValue = performance.ReturnValue,
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FundFactWriteResult(inserted, unchanged, conflicts);
    }

    /// <summary>
    /// Upserts a holdings snapshot keyed <c>(fund_id, as_of_date, line_no)</c>. A line with
    /// a published symbol is matched to the MDF instrument (the future look-through link);
    /// an unmatched line keeps its <c>name_raw</c> with a NULL instrument id (FR-FDF-003).
    /// </summary>
    public async Task<FundFactWriteResult> UpsertHoldingsAsync(
        string fundId,
        DateOnly asOfDate,
        string sourceRef,
        DateTimeOffset recordedAt,
        IReadOnlyList<TefasHoldingItem> holdings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundId);
        ArgumentNullException.ThrowIfNull(holdings);

        var symbols = holdings
            .Where(h => !string.IsNullOrWhiteSpace(h.Symbol))
            .Select(h => h.Symbol!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var instrumentIds = symbols.Count == 0
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            : (await _db.Instruments
                    .Where(i => symbols.Contains(i.Symbol))
                    .Select(i => new { i.Id, i.Symbol })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(i => i.Symbol, i => i.Id, StringComparer.OrdinalIgnoreCase);

        var existing = await _db.FundHoldings
            .Where(h => h.FundId == fundId && h.AsOfDate == asOfDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byLine = existing.ToDictionary(h => h.LineNo);

        var inserted = 0;
        var unchanged = 0;
        var conflicts = new List<FundConflict>();

        foreach (var holding in holdings)
        {
            var instrumentId = holding.Symbol is not null
                && instrumentIds.TryGetValue(holding.Symbol.Trim(), out var matched)
                    ? matched
                    : (long?)null;

            if (byLine.TryGetValue(holding.LineNo, out var stored))
            {
                if (stored.InstrumentId == instrumentId
                    && stored.NameRaw == holding.NameRaw
                    && stored.Weight == holding.Weight
                    && stored.Units == holding.Units)
                {
                    unchanged++;
                }
                else
                {
                    conflicts.Add(new FundConflict(fundId, Serialize(new
                    {
                        fundId,
                        asOfDate,
                        holding.LineNo,
                        published = holding,
                        stored = new { stored.InstrumentId, stored.NameRaw, stored.Weight, stored.Units },
                    })));
                }

                continue;
            }

            _db.FundHoldings.Add(new FundHolding
            {
                FundId = fundId,
                AsOfDate = asOfDate,
                LineNo = holding.LineNo,
                InstrumentId = instrumentId,
                NameRaw = holding.NameRaw,
                Weight = holding.Weight,
                Units = holding.Units,
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FundFactWriteResult(inserted, unchanged, conflicts);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}
